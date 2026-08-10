using System;

namespace SkullDive.Core
{
    /// <summary>int 値はワイヤーフォーマットに載るため変更禁止。</summary>
    public enum ActionKind
    {
        PlaceCard = 0,
        Bid = 1,
        Pass = 2,
        Flip = 3,
        Discard = 4,
        AdvanceRound = 5,
    }

    /// <summary>
    /// プレイヤーが取りうる操作。エンジンへの入力はこれ 1 種類だけで、
    /// サーバは受け取ったアクションが合法手集合に含まれるかだけを検証する。
    /// </summary>
    [Serializable]
    public struct GameAction : IEquatable<GameAction>
    {
        public ActionKind Kind;

        /// <summary>操作したプレイヤー。AdvanceRound はシステム発行なので -1。</summary>
        public int Player;

        /// <summary>CardType を int にしたもの。使わない場合は Cards.Hidden(-1)。</summary>
        public int Card;

        /// <summary>宣言する枚数(Bid のみ)。</summary>
        public int Amount;

        /// <summary>めくる対象のプレイヤー(Flip のみ)。</summary>
        public int Target;

        public const int SystemPlayer = -1;

        public static GameAction PlaceCard(int player, CardType card)
        {
            return new GameAction { Kind = ActionKind.PlaceCard, Player = player, Card = (int)card, Amount = 0, Target = -1 };
        }

        public static GameAction Bid(int player, int amount)
        {
            return new GameAction { Kind = ActionKind.Bid, Player = player, Card = Cards.Hidden, Amount = amount, Target = -1 };
        }

        public static GameAction Pass(int player)
        {
            return new GameAction { Kind = ActionKind.Pass, Player = player, Card = Cards.Hidden, Amount = 0, Target = -1 };
        }

        public static GameAction Flip(int player, int target)
        {
            return new GameAction { Kind = ActionKind.Flip, Player = player, Card = Cards.Hidden, Amount = 0, Target = target };
        }

        public static GameAction Discard(int player, CardType card)
        {
            return new GameAction { Kind = ActionKind.Discard, Player = player, Card = (int)card, Amount = 0, Target = -1 };
        }

        public static GameAction AdvanceRound()
        {
            return new GameAction { Kind = ActionKind.AdvanceRound, Player = SystemPlayer, Card = Cards.Hidden, Amount = 0, Target = -1 };
        }

        public CardType CardType
        {
            get { return (SkullDive.Core.CardType)Card; }
        }

        public bool Equals(GameAction other)
        {
            return Kind == other.Kind && Player == other.Player && Card == other.Card
                   && Amount == other.Amount && Target == other.Target;
        }

        public override bool Equals(object obj)
        {
            return obj is GameAction && Equals((GameAction)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind;
                h = (h * 397) ^ Player;
                h = (h * 397) ^ Card;
                h = (h * 397) ^ Amount;
                h = (h * 397) ^ Target;
                return h;
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case ActionKind.PlaceCard: return "P" + Player + " place " + Cards.ShortLabel(CardType);
                case ActionKind.Bid: return "P" + Player + " bid " + Amount;
                case ActionKind.Pass: return "P" + Player + " pass";
                case ActionKind.Flip: return "P" + Player + " flip P" + Target;
                case ActionKind.Discard: return "P" + Player + " discard " + Cards.ShortLabel(CardType);
                case ActionKind.AdvanceRound: return "advance round";
                default: return "?";
            }
        }
    }
}
