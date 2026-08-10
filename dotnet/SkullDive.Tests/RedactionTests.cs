using System.Collections.Generic;
using System.Reflection;
using SkullDive.Core;

namespace SkullDive.Tests
{
    /// <summary>
    /// 秘匿情報が漏れないことのテスト。ブラフゲームではここが壊れると
    /// ゲームそのものが成立しなくなるので、他のどのテストより重要。
    /// </summary>
    public static class RedactionTests
    {
        private static readonly List<GameEvent> Sink = new List<GameEvent>();

        private static void Do(GameState state, GameAction action)
        {
            GameEngine.Apply(state, action, new XorShiftRng(1), Sink);
        }

        public static void Register()
        {
            T.Section("redaction/view");

            T.Test("PlayerPublicView は枚数と公開済みカードしか持たない", () =>
            {
                // 将来 PlayerPublicView にフィールドを足したとき、それが秘匿情報を運んでいないか
                // 必ず考えさせるためのスキーマガード。増やすなら意図的にここを更新する。
                var allowed = new HashSet<string>
                {
                    "Index", "Name", "HandCount", "StackCount", "FlippedCount",
                    "RevealedFromTop", "Points", "Eliminated", "HasPassed", "CurrentBid", "CardsOwned",
                };

                foreach (var field in typeof(PlayerPublicView).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    T.True(allowed.Contains(field.Name),
                        "PlayerPublicView の新しいフィールド '" + field.Name + "' が秘匿情報を漏らしていないか確認し、"
                        + "問題なければこのテストの許可リストに追加すること");
                }
            });

            T.Test("自分の手札と山は自分の View にだけ入る", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Skull));
                Do(state, GameAction.PlaceCard(1, CardType.Crown));
                Do(state, GameAction.PlaceCard(2, CardType.Rose));

                var mine = ViewRedactor.Redact(state, 0);
                T.Eq(3, mine.MyHand.Length, "自分の手札は見える");
                T.Eq(1, mine.MyStack.Length, "自分の山は見える");
                T.Eq((int)CardType.Skull, mine.MyStack[0], "自分が置いたカードは分かる");

                // 他プレイヤーについては枚数だけ。
                T.Eq(1, mine.Players[1].StackCount, "相手の山は枚数だけ");
                T.Eq(0, mine.Players[1].RevealedFromTop.Length, "未公開なので中身はゼロ件");
                T.Eq(3, mine.Players[1].HandCount, "相手の手札も枚数だけ");
            });

            T.Test("観戦者の View には誰の伏せカードも入らない", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Skull));

                var spectator = ViewRedactor.Redact(state, -1);
                T.Eq(0, spectator.MyHand.Length, "手札なし");
                T.Eq(0, spectator.MyStack.Length, "山なし");
                T.Eq(0, spectator.LegalActions.Length, "手もなし");
                foreach (var player in spectator.Players)
                {
                    T.Eq(0, player.RevealedFromTop.Length, "公開済みカードはまだ無い");
                }
            });

            T.Test("公開済みカードは山の上から順に全員へ見える", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(0, CardType.Crown)); // 0 の山は [Rose, Crown]、上は Crown
                Do(state, GameAction.Bid(1, 1));
                Do(state, GameAction.Bid(0, 3)); // 場の全枚数なので即チャレンジ
                Do(state, GameAction.Flip(0, 0));

                var opponentView = ViewRedactor.Redact(state, 1);
                T.Eq(1, opponentView.Players[0].RevealedFromTop.Length, "1 枚公開された");
                T.Eq((int)CardType.Crown, opponentView.Players[0].RevealedFromTop[0], "上から順に並ぶ");
                T.Eq(1, opponentView.Players[0].FlippedCount, "公開枚数");
            });

            T.Test("手番でないプレイヤーの View に合法手は入らない", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var current = ViewRedactor.Redact(state, 0);
                var waiting = ViewRedactor.Redact(state, 1);

                T.True(current.LegalActions.Length > 0, "手番のプレイヤーには手がある");
                T.True(current.IsMyTurn, "IsMyTurn");
                T.Eq(0, waiting.LegalActions.Length, "手番でなければ空");
                T.False(waiting.IsMyTurn, "IsMyTurn は false");
            });

            T.Section("redaction/events");

            T.Test("伏せて置いたカードは本人以外には見えない", () =>
            {
                var placed = GameEvent.Make(GameEventKind.CardPlaced, player: 2, card: (int)CardType.Skull);

                T.Eq((int)CardType.Skull, EventRedactor.Redact(placed, 2).Card, "本人には見える");
                T.Eq(Cards.Hidden, EventRedactor.Redact(placed, 0).Card, "他人には伏せられる");
                T.Eq(Cards.Hidden, EventRedactor.Redact(placed, -1).Card, "観戦者にも伏せられる");
            });

            T.Test("伏せて捨てたカードも本人以外には見えない", () =>
            {
                var discarded = GameEvent.Make(GameEventKind.CardDiscarded, player: 1, card: (int)CardType.Skull);

                T.Eq((int)CardType.Skull, EventRedactor.Redact(discarded, 1).Card, "本人には見える");
                T.Eq(Cards.Hidden, EventRedactor.Redact(discarded, 0).Card, "他人には伏せられる");
            });

            T.Test("めくられたカードは全員に見える", () =>
            {
                var revealed = GameEvent.Make(GameEventKind.CardRevealed, player: 1, card: (int)CardType.Crown);

                T.Eq((int)CardType.Crown, EventRedactor.Redact(revealed, 0).Card, "他人にも見える");
                T.Eq((int)CardType.Crown, EventRedactor.Redact(revealed, -1).Card, "観戦者にも見える");
            });

            T.Test("SoloMatch は既定で秘匿処理済みのイベントを返す", () =>
            {
                // ソロでも他人の伏せカードを表示してはいけない。
                // UI 側の実装ミスで漏れないよう、既定を安全側にしていることを固定する。
                var match = new SkullDive.Ai.SoloMatch(
                    GameConfig.Default(), SkullDive.Ai.AiPersonality.DefaultTable(), 0, 4321);

                // 席 0 が開始プレイヤーなので、まず自分が置かないと AI は動かない。
                match.SubmitHumanAction(GameAction.PlaceCard(0, CardType.Rose));
                match.RunUntilHumanTurn();

                var events = match.TakeEvents();
                int hiddenPlacements = 0;
                foreach (var e in events)
                {
                    if (e.Kind != GameEventKind.CardPlaced) continue;
                    if (e.Player == 0) continue;
                    T.False(e.HasCard, "AI が伏せたカードは見えてはいけない");
                    hiddenPlacements++;
                }
                T.True(hiddenPlacements > 0, "AI の配置イベントが実際に含まれている");
            });

            T.Test("1 ラウンド分のイベント列に他人の伏せカードが含まれない", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var events = new List<GameEvent>();
                var rng = new XorShiftRng(7);

                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Skull), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Crown), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(2, CardType.Rose), rng, events);

                var forPlayer2 = EventRedactor.Redact(events, 2);
                foreach (var e in forPlayer2)
                {
                    if (e.Kind != GameEventKind.CardPlaced) continue;
                    if (e.Player == 2) T.True(e.HasCard, "自分の置いたカードは見える");
                    else T.False(e.HasCard, "他人が置いたカードは見えない");
                }
            });
        }
    }
}
