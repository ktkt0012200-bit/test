using System;
using System.Threading.Tasks;

namespace SkullDive.Cli
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                var options = Options.Parse(args);
                switch (options.Mode)
                {
                    case CliMode.Simulate:
                        BalanceReport.Simulate(options);
                        break;
                    case CliMode.Compare:
                        BalanceReport.Compare(options);
                        break;
                    case CliMode.NetSmoke:
                        return await NetSmoke.RunAsync(options.ServerUrl, options.Bots, options.TimeoutSeconds, options.IdleGuest);
                    default:
                        InteractivePlay.Run(options);
                        break;
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.GetType().Name + ": " + ex.Message);
                if (Environment.GetEnvironmentVariable("SKULLDIVE_DEBUG") == "1") Console.Error.WriteLine(ex.ToString());
                Console.Error.WriteLine();
                Options.PrintUsage();
                return 1;
            }
        }
    }
}
