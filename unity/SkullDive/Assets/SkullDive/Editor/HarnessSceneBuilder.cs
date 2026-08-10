using SkullDive.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SkullDive.Editor
{
    /// <summary>
    /// 動作確認シーンをメニューから作る。
    ///
    /// シーンや Prefab を YAML で手書きしてリポジトリに入れるとマージ衝突と GUID 破損の温床になるため、
    /// 「生成する」形にしてある。必要な人が 1 クリックで作れれば十分。
    /// </summary>
    public static class HarnessSceneBuilder
    {
        private const string ScenePath = "Assets/SkullDive/Scenes/Harness.unity";

        [MenuItem("Tools/SkullDive/動作確認シーンを作成")]
        public static void CreateHarnessScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var root = new GameObject("SkullDive");
            root.AddComponent<SoloGameController>();
            root.AddComponent<OnlineGameController>();
            root.AddComponent<HarnessScreen>();

            var directory = System.IO.Path.GetDirectoryName(ScenePath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorUtility.DisplayDialog("SkullDive",
                "動作確認シーンを作成しました。\n" + ScenePath + "\n\n再生してソロ / オンラインを試せます。", "OK");
        }

        [MenuItem("Tools/SkullDive/ルール設定を Console に出力")]
        public static void DumpConfig()
        {
            var config = SkullDive.Core.GameConfig.Default();
            Debug.Log("手札: " + config.RoseCount + "R + " + config.CrownCount + "C + " + config.SkullCount + "S"
                + " / クラウン = " + config.CrownValue + " 枚分"
                + " / 勝利ポイント = " + config.PointsToWin
                + " / 安全カードの平均カウント = " + config.AverageSafeValue().ToString("0.00"));
        }
    }
}
