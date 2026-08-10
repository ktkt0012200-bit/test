namespace SkullDive.Net
{
    /// <summary>
    /// JSON の読み書き。実装をプラットフォームごとに差し替えるための境界。
    ///
    /// サーバは System.Text.Json、Unity は Newtonsoft.Json を使う。
    /// どちらも netstandard2.1 に同じライブラリを持ち込めないため、
    /// 共有コードはこのインターフェース越しにしか JSON を触らない。
    /// </summary>
    public interface IMessageCodec
    {
        string Encode(ClientMessage message);
        ServerMessage DecodeServer(string json);
    }
}
