#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    // 白地に赤い斜線は他のソフトの「色なし」の見た目に合わせた
    public static class NoneSwatch
    {
        public const string Label = "なし";
        public const string Hint = "色を重ねず、デフォルトの髪のまま見えます";

        static readonly Color Paper = Color.white;
        static readonly Color Slash = new Color32(0xE0, 0x3A, 0x3A, 0xFF);

        public static void Draw(Rect r, bool selected)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, selected ? new Color32(0x8F, 0xE3, 0xCF, 0xFF) : new Color(0f, 0f, 0f, 0.4f));
            var edge = selected ? 2f : 1f;
            var inner = new Rect(r.x + edge, r.y + edge, r.width - edge * 2f, r.height - edge * 2f);
            EditorGUI.DrawRect(inner, Paper);
            DrawSlash(new Vector2(inner.xMin + 1f, inner.yMax - 1f), new Vector2(inner.xMax - 1f, inner.yMin + 1f), 2f);
        }

        static void DrawSlash(Vector2 from, Vector2 to, float width)
        {
            var keep = Handles.color;
            Handles.color = Slash * GUI.color; // まわりを薄くしているときに合わせる
            Handles.DrawAAPolyLine(width, from, to);
            Handles.color = keep;
        }

        // PaintBox は四角しか描けないため、斜線は細い四角を斜めに並べる
        public static void Paint(Rect r, Action<Rect, Color> add)
        {
            add(r, Paper);
            var steps = Mathf.Max(1, Mathf.CeilToInt(r.width));
            var thick = Mathf.Max(2f, r.width * 0.1f);
            var step = r.width / steps;
            for (var i = 0; i < steps; i++)
            {
                var y = r.yMax - (i + 0.5f) * r.height / steps;
                add(new Rect(r.x + i * step, y - thick * 0.5f, step + 0.5f, thick), Slash);
            }
        }
    }
}
#endif
