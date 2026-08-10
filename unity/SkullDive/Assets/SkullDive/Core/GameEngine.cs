using System;
using System.Collections.Generic;

namespace SkullDive.Core
{
    /// <summary>
    /// ルールの全体。副作用は引数で渡された state / events / rng に限られ、
    /// 静的な可変状態を一切持たないので Unity・サーバ・テストで同一に動く。
    /// </summary>
    public static class GameEngine
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 6;

        // ---------------------------------------------------------------- setup

        /// <summary>
        /// マッチを作る。events を渡すと最初のラウンド開始イベントが積まれるので、
        /// 初期状態の配信もラウンド送りと同じ経路で扱える。
        /// </summary>
        public static GameState CreateMatch(GameConfig config, int playerCount, int startingPlayer = 0,
            List<GameEvent> events = null)
        {
            if (config == null) throw new ArgumentNullException("config");
            config.Validate();
            if (playerCount < MinPlayers || playerCount > MaxPlayers)
                throw new ArgumentOutOfRangeException("playerCount", "player count must be " + MinPlayers + ".." + MaxPlayers);
            if (startingPlayer < 0 || startingPlayer >= playerCount)
                throw new ArgumentOutOfRangeException("startingPlayer");

            var state = new GameState
            {
                Config = config.Clone(),
                Players = new PlayerState[playerCount],
                RoundNumber = 0,
            };

            for (int i = 0; i < playerCount; i++)
            {
                var player = new PlayerState { Index = i };
                for (int n = 0; n < config.RoseCount; n++) player.Hand.Add(CardType.Rose);
                for (int n = 0; n < config.CrownCount; n++) player.Hand.Add(CardType.Crown);
                for (int n = 0; n < config.SkullCount; n++) player.Hand.Add(CardType.Skull);
                state.Players[i] = player;
            }

            // RoundStarter は StartRound 内で「前ラウンドのチャレンジャーの次」として解決されるため、
            // 初回だけ直接指定する。
            state.Challenger = -1;
            state.RoundStarter = startingPlayer;
            BeginRound(state, startingPlayer, events);
            return state;
        }

        // ---------------------------------------------------------------- queries

        /// <summary>今アクションを取るべき主体。RoundEnd / MatchEnd ではシステム(-1)。</summary>
        public static int CurrentActor(GameState state)
        {
            switch (state.Phase)
            {
                case Phase.Placing:
                case Phase.Bidding:
                    return state.CurrentPlayer;
                case Phase.Challenging:
                case Phase.AwaitingDiscard:
                    return state.Challenger;
                default:
                    return GameAction.SystemPlayer;
            }
        }

        public static List<GameAction> GetLegalActions(GameState state, int player)
        {
            var list = new List<GameAction>();
            GetLegalActions(state, player, list);
            return list;
        }

        /// <summary>合法手を into に詰める(呼び出し側で Clear される)。GC を避けたい経路のために用意。</summary>
        public static void GetLegalActions(GameState state, int player, List<GameAction> into)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (into == null) throw new ArgumentNullException("into");
            into.Clear();

            if (state.Phase == Phase.MatchEnd) return;

            if (state.Phase == Phase.RoundEnd)
            {
                // ラウンド決着の演出を見せるための間。次ラウンドへ進めるのはシステム側の責務。
                if (player == GameAction.SystemPlayer) into.Add(GameAction.AdvanceRound());
                return;
            }

            if (player < 0 || player >= state.PlayerCount) return;
            if (CurrentActor(state) != player) return;

            var self = state.Players[player];
            if (!self.IsActive) return;

            switch (state.Phase)
            {
                case Phase.Placing:
                    AddPlacingActions(state, self, into);
                    break;
                case Phase.Bidding:
                    AddBiddingActions(state, self, into);
                    break;
                case Phase.Challenging:
                    AddFlipActions(state, self, into);
                    break;
                case Phase.AwaitingDiscard:
                    AddDiscardActions(self, into);
                    break;
            }
        }

        private static void AddPlacingActions(GameState state, PlayerState self, List<GameAction> into)
        {
            // 手札があるなら伏せて置ける。同じ種類は 1 手としてまとめる。
            AddDistinctCardActions(self.Hand, self.Index, ActionKind.PlaceCard, into);

            // 1 周目は全員が必ず 1 枚置く。宣言できるのはその後。
            if (!state.FirstPassComplete) return;

            int max = state.TotalOnTable;
            for (int amount = state.HighestBid + 1; amount <= max; amount++)
            {
                into.Add(GameAction.Bid(self.Index, amount));
            }
        }

        private static void AddBiddingActions(GameState state, PlayerState self, List<GameAction> into)
        {
            int max = state.TotalOnTable;
            for (int amount = state.HighestBid + 1; amount <= max; amount++)
            {
                into.Add(GameAction.Bid(self.Index, amount));
            }
            // 最高宣言者は自分の宣言に上乗せできないし、降りることもできない(他全員のパスを待つ)。
            if (self.Index != state.HighestBidder)
            {
                into.Add(GameAction.Pass(self.Index));
            }
        }

        private static void AddFlipActions(GameState state, PlayerState self, List<GameAction> into)
        {
            // 自分の山を全部めくり終えるまでは他人の山に触れない。
            if (!state.OwnStackCleared)
            {
                if (UnflippedCount(self) > 0) into.Add(GameAction.Flip(self.Index, self.Index));
                return;
            }

            for (int i = 0; i < state.PlayerCount; i++)
            {
                if (i == self.Index) continue;
                if (UnflippedCount(state.Players[i]) > 0) into.Add(GameAction.Flip(self.Index, i));
            }
        }

        private static void AddDiscardActions(PlayerState self, List<GameAction> into)
        {
            // 失うカードは手札と場の山の両方から選べる(ラウンド終了時にどちらも手札に戻るため)。
            var owned = new List<CardType>(self.Hand);
            owned.AddRange(self.Stack);
            AddDistinctCardActions(owned, self.Index, ActionKind.Discard, into);
        }

        private static void AddDistinctCardActions(List<CardType> cards, int player, ActionKind kind, List<GameAction> into)
        {
            bool rose = false, crown = false, skull = false;
            for (int i = 0; i < cards.Count; i++)
            {
                switch (cards[i])
                {
                    case CardType.Rose: rose = true; break;
                    case CardType.Crown: crown = true; break;
                    case CardType.Skull: skull = true; break;
                }
            }
            if (rose) into.Add(Make(kind, player, CardType.Rose));
            if (crown) into.Add(Make(kind, player, CardType.Crown));
            if (skull) into.Add(Make(kind, player, CardType.Skull));
        }

        private static GameAction Make(ActionKind kind, int player, CardType card)
        {
            return kind == ActionKind.PlaceCard
                ? GameAction.PlaceCard(player, card)
                : GameAction.Discard(player, card);
        }

        public static bool IsLegal(GameState state, GameAction action)
        {
            var legal = new List<GameAction>();
            GetLegalActions(state, action.Player, legal);
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].Equals(action)) return true;
            }
            return false;
        }

        private static int UnflippedCount(PlayerState player)
        {
            return player.Stack.Count - player.FlippedCount;
        }

        // ---------------------------------------------------------------- apply

        /// <summary>
        /// アクションを適用する。不正なアクションは InvalidActionException になるので、
        /// サーバは受信したアクションをそのまま渡すだけでよい。
        /// </summary>
        public static void Apply(GameState state, GameAction action, IRng rng, List<GameEvent> events)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (rng == null) throw new ArgumentNullException("rng");
            if (events == null) throw new ArgumentNullException("events");

            if (!IsLegal(state, action))
            {
                throw new InvalidActionException("illegal action " + action + " in phase " + state.Phase);
            }

            switch (action.Kind)
            {
                case ActionKind.PlaceCard:
                    ApplyPlace(state, action, events);
                    break;
                case ActionKind.Bid:
                    ApplyBid(state, action, events);
                    break;
                case ActionKind.Pass:
                    ApplyPass(state, action, events);
                    break;
                case ActionKind.Flip:
                    ApplyFlip(state, action, rng, events);
                    break;
                case ActionKind.Discard:
                    ApplyDiscard(state, action, events);
                    break;
                case ActionKind.AdvanceRound:
                    ApplyAdvanceRound(state, events);
                    break;
                default:
                    throw new InvalidActionException("unknown action kind " + action.Kind);
            }
        }

        private static void ApplyPlace(GameState state, GameAction action, List<GameEvent> events)
        {
            var self = state.Players[action.Player];
            var card = action.CardType;

            self.Hand.Remove(card);
            self.Stack.Add(card);
            events.Add(GameEvent.Make(GameEventKind.CardPlaced, action.Player, card: (int)card,
                amount: self.Stack.Count, extra: state.TotalOnTable));

            if (!state.FirstPassComplete)
            {
                state.PlacementsInFirstPass++;
                if (state.PlacementsInFirstPass >= state.ActivePlayerCount)
                {
                    state.FirstPassComplete = true;
                }
            }

            state.CurrentPlayer = state.NextActive(state.CurrentPlayer);
        }

        private static void ApplyBid(GameState state, GameAction action, List<GameEvent> events)
        {
            var self = state.Players[action.Player];
            state.Phase = Phase.Bidding;
            state.HighestBid = action.Amount;
            state.HighestBidder = action.Player;
            self.CurrentBid = action.Amount;
            events.Add(GameEvent.Make(GameEventKind.BidMade, action.Player, amount: action.Amount,
                extra: state.TotalOnTable));

            // 場の全枚数を宣言したら、それ以上の上乗せは不可能なので即チャレンジ。
            if (action.Amount >= state.TotalOnTable)
            {
                BeginChallenge(state, action.Player, events);
                return;
            }

            AdvanceBiddingTurn(state, events);
        }

        private static void ApplyPass(GameState state, GameAction action, List<GameEvent> events)
        {
            state.Players[action.Player].HasPassed = true;
            events.Add(GameEvent.Make(GameEventKind.Passed, action.Player));
            AdvanceBiddingTurn(state, events);
        }

        /// <summary>次に宣言できるプレイヤーを探す。誰も残っていなければチャレンジ開始。</summary>
        private static void AdvanceBiddingTurn(GameState state, List<GameEvent> events)
        {
            for (int step = 1; step <= state.PlayerCount; step++)
            {
                int candidate = (state.CurrentPlayer + step) % state.PlayerCount;
                var player = state.Players[candidate];
                if (!player.IsActive) continue;
                if (player.HasPassed) continue;
                if (candidate == state.HighestBidder) continue;
                state.CurrentPlayer = candidate;
                return;
            }

            BeginChallenge(state, state.HighestBidder, events);
        }

        private static void BeginChallenge(GameState state, int challenger, List<GameEvent> events)
        {
            state.Phase = Phase.Challenging;
            state.Challenger = challenger;
            state.CurrentPlayer = challenger;
            state.FlipValue = 0;
            state.FlipsMade = 0;
            state.LastSkullOwner = -1;
            state.OwnStackCleared = UnflippedCount(state.Players[challenger]) == 0;
            events.Add(GameEvent.Make(GameEventKind.ChallengeStarted, challenger, amount: state.HighestBid,
                extra: state.TotalOnTable));
        }

        private static void ApplyFlip(GameState state, GameAction action, IRng rng, List<GameEvent> events)
        {
            var target = state.Players[action.Target];
            bool ownStack = action.Target == state.Challenger;

            // 山の上から順にめくる。
            int index = target.Stack.Count - 1 - target.FlippedCount;
            CardType card = target.Stack[index];
            target.FlippedCount++;
            state.FlipsMade++;

            if (card == CardType.Skull)
            {
                events.Add(GameEvent.Make(GameEventKind.CardRevealed, action.Target, card: (int)card,
                    amount: state.FlipValue, extra: state.FlipsMade));
                FailChallenge(state, action.Target, rng, events);
                return;
            }

            state.FlipValue += state.Config.ValueOf(card, ownStack);
            events.Add(GameEvent.Make(GameEventKind.CardRevealed, action.Target, card: (int)card,
                amount: state.FlipValue, extra: state.FlipsMade));

            if (ownStack && UnflippedCount(target) == 0)
            {
                state.OwnStackCleared = true;
            }

            // 自分の山を全部めくり切るまでは成功判定しない(手札切れで強制宣言した場合に
            // 自分のスカルを回避できてしまうのを防ぐ)。
            if (state.OwnStackCleared && state.FlipValue >= state.HighestBid)
            {
                SucceedChallenge(state, events);
                return;
            }

            if (GetLegalActions(state, state.Challenger).Count == 0)
            {
                // 宣言上限が場の総枚数なので、スカルを踏まない限りここには到達しないはず。
                throw new InvalidOperationException(
                    "challenge deadlocked: bid=" + state.HighestBid + " value=" + state.FlipValue);
            }
        }

        private static void SucceedChallenge(GameState state, List<GameEvent> events)
        {
            var challenger = state.Players[state.Challenger];
            challenger.Points++;
            state.LastChallengeSucceeded = true;
            events.Add(GameEvent.Make(GameEventKind.ChallengeSucceeded, state.Challenger,
                amount: challenger.Points, extra: state.HighestBid));

            // 残り 1 枚でのチャレンジ成功は即勝利。
            if (state.Config.OneCardWinEnabled && challenger.CardsOwned == 1)
            {
                EndMatch(state, state.Challenger, events);
                return;
            }

            if (challenger.Points >= state.Config.PointsToWin)
            {
                EndMatch(state, state.Challenger, events);
                return;
            }

            EndRound(state, events);
        }

        private static void FailChallenge(GameState state, int skullOwner, IRng rng, List<GameEvent> events)
        {
            state.LastChallengeSucceeded = false;
            state.LastSkullOwner = skullOwner;
            events.Add(GameEvent.Make(GameEventKind.ChallengeFailed, state.Challenger, target: skullOwner,
                amount: state.HighestBid, extra: state.FlipValue));

            // 自分のスカルを踏んだ場合だけ、失うカードを本人が選べる。
            if (skullOwner == state.Challenger && state.Config.ChooseDiscardOnOwnSkull)
            {
                state.Phase = Phase.AwaitingDiscard;
                return;
            }

            DiscardRandom(state, state.Challenger, rng, events);
            EndRound(state, events);
        }

        private static void ApplyDiscard(GameState state, GameAction action, List<GameEvent> events)
        {
            DiscardCard(state, action.Player, action.CardType, events);
            EndRound(state, events);
        }

        private static void DiscardRandom(GameState state, int playerIndex, IRng rng, List<GameEvent> events)
        {
            var player = state.Players[playerIndex];
            int owned = player.CardsOwned;
            if (owned <= 0) return;

            int pick = rng.NextInt(owned);
            CardType card = pick < player.Hand.Count
                ? player.Hand[pick]
                : player.Stack[pick - player.Hand.Count];
            DiscardCard(state, playerIndex, card, events);
        }

        private static void DiscardCard(GameState state, int playerIndex, CardType card, List<GameEvent> events)
        {
            var player = state.Players[playerIndex];

            // 手札から優先的に取り除く。無ければ場の山から。
            if (!player.Hand.Remove(card))
            {
                int index = player.Stack.LastIndexOf(card);
                if (index < 0) throw new InvalidActionException("player " + playerIndex + " does not own " + card);
                player.Stack.RemoveAt(index);
                if (player.FlippedCount > player.Stack.Count) player.FlippedCount = player.Stack.Count;
            }

            // 捨て札は伏せたままなので、他プレイヤーには中身が見えない(EventRedactor が落とす)。
            events.Add(GameEvent.Make(GameEventKind.CardDiscarded, playerIndex, card: (int)card,
                amount: player.CardsOwned));

            if (player.CardsOwned == 0)
            {
                player.Eliminated = true;
                events.Add(GameEvent.Make(GameEventKind.PlayerEliminated, playerIndex));
            }
        }

        // ---------------------------------------------------------------- round flow

        private static void EndRound(GameState state, List<GameEvent> events)
        {
            events.Add(GameEvent.Make(GameEventKind.RoundEnded, state.Challenger,
                amount: state.LastChallengeSucceeded ? 1 : 0, extra: state.RoundNumber));

            int active = state.ActivePlayerCount;
            if (active <= 1)
            {
                int winner = -1;
                for (int i = 0; i < state.PlayerCount; i++)
                {
                    if (state.Players[i].IsActive) winner = i;
                }
                EndMatch(state, winner, events);
                return;
            }

            state.Phase = Phase.RoundEnd;
        }

        private static void ApplyAdvanceRound(GameState state, List<GameEvent> events)
        {
            // 次ラウンドの開始者は、直前のチャレンジャー(成否は問わない)。
            // 脱落していればその左隣。
            int starter = state.Challenger;
            if (starter < 0 || !state.Players[starter].IsActive)
            {
                starter = state.NextActive(starter < 0 ? state.RoundStarter : starter);
            }
            BeginRound(state, starter, events);
        }

        private static void BeginRound(GameState state, int starter, List<GameEvent> events)
        {
            state.RoundNumber++;
            for (int i = 0; i < state.PlayerCount; i++)
            {
                var player = state.Players[i];
                // 場に伏せたカードはすべて手札に戻る。失ったカードだけが永久に減る。
                player.Hand.AddRange(player.Stack);
                player.Stack.Clear();
                player.Hand.Sort(CompareCards);
                player.FlippedCount = 0;
                player.HasPassed = false;
                player.CurrentBid = 0;
            }

            state.Phase = Phase.Placing;
            state.RoundStarter = starter;
            state.CurrentPlayer = starter;
            state.FirstPassComplete = false;
            state.PlacementsInFirstPass = 0;
            state.HighestBid = 0;
            state.HighestBidder = -1;
            state.Challenger = -1;
            state.FlipValue = 0;
            state.FlipsMade = 0;
            state.OwnStackCleared = false;
            state.LastSkullOwner = -1;

            if (events != null)
            {
                events.Add(GameEvent.Make(GameEventKind.RoundStarted, starter, extra: state.RoundNumber));
            }
        }

        private static int CompareCards(CardType a, CardType b)
        {
            return Cards.SortKey(a).CompareTo(Cards.SortKey(b));
        }

        private static void EndMatch(GameState state, int winner, List<GameEvent> events)
        {
            state.Phase = Phase.MatchEnd;
            state.Winner = winner;
            events.Add(GameEvent.Make(GameEventKind.MatchEnded, winner,
                amount: winner >= 0 ? state.Players[winner].Points : 0));
        }
    }

    /// <summary>合法手でないアクションが適用されたときに投げられる。</summary>
    public class InvalidActionException : Exception
    {
        public InvalidActionException(string message) : base(message) { }
    }
}
