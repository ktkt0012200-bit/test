using System.Text.Json;
using System.Text.Json.Serialization;
using SkullDive.Net;

namespace SkullDive.Json
{
    /// <summary>
    /// System.Text.Json による参照実装。テスト専用のオラクル。
    ///
    /// 本番の通信は WireCodec(手書き・リフレクション非依存)が担当する。
    /// こちらは「WireCodec の出力が標準的なシリアライザと相互運用できるか」を
    /// 独立に確かめるためだけに使う。
    ///
    /// IncludeFields は必須。DTO は public フィールドで構成されているため、
    /// これを忘れると中身が空になる。
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
