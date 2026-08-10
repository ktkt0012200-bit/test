using System;

namespace SkullDive.Ai
{
    /// <summary>
    /// AI の性格。ソロプレイの面白さはここで決まる。
    ///
    /// 設計意図: AI を「強くする」のではなく「読めるようにする」。
    /// 各性格は一貫した癖を持ち、プレイヤーはその癖を学習することで勝てるようになる。
    /// Tell は UI にそのまま出せるヒント文で、これが上達の教科書になる。
    /// </summary>
    [Serializable]
    public sealed class AiPersonality
    {
        public string Id;
        public string Name;

        /// <summary>UI に出す「この相手の癖」。プレイヤーが読みを学ぶための手がかり。</summary>
        public string Tell;

        /// <summary>ラウンドの 1 枚目にスカルを置く確率。</summary>
        public double SkullEarly = 0.25;

        /// <summary>クラウンをバラより先に置く確率。</summary>
        public double CrownFirst = 0.35;

        /// <summary>得点 1 点の価値。大きいほど無理をしてでも宣言を取りにいく。</summary>
        public double BidAggression = 0.85;

        /// <summary>宣言できる状況でも、あえてもう 1 枚置く確率。</summary>
        public double ExtraCardChance = 0.5;

        /// <summary>
        /// カードを 1 枚失うことをどれだけ軽く見るか。大きいほど痛みを感じず、危険な宣言をする。
        /// </summary>
        public double RiskTolerance = 0.28;

        /// <summary>
        /// 受け入れ可能な範囲の上限まで一気に吊り上げる確率。
        /// 0 に近いほど「+1 で張り付く」性格になり、高いほど高値で他人を降ろしにかかる。
        /// </summary>
        public double JumpChance = 0.25;

        /// <summary>カードを失うとき、スカルを残してバラを捨てるか。</summary>
        public bool KeepsSkull = true;

        /// <summary>難易度の目安。1=やさしい 3=むずかしい。</summary>
        public int Difficulty = 2;

        public static AiPersonality Timid()
        {
            return new AiPersonality
            {
                Id = "timid",
                Name = "臆病者",
                Tell = "スカルを最後まで抱える。1 枚目はほぼ安全だが、山が伸びたら危ない。",
                SkullEarly = 0.05,
                CrownFirst = 0.20,
                // 「弱さ」ではなく「癖の読みやすさ」で難易度を表現したいので、勝負には絡ませる。
                BidAggression = 0.75,
                ExtraCardChance = 0.60,
                RiskTolerance = 0.26,
                JumpChance = 0.10,
                KeepsSkull = false,
                Difficulty = 1,
            };
        }

        public static AiPersonality Gambler()
        {
            return new AiPersonality
            {
                Id = "gambler",
                Name = "博打屋",
                Tell = "1 枚目からスカルを置いてくる。序盤の山には触るな。",
                SkullEarly = 0.55,
                CrownFirst = 0.50,
                BidAggression = 1.05,
                ExtraCardChance = 0.25,
                RiskTolerance = 0.40,
                JumpChance = 0.50,
                KeepsSkull = true,
                Difficulty = 2,
            };
        }

        public static AiPersonality Copycat()
        {
            return new AiPersonality
            {
                Id = "copycat",
                Name = "真似っ子",
                Tell = "直前の宣言に +1 で必ず乗せてくる。降ろすには高く吊り上げるしかない。",
                SkullEarly = 0.20,
                CrownFirst = 0.35,
                BidAggression = 0.95,
                ExtraCardChance = 0.40,
                // +1 で張り付く性格は、競りの最後に「自分の限界ぎりぎりの宣言」で
                // 勝ち残りやすい = 一番危ない位置を引きやすい。
                // その分、降りる判断は早めにしておかないと失敗ばかりになる。
                RiskTolerance = 0.24,
                JumpChance = 0.02,
                KeepsSkull = true,
                Difficulty = 2,
            };
        }

        public static AiPersonality Steady()
        {
            return new AiPersonality
            {
                Id = "steady",
                Name = "堅実家",
                Tell = "確率に従って淡々と降りる。ブラフはほぼ通らない。",
                SkullEarly = 0.25,
                CrownFirst = 0.35,
                BidAggression = 0.85,
                ExtraCardChance = 0.50,
                RiskTolerance = 0.28,
                JumpChance = 0.25,
                KeepsSkull = true,
                Difficulty = 3,
            };
        }

        /// <summary>ソロモードの標準卓(自分 + この 3 人)。</summary>
        public static AiPersonality[] DefaultTable()
        {
            return new[] { Timid(), Gambler(), Copycat() };
        }

        public static AiPersonality[] All()
        {
            return new[] { Timid(), Gambler(), Copycat(), Steady() };
        }
    }
}
