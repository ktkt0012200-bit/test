using System;

namespace SkullDive.Core
{
    /// <summary>
    /// ルールのパラメータ。バランス調整はすべてここを触るだけで済むようにしてある。
    /// 公開情報なので、そのままクライアントへ送ってよい。
    /// </summary>
    [Serializable]
    public sealed class GameConfig
    {
        /// <summary>初期手札のバラの枚数。</summary>
        public int RoseCount = 2;

        /// <summary>初期手札のクラウンの枚数。0 にすると原作『スカル』のルールになる。</summary>
        public int CrownCount = 1;

        /// <summary>初期手札のスカルの枚数。</summary>
        public int SkullCount = 1;

        /// <summary>クラウンをめくったときのカウント値。</summary>
        public int CrownValue = 2;

        /// <summary>
        /// true にすると「自分の山のクラウンは 1 枚分、他人の山のクラウンだけ 2 枚分」になる。
        /// クラウンが強すぎた場合の調整ノブ。
        /// </summary>
        public bool CrownCountsAsOneInOwnStack = false;

        /// <summary>勝利に必要なポイント数。</summary>
        public int PointsToWin = 2;

        /// <summary>手札 1 枚でチャレンジに成功したら即勝利(原作の特別ルール)。</summary>
        public bool OneCardWinEnabled = true;

        /// <summary>自分のスカルを踏んだ場合、捨てるカードを自分で選べる。false なら常にランダム。</summary>
        public bool ChooseDiscardOnOwnSkull = true;

        public int HandSize
        {
            get { return RoseCount + CrownCount + SkullCount; }
        }

        /// <summary>クラウンありの標準ルール。</summary>
        public static GameConfig Default()
        {
            return new GameConfig();
        }

        /// <summary>原作『スカル』準拠(バラ 3 + スカル 1、クラウンなし)。A/B 比較用。</summary>
        public static GameConfig Classic()
        {
            return new GameConfig
            {
                RoseCount = 3,
                CrownCount = 0,
                SkullCount = 1,
                CrownValue = 2,
            };
        }

        public GameConfig Clone()
        {
            return new GameConfig
            {
                RoseCount = RoseCount,
                CrownCount = CrownCount,
                SkullCount = SkullCount,
                CrownValue = CrownValue,
                CrownCountsAsOneInOwnStack = CrownCountsAsOneInOwnStack,
                PointsToWin = PointsToWin,
                OneCardWinEnabled = OneCardWinEnabled,
                ChooseDiscardOnOwnSkull = ChooseDiscardOnOwnSkull,
            };
        }

        /// <summary>設定が破綻していないかの検証。エンジンに渡す前に呼ぶ。</summary>
        public void Validate()
        {
            if (RoseCount < 0 || CrownCount < 0 || SkullCount < 0)
                throw new ArgumentException("card counts must not be negative");
            if (HandSize < 2)
                throw new ArgumentException("hand size must be at least 2");
            if (SkullCount < 1)
                throw new ArgumentException("at least one skull is required");
            if (SkullCount >= HandSize)
                throw new ArgumentException("hand must contain at least one non-skull card");
            if (CrownValue < 1)
                throw new ArgumentException("crown value must be at least 1");
            if (PointsToWin < 1)
                throw new ArgumentException("points to win must be at least 1");
        }

        /// <summary>カード 1 枚のカウント値。</summary>
        public int ValueOf(CardType card, bool inOwnStack)
        {
            switch (card)
            {
                case CardType.Rose:
                    return 1;
                case CardType.Crown:
                    return (inOwnStack && CrownCountsAsOneInOwnStack) ? 1 : CrownValue;
                default:
                    return 0;
            }
        }

        /// <summary>スカル以外のカードの平均カウント値。AI の見積りに使う。</summary>
        public double AverageSafeValue()
        {
            int safeCards = RoseCount + CrownCount;
            if (safeCards <= 0) return 1.0;
            return (RoseCount * 1.0 + CrownCount * (double)CrownValue) / safeCards;
        }
    }
}
