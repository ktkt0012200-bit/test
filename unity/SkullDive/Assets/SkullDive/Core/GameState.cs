using System;
using System.Collections.Generic;

namespace SkullDive.Core
{
    /// <summary>int 値はワイヤーフォーマットに載るため変更禁止。</summary>
    public enum Phase
    {
        /// <summary>カードを伏せて置くフェーズ。</summary>
        Placing = 0,

        /// <summary>宣言フェーズ。誰かが宣言を始めたらもうカードは置けない。</summary>
        Bidding = 1,

        /// <summary>チャレンジャーがカードをめくっているフェーズ。</summary>
        Challenging = 2,

        /// <summary>自分のスカルを踏んだので、失うカードを本人が選んでいる。</summary>
        AwaitingDiscard = 3,

        /// <summary>ラウンド決着。演出を見せる間の待ちで、AdvanceRound で次ラウンドへ。</summary>
        RoundEnd = 4,

        /// <summary>マッチ終了。</summary>
        MatchEnd = 5,
    }

    /// <summary>プレイヤー 1 人の完全な状態(秘匿情報を含む)。</summary>
    public sealed class PlayerState
    {
        public int Index;

        /// <summary>まだ場に出していない手札。順序が情報を持たないよう常にソートしておく。</summary>
        public List<CardType> Hand = new List<CardType>();

        /// <summary>場に伏せた山。index 0 が一番下、末尾が一番上。</summary>
        public List<CardType> Stack = new List<CardType>();

        /// <summary>山の上から何枚が公開済みか。</summary>
        public int FlippedCount;

        public int Points;
        public bool Eliminated;

        /// <summary>宣言フェーズで降りたか。降りたら再参加できない。</summary>
        public bool HasPassed;

        /// <summary>このラウンドで宣言した枚数(0 は未宣言)。</summary>
        public int CurrentBid;

        /// <summary>マッチ中に所持しているカードの総数(手札 + 場の山)。0 になると脱落。</summary>
        public int CardsOwned
        {
            get { return Hand.Count + Stack.Count; }
        }

        public bool IsActive
        {
            get { return !Eliminated; }
        }

        public bool StackContainsSkull
        {
            get
            {
                for (int i = 0; i < Stack.Count; i++)
                {
                    if (Stack[i] == CardType.Skull) return true;
                }
                return false;
            }
        }

        public PlayerState Clone()
        {
            return new PlayerState
            {
                Index = Index,
                Hand = new List<CardType>(Hand),
                Stack = new List<CardType>(Stack),
                FlippedCount = FlippedCount,
                Points = Points,
                Eliminated = Eliminated,
                HasPassed = HasPassed,
                CurrentBid = CurrentBid,
            };
        }
    }

    /// <summary>
    /// 権威ある完全な盤面。サーバとソロモードのみが保持する。
    /// クライアントへは必ず ViewRedactor で PlayerView に落としてから渡す。
    /// </summary>
    public sealed class GameState
    {
        public GameConfig Config;
        public PlayerState[] Players;

        public Phase Phase;
        public int RoundNumber;

        /// <summary>手番のプレイヤー。</summary>
        public int CurrentPlayer;

        /// <summary>このラウンドの開始プレイヤー。</summary>
        public int RoundStarter;

        /// <summary>全員が 1 枚目を置き終えたか。これが false の間は宣言できない。</summary>
        public bool FirstPassComplete;

        /// <summary>1 周目に置かれた枚数。</summary>
        public int PlacementsInFirstPass;

        public int HighestBid;
        public int HighestBidder = -1;
        public int Challenger = -1;

        /// <summary>めくったカードの累計カウント値(クラウンは CrownValue 分)。</summary>
        public int FlipValue;

        /// <summary>めくった枚数。</summary>
        public int FlipsMade;

        /// <summary>チャレンジャーが自分の山を全部めくり終えたか。</summary>
        public bool OwnStackCleared;

        /// <summary>直近のチャレンジで踏んだスカルの持ち主。踏んでいなければ -1。</summary>
        public int LastSkullOwner = -1;

        /// <summary>直近のチャレンジが成功したか。</summary>
        public bool LastChallengeSucceeded;

        public int Winner = -1;

        public int PlayerCount
        {
            get { return Players.Length; }
        }

        /// <summary>場に出ている伏せカードの総枚数。宣言の上限値でもある。</summary>
        public int TotalOnTable
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Players.Length; i++)
                {
                    total += Players[i].Stack.Count;
                }
                return total;
            }
        }

        public int ActivePlayerCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Players.Length; i++)
                {
                    if (Players[i].IsActive) n++;
                }
                return n;
            }
        }

        public bool IsOver
        {
            get { return Phase == Phase.MatchEnd; }
        }

        /// <summary>時計回りに次のアクティブなプレイヤー。全員脱落していれば -1。</summary>
        public int NextActive(int from)
        {
            for (int step = 1; step <= Players.Length; step++)
            {
                int candidate = (from + step) % Players.Length;
                if (Players[candidate].IsActive) return candidate;
            }
            return -1;
        }

        public GameState Clone()
        {
            var clone = new GameState
            {
                Config = Config.Clone(),
                Players = new PlayerState[Players.Length],
                Phase = Phase,
                RoundNumber = RoundNumber,
                CurrentPlayer = CurrentPlayer,
                RoundStarter = RoundStarter,
                FirstPassComplete = FirstPassComplete,
                PlacementsInFirstPass = PlacementsInFirstPass,
                HighestBid = HighestBid,
                HighestBidder = HighestBidder,
                Challenger = Challenger,
                FlipValue = FlipValue,
                FlipsMade = FlipsMade,
                OwnStackCleared = OwnStackCleared,
                LastSkullOwner = LastSkullOwner,
                LastChallengeSucceeded = LastChallengeSucceeded,
                Winner = Winner,
            };
            for (int i = 0; i < Players.Length; i++)
            {
                clone.Players[i] = Players[i].Clone();
            }
            return clone;
        }
    }
}
