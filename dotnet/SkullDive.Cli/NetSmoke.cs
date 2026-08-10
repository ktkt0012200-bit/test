using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkullDive.Ai;
using SkullDive.Core;
using SkullDive.Net;

namespace SkullDive.Cli
{
    /// <summary>
    /// 実際に起動したサーバへ 2 人のクライアントで接続し、bot を足して 1 マッチ完走させる結合テスト。
    ///
    /// ここで確認しているのは以下。
    ///  - プロトコルの往復(サーバの System.Text.Json とクライアントの往復)
    ///  - サーバがアクションを検証して適用し、各席に別々の View を配れること
    ///  - 受信した View に他人の伏せカードが含まれないこと(実際のワイヤー越しの検証)
    ///  - bot / ラウンド送り / 時間切れを含めてゲームが最後まで進むこと
    /// </summary>
    public static class NetSmoke
    {
        public static async Task<int> RunAsync(string url, int bots, int timeoutSeconds, bool idleGuest = false)
        {
            // Unity クライアントと同一の実装を使う。
            var codec = WireCodec.Instance;
            var failures = new List<string>();

            using var host = new GameClient(codec);
            using var guest = new GameClient(codec);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds + 10));

            Console.WriteLine("connecting to " + url + (idleGuest ? "  [guest は放置]" : ""));
            await host.ConnectAsync(new Uri(url), null, "HOST", null, cancellation.Token);
            var hostLoop = host.ReceiveLoopAsync(cancellation.Token);

            string code = await WaitForAsync(() => host.RoomCode, cancellation.Token);
            if (code == null) { Console.Error.WriteLine("FAIL: ルームコードが返ってこなかった"); return 1; }
            Console.WriteLine("room code: " + code + " (host seat " + host.Seat + ")");

            await guest.ConnectAsync(new Uri(url), code, "GUEST", null, cancellation.Token);
            var guestLoop = guest.ReceiveLoopAsync(cancellation.Token);

            if (await WaitForAsync(() => guest.Seat >= 0 ? (object)guest.Seat : null, cancellation.Token) == null)
            {
                Console.Error.WriteLine("FAIL: guest が席を割り当てられなかった");
                return 1;
            }
            Console.WriteLine("guest seat: " + guest.Seat);

            for (int i = 0; i < bots; i++) await host.SendAddBotAsync(cancellation.Token);

            int expectedSeats = 2 + bots;
            if (await WaitForAsync(() =>
                    host.Room != null && host.Room.Seats.Length == expectedSeats ? (object)true : null,
                    cancellation.Token) == null)
            {
                Console.Error.WriteLine("FAIL: bot が席に入らなかった (" +
                    (host.Room == null ? "room null" : host.Room.Seats.Length + " seats") + ")");
                return 1;
            }
            Console.WriteLine("seats: " + expectedSeats + " (" + bots + " bots)");

            await host.SendStartAsync(cancellation.Token);

            // --- ここから対局。State を受け取ったときだけ手を返す(二重送信を避ける) ---

            var brains = new Dictionary<GameClient, AiBrain>
            {
                { host, new AiBrain(AiPersonality.Steady()) },
                { guest, new AiBrain(AiPersonality.Gambler()) },
            };
            var rng = new XorShiftRng(777);
            var clients = new[] { host, guest };

            int stateMessages = 0;
            int actionsSent = 0;
            int winner = -2;
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (winner == -2 && DateTime.UtcNow < deadline)
            {
                foreach (var client in clients)
                {
                    ServerMessage message;
                    while (client.TryDequeue(out message))
                    {
                        if (message.Kind == ServerMessageType.Error)
                        {
                            failures.Add("server error: " + message.Error);
                            continue;
                        }
                        if (message.Kind != ServerMessageType.State || message.View == null) continue;

                        stateMessages++;
                        Validate(client, message, failures);

                        var view = message.View;
                        if (view.Phase == Phase.MatchEnd)
                        {
                            winner = view.Winner;
                            break;
                        }

                        // 放置テスト: guest は手を返さない。サーバの時間切れ代打が働かないと
                        // ここでゲームが止まり、テストがタイムアウトで落ちる。
                        if (idleGuest && client == guest) continue;

                        if (view.IsMyTurn)
                        {
                            var action = brains[client].Decide(view, rng);
                            await client.SendActionAsync(action, cancellation.Token);
                            actionsSent++;
                        }
                    }
                    if (winner != -2) break;
                }

                await Task.Delay(10, cancellation.Token).ConfigureAwait(false);
            }

            Console.WriteLine();
            Console.WriteLine("state messages : " + stateMessages);
            Console.WriteLine("actions sent   : " + actionsSent);
            Console.WriteLine("winner         : " + (winner >= 0 ? "seat " + winner : "(未決着)"));

            if (winner == -2) failures.Add("制限時間内にマッチが終わらなかった");

            // 先に受信ループを畳んでからソケットを閉じる。逆順にすると
            // 受信中のソケットを閉じることになり、切断が例外扱いになってしまう。
            cancellation.Cancel();
            await Quietly(hostLoop);
            await Quietly(guestLoop);
            await host.CloseAsync();
            await guest.CloseAsync();

            Console.WriteLine();
            if (failures.Count == 0)
            {
                Console.WriteLine("PASS: サーバ経由で 1 マッチ完走し、秘匿情報の漏れも無し");
                return 0;
            }

            foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
            return 1;
        }

        /// <summary>受信ループの終了を待つ。キャンセルや切断による例外は正常系として捨てる。</summary>
        private static async Task Quietly(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>ワイヤー越しに届いた View が秘匿処理済みであることを確かめる。</summary>
        private static void Validate(GameClient client, ServerMessage message, List<string> failures)
        {
            var view = message.View;

            if (view.Viewer != client.Seat)
                failures.Add("View の宛先が違う: viewer=" + view.Viewer + " seat=" + client.Seat);

            if (view.MyStack.Length != view.Players[client.Seat].StackCount)
                failures.Add("自分の山の枚数が公開情報と食い違う");

            for (int i = 0; i < view.Players.Length; i++)
            {
                var player = view.Players[i];
                if (player.RevealedFromTop.Length != player.FlippedCount)
                {
                    failures.Add("seat " + i + " の公開カード数がめくった枚数と違う ("
                        + player.RevealedFromTop.Length + " vs " + player.FlippedCount + ")");
                }
            }

            if (message.Events == null) return;
            foreach (var e in message.Events)
            {
                bool secret = e.Kind == GameEventKind.CardPlaced || e.Kind == GameEventKind.CardDiscarded;
                if (secret && e.Player != client.Seat && e.Card != Cards.Hidden)
                {
                    failures.Add("他人の伏せカードが " + e.Kind + " イベントで漏れている");
                }
            }
        }

        /// <summary>条件が満たされるまで待つ。タイムアウトしたら null。</summary>
        private static async Task<T> WaitForAsync<T>(Func<T> probe, CancellationToken cancellation,
            int timeoutMs = 5000) where T : class
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline && !cancellation.IsCancellationRequested)
            {
                var value = probe();
                if (value != null) return value;
                await Task.Delay(20, cancellation);
            }
            return null;
        }
    }
}
