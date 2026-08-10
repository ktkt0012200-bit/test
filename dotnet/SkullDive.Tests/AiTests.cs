using System.Collections.Generic;
using SkullDive.Ai;
using SkullDive.Core;

namespace SkullDive.Tests
{
    public static class AiTests
    {
        private static readonly List<GameEvent> Sink = new List<GameEvent>();

        private static void Do(GameState state, GameAction action)
        {
            GameEngine.Apply(state, action, new XorShiftRng(1), Sink);
        }

        public static void Register()
        {
            T.Section("ai/judgement");

            T.Test("自分の山にスカルがあるなら絶対に宣言しない", () =>
            {
                // 自分の山を全部めくる義務があるので、スカル入りで宣言すると必ず失敗する。
                foreach (var personality in AiPersonality.All())
                {
                    var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                    Do(state, GameAction.PlaceCard(0, CardType.Rose));
                    Do(state, GameAction.PlaceCard(1, CardType.Skull));
                    Do(state, GameAction.PlaceCard(2, CardType.Rose));
                    Do(state, GameAction.Bid(0, 1));

                    T.Eq(Phase.Bidding, state.Phase, "宣言フェーズ");
                    T.Eq(1, state.CurrentPlayer, "スカルを置いた席の手番");

                    var brain = new AiBrain(personality);
                    for (int seed = 0; seed < 30; seed++)
                    {
                        var action = brain.Decide(ViewRedactor.Redact(state, 1), new XorShiftRng(seed + 1));
                        T.Eq(ActionKind.Pass, action.Kind, personality.Id + " は降りるべき");
                    }
                }
            });

            T.Test("めくる相手は最も安全な山を選ぶ", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(2, CardType.Rose));
                Do(state, GameAction.PlaceCard(0, CardType.Rose)); // 0 の山は 2 枚
                Do(state, GameAction.PlaceCard(1, CardType.Rose)); // 1 の山も 2 枚
                Do(state, GameAction.Bid(2, 1));
                Do(state, GameAction.Bid(0, 5)); // 場の全枚数なので即チャレンジ
                Do(state, GameAction.Flip(0, 0));
                Do(state, GameAction.Flip(0, 0));
                Do(state, GameAction.Flip(0, 1)); // 1 の山からバラが 1 枚公開された

                // 1 は 4 枚所持で 1 枚公開済みなので危険度 1/3、2 は未公開なので 1/4。
                // 安全な 2 を選ぶべき。
                var view = ViewRedactor.Redact(state, 0);
                T.True(Odds.SafeChance(view, 2) > Odds.SafeChance(view, 1), "前提: 2 の方が安全");

                var brain = new AiBrain(AiPersonality.Steady());
                for (int seed = 0; seed < 20; seed++)
                {
                    var action = brain.Decide(view, new XorShiftRng(seed + 1));
                    T.Eq(2, action.Target, "安全側の山をめくる");
                }
            });

            T.Test("同じ入力と同じシードなら同じ判断になる", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 4);
                var view = ViewRedactor.Redact(state, 0);
                var brain = new AiBrain(AiPersonality.Gambler());

                for (int seed = 0; seed < 20; seed++)
                {
                    var first = brain.Decide(view, new XorShiftRng(seed + 1));
                    var second = brain.Decide(view, new XorShiftRng(seed + 1));
                    T.Eq(first, second, "seed " + seed + " の再現性");
                }
            });

            T.Section("ai/personality");

            T.Test("性格ごとにスカルを置くタイミングの癖が出る", () =>
            {
                // ソロプレイの読み合いはこの差が成立していることに依存している。
                double timid = SkullFirstRate(AiPersonality.Timid());
                double gambler = SkullFirstRate(AiPersonality.Gambler());

                T.True(timid < 0.20, "臆病者は 1 枚目にスカルを置かない (" + timid.ToString("0.00") + ")");
                T.True(gambler > 0.35, "博打屋は 1 枚目にスカルを置く (" + gambler.ToString("0.00") + ")");
                T.True(gambler - timid > 0.3, "差がプレイヤーに読める大きさである");
            });

            T.Test("真似っ子は直前の宣言に +1 で張り付く", () =>
            {
                int plusOne = 0;
                int bids = 0;
                var brain = new AiBrain(AiPersonality.Copycat());

                for (int seed = 0; seed < 200; seed++)
                {
                    var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                    Do(state, GameAction.PlaceCard(0, CardType.Rose));
                    Do(state, GameAction.PlaceCard(1, CardType.Crown));
                    Do(state, GameAction.PlaceCard(2, CardType.Rose));
                    Do(state, GameAction.Bid(0, 1));

                    var action = brain.Decide(ViewRedactor.Redact(state, 1), new XorShiftRng(seed + 1));
                    if (action.Kind != ActionKind.Bid) continue;
                    bids++;
                    if (action.Amount == state.HighestBid + 1) plusOne++;
                }

                T.True(bids > 100, "宣言する場面が十分ある (" + bids + ")");
                T.True(plusOne / (double)bids > 0.7, "大半が +1 (" + plusOne + "/" + bids + ")");
            });

            T.Test("失うカードの選び方が性格で分かれる", () =>
            {
                // 臆病者はスカルを手放し、博打屋は脅しを残す。
                T.Eq(CardType.Skull, DiscardChoice(AiPersonality.Timid()), "臆病者はスカルを捨てる");
                T.Eq(CardType.Rose, DiscardChoice(AiPersonality.Gambler()), "博打屋はバラを捨ててスカルを残す");
            });

            T.Section("ai/balance");

            T.Test("性格が違えば勝率も分かれるが、一方的にはならない", () =>
            {
                var stats = Simulator.Run(GameConfig.Default(), AiPersonality.All(), 1200, 20250810);

                T.Eq(1200, stats.Matches, "マッチ数");
                foreach (var personality in AiPersonality.All())
                {
                    double rate = stats.WinRateOf(personality.Id);
                    // 4 人卓なので期待値は 25%。調整済みの実測は 17〜36% に収まる。
                    // ここを外れたらパラメータ調整が壊れたということなので、
                    // CLI の --sim / --compare で数字を見直す。
                    T.True(rate > 0.12 && rate < 0.45,
                        personality.Id + " の勝率が調整範囲を外れた (" + rate.ToString("0.000") + ")");
                }

                // 席順の有利不利は残っていてよいが、性格差より小さいこと。
                double first = stats.WinsBySeat[0] / (double)stats.Matches;
                T.True(first < 0.33, "先手番が強すぎる (" + first.ToString("0.000") + ")");
            });

            T.Test("チャレンジ成功率が極端でない", () =>
            {
                // 成功率が高すぎると緊張感が無く、低すぎると理不尽になる。
                var stats = Simulator.Run(GameConfig.Default(), AiPersonality.All(), 1200, 4242);
                T.True(stats.SuccessRate > 0.55 && stats.SuccessRate < 0.85,
                    "成功率 " + stats.SuccessRate.ToString("0.000"));
                T.True(stats.AverageFlipsPerChallenge > 2.0,
                    "1 チャレンジで複数枚めくる (" + stats.AverageFlipsPerChallenge.ToString("0.00") + ")");
                T.True(stats.AverageRoundsPerMatch > 1.5,
                    "1 マッチに複数ラウンドある (" + stats.AverageRoundsPerMatch.ToString("0.0") + ")");
            });
        }

        /// <summary>ラウンド 1 枚目にスカルを置いた割合。</summary>
        private static double SkullFirstRate(AiPersonality personality)
        {
            var brain = new AiBrain(personality);
            int skulls = 0;
            const int samples = 400;

            for (int seed = 0; seed < samples; seed++)
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 4);
                var action = brain.Decide(ViewRedactor.Redact(state, 0), new XorShiftRng(seed + 1));
                if (action.Kind == ActionKind.PlaceCard && action.CardType == CardType.Skull) skulls++;
            }

            return skulls / (double)samples;
        }

        /// <summary>自分のスカルを踏んだときに捨てるカード。</summary>
        private static CardType DiscardChoice(AiPersonality personality)
        {
            var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
            Do(state, GameAction.PlaceCard(0, CardType.Skull));
            Do(state, GameAction.PlaceCard(1, CardType.Rose));
            Do(state, GameAction.Bid(0, 2));
            Do(state, GameAction.Flip(0, 0));

            T.Eq(Phase.AwaitingDiscard, state.Phase, "前提: 捨てるカードの選択待ち");

            var brain = new AiBrain(personality);
            var action = brain.Decide(ViewRedactor.Redact(state, 0), new XorShiftRng(9));
            T.Eq(ActionKind.Discard, action.Kind, "捨てる手を返す");
            return action.CardType;
        }
    }
}
