using SkullDive.Ai;
using SkullDive.Core;
using SkullDive.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkullDive.Editor
{
    /// <summary>
    /// 開発用のメニュー。
    ///
    /// シーンや Prefab を YAML で手書きしてリポジトリに入れるとマージ衝突と GUID 破損の温床になるため、
    /// 「生成する」形にしてある。必要な人が 1 クリックで作れれば十分。
    /// </summary>
    public static class HarnessSceneBuilder
    {
        private const string SceneFolder = "Assets/SkullDive/Scenes";
        private const string ScenePath = SceneFolder + "/Harness.unity";

        [MenuItem("Tools/SkullDive/1. 動作確認シーンを作成", false, 10)]
        public static void CreateHarnessScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var root = new GameObject("SkullDive");
            root.AddComponent<SoloGameController>();
            root.AddComponent<OnlineGameController>();
            root.AddComponent<HarnessScreen>();

            if (!AssetDatabase.IsValidFolder(SceneFolder))
            {
                System.IO.Directory.CreateDirectory(SceneFolder);
                AssetDatabase.Refresh();
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[SkullDive] 動作確認シーンを作成しました: " + ScenePath);
            EditorUtility.DisplayDialog("SkullDive",
                "動作確認シーンを作成しました。\n" + ScenePath
                + "\n\n再生ボタンでソロプレイが始まります。\n"
                + "オンラインを試す場合は先にサーバを起動してください:\n"
                + "  dotnet run --project dotnet/SkullDive.Server", "OK");
        }

        /// <summary>
        /// 共有エンジンが Unity のランタイム上でも正しく動くかを 1 クリックで確かめる。
        /// ここが通れば、あとの不具合は UI 層に絞り込める。
        /// </summary>
        [MenuItem("Tools/SkullDive/2. エンジンの自己診断を実行", false, 11)]
        public static void RunEngineSelfCheck()
        {
            var config = GameConfig.Default();
            Debug.Log("[SkullDive] 手札 " + config.RoseCount + "R+" + config.CrownCount + "C+" + config.SkullCount + "S"
                + " / クラウン = " + config.CrownValue + " 枚分"
                + " / 勝利ポイント = " + config.PointsToWin);

            // 1 マッチをプレイヤー席込みで通す。
            var solo = new SoloMatch(config, AiPersonality.DefaultTable(), -1, 12345);
            int steps = 0;
            while (!solo.IsOver && steps++ < 20000) solo.Step();

            if (!solo.IsOver)
            {
                Debug.LogError("[SkullDive] 自己診断に失敗: マッチが終了しませんでした");
                return;
            }
            Debug.Log("[SkullDive] ソロ 1 マッチ完走 (ラウンド " + solo.RoundNumber + " / 勝者 席"
                + solo.Winner + ")");

            // AI 同士で少量回して統計が出ることも見る。
            var stats = Simulator.Run(config, AiPersonality.All(), 200, 777);
            Debug.Log("[SkullDive] 200 マッチのシミュレーション: 成功率 "
                + (stats.SuccessRate * 100f).ToString("0.0") + "% / 平均宣言 "
                + stats.AverageBid.ToString("0.00") + " / ラウンド per match "
                + stats.AverageRoundsPerMatch.ToString("0.0"));

            // 秘匿処理が効いていることも確認する。ここが壊れるとゲームが成立しない。
            var state = GameEngine.CreateMatch(config, 3);
            var events = new System.Collections.Generic.List<GameEvent>();
            GameEngine.Apply(state, GameAction.PlaceCard(0, CardType.Skull), new XorShiftRng(1), events);
            var view = ViewRedactor.Redact(state, 1);
            bool leaked = view.Players[0].RevealedFromTop.Length != 0 || view.MyStack.Length != 0;
            if (leaked)
            {
                Debug.LogError("[SkullDive] 自己診断に失敗: 秘匿情報が漏れています");
                return;
            }

            Debug.Log("[SkullDive] 自己診断 OK — エンジン・AI・秘匿処理はすべて Unity 上で動作しています");
        }

        /// <summary>
        /// モバイル向けの最小設定。縦持ち前提の設計なので、そこだけ先に固定しておく。
        /// </summary>
        [MenuItem("Tools/SkullDive/3. モバイル向け設定を適用", false, 12)]
        public static void ApplyMobileSettings()
        {
            PlayerSettings.companyName = "SkullDive";
            PlayerSettings.productName = "SKULL DIVE";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            AssetDatabase.SaveAssets();
            Debug.Log("[SkullDive] 縦持ち固定・製品名を設定しました");
            EditorUtility.DisplayDialog("SkullDive",
                "モバイル向けの基本設定を適用しました。\n\n"
                + "・画面の向き: 縦固定\n"
                + "・製品名: SKULL DIVE\n\n"
                + "バンドル ID とアイコンは配信前に Player Settings で設定してください。", "OK");
        }
    }
}
