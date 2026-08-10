using System;
using SkullDive.Core;

namespace SkullDive.Ai
{
    /// <summary>
    /// AI の思考。入力は PlayerView(公開情報 + 自分の手札)だけなので、
    /// 構造的に他人の伏せカードを覗くことができない。
    /// サーバ側の時間切れ処理でも同じクラスを使い、放置されたプレイヤーの代打を務める。
    /// </summary>
    public sealed class AiBrain
    {
        private readonly AiPersonality _personality;

        public AiBrain(AiPersonality personality)
        {
            if (personality == null) throw new ArgumentNullException("personality");
            _personality = personality;
        }

        public AiPersonality Personality
        {
            get { return _personality; }
        }

        public GameAction Decide(PlayerView view, IRng rng)
        {
            if (view == null) throw new ArgumentNullException("view");
            if (rng == null) throw new ArgumentNullException("rng");
            if (view.LegalActions == null || view.LegalActions.Length == 0)
                throw new InvalidOperationException("AiBrain was asked to act with no legal actions");

            switch (view.Phase)
            {
                case Phase.Placing:
                    return DecidePlacing(view, rng);
                case Phase.Bidding:
                    return DecideBidding(view, rng);
                case Phase.Challenging:
                    return DecideFlip(view, rng);
                case Phase.AwaitingDiscard:
                    return DecideDiscard(view);
                default:
                    return view.LegalActions[0];
            }
        }

        // ---------------------------------------------------------------- placing

        private GameAction DecidePlacing(PlayerView view, IRng rng)
        {
            bool canPlace = HasKind(view, ActionKind.PlaceCard);
            bool canBid = HasKind(view, ActionKind.Bid);

            if (canBid)
            {
                int desired = DesiredBid(view, rng);
                bool worthBidding = desired > view.HighestBid;
                // 手札が尽きているなら宣言以外に選択肢がない。
                bool forced = !canPlace;
                if (forced || (worthBidding && rng.NextDouble() >= _personality.ExtraCardChance))
                {
                    int amount = forced ? Math.Max(desired, view.HighestBid + 1) : desired;
                    return ClampedBid(view, amount);
                }
            }

            if (canPlace) return ChoosePlacement(view, rng);

            // 置けず宣言もできない状況は本来起こらないが、念のため合法手にフォールバックする。
            return view.LegalActions[0];
        }

        private GameAction ChoosePlacement(PlayerView view, IRng rng)
        {
            bool firstCardOfRound = view.MyStack.Length == 0;

            // 性格に応じてラウンド 1 枚目にスカルを置く。ここが最大の「読ませどころ」。
            if (firstCardOfRound && rng.NextDouble() < _personality.SkullEarly && CanPlace(view, CardType.Skull))
            {
                return GameAction.PlaceCard(view.Viewer, CardType.Skull);
            }

            bool crownFirst = rng.NextDouble() < _personality.CrownFirst;
            if (crownFirst && CanPlace(view, CardType.Crown))
            {
                return GameAction.PlaceCard(view.Viewer, CardType.Crown);
            }
            if (CanPlace(view, CardType.Rose)) return GameAction.PlaceCard(view.Viewer, CardType.Rose);
            if (CanPlace(view, CardType.Crown)) return GameAction.PlaceCard(view.Viewer, CardType.Crown);
            return GameAction.PlaceCard(view.Viewer, CardType.Skull);
        }

        // ---------------------------------------------------------------- bidding

        private GameAction DecideBidding(PlayerView view, IRng rng)
        {
            bool canPass = HasKind(view, ActionKind.Pass);

            // 自分の山にスカルがあると、自分の山を全部めくる時点で必ず失敗する。宣言は常に損。
            if (view.MyStackContainsSkull() && canPass)
            {
                return GameAction.Pass(view.Viewer);
            }

            int desired = DesiredBid(view, rng);

            if (desired > view.HighestBid && IsLegalBid(view, Math.Min(desired, view.TotalOnTable)))
            {
                return ClampedBid(view, desired);
            }

            if (canPass) return GameAction.Pass(view.Viewer);
            return ClampedBid(view, view.HighestBid + 1);
        }

        /// <summary>
        /// 降りることの機会損失。
        ///
        /// 「降りれば損得ゼロ」と評価すると、AI は自分が確実に通せる最小の枚数しか宣言せず、
        /// 競りが危険域のはるか手前で終わってチャレンジがほぼ必ず成功してしまう。
        /// 実際には降りればほぼ確実に他の誰かが得点するので、降りること自体にコストがある。
        /// この値が大きいほど competitive に吊り上がる。
        /// </summary>
        private const double PassPenalty = 0.35;

        /// <summary>
        /// 今の盤面で宣言したい枚数。0 は「宣言したくない(降りる)」。
        ///
        ///   期待値 = 成功確率 * 得点の価値 - 失敗確率 * カードを失う痛み
        ///
        /// が「降りる」を上回る宣言の範囲を求め、その中から性格に応じて
        /// 最小(+1 で張り付く)か最大(高く吊り上げて降ろさせる)を選ぶ。
        /// 期待値は宣言枚数に対して単調減少なので、受け入れ可能な範囲は連続している。
        /// </summary>
        private int DesiredBid(PlayerView view, IRng rng)
        {
            if (view.MyStackContainsSkull()) return 0;

            double gain = _personality.BidAggression;
            // RiskTolerance が高い性格ほどカードを失う痛みを軽く見る。
            double cost = 2.0 * (1.0 - _personality.RiskTolerance);

            int minAcceptable = 0;
            int maxAcceptable = 0;

            for (int amount = view.HighestBid + 1; amount <= view.TotalOnTable; amount++)
            {
                double success = Odds.SurvivalForValue(view, amount);
                double expectedValue = success * gain - (1.0 - success) * cost;
                if (expectedValue <= -PassPenalty) break;

                if (minAcceptable == 0) minAcceptable = amount;
                maxAcceptable = amount;
            }

            if (minAcceptable == 0) return 0;
            return rng.NextDouble() < _personality.JumpChance ? maxAcceptable : minAcceptable;
        }

        private GameAction ClampedBid(PlayerView view, int amount)
        {
            if (amount < view.HighestBid + 1) amount = view.HighestBid + 1;
            if (amount > view.TotalOnTable) amount = view.TotalOnTable;
            return GameAction.Bid(view.Viewer, amount);
        }

        // ---------------------------------------------------------------- challenge

        private GameAction DecideFlip(PlayerView view, IRng rng)
        {
            // 自分の山が残っているうちは選択肢がない(合法手が 1 つだけ返る)。
            if (view.LegalActions.Length == 1) return view.LegalActions[0];

            GameAction best = view.LegalActions[0];
            double bestSafety = -1.0;
            for (int i = 0; i < view.LegalActions.Length; i++)
            {
                var action = view.LegalActions[i];
                if (action.Kind != ActionKind.Flip) continue;

                double safety = Odds.SafeChance(view, action.Target);
                // 完全同値のときは順番の偏りを消すために乱数で決める。
                if (safety > bestSafety || (Math.Abs(safety - bestSafety) < 1e-9 && rng.NextInt(2) == 0))
                {
                    bestSafety = safety;
                    best = action;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- discard

        private GameAction DecideDiscard(PlayerView view)
        {
            // スカルを残せばブラフの脅しは残るが、自分の宣言が縛られる。性格で分岐させる。
            CardType[] order = _personality.KeepsSkull
                ? new[] { CardType.Rose, CardType.Crown, CardType.Skull }
                : new[] { CardType.Skull, CardType.Rose, CardType.Crown };

            for (int i = 0; i < order.Length; i++)
            {
                for (int j = 0; j < view.LegalActions.Length; j++)
                {
                    var action = view.LegalActions[j];
                    if (action.Kind == ActionKind.Discard && action.CardType == order[i]) return action;
                }
            }
            return view.LegalActions[0];
        }

        // ---------------------------------------------------------------- helpers

        private static bool HasKind(PlayerView view, ActionKind kind)
        {
            for (int i = 0; i < view.LegalActions.Length; i++)
            {
                if (view.LegalActions[i].Kind == kind) return true;
            }
            return false;
        }

        private static bool CanPlace(PlayerView view, CardType card)
        {
            for (int i = 0; i < view.LegalActions.Length; i++)
            {
                var action = view.LegalActions[i];
                if (action.Kind == ActionKind.PlaceCard && action.CardType == card) return true;
            }
            return false;
        }

        private static bool IsLegalBid(PlayerView view, int amount)
        {
            for (int i = 0; i < view.LegalActions.Length; i++)
            {
                var action = view.LegalActions[i];
                if (action.Kind == ActionKind.Bid && action.Amount == amount) return true;
            }
            return false;
        }
    }
}
