#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    // EditorPrefs に保存（マシン単位・アバター横断）。TTT が無くてもコンパイルできるようにする
    public static class RecentColors
    {
        public const string PrefKey = ToolInfo.ToolName + ".RecentColors";
        public const int Max = 10;

        public static List<Color> Load()
        {
            var result = new List<Color>();
            var raw = EditorPrefs.GetString(PrefKey, "");
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (var part in raw.Split(','))
            {
                if (result.Count >= Max) break;
                if (TryParse(part.Trim(), out var c) && !result.Exists(x => Same(x, c))) result.Add(c);
            }
            return result;
        }

        public static void Push(Color c)
        {
            c.a = 1f;
            var list = Load();
            list.RemoveAll(x => Same(x, c));
            list.Insert(0, c);
            if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
            Save(list);
        }

        // Undo は付けない（EditorPrefs だけのため）
        public static void RemoveAt(int index)
        {
            var list = Load();
            if (index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            Save(list);
        }

        static void Save(List<Color> list)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < list.Count && i < Max; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ColorUtility.ToHtmlStringRGB(list[i]));
            }
            EditorPrefs.SetString(PrefKey, sb.ToString());
        }

        static bool TryParse(string hex, out Color c)
        {
            c = Color.white;
            if (hex.Length != 6) return false;
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
            c = new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
            return true;
        }

        static bool Same(Color a, Color b) => ColorUtility.ToHtmlStringRGB(a) == ColorUtility.ToHtmlStringRGB(b);
    }
}
#endif
