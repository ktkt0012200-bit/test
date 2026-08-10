using System;
using System.Collections.Generic;
using SkullDive.Ai;
using SkullDive.Core;

namespace SkullDive.Tests
{
    /// <summary>AI 同士の大量自動対戦で、ルールの不変条件が崩れないことを確かめる。</summary>
    public static class SoakTests
    {
        /// <summary>1 手ごとに呼ばれる不変条件チェック。</summary>
        private static Action<GameState> Invariants()
        {
            int previousTotalCards = int.MaxValue;
            GameState tracked = null;

            return state =>
            {
                // Simulator は 1 回の Run で複数マッチを回す。盤面が入れ替わったら追跡をやり直す。
                if (!ReferenceEquals(tracked, state))
                {
                    tracked = state;
                    previousTotalCards = int.MaxValue;
                }

                int totalCards = 0;
                int active = 0;

                for (int i = 0; i < state.PlayerCount; i++)
                {
                    var player = state.Players[i];
                    totalCards += player.CardsOwned;
                    if (player.IsActive) active++;

                    if (player.FlippedCount > player.Stack.Count)
                        throw new AssertException("flipped more cards than the stack holds (player " + i + ")");
                    if (player.CardsOwned > state.Config.HandSize)
                        throw new AssertException("player " + i + " owns more cards than the starting hand");
                    if (player.Eliminated != (player.CardsOwned == 0))
                        throw new AssertException("elimination flag disagrees with card count (player " + i + ")");

                    // 宣言はチャレンジ解決後には過去の記録になる(失敗すると場のカードが 1 枚減るため
                    // 当時の宣言枚数が現在の場の枚数を上回りうる)。宣言中だけ検証する。
                    if (Bidding(state) && player.CurrentBid > state.TotalOnTable)
                        throw new AssertException("bid exceeds the cards on the table (player " + i + ")");
                }

                // カードは永久に失われるだけで、増えることはない。
                if (totalCards > previousTotalCards)
                    throw new AssertException("total card count increased from " + previousTotalCards + " to " + totalCards);
                previousTotalCards = totalCards;

                if (Bidding(state) && state.HighestBid > state.TotalOnTable)
                    throw new AssertException("highest bid " + state.HighestBid + " exceeds table " + state.TotalOnTable);

                if (state.Phase == Phase.MatchEnd)
                {
                    if (state.Winner < 0) throw new AssertException("match ended without a winner");
                    return;
                }

                if (active < 1) throw new AssertException("no active players but the match is not over");
                if (state.Winner >= 0) throw new AssertException("winner set before the match ended");

                // 進行が止まらないこと: 手番の主体には必ず手がある。
                int actor = GameEngine.CurrentActor(state);
                if (GameEngine.GetLegalActions(state, actor).Count == 0)
                    throw new AssertException("no legal actions for actor " + actor + " in phase " + state.Phase);

                if (state.Phase == Phase.Challenging || state.Phase == Phase.AwaitingDiscard)
                {
                    if (state.Challenger < 0) throw new AssertException("challenge without a challenger");
                }
            };
        }

        /// <summary>宣言枚数がまだ「現在の値」として意味を持つフェーズか。</summary>
        private static bool Bidding(GameState state)
        {
            return state.Phase == Phase.Placing || state.Phase == Phase.Bidding;
        }

        public static void Register()
        {
            T.Section("soak");

            T.Test("2〜6 人・クラウンあり/なしで 1200 マッチ完走し、不変条件を保つ", () =>
            {
                var configs = new[] { GameConfig.Default(), GameConfig.Classic() };
                int totalMatches = 0;

                foreach (var config in configs)
                {
                    for (int seats = 2; seats <= 6; seats++)
                    {
                        var table = BuildTable(seats);
                        var stats = Simulator.Run(config, table, 120, 1000 + seats, Invariants());

                        T.Eq(120, stats.Matches, seats + " 人卓のマッチ数");
                        T.True(stats.Challenges > 0, "チャレンジが発生している");
                        totalMatches += stats.Matches;
                    }
                }

                T.Eq(1200, totalMatches, "総マッチ数");
            });

            T.Test("合法手として返された手はすべて適用できる", () =>
            {
                // 合法手集合と Apply の受け入れ判定がずれていないことの確認。
                var rng = new XorShiftRng(4242);
                var events = new List<GameEvent>();
                int checkedActions = 0;

                for (int match = 0; match < 40; match++)
                {
                    var state = GameEngine.CreateMatch(GameConfig.Default(), 4, match % 4);
                    int steps = 0;

                    while (!state.IsOver && steps++ < 4000)
                    {
                        int actor = GameEngine.CurrentActor(state);
                        var legal = GameEngine.GetLegalActions(state, actor);
                        T.True(legal.Count > 0, "手がある");

                        // すべての合法手を複製した盤面に適用してみる。
                        foreach (var candidate in legal)
                        {
                            var clone = state.Clone();
                            var scratch = new List<GameEvent>();
                            GameEngine.Apply(clone, candidate, new XorShiftRng(steps + 1), scratch);
                            checkedActions++;
                        }

                        events.Clear();
                        GameEngine.Apply(state, legal[rng.NextInt(legal.Count)], rng, events);
                    }

                    T.True(state.IsOver, "ランダムプレイでも決着する");
                }

                T.True(checkedActions > 1000, "十分な数の手を検証した (" + checkedActions + ")");
            });

            T.Test("Clone は元の盤面から独立している", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                var clone = state.Clone();
                var events = new List<GameEvent>();

                GameEngine.Apply(clone, GameAction.PlaceCard(0, CardType.Skull), new XorShiftRng(1), events);

                T.Eq(0, state.TotalOnTable, "元の盤面は変わらない");
                T.Eq(1, clone.TotalOnTable, "複製だけが進む");
                T.Eq(4, state.Players[0].Hand.Count, "元の手札も変わらない");
            });

            T.Section("determinism");

            T.Test("同じシードなら完全に同じ結果になる", () =>
            {
                for (int seed = 0; seed < 25; seed++)
                {
                    var first = Fingerprint(seed);
                    var second = Fingerprint(seed);
                    T.Eq(first, second, "seed " + seed + " の再現性");
                }
            });

            T.Test("シードが違えば結果も分岐する", () =>
            {
                var seen = new HashSet<string>();
                for (int seed = 0; seed < 40; seed++) seen.Add(Fingerprint(seed));

                // 完全一致するはずがない。1 種類しか出ないなら乱数が効いていない。
                T.True(seen.Count > 5, "シードごとに結果が変わる (" + seen.Count + " 種類)");
            });

            T.Test("シードを撹拌しないと 1 発目の乱数が偏る(回帰テスト)", () =>
            {
                // シードをそのまま xorshift の状態にすると、小さいシードでは
                // NextDouble() の 1 発目が 0 付近に張り付く。
                // その結果 SkullEarly=0.05 のような低確率判定が常に真になり、
                // AI の性格差が消えてしまっていた。
                double sum = 0;
                int belowFivePercent = 0;
                const int samples = 4000;

                for (int seed = 1; seed <= samples; seed++)
                {
                    double first = new XorShiftRng(seed).NextDouble();
                    sum += first;
                    if (first < 0.05) belowFivePercent++;
                }

                double mean = sum / samples;
                double rate = belowFivePercent / (double)samples;

                T.True(mean > 0.45 && mean < 0.55, "1 発目の平均が 0.5 付近 (" + mean.ToString("0.000") + ")");
                T.True(rate > 0.03 && rate < 0.08,
                    "1 発目が 5% 判定を通す割合は約 5% (" + rate.ToString("0.000") + ")");
            });

            T.Test("XorShiftRng の分布に大きな偏りがない", () =>
            {
                var rng = new XorShiftRng(99);
                var buckets = new int[6];
                const int rolls = 60000;
                for (int i = 0; i < rolls; i++) buckets[rng.NextInt(6)]++;

                foreach (var count in buckets)
                {
                    // 期待値 10000。±8% に収まっていれば実用上問題ない。
                    T.True(count > rolls / 6 * 0.92 && count < rolls / 6 * 1.08,
                        "bucket count " + count + " should sit near " + (rolls / 6));
                }
            });
        }

        /// <summary>1 マッチの進行をすべて文字列化したもの。再現性の比較に使う。</summary>
        private static string Fingerprint(int seed)
        {
            var table = BuildTable(4);
            var brains = new AiBrain[table.Length];
            for (int i = 0; i < table.Length; i++) brains[i] = new AiBrain(table[i]);

            var rng = new XorShiftRng(seed);
            var state = GameEngine.CreateMatch(GameConfig.Default(), table.Length, seed % table.Length);
            var events = new List<GameEvent>();
            var log = new System.Text.StringBuilder();

            int steps = 0;
            while (!state.IsOver && steps++ < 20000)
            {
                events.Clear();
                if (state.Phase == Phase.RoundEnd)
                {
                    GameEngine.Apply(state, GameAction.AdvanceRound(), rng, events);
                }
                else
                {
                    int actor = GameEngine.CurrentActor(state);
                    var action = brains[actor].Decide(ViewRedactor.Redact(state, actor), rng);
                    log.Append(action).Append(';');
                    GameEngine.Apply(state, action, rng, events);
                }
                foreach (var e in events) log.Append(e.Kind).Append(',').Append(e.Card).Append('|');
            }

            log.Append("winner=").Append(state.Winner);
            return log.ToString();
        }

        private static AiPersonality[] BuildTable(int seats)
        {
            var all = AiPersonality.All();
            var table = new AiPersonality[seats];
            for (int i = 0; i < seats; i++) table[i] = all[i % all.Length];
            return table;
        }
    }
}
