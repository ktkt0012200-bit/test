using System;
using System.Collections.Generic;
using System.Text;
using SkullDive.Ai;
using SkullDive.Core;

namespace SkullDive.Cli
{
    /// <summary>
    /// ターミナルで実際に遊べるソロモード。
    /// Unity を開かずにルールと AI の手触りを確認するためのもので、
    /// 演出以外のゲーム内容はモバイル版とまったく同じ(同じ GameEngine を呼んでいる)。
    /// </summary>
    public static class InteractivePlay
    {
        public static void Run(Options options)
        {
            var config = options.BuildConfig();
            var opponents = new AiPersonality[options.Players - 1];
            var pool = AiPersonality.DefaultTable();
            var all = AiPersonality.All();
            for (int i = 0; i < opponents.Length; i++)
            {
                opponents[i] = i < pool.Length ? pool[i] : all[i % all.Length];
            }

            var match = new SoloMatch(config, opponents, 0, options.Seed);
            // 自動操縦用。入力が尽きたときに手を選ばせる。
            var autopilot = new AiBrain(AiPersonality.Steady());
            var autopilotRng = new XorShiftRng(options.Seed ^ 0x5EED);

            Console.WriteLine("=== SKULL DIVE ===");
            Console.WriteLine(config.PointsToWin + " ポイント先取。自分の山は必ず全部めくる。クラウンは "
                + config.CrownValue + " 枚分。");
            Console.WriteLine();
            PrintOpponents(match, options.Players);

            while (!match.IsOver)
            {
                match.RunUntilHumanTurn();
                PrintEvents(match);
                if (match.IsOver) break;

                var view = match.HumanView();
                PrintTable(view, match);

                GameAction action;
                if (!ChooseAction(view, autopilot, autopilotRng, options.StopOnEof, out action))
                {
                    // 入力が尽きた。ここまでの盤面を見せて終了する。
                    Console.WriteLine();
                    Console.WriteLine("(選択肢の番号を渡すと続きから進みます)");
                    return;
                }
                match.SubmitHumanAction(action);
                PrintEvents(match);
            }

            Console.WriteLine();
            Console.WriteLine("=== " + (match.Winner == 0 ? "YOU WIN" : match.Names[match.Winner] + " の勝ち") + " ===");
        }

        private static void PrintOpponents(SoloMatch match, int players)
        {
            Console.WriteLine("対戦相手:");
            for (int seat = 1; seat < players; seat++)
            {
                var personality = match.PersonalityOf(seat);
                Console.WriteLine("  " + seat + ". " + personality.Name + " — " + personality.Tell);
            }
            Console.WriteLine();
        }

        // ------------------------------------------------------------ rendering

        private static void PrintTable(PlayerView view, SoloMatch match)
        {
            Console.WriteLine();
            Console.WriteLine("--- round " + view.RoundNumber + " / " + PhaseLabel(view.Phase)
                + " / 場に " + view.TotalOnTable + " 枚"
                + (view.HighestBid > 0 ? " / 宣言 " + view.HighestBid + " (" + view.Players[view.HighestBidder].Name + ")" : "")
                + " ---");

            for (int i = 0; i < view.Players.Length; i++)
            {
                var player = view.Players[i];
                var line = new StringBuilder();
                line.Append(i == view.CurrentPlayer ? "> " : "  ");
                line.Append(Text.Pad(player.Name, 11));
                line.Append("pt ").Append(player.Points);
                line.Append("  cards ").Append(player.CardsOwned);
                line.Append("  ").Append(Text.Pad(StackString(view, i), 15));

                if (player.Eliminated) line.Append(" [脱落]");
                else if (player.HasPassed) line.Append(" [降りた]");
                else if (player.CurrentBid > 0) line.Append(" [宣言 ").Append(player.CurrentBid).Append("]");

                // チャレンジ中は各山の安全度を見せる。これがそのまま UI の「読み」メーターになる。
                if (view.Phase == Phase.Challenging && i != view.Challenger
                    && player.StackCount - player.FlippedCount > 0)
                {
                    line.Append("  安全 ").Append((Odds.SafeChance(view, i) * 100).ToString("0")).Append('%');
                }

                Console.WriteLine(line.ToString());
            }

            Console.WriteLine("  手札: " + HandString(view));
            if (view.Phase == Phase.Challenging)
            {
                Console.WriteLine("  進捗: " + view.FlipValue + " / " + view.HighestBid
                    + (view.OwnStackCleared ? "  (自分の山は消化済み)" : "  (まず自分の山を全部めくる)"));
            }
        }

        private static string StackString(PlayerView view, int seat)
        {
            var player = view.Players[seat];
            if (player.StackCount == 0) return "(なし)";

            // 下から上へ並べる。自分の山は中身が分かる。
            var cards = new string[player.StackCount];
            for (int i = 0; i < player.StackCount; i++)
            {
                int fromTop = player.StackCount - 1 - i;
                if (fromTop < player.FlippedCount)
                {
                    cards[i] = "[" + Cards.ShortLabel((CardType)player.RevealedFromTop[fromTop]) + "]";
                }
                else if (seat == view.Viewer)
                {
                    cards[i] = "(" + Cards.ShortLabel((CardType)view.MyStack[i]) + ")";
                }
                else
                {
                    cards[i] = "(?)";
                }
            }
            return string.Join("", cards);
        }

        private static string HandString(PlayerView view)
        {
            if (view.MyHand.Length == 0) return "(なし)";
            var parts = new List<string>();
            foreach (int card in view.MyHand) parts.Add(Cards.ShortLabel((CardType)card));
            return string.Join(" ", parts);
        }

        private static void PrintEvents(SoloMatch match)
        {
            foreach (var e in match.TakeEvents())
            {
                string text = Describe(e, match);
                if (text != null) Console.WriteLine("    " + text);
            }
        }

        private static string Describe(GameEvent e, SoloMatch match)
        {
            // 文言の正本は Core の EventNarrator。CLI と Unity で表示が食い違わないようにする。
            return EventNarrator.Describe(e, match.Names, match.HumanSeat);
        }

        private static string CardName(CardType card)
        {
            return EventNarrator.CardName(card);
        }

        private static string PhaseLabel(Phase phase)
        {
            return EventNarrator.PhaseLabel(phase);
        }

        // ------------------------------------------------------------ input

        private static bool ChooseAction(PlayerView view, AiBrain autopilot, IRng rng, bool stopOnEof,
            out GameAction action)
        {
            var legal = view.LegalActions;

            Console.WriteLine();
            for (int i = 0; i < legal.Length; i++)
            {
                Console.WriteLine("  [" + i + "] " + ActionLabel(view, legal[i]));
            }
            Console.Write("選択 > ");

            string line = Console.ReadLine();

            if (line == null)
            {
                if (stopOnEof)
                {
                    Console.WriteLine("(入力待ち)");
                    action = default(GameAction);
                    return false;
                }

                // 入力が尽きたら自動操縦に切り替える(パイプ実行やデモ用)。
                action = autopilot.Decide(view, rng);
                Console.WriteLine("[auto] " + ActionLabel(view, action));
                return true;
            }

            line = line.Trim();
            if (line.Length == 0 || !int.TryParse(line, out int index) || index < 0 || index >= legal.Length)
            {
                Console.WriteLine("  → 入力が不正なので自動選択します");
                action = autopilot.Decide(view, rng);
                Console.WriteLine("[auto] " + ActionLabel(view, action));
                return true;
            }

            action = legal[index];
            Console.WriteLine("  → " + ActionLabel(view, action));
            return true;
        }

        private static string ActionLabel(PlayerView view, GameAction action)
        {
            switch (action.Kind)
            {
                case ActionKind.PlaceCard:
                    return CardName(action.CardType) + " を伏せて置く";
                case ActionKind.Bid:
                    return action.Amount + " 枚を宣言する"
                        + (action.Amount == view.TotalOnTable ? " (場の全枚数 = 即チャレンジ)" : "");
                case ActionKind.Pass:
                    return "降りる";
                case ActionKind.Flip:
                    return view.Players[action.Target].Name + " の山をめくる"
                        + "  (安全 " + (Odds.SafeChance(view, action.Target) * 100).ToString("0") + "%)";
                case ActionKind.Discard:
                    return CardName(action.CardType) + " を失う";
                default:
                    return action.ToString();
            }
        }
    }
}
