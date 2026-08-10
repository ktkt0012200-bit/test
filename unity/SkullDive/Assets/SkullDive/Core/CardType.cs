namespace SkullDive.Core
{
    /// <summary>
    /// カードの種類。Crown が本作のオリジナル要素で、めくられたとき複数枚分としてカウントされる。
    /// int 値はワイヤーフォーマットに直接載るため、既存の値を変更してはいけない。
    /// </summary>
    public enum CardType
    {
        Rose = 0,
        Crown = 1,
        Skull = 2,
    }

    public static class Cards
    {
        /// <summary>カード情報が伏せられていることを表す値。</summary>
        public const int Hidden = -1;

        /// <summary>並び順を安定させるためのソートキー(手札の並びから情報が漏れないようにする)。</summary>
        public static int SortKey(CardType card)
        {
            return (int)card;
        }

        public static string ShortLabel(CardType card)
        {
            switch (card)
            {
                case CardType.Rose: return "R";
                case CardType.Crown: return "C";
                case CardType.Skull: return "S";
                default: return "?";
            }
        }
    }
}
