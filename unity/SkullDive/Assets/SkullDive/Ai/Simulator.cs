using System;
using System.Collections.Generic;
using SkullDive.Core;

namespace SkullDive.Ai
{
    /// <summary>バランス調整のための統計。</summary>
    public sealed class SimulationStats
    {
        public int Matches;
        public int Rounds;
        public int Challenges;
        public int Successes;
        public int OwnSkullFailures;
        public int OtherSkullFailures;
        public int TotalBid;
        public int TotalFlips;
        public int CrownsRevealed;
        public int RosesRevealed;
        public int SkullsRevealed;
        public int Eliminations;
        public int OneCardWins;
        public int[] WinsBySeat = new int[0];
        public readonly Dictionary<string, int> WinsByPersonality = new Dictionary<string, int>();
        public readonly Dictionary<string, int> MatchesByPersonality = new Dictionary<string, int>();

        public double SuccessRate
        {
            get { return Challenges == 0 ? 0 : Successes / (double)Challenges; }
        }

        public double AverageBid
        {
            get { return Challenges == 0 ? 0 : TotalBid / (double)Challenges; }
        }

        public double AverageFlipsPerChallenge
        {
            get { return Challenges == 0 ? 0 : TotalFlips / (double)Challenges; }
        }

        public double AverageRoundsPerMatch
        {
            get { return Matches == 0 ? 0 : Rounds / (double)Matches; }
        }

        public double AverageChallengesPerMatch
        {
            get { return Matches == 0 ? 0 : Challenges / (double)Matches; }
        }

        public double WinRateOf(string personalityId)
        {
            int played;
            if (!MatchesByPersonality.TryGetValue(personalityId, out played) || played == 0) return 0;
            int won;
            WinsByPersonality.TryGetValue(personalityId, out won);
            return won / (double)played;
        }
    }

    /// <summary>
    /// AI 同士に大量に対戦させてバランスを測る。
    /// クラウンの値などを変えたときに「何が起きるか」を数字で見るための道具で、
    /// テストからは不変条件チェッカーを差し込んで soak テストとしても使う。
    /// </summary>
    public static class Simulator
    {
        public const int MaxStepsPerMatch = 20000;

        /// <summary>
        /// matches 回の対戦を実行する。
        /// validate に渡した処理は 1 手ごとに呼ばれる(テスト用。null なら呼ばれない)。
        /// </summary>
        public static SimulationStats Run(GameConfig config, AiPersonality[] table, int matches, int seed,
            Action<GameState> validate = null)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (table == null) throw new ArgumentNullException("table");
            if (table.Length < GameEngine.MinPlayers) throw new ArgumentException("need at least 2 seats");

            var stats = new SimulationStats { WinsBySeat = new int[table.Length] };
            var brains = new AiBrain[table.Length];
            for (int i = 0; i < table.Length; i++)
            {
                Increment(stats.MatchesByPersonality, table[i].Id, matches);
            }

            var events = new List<GameEvent>();

            for (int match = 0; match < matches; match++)
            {
                var rng = new XorShiftRng(seed + match * 7919);

                // 性格と席の対応をマッチごとに回す。
                // 固定すると「性格の強さ」と「手番順の有利不利」が分離できず、
                // 席別勝率と性格別勝率がまったく同じ数字になってしまう。
                int rotation = match % table.Length;
                for (int seat = 0; seat < table.Length; seat++)
                {
                    brains[seat] = new AiBrain(table[(seat + rotation) % table.Length]);
                }

                events.Clear();
                var state = GameEngine.CreateMatch(config, table.Length, 0, events);
                Collect(stats, events, state);
                if (validate != null) validate(state);

                int steps = 0;
                while (!state.IsOver)
                {
                    if (++steps > MaxStepsPerMatch)
                        throw new InvalidOperationException("match did not terminate in " + MaxStepsPerMatch + " steps");

                    events.Clear();

                    if (state.Phase == Phase.RoundEnd)
                    {
                        GameEngine.Apply(state, GameAction.AdvanceRound(), rng, events);
                    }
                    else
                    {
                        int actor = GameEngine.CurrentActor(state);
                        var view = ViewRedactor.Redact(state, actor);
                        var action = brains[actor].Decide(view, rng);
                        GameEngine.Apply(state, action, rng, events);
                    }

                    Collect(stats, events, state);
                    if (validate != null) validate(state);
                }

                stats.Matches++;
                if (state.Winner >= 0)
                {
                    stats.WinsBySeat[state.Winner]++;
                    Increment(stats.WinsByPersonality, brains[state.Winner].Personality.Id, 1);
                }
            }

            return stats;
        }

        private static void Collect(SimulationStats stats, List<GameEvent> events, GameState state)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.Kind)
                {
                    case GameEventKind.RoundStarted:
                        stats.Rounds++;
                        break;
                    case GameEventKind.ChallengeStarted:
                        stats.Challenges++;
                        stats.TotalBid += e.Amount;
                        break;
                    case GameEventKind.CardRevealed:
                        stats.TotalFlips++;
                        switch ((CardType)e.Card)
                        {
                            case CardType.Rose: stats.RosesRevealed++; break;
                            case CardType.Crown: stats.CrownsRevealed++; break;
                            case CardType.Skull: stats.SkullsRevealed++; break;
                        }
                        break;
                    case GameEventKind.ChallengeSucceeded:
                        stats.Successes++;
                        break;
                    case GameEventKind.ChallengeFailed:
                        if (e.Player == e.Target) stats.OwnSkullFailures++;
                        else stats.OtherSkullFailures++;
                        break;
                    case GameEventKind.PlayerEliminated:
                        stats.Eliminations++;
                        break;
                    case GameEventKind.MatchEnded:
                        if (e.Player >= 0 && state.Players[e.Player].Points < state.Config.PointsToWin
                            && state.ActivePlayerCount > 1)
                        {
                            stats.OneCardWins++;
                        }
                        break;
                }
            }
        }

        private static void Increment(Dictionary<string, int> counter, string key, int by)
        {
            if (string.IsNullOrEmpty(key)) return;
            int current;
            counter.TryGetValue(key, out current);
            counter[key] = current + by;
        }
    }
}
