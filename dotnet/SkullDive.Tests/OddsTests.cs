using SkullDive.Core;

namespace SkullDive.Tests
{
    public static class OddsTests
    {
        private static PlayerPublicView Player(int owned, int stack, int flipped, params CardType[] revealed)
        {
            var revealedInts = new int[revealed.Length];
            for (int i = 0; i < revealed.Length; i++) revealedInts[i] = (int)revealed[i];
            return new PlayerPublicView
            {
                Index = 1,
                CardsOwned = owned,
                StackCount = stack,
                FlippedCount = flipped,
                RevealedFromTop = revealedInts,
            };
        }

        public static void Register()
        {
            T.Section("odds");

            T.Test("未公開カード 1 枚がスカルである確率は 1/所持枚数", () =>
            {
                var config = GameConfig.Default();
                // 4 枚所持・1 枚だけ場に伏せている。スカルが手札に残っている可能性も込みで 1/4。
                T.Near(0.25, Odds.TopCardSkullChance(Player(4, 1, 0), config), 1e-9, "4 枚所持");
            });

            T.Test("安全なカードが公開されるほど残りの危険度が上がる", () =>
            {
                var config = GameConfig.Default();
                // 4 枚所持・3 枚を場に出し 1 枚公開済み(バラ)。残り未公開は 3 枚なので 1/3。
                T.Near(1.0 / 3.0, Odds.TopCardSkullChance(Player(4, 3, 1, CardType.Rose), config), 1e-9,
                    "1 枚公開後");
                // 2 枚公開済みなら残り 2 枚で 1/2。
                T.Near(0.5, Odds.TopCardSkullChance(Player(4, 3, 2, CardType.Rose, CardType.Crown), config), 1e-9,
                    "2 枚公開後");
            });

            T.Test("所持 1 枚を場に出しているなら、それがスカルである確率は所持枚数分の 1", () =>
            {
                var config = GameConfig.Default();
                T.Near(1.0, Odds.TopCardSkullChance(Player(1, 1, 0), config), 1e-9, "1 枚しか持っていない");
                T.Near(0.5, Odds.TopCardSkullChance(Player(2, 1, 0), config), 1e-9, "2 枚所持");
            });

            T.Test("スカルが公開済みなら残りは安全", () =>
            {
                var config = GameConfig.Default();
                T.Near(0.0, Odds.TopCardSkullChance(Player(4, 3, 1, CardType.Skull), config), 1e-9,
                    "スカルはもう出た");
            });

            T.Test("めくる対象が残っていなければ確率は 0", () =>
            {
                var config = GameConfig.Default();
                T.Near(0.0, Odds.TopCardSkullChance(Player(4, 2, 2, CardType.Rose, CardType.Rose), config), 1e-9,
                    "山は消化済み");
                T.Near(0.0, Odds.TopCardSkullChance(Player(4, 0, 0), config), 1e-9, "山が空");
            });

            T.Test("自分の山は中身を知っているので確定値になる", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                var events = new System.Collections.Generic.List<GameEvent>();
                var rng = new XorShiftRng(1);

                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Skull), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Rose), rng, events);

                var view = ViewRedactor.Redact(state, 0);
                T.Near(0.0, Odds.SafeChance(view, 0), 1e-9, "自分の山の上はスカルだと分かっている");
                T.Near(0.75, Odds.SafeChance(view, 1), 1e-9, "相手は 1/4 で危険");
            });

            T.Test("成功確率の見積りは宣言枚数に対して単調に下がる", () =>
            {
                // AI の宣言選択は「受け入れ可能な範囲が連続している」ことを前提にしているので、
                // 単調性が崩れると宣言の決め方が壊れる。
                var state = GameEngine.CreateMatch(GameConfig.Default(), 4);
                var events = new System.Collections.Generic.List<GameEvent>();
                var rng = new XorShiftRng(11);
                for (int seat = 0; seat < 4; seat++)
                {
                    GameEngine.Apply(state, GameAction.PlaceCard(seat, CardType.Rose), rng, events);
                }

                var view = ViewRedactor.Redact(state, 0);
                double previous = 1.0;
                for (int target = 1; target <= view.TotalOnTable; target++)
                {
                    double survival = Odds.SurvivalForValue(view, target);
                    T.True(survival <= previous + 1e-9,
                        "宣言 " + target + " の成功確率が前より上がっている (" + survival + " > " + previous + ")");
                    previous = survival;
                }
            });

            T.Test("自分の山だけで達成できる宣言は確定成功", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var events = new System.Collections.Generic.List<GameEvent>();
                var rng = new XorShiftRng(13);
                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Crown), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Rose), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(2, CardType.Rose), rng, events);

                var view = ViewRedactor.Redact(state, 0);
                // クラウン 1 枚 = 2 カウントなので、宣言 2 までは自分の山だけで確定。
                T.Near(1.0, Odds.SurvivalForValue(view, 1), 1e-9, "宣言 1");
                T.Near(1.0, Odds.SurvivalForValue(view, 2), 1e-9, "宣言 2");
                T.True(Odds.SurvivalForValue(view, 3) < 1.0, "宣言 3 は他人の山が必要になる");
            });

            T.Test("自分の山にスカルがあれば成功確率は 0", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var events = new System.Collections.Generic.List<GameEvent>();
                var rng = new XorShiftRng(17);
                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Skull), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Rose), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(2, CardType.Rose), rng, events);

                var view = ViewRedactor.Redact(state, 0);
                for (int target = 1; target <= view.TotalOnTable; target++)
                {
                    T.Near(0.0, Odds.SurvivalForValue(view, target), 1e-9, "宣言 " + target);
                }
            });

            T.Test("自分の山にスカルがあると山の価値は -1 になる", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                var events = new System.Collections.Generic.List<GameEvent>();
                var rng = new XorShiftRng(1);

                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Crown), rng, events);
                GameEngine.Apply(state, GameAction.PlaceCard(1, CardType.Rose), rng, events);
                var safe = ViewRedactor.Redact(state, 0);
                T.Eq(2, safe.MyStackValue(), "クラウン 1 枚なら 2");

                GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Skull), rng, events);
                var risky = ViewRedactor.Redact(state, 0);
                T.Eq(-1, risky.MyStackValue(), "スカル入りは -1");
                T.True(risky.MyStackContainsSkull(), "スカルを検出できる");
            });
        }
    }
}
