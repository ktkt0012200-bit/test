using Microsoft.Extensions.Configuration;
using SkullDive.Net;

namespace SkullDive.Server
{
    /// <summary>
    /// 運用時に調整したい値。appsettings.json や環境変数から読む。
    /// 手番の制限時間は「快適さ」と「放置耐性」のトレードオフなので、
    /// 再ビルドせずに変えられるようにしておく(結合テストで短縮するのにも使う)。
    /// </summary>
    public sealed class ServerOptions
    {
        public int TurnTimeoutMs { get; set; } = Protocol.TurnTimeoutMs;
        public int RoundEndDelayMs { get; set; } = Protocol.RoundEndDelayMs;

        public static ServerOptions FromConfiguration(IConfiguration configuration)
        {
            var options = new ServerOptions();
            var section = configuration.GetSection("SkullDive");
            if (section.Exists())
            {
                options.TurnTimeoutMs = section.GetValue("TurnTimeoutMs", options.TurnTimeoutMs);
                options.RoundEndDelayMs = section.GetValue("RoundEndDelayMs", options.RoundEndDelayMs);
            }
            return options;
        }
    }
}
