using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkullDive.Ai;
using SkullDive.Core;
using SkullDive.Net;

namespace SkullDive.Server
{
    public sealed class Seat
    {
        public int Index;
        public string Name;
        public string Token;
        public bool IsBot;
        public AiPersonality Personality;
        public WebSocket Socket;
        public readonly SemaphoreSlim SendGate = new SemaphoreSlim(1, 1);

        public bool Connected
        {
            get { return IsBot || (Socket != null && Socket.State == WebSocketState.Open); }
        }
    }

    /// <summary>
    /// 1 ルーム分の権威ある状態。
    ///
    /// 重要な設計点:
    ///  - GameState を持つのはサーバだけ。クライアントには ViewRedactor を通した View しか送らない。
    ///    ホストがゲームを持つ方式(リレー型)だと、ホストのメモリに全員の伏せカードが載るため、
    ///    ブラフゲームでは原理的にチートを防げない。
    ///  - クライアントから来たアクションは合法手集合と照合してから適用する。
    ///    クライアントを信用する箇所がひとつも無い。
    ///  - 放置・切断は AI が代打する。ターン制ゲームで最も多い離脱要因なので、
    ///    最初から入れておかないと運用で困る。
    /// </summary>
    public sealed class Room
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly List<Seat> _seats = new List<Seat>();
        private readonly List<GameEvent> _pending = new List<GameEvent>();
        private readonly IRng _rng;
        private readonly Random _tokens = new Random();

        private GameState _state;
        private DateTime _turnDeadlineUtc = DateTime.MaxValue;
        private DateTime _roundAdvanceAtUtc = DateTime.MaxValue;

        private readonly int _turnTimeoutMs;
        private readonly int _roundEndDelayMs;

        public string Code { get; }
        public GameConfig Config { get; }
        public bool Started { get; private set; }
        public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

        public Room(string code, GameConfig config, int seed,
            int turnTimeoutMs = Protocol.TurnTimeoutMs, int roundEndDelayMs = Protocol.RoundEndDelayMs)
        {
            Code = code;
            Config = config;
            _rng = new XorShiftRng(seed);
            _turnTimeoutMs = turnTimeoutMs;
            _roundEndDelayMs = roundEndDelayMs;
        }

        public bool IsEmpty
        {
            get
            {
                foreach (var seat in _seats)
                {
                    if (!seat.IsBot && seat.Connected) return false;
                }
                return true;
            }
        }

        // ---------------------------------------------------------------- joining

        /// <summary>参加または再接続。割り当てられた席を返す。満席なら null。</summary>
        public async Task<Seat> JoinAsync(string name, string token, WebSocket socket)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                LastActivityUtc = DateTime.UtcNow;

                // 再接続: トークンが一致する席に繋ぎ直す。
                if (!string.IsNullOrEmpty(token))
                {
                    foreach (var existing in _seats)
                    {
                        if (existing.Token == token)
                        {
                            existing.Socket = socket;
                            await SendWelcomeAsync(existing).ConfigureAwait(false);
                            await BroadcastRoomAsync().ConfigureAwait(false);
                            if (Started) await BroadcastStateAsync().ConfigureAwait(false);
                            return existing;
                        }
                    }
                }

                // 開始後の新規参加はできない(観戦は未実装)。
                if (Started) return null;
                if (_seats.Count >= Protocol.MaxRoomPlayers) return null;

                var seat = new Seat
                {
                    Index = _seats.Count,
                    Name = string.IsNullOrEmpty(name) ? "P" + (_seats.Count + 1) : name,
                    Token = NewToken(),
                    Socket = socket,
                };
                _seats.Add(seat);

                await SendWelcomeAsync(seat).ConfigureAwait(false);
                await BroadcastRoomAsync().ConfigureAwait(false);
                return seat;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync(Seat seat)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                seat.Socket = null;
                LastActivityUtc = DateTime.UtcNow;

                // 未開始なら席を空ける。開始後は AI が代打するので席は残す。
                if (!Started)
                {
                    _seats.Remove(seat);
                    for (int i = 0; i < _seats.Count; i++) _seats[i].Index = i;
                }

                await BroadcastRoomAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        // ---------------------------------------------------------------- messages

        public async Task HandleAsync(Seat seat, ClientMessage message)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                LastActivityUtc = DateTime.UtcNow;

                switch (message.Kind)
                {
                    case ClientMessageType.Start:
                        await StartAsync(seat).ConfigureAwait(false);
                        break;
                    case ClientMessageType.AddBot:
                        await AddBotAsync(seat).ConfigureAwait(false);
                        break;
                    case ClientMessageType.Action:
                        await ApplyPlayerActionAsync(seat, message.Action).ConfigureAwait(false);
                        break;
                    case ClientMessageType.Ping:
                        await SendAsync(seat, new ServerMessage { Kind = ServerMessageType.Pong }).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task StartAsync(Seat seat)
        {
            if (Started)
            {
                await SendErrorAsync(seat, "すでに開始しています").ConfigureAwait(false);
                return;
            }
            if (seat.Index != 0)
            {
                await SendErrorAsync(seat, "開始できるのはホストだけです").ConfigureAwait(false);
                return;
            }
            if (_seats.Count < Protocol.MinRoomPlayers)
            {
                await SendErrorAsync(seat, "あと " + (Protocol.MinRoomPlayers - _seats.Count) + " 人必要です").ConfigureAwait(false);
                return;
            }

            Started = true;
            _pending.Clear();
            _state = GameEngine.CreateMatch(Config, _seats.Count, 0, _pending);

            await BroadcastRoomAsync().ConfigureAwait(false);
            await AdvanceAsync().ConfigureAwait(false);
        }

        private async Task AddBotAsync(Seat seat)
        {
            if (Started || seat.Index != 0) return;
            if (_seats.Count >= Protocol.MaxRoomPlayers) return;

            var personalities = AiPersonality.All();
            var personality = personalities[_seats.Count % personalities.Length];
            _seats.Add(new Seat
            {
                Index = _seats.Count,
                Name = personality.Name,
                Token = NewToken(),
                IsBot = true,
                Personality = personality,
            });

            await BroadcastRoomAsync().ConfigureAwait(false);
        }

        private async Task ApplyPlayerActionAsync(Seat seat, GameAction action)
        {
            if (!Started || _state == null || _state.IsOver) return;

            // クライアントは自分の席のアクションしか送れない。
            if (action.Player != seat.Index)
            {
                await SendErrorAsync(seat, "他の席のアクションは送れません").ConfigureAwait(false);
                return;
            }

            // 合法手かどうかはサーバが決める。クライアントの主張は一切信用しない。
            if (!GameEngine.IsLegal(_state, action))
            {
                await SendErrorAsync(seat, "その手は今使えません").ConfigureAwait(false);
                // 盤面がずれている可能性があるので、正しい状態を送り直す。
                await BroadcastStateAsync().ConfigureAwait(false);
                return;
            }

            GameEngine.Apply(_state, action, _rng, _pending);
            await AdvanceAsync().ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- pump

        /// <summary>
        /// 定期的に呼ばれる。手番の時間切れとラウンド送りを進める。
        /// </summary>
        public async Task TickAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!Started || _state == null || _state.IsOver) return;

                var now = DateTime.UtcNow;

                if (_state.Phase == Phase.RoundEnd)
                {
                    // 決着の演出を見せる時間を全員に与えてから次ラウンドへ。
                    if (now >= _roundAdvanceAtUtc)
                    {
                        GameEngine.Apply(_state, GameAction.AdvanceRound(), _rng, _pending);
                        await AdvanceAsync().ConfigureAwait(false);
                    }
                    return;
                }

                if (now >= _turnDeadlineUtc)
                {
                    // 放置・切断された席は AI が代打する。止まらないことを優先する。
                    int actor = GameEngine.CurrentActor(_state);
                    if (actor >= 0)
                    {
                        PlayBotTurn(actor);
                        await AdvanceAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// bot / 切断席の手を消化してから、全員に状態を配る。
        /// 呼び出し側は _gate を取得済みであること。
        /// </summary>
        private async Task AdvanceAsync()
        {
            int guard = 0;
            while (!_state.IsOver && _state.Phase != Phase.RoundEnd)
            {
                if (++guard > 1000) break;

                int actor = GameEngine.CurrentActor(_state);
                if (actor < 0) break;

                var seat = _seats[actor];
                // 人間の手番なら、そこで止めて入力を待つ。
                if (!seat.IsBot && seat.Connected) break;

                PlayBotTurn(actor);
            }

            var now = DateTime.UtcNow;
            if (_state.IsOver)
            {
                _turnDeadlineUtc = DateTime.MaxValue;
                _roundAdvanceAtUtc = DateTime.MaxValue;
            }
            else if (_state.Phase == Phase.RoundEnd)
            {
                _turnDeadlineUtc = DateTime.MaxValue;
                _roundAdvanceAtUtc = now.AddMilliseconds(_roundEndDelayMs);
            }
            else
            {
                _turnDeadlineUtc = now.AddMilliseconds(_turnTimeoutMs);
                _roundAdvanceAtUtc = DateTime.MaxValue;
            }

            await BroadcastStateAsync().ConfigureAwait(false);
        }

        /// <summary>その席の手を AI に選ばせて適用する。bot 席と時間切れの両方で使う。</summary>
        private void PlayBotTurn(int actor)
        {
            var seat = _seats[actor];
            var personality = seat.Personality ?? AiPersonality.Steady();
            var brain = new AiBrain(personality);
            // AI にも秘匿処理済みの View しか渡さない。サーバ側の AI もカンニングしない。
            var view = ViewRedactor.Redact(_state, actor, Names());
            var action = brain.Decide(view, _rng);
            GameEngine.Apply(_state, action, _rng, _pending);
        }

        // ---------------------------------------------------------------- sending

        private string[] Names()
        {
            var names = new string[_seats.Count];
            for (int i = 0; i < _seats.Count; i++) names[i] = _seats[i].Name;
            return names;
        }

        private RoomInfo BuildRoomInfo()
        {
            var seats = new SeatInfo[_seats.Count];
            for (int i = 0; i < _seats.Count; i++)
            {
                var seat = _seats[i];
                seats[i] = new SeatInfo
                {
                    Seat = i,
                    Name = seat.Name,
                    IsBot = seat.IsBot,
                    Connected = seat.Connected,
                    PersonalityId = seat.Personality != null ? seat.Personality.Id : null,
                };
            }
            return new RoomInfo
            {
                Code = Code,
                Seats = seats,
                Started = Started,
                HostSeat = _seats.Count > 0 ? 0 : -1,
                Config = Config,
            };
        }

        private Task SendWelcomeAsync(Seat seat)
        {
            return SendAsync(seat, new ServerMessage
            {
                Kind = ServerMessageType.Welcome,
                Seat = seat.Index,
                Token = seat.Token,
                Room = BuildRoomInfo(),
            });
        }

        private async Task BroadcastRoomAsync()
        {
            var info = BuildRoomInfo();
            foreach (var seat in _seats)
            {
                if (seat.IsBot) continue;
                await SendAsync(seat, new ServerMessage { Kind = ServerMessageType.Room, Room = info })
                    .ConfigureAwait(false);
            }
        }

        /// <summary>各席にその席から見える盤面だけを送る。</summary>
        private async Task BroadcastStateAsync()
        {
            if (_state == null) return;

            var names = Names();
            int remainingMs = _turnDeadlineUtc == DateTime.MaxValue
                ? 0
                : Math.Max(0, (int)(_turnDeadlineUtc - DateTime.UtcNow).TotalMilliseconds);

            foreach (var seat in _seats)
            {
                if (seat.IsBot || !seat.Connected) continue;

                await SendAsync(seat, new ServerMessage
                {
                    Kind = ServerMessageType.State,
                    Seat = seat.Index,
                    View = ViewRedactor.Redact(_state, seat.Index, names),
                    Events = EventRedactor.Redact(_pending, seat.Index),
                    TurnDeadlineMs = remainingMs,
                }).ConfigureAwait(false);
            }

            // 全員に配り終えたので、次の差分のためにイベントを空にする。
            _pending.Clear();
        }

        private Task SendErrorAsync(Seat seat, string error)
        {
            return SendAsync(seat, new ServerMessage { Kind = ServerMessageType.Error, Error = error });
        }

        private static async Task SendAsync(Seat seat, ServerMessage message)
        {
            var socket = seat.Socket;
            if (socket == null || socket.State != WebSocketState.Open) return;

            var bytes = Encoding.UTF8.GetBytes(WireCodec.EncodeServer(message));

            await seat.SendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // 切断は Connected 判定と代打で吸収されるので、ここでは落とさない。
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                seat.SendGate.Release();
            }
        }

        private string NewToken()
        {
            var bytes = new byte[16];
            lock (_tokens) _tokens.NextBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
