namespace SkullDive.Core
{
    /// <summary>
    /// イベントを日本語の 1 行に変換する。CLI と Unity の両方から使うので、
    /// 表示文言の正本はここ 1 か所だけ。
    ///
    /// 秘匿情報の扱いに注意: CardPlaced / CardDiscarded は EventRedactor を通っていれば
    /// 他人のカードが Cards.Hidden になっているため、ここでは「伏せた」としか書けない。
    /// 逆に言えば、ここでカード名が出てしまう場合は秘匿処理を通し忘れている。
    /// </summary>
    public static class EventNarrator
    {
        public static string CardName(CardType card)
        {
            switch (card)
            {
                case CardType.Rose: return "バラ";
                case CardType.Crown: return "クラウン";
                case CardType.Skull: return "スカル";
                default: return "?";
            }
        }

        public static string PhaseLabel(Phase phase)
        {
            switch (phase)
            {
                case Phase.Placing: return "カードを置く";
                case Phase.Bidding: return "宣言";
                case Phase.Challenging: return "チャレンジ";
                case Phase.AwaitingDiscard: return "失うカードを選ぶ";
                case Phase.RoundEnd: return "ラウンド終了";
                default: return "終了";
            }
        }

        /// <summary>表示不要なイベントには null を返す。</summary>
        public static string Describe(GameEvent e, string[] names, int viewer = -1)
        {
            switch (e.Kind)
            {
                case GameEventKind.RoundStarted:
                    return "-- ラウンド " + e.Extra + " 開始 (" + Name(names, e.Player) + " から) --";

                case GameEventKind.CardPlaced:
                    return Name(names, e.Player) + " が 1 枚伏せた"
                        + (e.HasCard ? " (" + CardName((CardType)e.Card) + ")" : "");

                case GameEventKind.BidMade:
                    return Name(names, e.Player) + " が " + e.Amount + " 枚を宣言";

                case GameEventKind.Passed:
                    return Name(names, e.Player) + " が降りた";

                case GameEventKind.ChallengeStarted:
                    return Name(names, e.Player) + " が " + e.Amount + " 枚めくりに挑戦";

                case GameEventKind.CardRevealed:
                    return "めくった: " + CardName((CardType)e.Card)
                        + " (" + Name(names, e.Player) + " の山)  累計 " + e.Amount;

                case GameEventKind.ChallengeSucceeded:
                    return "成功! " + Name(names, e.Player) + " が " + e.Amount + " ポイント";

                case GameEventKind.ChallengeFailed:
                    return "失敗! " + Name(names, e.Player) + " が "
                        + (e.Player == e.Target ? "自分の" : Name(names, e.Target) + " の") + "スカルを踏んだ";

                case GameEventKind.CardDiscarded:
                    return Name(names, e.Player) + " がカードを 1 枚失った"
                        + (e.HasCard ? " (" + CardName((CardType)e.Card) + ")" : "")
                        + "  残り " + e.Amount + " 枚";

                case GameEventKind.PlayerEliminated:
                    return Name(names, e.Player) + " は脱落";

                case GameEventKind.MatchEnded:
                    return e.Player < 0 ? null : Name(names, e.Player) + " の勝ち";

                default:
                    return null;
            }
        }

        private static string Name(string[] names, int seat)
        {
            if (seat < 0) return "?";
            if (names != null && seat < names.Length && !string.IsNullOrEmpty(names[seat])) return names[seat];
            return "P" + seat;
        }
    }
}
