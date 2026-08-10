using System.Text.Json;
using System.Text.Json.Serialization;
using SkullDive.Net;

namespace SkullDive.Json
{
    /// <summary>
    /// サーバ側の JSON 実装。
    ///
    /// IncludeFields は必須。DTO は public フィールドで構成されているため、
    /// これを忘れると中身が空のメッセージを送ることになる。
    /// enum は既定どおり数値で書き出す(Unity の Newtonsoft.Json の既定と一致させるため)。
    /// </summary>
    public sealed class JsonCodec : IMessageCodec
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public string Encode(ClientMessage message)
        {
            return JsonSerializer.Serialize(message, Options);
        }

        public ServerMessage DecodeServer(string json)
        {
            return JsonSerializer.Deserialize<ServerMessage>(json, Options);
        }

        public static string EncodeServer(ServerMessage message)
        {
            return JsonSerializer.Serialize(message, Options);
        }

        public static ClientMessage DecodeClient(string json)
        {
            return JsonSerializer.Deserialize<ClientMessage>(json, Options);
        }
    }
}
