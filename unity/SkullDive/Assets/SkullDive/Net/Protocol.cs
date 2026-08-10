using System;
using SkullDive.Core;

namespace SkullDive.Net
{
    /// <summary>
    /// クライアントとサーバで共有するワイヤーフォーマット。
    /// 読み書きは WireCodec が担当し、サーバと Unity で同一の実装を使う。
    ///
    /// DTO は public フィールドだけで構成し、enum は数値として送る。
    /// enum の数値は互換性のため変更禁止。
    /// </summary>
    public static class Protocol
    {
        public const int Version = 1;

        /// <summary>手番の制限時間(ミリ秒)。切れたら AI が代打する。</summary>
        public const int TurnTimeoutMs = 20000;

        /// <summary>ラウンド決着の演出を見せる時間(ミリ秒)。</summary>
        public const int RoundEndDelayMs = 3500;

        public const int MinRoomPlayers = 2;
        public const int MaxRoomPlayers = 6;
    }

    public enum ClientMessageType
    {
        Join = 0,
        Start = 1,
        Action = 2,
        Ping = 3,
        Leave = 4,
        AddBot = 5,
    }

    public enum ServerMessageType
    {
        Welcome = 0,
        Room = 1,
        State = 2,
        Error = 3,
        Pong = 4,
    }

    [Serializable]
    public sealed class ClientMessage
    {
        public int Type;

        /// <summary>Join 時のみ。null または空文字なら新規ルーム作成。</summary>
        public string RoomCode;

        public string Name;

        /// <summary>再接続用トークン。Welcome で受け取った値をそのまま返す。</summary>
        public string Token;

        /// <summary>Action 時のみ。</summary>
        public GameAction Action;

        public ClientMessageType Kind
        {
            get { return (ClientMessageType)Type; }
            set { Type = (int)value; }
        }
    }

    [Serializable]
    public sealed class ServerMessage
    {
        public int Type;

        /// <summary>Welcome 時のみ: 割り当てられた席番号。</summary>
        public int Seat = -1;

        /// <summary>Welcome 時のみ: 再接続用トークン。</summary>
        public string Token;

        public RoomInfo Room;

        /// <summary>State 時のみ: 受信者から見た盤面。</summary>
        public PlayerView View;

        /// <summary>State 時のみ: 受信者向けに秘匿処理済みのイベント列。</summary>
        public GameEvent[] Events;

        /// <summary>State 時のみ: 手番の残り時間(ミリ秒)。0 なら制限なし。</summary>
        public int TurnDeadlineMs;

        public string Error;

        public int ProtocolVersion = Protocol.Version;

        public ServerMessageType Kind
        {
            get { return (ServerMessageType)Type; }
            set { Type = (int)value; }
        }
    }

    [Serializable]
    public sealed class RoomInfo
    {
        public string Code;
        public SeatInfo[] Seats = new SeatInfo[0];
        public bool Started;
        public int HostSeat = -1;
        public GameConfig Config;
    }

    [Serializable]
    public sealed class SeatInfo
    {
        public int Seat;
        public string Name;
        public bool IsBot;
        public bool Connected;

        /// <summary>Bot の場合の性格 ID(UI で癖のヒントを出すため)。</summary>
        public string PersonalityId;
    }
}
