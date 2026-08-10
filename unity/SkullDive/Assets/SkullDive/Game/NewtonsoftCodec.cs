using Newtonsoft.Json;
using SkullDive.Net;

namespace SkullDive.Game
{
    /// <summary>
    /// Unity 側の JSON 実装。
    ///
    /// Newtonsoft.Json は既定で public フィールドを書き出し、enum を数値で扱う。
    /// これはサーバ側 (System.Text.Json + IncludeFields) の出力と一致するため、
    /// 同じ DTO をそのまま共有できる。
    /// 設定を変える場合(例: enum を文字列化する)はサーバ側と必ず揃えること。
    /// </summary>
    public sealed class NewtonsoftCodec : IMessageCodec
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            // フィールドのみで構成された DTO を、余計なメタデータ無しで往復させる。
            TypeNameHandling = TypeNameHandling.None,
        };

        public string Encode(ClientMessage message)
        {
            return JsonConvert.SerializeObject(message, Settings);
        }

        public ServerMessage DecodeServer(string json)
        {
            return JsonConvert.DeserializeObject<ServerMessage>(json, Settings);
        }
    }
}
