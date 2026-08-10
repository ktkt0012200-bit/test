namespace SkullDive.Net
{
    /// <summary>
    /// メッセージの読み書き。既定の実装は WireCodec で、サーバも Unity もこれを使う。
    ///
    /// インターフェースとして切っておく理由は、WebGL 向けに
    /// ブラウザの WebSocket を使う実装へ差し替える余地を残すため。
    /// </summary>
    public interface IMessageCodec
    {
        string Encode(ClientMessage message);
        ServerMessage DecodeServer(string json);
    }
}
