#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

using System;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    // 下地は USS で固定色のため、文字の色はテーマによらずここで決める（つまみのノブ・ColorField・チェックボックスの箱は標準のまま）
    // EditorStyles は IMGUI の描画中しか触れないため、使うときに作る（ドメインの読み直しで消えたら作り直す）
    public static class ImguiSkin
    {
        // USS と同じ色
        public static readonly Color Ink = new Color32(0xE8, 0xEE, 0xEC, 0xFF);
        public static readonly Color Sub = new Color32(0xA9, 0xB6, 0xB2, 0xFF);
        public static readonly Color Mint = new Color32(0x8F, 0xE3, 0xCF, 0xFF);
        public static readonly Color Dark = new Color32(0x1F, 0x23, 0x26, 0xFF); // ミントの上の文字
        public static readonly Color Panel = new Color32(0x2A, 0x30, 0x34, 0xFF);
        static readonly Color Line = new Color32(0x3A, 0x44, 0x48, 0xFF);
        static readonly Color Fill = new Color32(0x23, 0x29, 0x2C, 0xFF);
        static readonly Color FillHover = new Color32(0x32, 0x3A, 0x3E, 0xFF);
        static readonly Color FillActive = new Color32(0x1F, 0x23, 0x26, 0xFF);
        static readonly Color MintHover = new Color32(0x4F, 0xC1, 0xA6, 0xFF);
        static readonly Color WarnLine = new Color32(0xE3, 0xC4, 0x6F, 0xFF);
        static readonly Color WarnFill = new Color32(0x3F, 0x3F, 0x37, 0xFF); // #2A3034 に黄色を 14% 重ねた色

        public const int FontSize = 13;

        static GUIStyle s_label, s_text, s_note, s_bold, s_value, s_button, s_toggleLabel, s_warn;
        static Texture2D s_texFill, s_texHover, s_texActive, s_texMint, s_texMintHover, s_texWarn;

        public static GUIStyle Label { get { Ensure(); return s_label; } }
        public static GUIStyle Text { get { Ensure(); return s_text; } }
        public static GUIStyle Note { get { Ensure(); return s_note; } }
        public static GUIStyle Bold { get { Ensure(); return s_bold; } }
        public static GUIStyle Value { get { Ensure(); return s_value; } }
        // GUILayout.Toggle(on, 名前, Button) で選ぶボタンにもなる
        public static GUIStyle Button { get { Ensure(); return s_button; } }
        public static GUIStyle ToggleLabel { get { Ensure(); return s_toggleLabel; } }
        // HelpBox の代わり
        public static GUIStyle Warn { get { Ensure(); return s_warn; } }

        static bool Valid => s_label != null && s_texFill != null && s_texMint != null && s_texWarn != null;

        static void Ensure()
        {
            if (Valid) return;
            s_texFill = Frame(Line, Fill);
            s_texHover = Frame(Mint, FillHover);
            s_texActive = Frame(Line, FillActive);
            s_texMint = Frame(Mint, Mint);
            s_texMintHover = Frame(MintHover, MintHover);
            s_texWarn = Frame(WarnLine, WarnFill);

            s_label = Colored(new GUIStyle(EditorStyles.label) { fontSize = FontSize, wordWrap = false, richText = false }, Ink);
            s_text = Colored(new GUIStyle(EditorStyles.label) { fontSize = FontSize, wordWrap = true, richText = false }, Ink);
            s_note = Colored(new GUIStyle(EditorStyles.label) { fontSize = FontSize - 1, wordWrap = true, richText = false }, Sub);
            s_bold = Colored(new GUIStyle(EditorStyles.boldLabel) { fontSize = FontSize + 1, wordWrap = true, richText = false }, Ink);
            s_bold.margin = new RectOffset(s_bold.margin.left, s_bold.margin.right, 4, 4);
            s_value = Colored(new GUIStyle(EditorStyles.label) { fontSize = FontSize - 1, alignment = TextAnchor.MiddleRight, wordWrap = false }, Sub);
            s_toggleLabel = Colored(new GUIStyle(EditorStyles.label) { fontSize = FontSize, wordWrap = true }, Ink);

            var b = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = FontSize,
                fixedHeight = 0f,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(2, 2, 2, 2),
                overflow = new RectOffset(0, 0, 0, 0),
            };
            SetState(b.normal, s_texFill, Ink);
            SetState(b.hover, s_texHover, Ink);
            SetState(b.active, s_texActive, Ink);
            SetState(b.focused, s_texFill, Ink);
            SetState(b.onNormal, s_texMint, Dark);
            SetState(b.onHover, s_texMintHover, Dark);
            SetState(b.onActive, s_texMintHover, Dark);
            SetState(b.onFocused, s_texMint, Dark);
            s_button = b;

            s_warn = new GUIStyle(EditorStyles.label)
            {
                fontSize = FontSize,
                wordWrap = true,
                richText = false,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 4, 4),
            };
            foreach (var st in States(s_warn)) SetState(st, s_texWarn, Ink);
        }

        static GUIStyle Colored(GUIStyle s, Color c)
        {
            foreach (var st in States(s))
            {
                st.textColor = c;
                st.background = null;
                st.scaledBackgrounds = Array.Empty<Texture2D>();
            }
            return s;
        }

        static GUIStyleState[] States(GUIStyle s) => new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused };

        // scaledBackgrounds を空にしないと高解像度の画面でテーマの @2x の絵が勝つ
        static void SetState(GUIStyleState st, Texture2D bg, Color text)
        {
            st.background = bg;
            st.scaledBackgrounds = Array.Empty<Texture2D>();
            st.textColor = text;
        }

        // 3×3 を border 1 で引き伸ばす
        static Texture2D Frame(Color edge, Color fill)
        {
            var t = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "CustomHairColorTool_ImguiSkin",
            };
            var px = new Color[9];
            for (var i = 0; i < 9; i++) px[i] = i == 4 ? fill : edge;
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }
    }
}
#endif
