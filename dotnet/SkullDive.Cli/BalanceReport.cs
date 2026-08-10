using System;
using SkullDive.Ai;
using SkullDive.Core;

namespace SkullDive.Cli
{
    /// <summary>バランス統計の出力。クラウンの値を決めるための判断材料。</summary>
    public static class BalanceReport
    {
        public static void Simulate(Options options)
        {
            var config = options.BuildConfig();
            var table = BuildTable(options.Players);

            Console.WriteLine("=== balance simulation ===");
            Console.WriteLine(Describe(config, options.Players, options.Matches));
            Console.WriteLine();

            var started = DateTime.UtcNow;
            var stats = Simulator.Run(config, table, options.Matches, options.Seed);
            var elapsed = DateTime.UtcNow - started;

            PrintStats(stats, table);
            Console.WriteLine();
            Console.WriteLine("elapsed: " + elapsed.TotalSeconds.ToString("0.00") + "s ("
                + (options.Matches / Math.Max(0.001, elapsed.TotalSeconds)).ToString("0") + " matches/sec)");
        }

        public static void Compare(Options options)
        {
            var table = BuildTable(options.Players);

            var variants = new[]
            {
                new { Label = "原作ルール (バラ3 + スカル1)", Config = GameConfig.Classic() },
                new { Label = "クラウン value=2", Config = Crown(2, false) },
                new { Label = "クラウン value=2 (自分の山は1枚分)", Config = Crown(2, true) },
                new { Label = "クラウン value=3", Config = Crown(3, false) },
            };

            Console.WriteLine("=== rule comparison ===");
            Console.WriteLine(options.Players + " 人卓 / " + options.Matches + " マッチ / seed " + options.Seed);
            Console.WriteLine();

            Console.WriteLine(Text.Pad("variant", 34) + Text.Pad("成功率", 9) + Text.Pad("平均宣言", 10)
                + Text.Pad("平均めくり", 11) + Text.Pad("R/match", 9) + Text.Pad("C/match", 9) + "自スカル失敗率");
            Console.WriteLine(new string('-', 100));

            foreach (var variant in variants)
            {
                var stats = Simulator.Run(variant.Config, table, options.Matches, options.Seed);
                int failures = stats.OwnSkullFailures + stats.OtherSkullFailures;
                double ownShare = failures == 0 ? 0 : stats.OwnSkullFailures / (double)failures;

                Console.WriteLine(
                    Text.Pad(variant.Label, 34)
                    + Text.Pad(Text.Percent(stats.SuccessRate), 9)
                    + Text.Pad(stats.AverageBid.ToString("0.00"), 10)
                    + Text.Pad(stats.AverageFlipsPerChallenge.ToString("0.00"), 11)
                    + Text.Pad(stats.AverageRoundsPerMatch.ToString("0.0"), 9)
                    + Text.Pad(stats.AverageChallengesPerMatch.ToString("0.0"), 9)
                    + Text.Percent(ownShare));
            }

            Console.WriteLine();
            Console.WriteLine("成功率が高すぎると緊張感が消え、低すぎると理不尽になる。");
            Console.WriteLine("平均めくり枚数が多いほどリビール演出が長く続き、1 チャレンジの見せ場が増える。");
        }

        private static GameConfig Crown(int value, bool oneInOwnStack)
        {
            var config = GameConfig.Default();
            config.CrownValue = value;
            config.CrownCountsAsOneInOwnStack = oneInOwnStack;
            return config;
        }

        private static void PrintStats(SimulationStats stats, AiPersonality[] table)
        {
            int failures = stats.OwnSkullFailures + stats.OtherSkullFailures;

            Console.WriteLine("matches                 " + stats.Matches);
            Console.WriteLine("rounds / match          " + stats.AverageRoundsPerMatch.ToString("0.00"));
            Console.WriteLine("challenges / match      " + stats.AverageChallengesPerMatch.ToString("0.00"));
            Console.WriteLine("challenge success rate  " + Text.Percent(stats.SuccessRate));
            Console.WriteLine("average bid             " + stats.AverageBid.ToString("0.00"));
            Console.WriteLine("average flips/challenge " + stats.AverageFlipsPerChallenge.ToString("0.00"));
            Console.WriteLine("failures (own skull)    " + stats.OwnSkullFailures
                + (failures == 0 ? "" : "  (" + Text.Percent(stats.OwnSkullFailures / (double)failures) + " of failures)"));
            Console.WriteLine("failures (other skull)  " + stats.OtherSkullFailures);
            Console.WriteLine("eliminations            " + stats.Eliminations);
            Console.WriteLine("one-card instant wins   " + stats.OneCardWins);
            Console.WriteLine();
            Console.WriteLine("revealed cards          rose " + stats.RosesRevealed
                + " / crown " + stats.CrownsRevealed + " / skull " + stats.SkullsRevealed);
            Console.WriteLine();

            Console.WriteLine("win rate by personality");
            foreach (var personality in Distinct(table))
            {
                Console.WriteLine("  " + Text.Pad(personality.Name + " (" + personality.Id + ")", 26)
                    + Text.Percent(stats.WinRateOf(personality.Id)));
            }

            Console.WriteLine();
            Console.WriteLine("win rate by seat (先手番の有利不利)");
            for (int seat = 0; seat < stats.WinsBySeat.Length; seat++)
            {
                Console.WriteLine("  seat " + seat + "  " + Text.Percent(stats.WinsBySeat[seat] / (double)stats.Matches));
            }
        }

        private static AiPersonality[] Distinct(AiPersonality[] table)
        {
            var seen = new System.Collections.Generic.List<AiPersonality>();
            foreach (var personality in table)
            {
                bool duplicate = false;
                foreach (var known in seen)
                {
                    if (known.Id == personality.Id) duplicate = true;
                }
                if (!duplicate) seen.Add(personality);
            }
            return seen.ToArray();
        }

        public static AiPersonality[] BuildTable(int seats)
        {
            var all = AiPersonality.All();
            var table = new AiPersonality[seats];
            for (int i = 0; i < seats; i++) table[i] = all[i % all.Length];
            return table;
        }

        private static string Describe(GameConfig config, int players, int matches)
        {
            return players + " 人卓 / " + matches + " マッチ / 手札 "
                + config.RoseCount + "R+" + config.CrownCount + "C+" + config.SkullCount + "S"
                + " / crown=" + config.CrownValue
                + (config.CrownCountsAsOneInOwnStack ? " (own=1)" : "")
                + " / " + config.PointsToWin + " points to win";
        }

    }
}
