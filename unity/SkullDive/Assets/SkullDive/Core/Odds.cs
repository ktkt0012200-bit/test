using System;

namespace SkullDive.Core
{
    /// <summary>
    /// 公開情報だけから計算できる確率。AI の判断と、UI の「読み」メーター表示の両方で使う。
    ///
    /// 前提: あるプレイヤーの所持カードのうちどれがスカルかは、公開されていない限り
    /// 一様にランダムとみなせる。したがって未公開カード 1 枚がスカルである確率は
    ///   スカルの想定枚数 / 未公開カードの枚数
    /// になる。山の枚数ではなく「所持カード全体の未公開枚数」で割るのが要点で、
    /// スカルが手札に残っている可能性もこれで自然に織り込まれる。
    /// </summary>
    public static class Odds
    {
        /// <summary>指定プレイヤーの山の一番上(未公開の最上段)がスカルである確率。</summary>
        public static double TopCardSkullChance(PlayerPublicView player, GameConfig config)
        {
            if (player == null || config == null) return 0.0;
            if (player.StackCount - player.FlippedCount <= 0) return 0.0;

            int revealedSkulls = 0;
            for (int i = 0; i < player.RevealedFromTop.Length; i++)
            {
                if ((CardType)player.RevealedFromTop[i] == CardType.Skull) revealedSkulls++;
            }

            // 想定スカル枚数。失ったカードがスカルだったかは伏せられているため、
            // 「まだ持っている」と仮定して危険側に見積もる。
            int assumedSkulls = Math.Min(config.SkullCount, player.CardsOwned) - revealedSkulls;
            if (assumedSkulls <= 0) return 0.0;

            int unknown = player.CardsOwned - player.FlippedCount;
            if (unknown <= 0) return 0.0;
            if (assumedSkulls >= unknown) return 1.0;

            return assumedSkulls / (double)unknown;
        }

        /// <summary>
        /// view の持ち主が targetValue を宣言した場合の成功確率の見積り。
        ///
        /// 自分の山は中身を知っているので確定値として扱い(スカル入りなら 0)、
        /// 足りない分を他人の山から安全な順にめくると仮定して生存確率を掛け合わせる。
        /// 他人のカードのカウント値は平均値で近似する。
        /// </summary>
        public static double SurvivalForValue(PlayerView view, int targetValue)
        {
            if (view == null) return 0.0;

            int ownValue = view.MyStackValue();
            if (ownValue < 0) return 0.0;               // 自分の山にスカルがある = 必ず失敗
            if (ownValue >= targetValue) return 1.0;    // 自分の山だけで達成できる = 確定成功

            var config = view.Config;
            double averageValue = config.AverageSafeValue();
            int playerCount = view.Players.Length;

            var risks = new double[playerCount];
            var remaining = new int[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                var player = view.Players[i];
                if (i == view.Viewer || player.Eliminated)
                {
                    risks[i] = double.MaxValue;
                    remaining[i] = 0;
                    continue;
                }
                risks[i] = TopCardSkullChance(player, config);
                remaining[i] = player.StackCount - player.FlippedCount;
            }

            double value = ownValue;
            double survival = 1.0;

            while (value < targetValue)
            {
                int best = -1;
                for (int i = 0; i < playerCount; i++)
                {
                    if (remaining[i] <= 0) continue;
                    if (best < 0 || risks[i] < risks[best]) best = i;
                }
                // めくる山が尽きた = その宣言は達成不可能。
                if (best < 0) return 0.0;

                survival *= 1.0 - risks[best];
                value += averageValue;
                remaining[best]--;

                // 同じ山を続けてめくるほど危険度は上がる(安全なカードが減っていく)。
                risks[best] = Math.Min(1.0, risks[best] * 1.6 + 0.05);
            }

            return survival;
        }

        /// <summary>
        /// UI 表示用: 「今この山をめくったら何 % 安全か」。0..1。
        /// 自分の山は中身を知っているので確定値になる。
        /// </summary>
        public static double SafeChance(PlayerView view, int target)
        {
            if (view == null || target < 0 || target >= view.Players.Length) return 0.0;

            if (target == view.Viewer)
            {
                int index = view.MyStack.Length - 1 - view.Players[target].FlippedCount;
                if (index < 0) return 1.0;
                return (CardType)view.MyStack[index] == CardType.Skull ? 0.0 : 1.0;
            }

            return 1.0 - TopCardSkullChance(view.Players[target], view.Config);
        }
    }
}
