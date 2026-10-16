#if UNITY_EDITOR
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    public static class HueRing
    {
        const int TextureSize = 256;
        const float OuterRadiusRatio = 0.5f;
        const float InnerRadiusRatio = 0.40f;

        static Texture2D s_ringTexture;
        static readonly int s_controlHint = "AtelierCloveria.CustomHairColorTool.HueRing".GetHashCode();

        public static Texture2D RingTexture
        {
            get
            {
                if (s_ringTexture == null) s_ringTexture = BuildRingTexture();
                return s_ringTexture;
            }
        }

        static Texture2D BuildRingTexture()
        {
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "CustomHairColorTool_HueRing",
            };
            var pixels = new Color32[TextureSize * TextureSize];
            var center = TextureSize * 0.5f;
            var outer = TextureSize * OuterRadiusRatio;
            var inner = TextureSize * InnerRadiusRatio;
            for (var y = 0; y < TextureSize; y++)
            {
                for (var x = 0; x < TextureSize; x++)
                {
                    var dx = x + 0.5f - center;
                    var dy = y + 0.5f - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01(outer - dist) * Mathf.Clamp01(dist - inner);
                    if (a <= 0f)
                    {
                        pixels[y * TextureSize + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    // テクスチャは y が上向き（GUI と逆）
                    var c = Color.HSVToRGB(AngleToHue(dx, -dy), 1f, 1f);
                    c.a = a;
                    pixels[y * TextureSize + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // GUI 座標で上が 0、時計回り
        public static float AngleToHue(float dx, float dy)
        {
            var angle = Mathf.Atan2(dx, -dy);
            return Mathf.Repeat(angle / (2f * Mathf.PI), 1f);
        }

        const float SquareRatio = 0.9f;
        static Texture2D s_whiteFade, s_blackFade;
        static int s_dragPart; // 0＝なし、1＝リング、2＝四角

        static Texture2D Fade(int w, int h, Color from, Color to)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            // w か h が 1 の前提
            var n = Mathf.Max(w, h);
            var pixels = new Color[n];
            for (var i = 0; i < n; i++) pixels[i] = Color.Lerp(from, to, i / (n - 1f));
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static Rect InnerSquare(Vector2 center, float innerR)
        {
            var side = innerR * 2f * 0.7071f * SquareRatio;
            return new Rect(center.x - side * 0.5f, center.y - side * 0.5f, side, side);
        }

        static void SetSV(Rect sq, Vector2 p, ref float sat, ref float val, ref bool changed)
        {
            var s = Mathf.Clamp01((p.x - sq.x) / sq.width);
            var v = Mathf.Clamp01(1f - (p.y - sq.y) / sq.height);
            if (Mathf.Approximately(s, sat) && Mathf.Approximately(v, val)) return;
            sat = s;
            val = v;
            changed = true;
        }

        public static bool Hits(Rect rect, Vector2 p)
        {
            var size = Mathf.Min(rect.width, rect.height);
            var center = rect.center;
            var dist = (p - center).magnitude;
            return InnerSquare(center, size * InnerRadiusRatio).Contains(p) || (dist <= size * OuterRadiusRatio + 4f && dist >= size * InnerRadiusRatio - 4f);
        }

        public static bool Draw(Rect rect, ref float hue, ref float sat, ref float val)
        {
            var size = Mathf.Min(rect.width, rect.height);
            var square = new Rect(rect.x + (rect.width - size) * 0.5f, rect.y + (rect.height - size) * 0.5f, size, size);
            var center = square.center;
            var outerR = size * OuterRadiusRatio;
            var innerR = size * InnerRadiusRatio;
            var sv = InnerSquare(center, innerR);

            var id = GUIUtility.GetControlID(s_controlHint, FocusType.Passive, square);
            var e = Event.current;
            var changed = false;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && square.Contains(e.mousePosition))
                    {
                        var d = e.mousePosition - center;
                        var dist = d.magnitude;
                        if (sv.Contains(e.mousePosition))
                        {
                            GUIUtility.hotControl = id;
                            s_dragPart = 2;
                            SetSV(sv, e.mousePosition, ref sat, ref val, ref changed);
                            e.Use();
                        }
                        else if (dist <= outerR + 4f && dist >= innerR - 4f)
                        {
                            GUIUtility.hotControl = id;
                            s_dragPart = 1;
                            var h = AngleToHue(d.x, d.y);
                            if (!Mathf.Approximately(h, hue)) { hue = h; changed = true; }
                            e.Use();
                        }
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        if (s_dragPart == 2) SetSV(sv, e.mousePosition, ref sat, ref val, ref changed);
                        else
                        {
                            var d = e.mousePosition - center;
                            var h = AngleToHue(d.x, d.y);
                            if (!Mathf.Approximately(h, hue)) { hue = h; changed = true; }
                        }
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && e.button == 0)
                    {
                        GUIUtility.hotControl = 0;
                        s_dragPart = 0;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    GUI.DrawTexture(square, RingTexture, ScaleMode.StretchToFill, true);

                    if (s_whiteFade == null) s_whiteFade = Fade(64, 1, Color.white, new Color(1f, 1f, 1f, 0f));
                    if (s_blackFade == null) s_blackFade = Fade(1, 64, Color.black, new Color(0f, 0f, 0f, 0f));
                    // 3枚重ねのため GUI.color は使わず、重ねたあとに暗い色をかぶせて同じだけ薄くする
                    var keep = GUI.color;
                    GUI.color = Color.white;
                    GUI.DrawTexture(sv, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, Color.HSVToRGB(Mathf.Repeat(hue, 1f), 1f, 1f), 0f, 0f);
                    GUI.DrawTexture(sv, s_whiteFade, ScaleMode.StretchToFill, true);
                    GUI.DrawTexture(sv, s_blackFade, ScaleMode.StretchToFill, true);
                    if (keep.a < 1f) GUI.DrawTexture(sv, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(0.14f, 0.16f, 0.17f, 1f - keep.a), 0f, 0f);
                    GUI.color = keep;
                    var p = new Vector2(sv.x + sat * sv.width, sv.y + (1f - val) * sv.height);
                    DrawDisc(new Rect(p.x - 7f, p.y - 7f, 14f, 14f), val > 0.55f ? Color.black : Color.white);
                    DrawDisc(new Rect(p.x - 5f, p.y - 5f, 10f, 10f), Color.HSVToRGB(Mathf.Repeat(hue, 1f), sat, val));

                    var angle = hue * 2f * Mathf.PI;
                    var markerPos = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * ((outerR + innerR) * 0.5f);
                    var markerSize = Mathf.Max(6f, (outerR - innerR) * 0.55f);
                    var markerRect = new Rect(markerPos.x - markerSize * 0.5f, markerPos.y - markerSize * 0.5f, markerSize, markerSize);
                    DrawDisc(new Rect(markerRect.x - 2f, markerRect.y - 2f, markerRect.width + 4f, markerRect.height + 4f), Color.black);
                    DrawDisc(markerRect, Color.white);
                    break;
            }

            if (changed) GUI.changed = true;
            return changed;
        }

        static void DrawDisc(Rect rect, Color color)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 1f, color, 0f, rect.width * 0.5f);
        }
    }
}
#endif
