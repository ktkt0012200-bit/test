using System;
using System.Collections.Generic;

namespace SkullDive.Core
{
    /// <summary>他プレイヤーについて公開されている情報だけをまとめたもの。</summary>
    [Serializable]
    public sealed class PlayerPublicView
    {
        public int Index;
        public string Name;

        /// <summary>手札の枚数だけが公開される。中身は見えない。</summary>
        public int HandCount;

        /// <summary>場に伏せた山の枚数。</summary>
        public int StackCount;

        /// <summary>そのうち公開済みの枚数。</summary>
        public int FlippedCount;

        /// <summary>公開済みカード。index 0 が山の一番上。未公開分は含まない。</summary>
        public int[] RevealedFromTop = new int[0];

        public int Points;
        public bool Eliminated;
        public bool HasPassed;
        public int CurrentBid;

        /// <summary>マッチ中に所持しているカードの総数。</summary>
        public int CardsOwned;
    }

    /// <summary>
    /// 1 人のプレイヤーに送っていい情報だけを持つ盤面。
    /// クライアントと AI はこれしか受け取らないので、構造的にカンニングができない。
    /// </summary>
    [Serializable]
    public sealed class PlayerView
    {
        /// <summary>この View の持ち主。観戦者の場合は -1。</summary>
        public int Viewer;

        public GameConfig Config;

        public Phase Phase;
        public int RoundNumber;
        public int CurrentPlayer;
        public int RoundStarter;
        public bool FirstPassComplete;

        public int HighestBid;
        public int HighestBidder;
        public int Challenger;

        public int FlipValue;
        public int FlipsMade;
        public bool OwnStackCleared;

        public int LastSkullOwner;
        public bool LastChallengeSucceeded;
        public int Winner;

        /// <summary>場の伏せカード総数。宣言できる上限。</summary>
        public int TotalOnTable;

        public PlayerPublicView[] Players = new PlayerPublicView[0];

        /// <summary>自分の手札(自分だけが見える)。</summary>
        public int[] MyHand = new int[0];

        /// <summary>自分が伏せた山。index 0 が一番下。自分は自分の置いたカードを知っている。</summary>
        public int[] MyStack = new int[0];

        /// <summary>この View の持ち主が今取れる手。手番でなければ空。</summary>
        public GameAction[] LegalActions = new GameAction[0];

        public bool IsMyTurn
        {
            get { return LegalActions != null && LegalActions.Length > 0; }
        }

        public PlayerPublicView Me
        {
            get { return (Viewer >= 0 && Viewer < Players.Length) ? Players[Viewer] : null; }
        }

        public List<CardType> MyHandCards()
        {
            var cards = new List<CardType>(MyHand.Length);
            for (int i = 0; i < MyHand.Length; i++) cards.Add((CardType)MyHand[i]);
            return cards;
        }

        public bool MyStackContainsSkull()
        {
            for (int i = 0; i < MyStack.Length; i++)
            {
                if ((CardType)MyStack[i] == CardType.Skull) return true;
            }
            return false;
        }

        /// <summary>自分の山をすべてめくったときに得られるカウント値。スカルが入っていれば -1。</summary>
        public int MyStackValue()
        {
            int total = 0;
            for (int i = 0; i < MyStack.Length; i++)
            {
                var card = (CardType)MyStack[i];
                if (card == CardType.Skull) return -1;
                total += Config.ValueOf(card, true);
            }
            return total;
        }
    }

    /// <summary>
    /// GameState から PlayerView を作る唯一の経路。
    /// ここを通さずに GameState をシリアライズすると全プレイヤーの伏せカードが漏れる。
    /// </summary>
    public static class ViewRedactor
    {
        public static PlayerView Redact(GameState state, int viewer, string[] names = null)
        {
            if (state == null) throw new ArgumentNullException("state");

            var view = new PlayerView
            {
                Viewer = viewer,
                Config = state.Config.Clone(),
                Phase = state.Phase,
                RoundNumber = state.RoundNumber,
                CurrentPlayer = state.CurrentPlayer,
                RoundStarter = state.RoundStarter,
                FirstPassComplete = state.FirstPassComplete,
                HighestBid = state.HighestBid,
                HighestBidder = state.HighestBidder,
                Challenger = state.Challenger,
                FlipValue = state.FlipValue,
                FlipsMade = state.FlipsMade,
                OwnStackCleared = state.OwnStackCleared,
                LastSkullOwner = state.LastSkullOwner,
                LastChallengeSucceeded = state.LastChallengeSucceeded,
                Winner = state.Winner,
                TotalOnTable = state.TotalOnTable,
                Players = new PlayerPublicView[state.PlayerCount],
            };

            for (int i = 0; i < state.PlayerCount; i++)
            {
                var player = state.Players[i];
                view.Players[i] = new PlayerPublicView
                {
                    Index = i,
                    Name = (names != null && i < names.Length) ? names[i] : ("P" + i),
                    HandCount = player.Hand.Count,
                    StackCount = player.Stack.Count,
                    FlippedCount = player.FlippedCount,
                    RevealedFromTop = RevealedFromTop(player),
                    Points = player.Points,
                    Eliminated = player.Eliminated,
                    HasPassed = player.HasPassed,
                    CurrentBid = player.CurrentBid,
                    CardsOwned = player.CardsOwned,
                };
            }

            if (viewer >= 0 && viewer < state.PlayerCount)
            {
                var self = state.Players[viewer];
                view.MyHand = ToIntArray(self.Hand);
                view.MyStack = ToIntArray(self.Stack);
                view.LegalActions = GameEngine.GetLegalActions(state, viewer).ToArray();
            }

            return view;
        }

        /// <summary>公開済みカードを山の上から順に並べる。</summary>
        private static int[] RevealedFromTop(PlayerState player)
        {
            int count = Math.Min(player.FlippedCount, player.Stack.Count);
            var revealed = new int[count];
            for (int i = 0; i < count; i++)
            {
                revealed[i] = (int)player.Stack[player.Stack.Count - 1 - i];
            }
            return revealed;
        }

        private static int[] ToIntArray(List<CardType> cards)
        {
            var array = new int[cards.Count];
            for (int i = 0; i < cards.Count; i++) array[i] = (int)cards[i];
            return array;
        }
    }
}
