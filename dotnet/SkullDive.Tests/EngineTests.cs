using System.Collections.Generic;
using SkullDive.Core;

namespace SkullDive.Tests
{
    public static class EngineTests
    {
        private static readonly List<GameEvent> Sink = new List<GameEvent>();

        private static void Do(GameState state, GameAction action)
        {
            GameEngine.Apply(state, action, new XorShiftRng(1), Sink);
        }

        /// <summary>手札 2 枚(バラ 1 + スカル 1)の最小ルール。短い決着を作りたいテスト用。</summary>
        private static GameConfig Tiny()
        {
            return new GameConfig { RoseCount = 1, CrownCount = 0, SkullCount = 1, PointsToWin = 2 };
        }

        private static bool HasBidAction(GameState state, int player)
        {
            foreach (var action in GameEngine.GetLegalActions(state, player))
            {
                if (action.Kind == ActionKind.Bid) return true;
            }
            return false;
        }

        public static void Register()
        {
            T.Section("engine/setup");

            T.Test("初期状態は 2R+1C+1S の手札とカード配置フェーズ", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 4);
                T.Eq(Phase.Placing, state.Phase, "phase");
                T.Eq(4, state.PlayerCount, "player count");
                T.Eq(1, state.RoundNumber, "round number");
                T.Eq(0, state.TotalOnTable, "nothing on table yet");
                foreach (var player in state.Players)
                {
                    T.Eq(4, player.Hand.Count, "hand size");
                    T.Eq(2, player.Hand.FindAll(c => c == CardType.Rose).Count, "roses");
                    T.Eq(1, player.Hand.FindAll(c => c == CardType.Crown).Count, "crowns");
                    T.Eq(1, player.Hand.FindAll(c => c == CardType.Skull).Count, "skulls");
                }
            });

            T.Test("プレイヤー数と設定は検証される", () =>
            {
                T.Throws<System.ArgumentOutOfRangeException>(
                    () => GameEngine.CreateMatch(GameConfig.Default(), 1), "1 人では開始できない");
                T.Throws<System.ArgumentOutOfRangeException>(
                    () => GameEngine.CreateMatch(GameConfig.Default(), 7), "7 人では開始できない");
                T.Throws<System.ArgumentException>(
                    () => GameEngine.CreateMatch(new GameConfig { RoseCount = 0, CrownCount = 0, SkullCount = 1 }, 3),
                    "安全なカードが 0 枚の設定は不正");
            });

            T.Section("engine/placing");

            T.Test("1 周目は全員が必ず 1 枚置き、その間は宣言できない", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                T.False(HasBidAction(state, 0), "1 枚目を置く前は宣言不可");

                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                T.Eq(1, state.CurrentPlayer, "手番が回る");
                T.False(state.FirstPassComplete, "まだ 1 周していない");
                T.False(HasBidAction(state, 1), "1 周目の途中は宣言不可");

                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                T.False(state.FirstPassComplete, "まだ 3 人目が置いていない");

                Do(state, GameAction.PlaceCard(2, CardType.Rose));
                T.True(state.FirstPassComplete, "3 人が置いたので 1 周完了");
                T.Eq(0, state.CurrentPlayer, "手番は開始プレイヤーに戻る");
                T.True(HasBidAction(state, 0), "1 周後は宣言できる");
            });

            T.Test("宣言の上限は場の伏せカード総数", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(2, CardType.Rose));

                T.Eq(3, state.TotalOnTable, "3 枚が場にある");
                T.True(GameEngine.IsLegal(state, GameAction.Bid(0, 3)), "3 は宣言できる");
                T.False(GameEngine.IsLegal(state, GameAction.Bid(0, 4)), "4 は宣言できない");
                T.False(GameEngine.IsLegal(state, GameAction.Bid(0, 0)), "0 は宣言できない");
            });

            T.Test("手札が尽きたプレイヤーは宣言しかできない", () =>
            {
                // 手札 2 枚の設定で 2 枚とも置くと、選択肢が宣言だけになる。
                var state = GameEngine.CreateMatch(Tiny(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(0, CardType.Skull));
                Do(state, GameAction.PlaceCard(1, CardType.Skull));

                var legal = GameEngine.GetLegalActions(state, 0);
                T.True(legal.Count > 0, "手はある");
                foreach (var action in legal) T.Eq(ActionKind.Bid, action.Kind, "宣言のみ");
            });

            T.Section("engine/bidding");

            T.Test("場の全枚数を宣言すると即チャレンジに入る", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(2, CardType.Rose));
                Do(state, GameAction.Bid(0, 3));

                T.Eq(Phase.Challenging, state.Phase, "phase");
                T.Eq(0, state.Challenger, "challenger");
            });

            T.Test("降りたプレイヤーは宣言に戻れない", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(2, CardType.Rose));
                Do(state, GameAction.Bid(0, 1));
                T.Eq(1, state.CurrentPlayer, "次の宣言者");

                Do(state, GameAction.Pass(1));
                T.Eq(2, state.CurrentPlayer, "手番は 2 へ");
                T.Eq(0, GameEngine.GetLegalActions(state, 1).Count, "降りた 1 に手はない");

                Do(state, GameAction.Pass(2));
                T.Eq(Phase.Challenging, state.Phase, "全員降りたのでチャレンジ");
                T.Eq(0, state.Challenger, "最高宣言者がチャレンジャー");
            });

            T.Test("最高宣言者は降りられず、自分に上乗せもできない", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(2, CardType.Rose));
                Do(state, GameAction.Bid(0, 1));
                Do(state, GameAction.Pass(1));

                // 手番は 2 なので 0 には手がない。
                T.Eq(0, GameEngine.GetLegalActions(state, 0).Count, "最高宣言者は待つだけ");
                T.False(GameEngine.IsLegal(state, GameAction.Pass(0)), "自分の宣言から降りられない");
            });

            T.Section("engine/challenge");

            T.Test("チャレンジャーは自分の山を先に全部めくる", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(2, CardType.Rose));
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.Bid(1, 1));
                Do(state, GameAction.Pass(2));
                Do(state, GameAction.Bid(0, 4)); // 場の全枚数なので即チャレンジ

                var legal = GameEngine.GetLegalActions(state, 0);
                T.Eq(1, legal.Count, "選択肢は自分の山だけ");
                T.Eq(GameAction.Flip(0, 0), legal[0], "自分の山をめくる");

                Do(state, GameAction.Flip(0, 0));
                T.False(state.OwnStackCleared, "自分の山はまだ 1 枚残っている");
                T.Eq(1, GameEngine.GetLegalActions(state, 0).Count, "まだ他人には触れない");

                Do(state, GameAction.Flip(0, 0));
                T.True(state.OwnStackCleared, "自分の山を消化した");
                T.Eq(2, GameEngine.GetLegalActions(state, 0).Count, "他 2 人の山が選べる");
            });

            T.Test("クラウンは 2 枚分としてカウントされる", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Crown));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 2));

                Do(state, GameAction.Flip(0, 0));

                T.Eq(2, state.FlipValue, "クラウン 1 枚で 2 カウント");
                T.Eq(1, state.FlipsMade, "めくったのは 1 枚だけ");
                T.Eq(1, state.Players[0].Points, "宣言 2 を 1 枚で達成");
                T.Eq(Phase.RoundEnd, state.Phase, "ラウンド決着");
            });

            T.Test("CrownCountsAsOneInOwnStack で自分のクラウンは 1 枚分になる", () =>
            {
                var config = GameConfig.Default();
                config.CrownCountsAsOneInOwnStack = true;
                var state = GameEngine.CreateMatch(config, 2);
                Do(state, GameAction.PlaceCard(0, CardType.Crown));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 2));

                Do(state, GameAction.Flip(0, 0));
                T.Eq(1, state.FlipValue, "自分のクラウンは 1 カウント");
                T.Eq(Phase.Challenging, state.Phase, "まだ足りないので継続");

                Do(state, GameAction.Flip(0, 1));
                T.Eq(2, state.FlipValue, "相手のバラで 2 に到達");
                T.Eq(1, state.Players[0].Points, "成功");
            });

            T.Test("他人のクラウンは常に 2 枚分", () =>
            {
                var config = GameConfig.Default();
                config.CrownCountsAsOneInOwnStack = true;
                var state = GameEngine.CreateMatch(config, 2);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Crown));
                Do(state, GameAction.Bid(0, 2));

                Do(state, GameAction.Flip(0, 0)); // 自分のバラ = 1
                Do(state, GameAction.Flip(0, 1)); // 相手のクラウン = 2
                T.Eq(3, state.FlipValue, "1 + 2");
                T.Eq(1, state.Players[0].Points, "成功");
            });

            T.Test("自分の山を消化するまでは成功判定しない", () =>
            {
                // 手札切れで強制的に宣言させられた場合、自分のスカルを回避できてはいけない。
                var state = GameEngine.CreateMatch(Tiny(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.PlaceCard(0, CardType.Skull)); // 0 の山は [Rose, Skull]、上はスカル
                Do(state, GameAction.PlaceCard(1, CardType.Skull));
                Do(state, GameAction.Bid(0, 1)); // 宣言 1。バラ 1 枚ぶんで足りるが…
                Do(state, GameAction.Pass(1));

                T.Eq(Phase.Challenging, state.Phase, "チャレンジ開始");
                Do(state, GameAction.Flip(0, 0)); // 山の一番上は後から置いたスカル
                T.Eq(Phase.AwaitingDiscard, state.Phase, "自分のスカルを踏んで失敗");
                T.Eq(0, state.Players[0].Points, "ポイントは入らない");
            });

            T.Section("engine/failure");

            T.Test("自分のスカルを踏んだら捨てるカードを自分で選ぶ", () =>
            {
                var state = GameEngine.CreateMatch(Tiny(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Skull));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 2));
                Do(state, GameAction.Flip(0, 0));

                T.Eq(Phase.AwaitingDiscard, state.Phase, "捨てるカードの選択待ち");
                T.Eq(0, state.LastSkullOwner, "踏んだのは自分のスカル");

                var legal = GameEngine.GetLegalActions(state, 0);
                T.Eq(2, legal.Count, "手札のバラと場のスカルから選べる");

                Do(state, GameAction.Discard(0, CardType.Skull));
                T.Eq(1, state.Players[0].CardsOwned, "1 枚失った");
                T.False(state.Players[0].Eliminated, "まだ脱落しない");
                T.Eq(Phase.RoundEnd, state.Phase, "ラウンド決着");
            });

            T.Test("他人のスカルを踏んだ場合はランダムに 1 枚失う", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Skull));
                Do(state, GameAction.Bid(0, 2));
                Do(state, GameAction.Flip(0, 0));
                Do(state, GameAction.Flip(0, 1));

                T.Eq(1, state.LastSkullOwner, "踏んだのは相手のスカル");
                T.Eq(Phase.RoundEnd, state.Phase, "選択フェーズには入らない");
                T.Eq(3, state.Players[0].CardsOwned, "チャレンジャーが 1 枚失う");
                T.Eq(4, state.Players[1].CardsOwned, "スカルの持ち主は減らない");
            });

            T.Test("カードを全て失うと脱落し、残り 1 人でマッチ終了", () =>
            {
                var state = GameEngine.CreateMatch(Tiny(), 2);
                // 1 回目の失敗でスカルを捨て、残り 1 枚(バラ)になる。
                Do(state, GameAction.PlaceCard(0, CardType.Skull));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 2));
                Do(state, GameAction.Flip(0, 0));
                Do(state, GameAction.Discard(0, CardType.Skull));
                Do(state, GameAction.AdvanceRound());

                T.Eq(0, state.RoundStarter, "チャレンジャーが次ラウンドの開始者");
                T.Eq(1, state.Players[0].CardsOwned, "残り 1 枚");

                // 2 回目は相手のスカルを踏んで最後の 1 枚を失う。
                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Skull));
                Do(state, GameAction.Bid(0, 2));
                Do(state, GameAction.Flip(0, 0));
                Do(state, GameAction.Flip(0, 1));

                T.True(state.Players[0].Eliminated, "脱落");
                T.Eq(Phase.MatchEnd, state.Phase, "残り 1 人なのでマッチ終了");
                T.Eq(1, state.Winner, "生き残りが勝者");
            });

            T.Section("engine/victory");

            T.Test("残り 1 枚でのチャレンジ成功は即勝利", () =>
            {
                var state = GameEngine.CreateMatch(Tiny(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Skull));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 2));
                Do(state, GameAction.Flip(0, 0));
                Do(state, GameAction.Discard(0, CardType.Skull));
                Do(state, GameAction.AdvanceRound());

                Do(state, GameAction.PlaceCard(0, CardType.Rose));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 1));
                Do(state, GameAction.Pass(1));
                Do(state, GameAction.Flip(0, 0));

                T.Eq(Phase.MatchEnd, state.Phase, "即勝利");
                T.Eq(0, state.Winner, "勝者");
                T.Eq(1, state.Players[0].Points, "ポイントは 1 のまま");
            });

            T.Test("規定ポイントに到達すると勝利", () =>
            {
                var config = GameConfig.Default();
                config.PointsToWin = 2;
                var state = GameEngine.CreateMatch(config, 2);

                for (int round = 0; round < 2; round++)
                {
                    Do(state, GameAction.PlaceCard(0, CardType.Crown));
                    Do(state, GameAction.PlaceCard(1, CardType.Rose));
                    Do(state, GameAction.Bid(0, 2));
                    Do(state, GameAction.Flip(0, 0));
                    if (state.Phase == Phase.RoundEnd) Do(state, GameAction.AdvanceRound());
                }

                T.Eq(2, state.Players[0].Points, "2 ポイント");
                T.Eq(Phase.MatchEnd, state.Phase, "マッチ終了");
                T.Eq(0, state.Winner, "勝者");
            });

            T.Section("engine/round flow");

            T.Test("ラウンド開始時に場のカードは手札へ戻り、順序は正規化される", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                Do(state, GameAction.PlaceCard(0, CardType.Skull));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 2));
                Do(state, GameAction.Flip(0, 0));
                Do(state, GameAction.Discard(0, CardType.Rose));
                Do(state, GameAction.AdvanceRound());

                T.Eq(0, state.TotalOnTable, "場は空");
                T.Eq(3, state.Players[0].Hand.Count, "失った 1 枚以外は手札に戻る");
                T.Eq(4, state.Players[1].Hand.Count, "相手は 4 枚のまま");
                T.Eq(2, state.RoundNumber, "ラウンド 2");

                // 手札の並びから情報が漏れないよう常にソートされている。
                var hand = state.Players[0].Hand;
                for (int i = 1; i < hand.Count; i++)
                {
                    T.True(Cards.SortKey(hand[i - 1]) <= Cards.SortKey(hand[i]), "手札はソート済み");
                }
            });

            T.Test("AdvanceRound はシステムのみ、RoundEnd 中のみ", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 2);
                T.False(GameEngine.IsLegal(state, GameAction.AdvanceRound()), "配置中は進められない");

                Do(state, GameAction.PlaceCard(0, CardType.Crown));
                Do(state, GameAction.PlaceCard(1, CardType.Rose));
                Do(state, GameAction.Bid(0, 2));
                Do(state, GameAction.Flip(0, 0));

                T.Eq(Phase.RoundEnd, state.Phase, "ラウンド決着");
                T.True(GameEngine.IsLegal(state, GameAction.AdvanceRound()), "システムは進められる");
                T.Eq(0, GameEngine.GetLegalActions(state, 0).Count, "プレイヤーには手がない");
                T.Eq(GameAction.SystemPlayer, GameEngine.CurrentActor(state), "手番はシステム");
            });

            T.Test("不正なアクションは例外になる", () =>
            {
                var state = GameEngine.CreateMatch(GameConfig.Default(), 3);
                T.Throws<InvalidActionException>(
                    () => Do(state, GameAction.PlaceCard(1, CardType.Rose)), "手番でないプレイヤー");
                T.Throws<InvalidActionException>(
                    () => Do(state, GameAction.Bid(0, 1)), "1 周目の宣言");
                T.Throws<InvalidActionException>(
                    () => Do(state, GameAction.Flip(0, 1)), "チャレンジ中でない");
            });
        }
    }
}
