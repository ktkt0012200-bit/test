using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkullDive.Core;

namespace SkullDive.Net
{
    /// <summary>
    /// オンライン対戦のクライアント。Unity からもテストからも同じコードを使う。
    ///
    /// 受信メッセージはキューに積むだけで、コールバックを直接呼ばない。
    /// Unity ではメインスレッド以外から UI を触れないため、
    /// 呼び出し側が Update などで TryDequeue して処理する形にしてある。
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly IMessageCodec _codec;
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly ConcurrentQueue<ServerMessage> _inbox = new ConcurrentQueue<ServerMessage>();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly byte[] _receiveBuffer = new byte[64 * 1024];

        /// <summary>サーバから割り当てられた席。未参加なら -1。</summary>
        public int Seat { get; private set; } = -1;

        /// <summary>再接続用トークン。</summary>
        public string Token { get; private set; }

        public string RoomCode { get; private set; }

        /// <summary>直近に受け取った盤面。</summary>
        public PlayerView View { get; private set; }

        public RoomInfo Room { get; private set; }

        public string LastError { get; private set; }

        public bool IsConnected
        {
            get { return _socket.State == WebSocketState.Open; }
        }

        public GameClient(IMessageCodec codec)
        {
            if (codec == null) throw new ArgumentNullException("codec");
            _codec = codec;
        }

        /// <summary>
        /// 接続して参加する。roomCode が null / 空なら新規ルームを作る。
        /// token を渡すと再接続として扱われる。
        /// </summary>
        public async Task ConnectAsync(Uri url, string roomCode, string playerName, string token = null,
            CancellationToken cancellation = default(CancellationToken))
        {
            await _socket.ConnectAsync(url, cancellation).ConfigureAwait(false);
            await SendAsync(new ClientMessage
            {
                Kind = ClientMessageType.Join,
                RoomCode = roomCode,
                Name = playerName,
                Token = token,
            }, cancellation).ConfigureAwait(false);
        }

        public Task SendActionAsync(GameAction action, CancellationToken cancellation = default(CancellationToken))
        {
            return SendAsync(new ClientMessage { Kind = ClientMessageType.Action, Action = action }, cancellation);
        }

        public Task SendStartAsync(CancellationToken cancellation = default(CancellationToken))
        {
            return SendAsync(new ClientMessage { Kind = ClientMessageType.Start }, cancellation);
        }

        public Task SendAddBotAsync(CancellationToken cancellation = default(CancellationToken))
        {
            return SendAsync(new ClientMessage { Kind = ClientMessageType.AddBot }, cancellation);
        }

        public async Task SendAsync(ClientMessage message, CancellationToken cancellation = default(CancellationToken))
        {
            string json = _codec.Encode(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            // WebSocket は同時送信を許さないので直列化する。
            await _sendGate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        /// <summary>受信ループ。接続が閉じるまで回し続ける。</summary>
        public async Task ReceiveLoopAsync(CancellationToken cancellation = default(CancellationToken))
        {
            var builder = new StringBuilder();

            while (_socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer), cancellation)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (WebSocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close) return;

                builder.Append(Encoding.UTF8.GetString(_receiveBuffer, 0, result.Count));
                // 大きな盤面はフレームが分割されて届くので、EndOfMessage まで待つ。
                if (!result.EndOfMessage) continue;

                string json = builder.ToString();
                builder.Clear();

                ServerMessage message;
                try
                {
                    message = _codec.DecodeServer(json);
                }
                catch (Exception)
                {
                    continue;
                }

                Absorb(message);
                _inbox.Enqueue(message);
            }
        }

        /// <summary>接続状態をクライアント側にも反映しておく(UI がここだけ見れば済むように)。</summary>
        private void Absorb(ServerMessage message)
        {
            switch (message.Kind)
            {
                case ServerMessageType.Welcome:
                    Seat = message.Seat;
                    Token = message.Token;
                    if (message.Room != null) RoomCode = message.Room.Code;
                    Room = message.Room ?? Room;
                    break;
                case ServerMessageType.Room:
                    Room = message.Room;
                    if (message.Room != null) RoomCode = message.Room.Code;
                    break;
                case ServerMessageType.State:
                    View = message.View;
                    break;
                case ServerMessageType.Error:
                    LastError = message.Error;
                    break;
            }
        }

        /// <summary>受信済みメッセージを 1 件取り出す。Unity では Update から呼ぶ。</summary>
        public bool TryDequeue(out ServerMessage message)
        {
            return _inbox.TryDequeue(out message);
        }

        /// <summary>
        /// 接続を閉じる。
        ///
        /// CloseAsync ではなく CloseOutputAsync を使うのが重要。
        /// CloseAsync は相手のクローズフレームを受け取るまで待つため、
        /// 受信ループが同時に動いている状況(= このクラスの通常の使い方)や
        /// サーバが先に切った場合に例外を投げる。
        /// 切断は正常系なので、ここで例外を上げてはいけない。
        /// </summary>
        public async Task CloseAsync()
        {
            if (_socket.State != WebSocketState.Open) return;
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                // 相手が既に切っている場合は無視してよい。
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
                // ObjectDisposedException もここに含まれる(InvalidOperationException の派生)。
            }
        }

        public void Dispose()
        {
            _socket.Dispose();
            _sendGate.Dispose();
        }
    }
}
