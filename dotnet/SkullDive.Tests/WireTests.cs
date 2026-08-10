using System.Collections.Generic;
using System.Text.Json;
using SkullDive.Core;
using SkullDive.Json;
using SkullDive.Net;

namespace SkullDive.Tests
{
    /// <summary>
    /// ワイヤーフォーマットのテスト。
    /// サーバは System.Text.Json、Unity は Newtonsoft.Json を使うため、
    /// 「public フィールドがそのまま出る」形を崩さないことが両者の互換性の条件になる。
    /// </summary>
    public static class WireTests
    {
        public static void Register()
        {
            T.Section("wire");

            T.Test("ServerMessage は往復しても内容が変わらない", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var events = new List<GameEvent>();
                var rng = new XorShiftRng(3);
                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Crown), rng, events);

                var original = new ServerMessage
                {
                    Kind = ServerMessageType.State,
                    Seat = 1,
                    View = ViewRedactor.Redact(state, 1, new[] { "A", "B", "C" }),
                    Events = EventRedactor.Redact(events, 1),
                    TurnDeadlineMs = 20000,
                };

                string json = JsonSerializer.Serialize(original, JsonCodec.Options);
                var restored = JsonSerializer.Deserialize<ServerMessage>(json, JsonCodec.Options);

                T.Eq(ServerMessageType.State, restored.Kind, "種別");
                T.Eq(1, restored.Seat, "席番号");
                T.Eq(20000, restored.TurnDeadlineMs, "残り時間");
                T.Eq(3, restored.View.Players.Length, "プレイヤー数");
                T.Eq("B", restored.View.Players[1].Name, "名前");
                T.Eq(1, restored.View.Players[0].StackCount, "相手の山の枚数");
                T.Eq(Phase.Placing, restored.View.Phase, "フェーズ");
                T.Eq(2, restored.View.Config.CrownValue, "設定も載る");
                T.Eq(events.Count, restored.Events.Length, "イベント数");
            });

            T.Test("System.Text.Json はフィールドを含める設定でなければ壊れる", () =>
            {
                // IncludeFields を忘れるとフィールドが丸ごと消える(プロパティだけが残る)。
                // Newtonsoft 側は既定でフィールドを書き出すため、この設定漏れは
                // 「サーバから送った内容がクライアントで空になる」形で表面化する。
                var message = new ClientMessage { Kind = ClientMessageType.Action, Name = "test" };

                string withoutFields = JsonSerializer.Serialize(message, new JsonSerializerOptions());
                T.False(withoutFields.Contains("\"Name\""), "IncludeFields なしではフィールドが落ちる: " + withoutFields);
                T.False(withoutFields.Contains("\"Type\""), "Type フィールドも落ちる: " + withoutFields);

                string withFields = JsonSerializer.Serialize(message, JsonCodec.Options);
                T.True(withFields.Contains("\"Name\":\"test\""), "IncludeFields ありなら載る: " + withFields);
                T.True(withFields.Contains("\"Type\":2"), "種別も載る: " + withFields);
            });

            T.Test("ClientMessage の GameAction が往復する", () =>
            {
                var original = new ClientMessage
                {
                    Kind = ClientMessageType.Action,
                    RoomCode = "ABCD",
                    Token = "tok",
                    Action = GameAction.Bid(2, 5),
                };

                string json = JsonSerializer.Serialize(original, JsonCodec.Options);
                var restored = JsonSerializer.Deserialize<ClientMessage>(json, JsonCodec.Options);

                T.Eq(ClientMessageType.Action, restored.Kind, "種別");
                T.Eq("ABCD", restored.RoomCode, "ルームコード");
                T.Eq(original.Action, restored.Action, "アクションが一致する");
            });

            T.Test("enum は数値として書き出される", () =>
            {
                // Newtonsoft.Json の既定も数値なので、文字列化するとクライアントと合わなくなる。
                string json = JsonSerializer.Serialize(GameAction.Pass(1), JsonCodec.Options);
                T.True(json.Contains("\"Kind\":2"), "ActionKind.Pass は 2 として出る: " + json);
            });

            T.Test("秘匿処理済みの View に他人の伏せカードが現れない", () =>
            {
                // JSON 化した結果そのものを検査する。View の構造が変わっても漏れを検出できる。
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                var events = new List<GameEvent>();
                var rng = new XorShiftRng(5);
                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Skull), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Rose), rng, events);

                var view = ViewRedactor.Redact(state, 1);
                string json = JsonSerializer.Serialize(view, JsonCodec.Options);

                // 席 1 の View なので MyStack は [Rose]。席 0 のスカルはどこにも入っていないはず。
                T.Eq(1, view.MyStack.Length, "自分の山だけ");
                T.Eq((int)CardType.Rose, view.MyStack[0], "自分が置いたバラ");
                T.False(json.Contains("\"RevealedFromTop\":[2]"), "スカルが公開扱いになっていない");
                T.Eq(0, view.Players[0].RevealedFromTop.Length, "相手の公開カードは無い");
            });
        }
    }

}
