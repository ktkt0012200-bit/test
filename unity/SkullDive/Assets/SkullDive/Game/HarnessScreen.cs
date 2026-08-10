using SkullDive.Ai;
using SkullDive.Core;
using UnityEngine;

namespace SkullDive.Game
{
    /// <summary>
    /// 開発用の動作確認画面。IMGUI で描いているので、シーンにこのスクリプトを 1 個置くだけで
    /// プレハブも Canvas も無しにソロ / オンラインの両方を通しで遊べる。
    ///
    /// これは製品 UI ではない。縦持ち片手操作・スワイプでめくる演出・読みメーターといった
    /// 実機向けの UI は docs/game-design.md の設計に沿って別途作る。
    /// ここでの目的は「共有エンジンが Unity 上でも正しく動く」ことを最短で確認することに絞っている。
    /// </summary>
    [RequireComponent(typeof(SoloGameController))]
    public sealed class HarnessScreen : MonoBehaviour
    {
        private enum Tab
        {
            Solo,
            Online,
        }

        private Tab _tab = Tab.Solo;
        private SoloGameController _solo;
        private OnlineGameController _online;
        private Vector2 _logScroll;
        private string _roomCodeInput = "";

        private void Awake()
        {
            _solo = GetComponent<SoloGameController>();
            _online = GetComponent<OnlineGameController>();
            if (_online == null) _online = gameObject.AddComponent<OnlineGameController>();
        }

        private void OnGUI()
        {
            // スマホ実機でも読めるように拡大しておく。
            float scale = Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi / 110f : Screen.height / 900f);
            GUIUtility.ScaleAroundPivot(Vector2.one * scale, Vector2.zero);

            float width = Screen.width / scale;
            float height = Screen.height / scale;

            GUILayout.BeginArea(new Rect(8, 8, width - 16, height - 16));

            _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "ソロ", "オンライン" });
            GUILayout.Space(6);

            if (_tab == Tab.Solo) DrawSolo();
            else DrawOnline();

            GUILayout.EndArea();
        }

        // ---------------------------------------------------------------- solo

        private void DrawSolo()
        {
            if (!_solo.HasMatch)
            {
                GUILayout.Label("SKULL DIVE — 開発用ハーネス");
                GUILayout.Label("クラウンは " + _solo.CrownValue + " 枚分。" + _solo.PointsToWin + " ポイント先取。");
                if (GUILayout.Button("ソロで開始", GUILayout.Height(40))) _solo.StartMatch();
                return;
            }

            var view = _solo.View;
            DrawTable(view, _solo.Names, seat => _solo.PersonalityOf(seat));

            if (_solo.IsOver)
            {
                GUILayout.Label(_solo.Winner == 0 ? "=== YOU WIN ===" : "=== " + NameOf(_solo.Names, _solo.Winner) + " の勝ち ===");
                if (GUILayout.Button("もう一度", GUILayout.Height(36))) _solo.StartMatch();
            }
            else if (_solo.WaitingForPlayer)
            {
                DrawActions(view, _solo.Submit);
            }
            else
            {
                GUILayout.Label("(相手の手番)");
            }

            DrawLog(_solo.Log);
        }

        // ---------------------------------------------------------------- online

        private void DrawOnline()
        {
            GUILayout.Label("サーバ: " + _online.ServerUrl);
            GUILayout.Label("状態: " + _online.Status);

            if (!_online.IsConnected)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("ルームコード (空欄で新規作成)", GUILayout.Width(220));
                _roomCodeInput = GUILayout.TextField(_roomCodeInput, 8, GUILayout.Width(90));
                GUILayout.EndHorizontal();

                if (GUILayout.Button("接続", GUILayout.Height(36)))
                {
                    _online.RoomCode = _roomCodeInput.Trim().ToUpperInvariant();
                    _online.Connect();
                }
                DrawLog(_online.Log);
                return;
            }

            var room = _online.Room;
            if (room != null && !room.Started)
            {
                GUILayout.Label("ルーム " + room.Code + " — " + room.Seats.Length + " 人");
                foreach (var seat in room.Seats)
                {
                    GUILayout.Label("  " + seat.Seat + ". " + seat.Name
                        + (seat.IsBot ? " [bot]" : "")
                        + (seat.Connected ? "" : " (切断中)"));
                }

                if (_online.IsHost)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("bot を追加")) _online.SendAddBot();
                    if (GUILayout.Button("開始")) _online.SendStart();
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("ホストの開始待ち");
                }

                DrawLog(_online.Log);
                return;
            }

            var view = _online.View;
            if (view != null)
            {
                DrawTable(view, _online.Names, null);
                if (view.Phase == Phase.MatchEnd)
                {
                    GUILayout.Label("=== " + (view.Winner == _online.Seat ? "YOU WIN" : NameOf(_online.Names, view.Winner) + " の勝ち") + " ===");
                }
                else if (_online.WaitingForPlayer)
                {
                    DrawActions(view, _online.Submit);
                }
                else
                {
                    GUILayout.Label("(相手の手番)");
                }
            }

            DrawLog(_online.Log);
        }

        // ---------------------------------------------------------------- shared

        private void DrawTable(PlayerView view, string[] names, System.Func<int, AiPersonality> personalityOf)
        {
            if (view == null) return;

            GUILayout.Label("ラウンド " + view.RoundNumber + " / " + EventNarrator.PhaseLabel(view.Phase)
                + " / 場に " + view.TotalOnTable + " 枚"
                + (view.HighestBid > 0 ? " / 宣言 " + view.HighestBid : ""));

            for (int i = 0; i < view.Players.Length; i++)
            {
                var player = view.Players[i];
                string row = (i == view.CurrentPlayer ? "> " : "  ")
                    + NameOf(names, i)
                    + "  pt " + player.Points
                    + "  cards " + player.CardsOwned
                    + "  " + StackText(view, i);

                if (player.Eliminated) row += "  [脱落]";
                else if (player.HasPassed) row += "  [降りた]";
                else if (player.CurrentBid > 0) row += "  [宣言 " + player.CurrentBid + "]";

                // 「読み」メーターの原型。製品版ではここをゲージ表示にする。
                if (view.Phase == Phase.Challenging && i != view.Challenger
                    && player.StackCount - player.FlippedCount > 0)
                {
                    row += "  安全 " + Mathf.RoundToInt((float)Odds.SafeChance(view, i) * 100f) + "%";
                }

                GUILayout.Label(row);

                if (personalityOf != null)
                {
                    var personality = personalityOf(i);
                    if (personality != null) GUILayout.Label("      癖: " + personality.Tell);
                }
            }

            GUILayout.Label("手札: " + HandText(view));
            if (view.Phase == Phase.Challenging)
            {
                GUILayout.Label("進捗: " + view.FlipValue + " / " + view.HighestBid
                    + (view.OwnStackCleared ? "  (自分の山は消化済み)" : "  (まず自分の山を全部めくる)"));
            }
            GUILayout.Space(6);
        }

        private void DrawActions(PlayerView view, System.Action<GameAction> submit)
        {
            GUILayout.Label("あなたの手番");
            foreach (var action in view.LegalActions)
            {
                if (GUILayout.Button(ActionLabel(view, action), GUILayout.Height(32)))
                {
                    submit(action);
                    return;
                }
            }
        }

        private void DrawLog(System.Collections.Generic.List<string> log)
        {
            GUILayout.Space(6);
            GUILayout.Label("ログ");
            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(140));
            for (int i = Mathf.Max(0, log.Count - 40); i < log.Count; i++)
            {
                GUILayout.Label(log[i]);
            }
            GUILayout.EndScrollView();
        }

        private static string ActionLabel(PlayerView view, GameAction action)
        {
            switch (action.Kind)
            {
                case ActionKind.PlaceCard:
                    return EventNarrator.CardName(action.CardType) + " を伏せて置く";
                case ActionKind.Bid:
                    return action.Amount + " 枚を宣言"
                        + (action.Amount == view.TotalOnTable ? " (場の全枚数 = 即チャレンジ)" : "");
                case ActionKind.Pass:
                    return "降りる";
                case ActionKind.Flip:
                    return view.Players[action.Target].Name + " の山をめくる  (安全 "
                        + Mathf.RoundToInt((float)Odds.SafeChance(view, action.Target) * 100f) + "%)";
                case ActionKind.Discard:
                    return EventNarrator.CardName(action.CardType) + " を失う";
                default:
                    return action.ToString();
            }
        }

        private static string StackText(PlayerView view, int seat)
        {
            var player = view.Players[seat];
            if (player.StackCount == 0) return "(なし)";

            var text = "";
            for (int i = 0; i < player.StackCount; i++)
            {
                int fromTop = player.StackCount - 1 - i;
                if (fromTop < player.FlippedCount)
                {
                    text += "[" + Cards.ShortLabel((CardType)player.RevealedFromTop[fromTop]) + "]";
                }
                else if (seat == view.Viewer && i < view.MyStack.Length)
                {
                    text += "(" + Cards.ShortLabel((CardType)view.MyStack[i]) + ")";
                }
                else
                {
                    text += "(?)";
                }
            }
            return text;
        }

        private static string HandText(PlayerView view)
        {
            if (view.MyHand.Length == 0) return "(なし)";
            var text = "";
            for (int i = 0; i < view.MyHand.Length; i++)
            {
                if (i > 0) text += " ";
                text += Cards.ShortLabel((CardType)view.MyHand[i]);
            }
            return text;
        }

        private static string NameOf(string[] names, int seat)
        {
            if (seat < 0) return "?";
            if (names != null && seat < names.Length && !string.IsNullOrEmpty(names[seat])) return names[seat];
            return "P" + seat;
        }
    }
}
