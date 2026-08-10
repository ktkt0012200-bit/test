using System;
using UnityEngine.SceneManagement;

// UnityEditor の最小スタブ。
//
// Unity は UnityEditor の参照アセンブリを公開していないため、Editor 向けコードの
// コンパイル検証にはここで宣言した形が使われる。
// つまりこれは「自分が書いた宣言に対する検証」であって、Unity 本体の
// シグネチャと一致していることは保証しない。構文ミスや型の取り違えは捕まえられるが、
// API の使い方が Unity の実物と合っているかは Editor で最終確認が必要。
//
// このファイルは Unity プロジェクトの外(dotnet/SkullDive.UnityCheck)にあるので、
// Unity 側で二重定義になることはない。

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string itemName) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction) { }
        public MenuItemAttribute(string itemName, bool isValidateFunction, int priority) { }
    }

    public static class AssetDatabase
    {
        public static bool IsValidFolder(string path) => throw new NotImplementedException();
        public static void Refresh() => throw new NotImplementedException();
        public static void SaveAssets() => throw new NotImplementedException();
    }

    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string ok)
            => throw new NotImplementedException();

        public static bool DisplayDialog(string title, string message, string ok, string cancel)
            => throw new NotImplementedException();
    }

    public enum UIOrientation
    {
        Portrait = 0,
        PortraitUpsideDown = 1,
        LandscapeRight = 2,
        LandscapeLeft = 3,
        AutoRotation = 4,
    }

    public static class PlayerSettings
    {
        public static string companyName { get; set; }
        public static string productName { get; set; }
        public static UIOrientation defaultInterfaceOrientation { get; set; }
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup
    {
        EmptyScene = 0,
        DefaultGameObjects = 1,
    }

    public enum NewSceneMode
    {
        Single = 0,
        Additive = 1,
    }

    public static class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode)
            => throw new NotImplementedException();

        public static bool SaveScene(Scene scene, string path) => throw new NotImplementedException();
    }
}
