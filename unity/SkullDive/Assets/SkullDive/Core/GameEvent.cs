using System;
using System.Collections.Generic;

namespace SkullDive.Core
{
    /// <summary>int 値はワイヤーフォーマットに載るため変更禁止。</summary>
    public enum GameEventKind
    {
        RoundStarted = 0,
        CardPlaced = 1,
        BidMade = 2,
        Passed = 3,
        ChallengeStarted = 4,
        CardRevealed = 5,
        ChallengeSucceeded = 6,
        ChallengeFailed = 7,
        CardDiscarded = 8,
        PlayerEliminated = 9,
        RoundEnded = 10,
        MatchEnded = 11,
    }

    /// <summary>
    /// 状態遷移の記録。演出はこのイベント列を再生するだけで作れる。
    /// ネットワークでもこれをそのまま流すので、秘匿情報の有無に注意すること
    /// (CardPlaced と CardDiscarded の Card は本人以外には伏せる。EventRedactor を必ず通す)。
    /// </summary>
    [Serializable]
    public struct GameEvent
    {
        public GameEventKind Kind;

        /// <summary>主体となるプレイヤー。該当しない場合は -1。</summary>
        public int Player;

        /// <summary>副次的な対象プレイヤー(踏んだスカルの持ち主など)。該当しない場合は -1。</summary>
        public int Target;

        /// <summary>CardType を int にしたもの。伏せられている / 該当しない場合は Cards.Hidden(-1)。</summary>
        public int Card;

        /// <summary>宣言枚数、めくった時点の累計カウント、獲得ポイントなど。</summary>
        public int Amount;

        /// <summary>めくった枚数、ラウンド番号など補助的な値。</summary>
        public int Extra;

        public static GameEvent Make(GameEventKind kind, int player = -1, int target = -1,
            int card = Cards.Hidden, int amount = 0, int extra = 0)
        {
            return new GameEvent { Kind = kind, Player = player, Target = target, Card = card, Amount = amount, Extra = extra };
        }

        public bool HasCard
        {
            get { return Card != Cards.Hidden; }
        }

        public override string ToString()
        {
            return Kind + "(p=" + Player + ",t=" + Target + ",card=" + Card + ",amt=" + Amount + ",ex=" + Extra + ")";
        }
    }

    /// <summary>
    /// イベントから秘匿情報を落とす。ここを通さずにイベントを送信すると
    /// 伏せカードの中身がそのままクライアントに漏れる。
    /// </summary>
    public static class EventRedactor
    {
        /// <summary>viewer から見えるべき形にイベントを変換する。</summary>
        public static GameEvent Redact(GameEvent e, int viewer)
        {
            switch (e.Kind)
            {
                // 伏せて置いたカード / 伏せて捨てたカードは本人以外には見えない。
                case GameEventKind.CardPlaced:
                case GameEventKind.CardDiscarded:
                    if (e.Player != viewer)
                    {
                        e.Card = Cards.Hidden;
                    }
                    return e;

                // それ以外(特に CardRevealed)は公開情報。
                default:
                    return e;
            }
        }

        public static GameEvent[] Redact(IList<GameEvent> events, int viewer)
        {
            var result = new GameEvent[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                result[i] = Redact(events[i], viewer);
            }
            return result;
        }
    }
}
