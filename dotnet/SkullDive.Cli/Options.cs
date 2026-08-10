using System;
using SkullDive.Core;

namespace SkullDive.Cli
{
    public enum CliMode
    {
        Play,
        Simulate,
        Compare,
        NetSmoke,
    }

    public sealed class Options
    {
        public CliMode Mode = CliMode.Play;
        public int Matches = 5000;
        public int Players = 4;
        public int Seed = 20260810;
        public bool Classic;
        public int CrownValue = 2;
        public bool CrownOneInOwnStack;
        public int PointsToWin = 2;
        public string ServerUrl = "ws://127.0.0.1:5099/ws";
        public int Bots = 2;
        public int TimeoutSeconds = 60;
        public bool IdleGuest;

        /// <summary>
        /// 入力が尽きた時点で自動操縦せずに終了する。
        /// 標準入力で選択肢を渡して 1 手ずつ進める(チャット越しのプレイなど)ための指定。
        /// シードが固定なので、同じ選択列を渡せば必ず同じ盤面が再現される。
        /// </summary>
        public bool StopOnEof;

        public GameConfig BuildConfig()
        {
            var config = Classic ? GameConfig.Classic() : GameConfig.Default();
            config.CrownValue = CrownValue;
            config.CrownCountsAsOneInOwnStack = CrownOneInOwnStack;
            config.PointsToWin = PointsToWin;
            return config;
        }

        public static Options Parse(string[] args)
        {
            var options = new Options();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--sim":
                    case "--simulate":
                        options.Mode = CliMode.Simulate;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int matches)) options.Matches = matches;
                        break;
                    case "--compare":
                        options.Mode = CliMode.Compare;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int compareMatches)) options.Matches = compareMatches;
                        break;
                    case "--players":
                        options.Players = int.Parse(args[++i]);
                        break;
                    case "--seed":
                        options.Seed = int.Parse(args[++i]);
                        break;
                    case "--classic":
                        options.Classic = true;
                        break;
                    case "--crown-value":
                        options.CrownValue = int.Parse(args[++i]);
                        break;
                    case "--crown-one-in-own-stack":
                        options.CrownOneInOwnStack = true;
                        break;
                    case "--points":
                        options.PointsToWin = int.Parse(args[++i]);
                        break;
                    case "--net-smoke":
                        options.Mode = CliMode.NetSmoke;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) options.ServerUrl = args[++i];
                        break;
                    case "--stop-on-eof":
                        options.StopOnEof = true;
                        break;
                    case "--idle-guest":
                        options.IdleGuest = true;
                        break;
                    case "--bots":
                        options.Bots = int.Parse(args[++i]);
                        break;
                    case "--timeout":
                        options.TimeoutSeconds = int.Parse(args[++i]);
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        Environment.Exit(0);
                        break;
                }
            }

            if (options.Mode == CliMode.NetSmoke) return options;

            if (options.Players < GameEngine.MinPlayers || options.Players > GameEngine.MaxPlayers)
            {
                throw new ArgumentException("--players must be " + GameEngine.MinPlayers + ".." + GameEngine.MaxPlayers);
            }

            return options;
        }

        public static void PrintUsage()
        {
            Console.WriteLine("SkullDive CLI");
            Console.WriteLine();
            Console.WriteLine("  (引数なし)                   ターミナルでソロプレイ");
            Console.WriteLine("  --sim <N>                    AI 同士で N 回対戦しバランス統計を出す");
            Console.WriteLine("  --compare <N>                原作ルールとクラウン入りを並べて比較する");
            Console.WriteLine("  --net-smoke [url]            起動中のサーバへ 2 人 + bot で接続し 1 マッチ完走させる");
            Console.WriteLine("      --bots <N>               --net-smoke で追加する bot の数 (既定 2)");
            Console.WriteLine("      --timeout <sec>          --net-smoke の制限時間 (既定 60)");
            Console.WriteLine("      --idle-guest             guest を放置し、サーバの時間切れ代打を検証する");
            Console.WriteLine();
            Console.WriteLine("  --players <2-6>              プレイヤー数 (既定 4)");
            Console.WriteLine("  --seed <int>                 乱数シード");
            Console.WriteLine("  --classic                    クラウンなし (バラ 3 + スカル 1)");
            Console.WriteLine("  --crown-value <int>          クラウンのカウント値 (既定 2)");
            Console.WriteLine("  --crown-one-in-own-stack     自分の山のクラウンは 1 枚分として扱う");
            Console.WriteLine("  --points <int>               勝利に必要なポイント (既定 2)");
            Console.WriteLine("  --stop-on-eof                入力が尽きたら自動操縦せず終了する");
            Console.WriteLine();
            Console.WriteLine("  1 手ずつ進める例 (シード固定なので同じ選択列は同じ盤面になる):");
            Console.WriteLine("    printf '0\\n2\\n' | dotnet run -- --stop-on-eof");
        }
    }
}
