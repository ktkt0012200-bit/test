using System.Collections.Generic;
using System.Text.Json;
using SkullDive.Core;
using SkullDive.Json;
using SkullDive.Net;

namespace SkullDive.Tests
{
    /// <summary>
    /// 手書きコーデック (WireCodec) のテスト。
    ///
    /// サーバと Unity が同じ WireCodec を使うので互換性は自明だが、手書きなので
    /// 「読み書きが対称であること」と「標準的なシリアライザと同じ形の JSON であること」を
    /// 独立に確かめておく。後者は System.Text.Json をオラクルとして突き合わせる
    /// (デバッグ時に汎用ツールで JSON を読めるようにしておきたいため)。
    /// </summary>
    public static class CodecTests
    {
        private static ServerMessage SampleServerMessage()
        {
            var state = GameEngine.CreateMatch(GameConfig.Default(), 4);
            var events = new List<GameEvent>();
            var rng = new XorShiftRng(31);

            GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Crown), rng, events);
            GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Skull), rng, events);
            GameEngine.Apply(state, GameAction.PlaceCard(2, CardType.Rose), rng, events);
            GameEngine.Apply(state, GameAction.PlaceCard(3, CardType.Rose), rng, events);
            GameEngine.Apply(state, GameAction.Bid(0, 4), rng, events);
            GameEngine.Apply(state, GameAction.Flip(0, 0), rng, events);

            return new ServerMessage
            {
                Kind = ServerMessageType.State,
                Seat = 2,
                Token = "tok-123",
                View = ViewRedactor.Redact(state, 2, new[] { "あなた", "臆病者", "博打屋", "真似っ子" }),
                Events = EventRedactor.Redact(events, 2),
                TurnDeadlineMs = 18500,
                Room = new RoomInfo
                {
                    Code = "AB2C",
                    Started = true,
                    HostSeat = 0,
                    Config = GameConfig.Default(),
                    Seats = new[]
                    {
                        new SeatInfo { Seat = 0, Name = "あなた", IsBot = false, Connected = true },
                        new SeatInfo { Seat = 1, Name = "臆病者", IsBot = true, Connected = true, PersonalityId = "timid" },
                    },
                },
            };
        }

        public static void Register()
        {
            T.Section("codec/roundtrip");

            T.Test("ServerMessage は WireCodec で往復しても内容が変わらない", () =>
            {
                var original = SampleServerMessage();
                string json = WireCodec.EncodeServer(original);
                var restored = WireCodec.Instance.DecodeServer(json);

                T.Eq(ServerMessageType.State, restored.Kind, "種別");
                T.Eq(2, restored.Seat, "席");
                T.Eq("tok-123", restored.Token, "トークン");
                T.Eq(18500, restored.TurnDeadlineMs, "残り時間");
                T.Eq(Protocol.Version, restored.ProtocolVersion, "プロトコル版");

                T.Eq("AB2C", restored.Room.Code, "ルームコード");
                T.Eq(2, restored.Room.Seats.Length, "席数");
                T.Eq("臆病者", restored.Room.Seats[1].Name, "日本語の名前");
                T.Eq(true, restored.Room.Seats[1].IsBot, "bot 判定");
                T.Eq("timid", restored.Room.Seats[1].PersonalityId, "性格 ID");
                T.Eq(2, restored.Room.Config.CrownValue, "ルーム設定");

                var view = restored.View;
                T.Eq(2, view.Viewer, "View の宛先");
                T.Eq(4, view.Players.Length, "プレイヤー数");
                T.Eq("真似っ子", view.Players[3].Name, "名前");
                T.Eq(Phase.Challenging, view.Phase, "フェーズ");
                T.Eq(original.View.TotalOnTable, view.TotalOnTable, "場の枚数");
                T.Eq(original.View.HighestBid, view.HighestBid, "宣言");
                T.Eq(original.View.MyHand.Length, view.MyHand.Length, "手札枚数");
                T.Eq(original.View.MyStack.Length, view.MyStack.Length, "山枚数");
                T.Eq(original.View.Players[0].FlippedCount, view.Players[0].FlippedCount, "公開枚数");
                T.Eq(original.View.Players[0].RevealedFromTop.Length,
                    view.Players[0].RevealedFromTop.Length, "公開カード数");
                T.Eq(original.View.Players[0].RevealedFromTop[0],
                    view.Players[0].RevealedFromTop[0], "公開カードの中身");
                T.Eq(original.Events.Length, restored.Events.Length, "イベント数");

                for (int i = 0; i < original.Events.Length; i++)
                {
                    T.Eq(original.Events[i].Kind, restored.Events[i].Kind, "イベント種別 " + i);
                    T.Eq(original.Events[i].Card, restored.Events[i].Card, "イベントのカード " + i);
                    T.Eq(original.Events[i].Amount, restored.Events[i].Amount, "イベントの値 " + i);
                }
            });

            T.Test("合法手も往復する", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var original = new ServerMessage
                {
                    Kind = ServerMessageType.State,
                    View = ViewRedactor.Redact(state, 0),
                };

                var restored = WireCodec.Instance.DecodeServer(WireCodec.EncodeServer(original));

                T.Eq(original.View.LegalActions.Length, restored.View.LegalActions.Length, "合法手の数");
                for (int i = 0; i < original.View.LegalActions.Length; i++)
                {
                    T.Eq(original.View.LegalActions[i], restored.View.LegalActions[i], "合法手 " + i);
                }
            });

            T.Test("ClientMessage は往復しても内容が変わらない", () =>
            {
                var original = new ClientMessage
                {
                    Kind = ClientMessageType.Action,
                    RoomCode = "XY9Z",
                    Name = "プレイヤー\"1\"",
                    Token = "abc/def+ghi",
                    Action = GameAction.Bid(2, 7),
                };

                var restored = WireCodec.DecodeClient(WireCodec.Instance.Encode(original));

                T.Eq(ClientMessageType.Action, restored.Kind, "種別");
                T.Eq("XY9Z", restored.RoomCode, "ルームコード");
                T.Eq("プレイヤー\"1\"", restored.Name, "引用符を含む名前");
                T.Eq("abc/def+ghi", restored.Token, "記号を含むトークン");
                T.Eq(original.Action, restored.Action, "アクション");
            });

            T.Test("null や空配列を含んでいても壊れない", () =>
            {
                var original = new ServerMessage { Kind = ServerMessageType.Error, Error = "そのルームは見つかりません" };
                var restored = WireCodec.Instance.DecodeServer(WireCodec.EncodeServer(original));

                T.Eq(ServerMessageType.Error, restored.Kind, "種別");
                T.Eq("そのルームは見つかりません", restored.Error, "エラー文");
                T.True(restored.View == null, "View は null");
                T.True(restored.Room == null, "Room は null");
                T.Eq(0, restored.Events.Length, "イベントは空配列として読める");
            });

            T.Section("codec/json");

            T.Test("知らないキーは読み飛ばす(前方互換)", () =>
            {
                // サーバに新しいフィールドが増えても、古いクライアントが壊れないこと。
                string json = "{\"Type\":2,\"Seat\":1,\"SomethingNew\":{\"a\":[1,2,3]},\"TurnDeadlineMs\":900}";
                var restored = WireCodec.Instance.DecodeServer(json);

                T.Eq(ServerMessageType.State, restored.Kind, "種別");
                T.Eq(1, restored.Seat, "席");
                T.Eq(900, restored.TurnDeadlineMs, "既知のフィールドは読める");
            });

            T.Test("エスケープと日本語を正しく扱う", () =>
            {
                var original = new ClientMessage
                {
                    Kind = ClientMessageType.Join,
                    Name = "改行\nタブ\t引用\"バックスラッシュ\\ 日本語 🎴",
                };
                var restored = WireCodec.DecodeClient(WireCodec.Instance.Encode(original));
                T.Eq(original.Name, restored.Name, "文字列がそのまま戻る");
            });

            T.Test("\\u エスケープを読める", () =>
            {
                // 他のシリアライザが非 ASCII をエスケープして送ってくる場合に備える。
                string json = "{\"Type\":0,\"Name\":\"\\u3042\\u3044\"}";
                var restored = WireCodec.DecodeClient(json);
                T.Eq("あい", restored.Name, "エスケープを復号できる");
            });

            T.Test("壊れた JSON は例外になる", () =>
            {
                T.Throws<System.FormatException>(
                    () => WireCodec.Instance.DecodeServer("{\"Type\":2,"), "途中で切れている");
                T.Throws<System.FormatException>(
                    () => WireCodec.Instance.DecodeServer("{\"Type\" 2}"), "コロンが無い");
            });

            T.Section("codec/oracle");

            T.Test("WireCodec の出力を System.Text.Json が読める", () =>
            {
                // 手書き実装が標準的な JSON の形から外れていないことの確認。
                var original = SampleServerMessage();
                string json = WireCodec.EncodeServer(original);

                var viaOracle = JsonSerializer.Deserialize<ServerMessage>(json, JsonCodec.Options);

                T.Eq(original.Seat, viaOracle.Seat, "席");
                T.Eq(original.TurnDeadlineMs, viaOracle.TurnDeadlineMs, "残り時間");
                T.Eq(original.View.Players.Length, viaOracle.View.Players.Length, "プレイヤー数");
                T.Eq(original.View.Players[3].Name, viaOracle.View.Players[3].Name, "日本語の名前");
                T.Eq(original.View.Phase, viaOracle.View.Phase, "フェーズ");
                T.Eq(original.View.MyStack.Length, viaOracle.View.MyStack.Length, "自分の山");
                T.Eq(original.Events.Length, viaOracle.Events.Length, "イベント数");
                T.Eq(original.Room.Seats[1].PersonalityId, viaOracle.Room.Seats[1].PersonalityId, "性格 ID");
            });

            T.Test("System.Text.Json の出力を WireCodec が読める", () =>
            {
                var original = SampleServerMessage();
                string json = JsonSerializer.Serialize(original, JsonCodec.Options);

                var restored = WireCodec.Instance.DecodeServer(json);

                T.Eq(original.Seat, restored.Seat, "席");
                T.Eq(original.View.Players.Length, restored.View.Players.Length, "プレイヤー数");
                T.Eq(original.View.Players[1].Name, restored.View.Players[1].Name, "日本語の名前");
                T.Eq(original.View.HighestBid, restored.View.HighestBid, "宣言");
                T.Eq(original.View.LegalActions.Length, restored.View.LegalActions.Length, "合法手");
                T.Eq(original.Events.Length, restored.Events.Length, "イベント数");
            });

            T.Test("ClientMessage も両方向で相互運用できる", () =>
            {
                var original = new ClientMessage
                {
                    Kind = ClientMessageType.Action,
                    RoomCode = "QQ11",
                    Action = GameAction.Flip(1, 3),
                };

                // 手書き → 標準
                var viaOracle = JsonSerializer.Deserialize<ClientMessage>(
                    WireCodec.Instance.Encode(original), JsonCodec.Options);
                T.Eq(original.Action, viaOracle.Action, "手書きの出力を標準が読める");

                // 標準 → 手書き
                var viaWire = WireCodec.DecodeClient(JsonSerializer.Serialize(original, JsonCodec.Options));
                T.Eq(original.Action, viaWire.Action, "標準の出力を手書きが読める");
                T.Eq("QQ11", viaWire.RoomCode, "ルームコード");
            });

            T.Test("秘匿処理済みの View を JSON にしても他人の伏せカードが現れない", () =>
            {
                // 手書きコーデックに切り替えたことで漏れが生じていないかの確認。
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var events = new List<GameEvent>();
                var rng = new XorShiftRng(77);
                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Skull), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Crown), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(2, CardType.Rose), rng, events);

                var message = new ServerMessage
                {
                    Kind = ServerMessageType.State,
                    Seat = 2,
                    View = ViewRedactor.Redact(state, 2),
                    Events = EventRedactor.Redact(events, 2),
                };

                var restored = WireCodec.Instance.DecodeServer(WireCodec.EncodeServer(message));

                T.Eq(1, restored.View.MyStack.Length, "自分の山だけ見える");
                T.Eq((int)CardType.Rose, restored.View.MyStack[0], "自分が置いたバラ");
                for (int i = 0; i < restored.View.Players.Length; i++)
                {
                    T.Eq(0, restored.View.Players[i].RevealedFromTop.Length, "公開カードは無い (seat " + i + ")");
                }
                foreach (var e in restored.Events)
                {
                    if (e.Kind != GameEventKind.CardPlaced) continue;
                    if (e.Player == 2) T.True(e.HasCard, "自分の置いたカードは見える");
                    else T.False(e.HasCard, "他人の置いたカードは見えない");
                }
            });
        }
    }
}
