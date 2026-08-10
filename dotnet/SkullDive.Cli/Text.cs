using System;

namespace SkullDive.Cli
{
    /// <summary>ターミナル表示のための桁揃え。日本語(全角)を 2 幅として数える。</summary>
    public static class Text
    {
        public static string Pad(string text, int width)
        {
            if (text == null) text = "";
            int padding = Math.Max(1, width - VisualWidth(text));
            return text + new string(' ', padding);
        }

        public static int VisualWidth(string text)
        {
            if (text == null) return 0;
            int width = 0;
            foreach (char c in text) width += IsWide(c) ? 2 : 1;
            return width;
        }

        public static string Percent(double value)
        {
            return (value * 100.0).ToString("0.0") + "%";
        }

        private static bool IsWide(char c)
        {
            return (c >= 0x1100 && c <= 0x115F)
                || (c >= 0x2E80 && c <= 0xA4CF)
                || (c >= 0xAC00 && c <= 0xD7A3)
                || (c >= 0xF900 && c <= 0xFAFF)
                || (c >= 0xFE30 && c <= 0xFE6F)
                || (c >= 0xFF00 && c <= 0xFF60)
                || (c >= 0xFFE0 && c <= 0xFFE6);
        }
    }
}
