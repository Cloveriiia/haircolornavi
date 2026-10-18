#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System;
using System.Collections.Generic;
using System.Linq;
using net.rs64.TexTransTool;
using net.rs64.TexTransTool.Decal;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    // Prefab：Custom/_Glow/<モード> に HairGlowSetting、その下に発光の Decal「<色の Decal の名前>_Glow」を色の数だけ（Padding 0・オフで出荷）
    // 発光の設定をマテリアルに入れるのは自前の NDMF プラグイン（TTT より先に動く）
    public sealed class GlowParts
    {
        public GameObject Root;
        public GameObject Node;
        public readonly SingleGradationDecal[] Decals;
        public HairGlowSetting Setting;
        public string Prefix => Setting.EmissionPrefix; // "_Emission" か "_Emission2nd"

        public GlowParts(int colorCount) => Decals = new SingleGradationDecal[colorCount];
    }

    public static partial class HairColorCore
    {
        public const string GlowNodeName = "_Glow";
        public const string GlowSuffix = "_Glow";

        static GlowParts DetectGlow(Transform custom, ColoringModeParts mt)
        {
            if (mt.Decals.Count < 2) return null;
            var root = FindChild(custom, GlowNodeName);
            if (root == null) return null;
            var node = FindChild(root, mt.Node.name);
            if (node == null) return null;
            var t = new GlowParts(mt.Decals.Count) { Root = root.gameObject, Node = node.gameObject };
            var decals = CollectDecals(node);
            for (var c = 0; c < t.Decals.Length; c++)
            {
                var lead = mt.Decals[c];
                t.Decals[c] = lead != null ? decals.FirstOrDefault(d => NameIs(d.transform, lead.name + GlowSuffix)) : null;
                if (t.Decals[c] == null) return null;
            }
            t.Setting = node.GetComponent<HairGlowSetting>();
            if (t.Setting == null) return null;
            return t;
        }

        // 光らせるときも全部オンにし、光らせない色の側に黒を書く
        // 色と同じく後ろの Decal が上に乗るため、色の境をまたぐ毛束でも色と光がずれない（Padding 0 だけでははみ出した）
        static bool IsBlack(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.5f / 255f;

        // 色の番号（0＝1色目）。-1＝光らせない
        public static int GetGlowingColorIndex(GlowParts g)
        {
            if (g == null) return -1;
            var on = Enumerable.Range(0, g.Decals.Length).Where(c => g.Decals[c] != null && g.Decals[c].gameObject.activeSelf).ToList();
            if (on.Count == 0) return -1;
            foreach (var c in on)
                if (!IsBlack(GetDecalColor(g.Decals[c]))) return c;
            return on[0];
        }

        // 光らせないときも全部に光の色を残す（次に同じ色で光らせるため）
        public static void SetGlowingColorIndex(ColoringProduct target, ColoringMode mode, int colorIndex)
        {
            if (target == null || !target.Has(mode)) return;
            var g = target.ModeParts[mode].Glow;
            if (g == null) return;
            var light = GetGlowColor(g);
            for (var c = 0; c < g.Decals.Length; c++)
            {
                if (g.Decals[c] == null) continue;
                SetActiveWithUndo(g.Decals[c].gameObject, colorIndex >= 0);
                SetDecalColor(g.Decals[c], colorIndex < 0 || c == colorIndex ? light : Color.black);
            }
            SetColoringMode(target, mode);
        }

        public static Color GetGlowColor(GlowParts g)
        {
            if (g == null) return Color.white;
            var glowing = GetGlowingColorIndex(g);
            if (glowing >= 0) return GetDecalColor(g.Decals[glowing]);
            foreach (var d in g.Decals)
                if (!IsBlack(GetDecalColor(d))) return GetDecalColor(d);
            return GetDecalColor(g.Decals[g.Decals.Length - 1]);
        }

        // 真っ黒は光らせない側の印のため、ほんの少し明るくする
        public static void SetGlowColor(GlowParts g, Color c)
        {
            if (g == null) return;
            c.a = 1f;
            if (IsBlack(c)) c = new Color(1f / 255f, 1f / 255f, 1f / 255f, 1f);
            var glowing = GetGlowingColorIndex(g);
            for (var i = 0; i < g.Decals.Length; i++)
                if (g.Decals[i] != null && (glowing < 0 || i == glowing)) SetDecalColor(g.Decals[i], c);
        }

        // lilToon の発光は「発光の画像 × 色」のため、色の明るさが強さになる
        public static float GetGlowStrength(GlowParts g) => g != null ? g.Setting.Strength : 0.6f;

        public static void SetGlowStrength(GlowParts g, float s)
        {
            if (g == null) return;
            WriteSetting(g, so => so.FindProperty(nameof(HairGlowSetting.Strength)).floatValue = s);
        }

        // ---- 流れる光 ----
        // マスクは髪の画像のサンプラーで読むため、髪の画像が Repeat でないと流れない
        public const float FlowSpeedDefault = 0.5f;

        public static bool CanFlowGlow(ColoringProduct target, GlowParts g)
        {
            if (g == null || g.Setting.FlowMask == null) return false;
            foreach (var m in g.Setting.TargetMaterials)
            {
                var tex = m != null ? m.mainTexture : null;
                if (tex != null && tex.wrapMode != TextureWrapMode.Repeat) return false;
            }
            return true;
        }

        public static bool IsGlowFlowOn(GlowParts g) => g != null && g.Setting.FlowOn;

        // ＋＝髪の画像の上→下、−＝下→上
        public static float GetGlowFlowVelocity(GlowParts g) => g != null ? g.Setting.FlowVelocity : FlowSpeedDefault;

        public static void SetGlowFlow(GlowParts g, bool on, float velocity)
        {
            if (g == null) return;
            WriteSetting(g, so =>
            {
                so.FindProperty(nameof(HairGlowSetting.FlowOn)).boolValue = on;
                so.FindProperty(nameof(HairGlowSetting.FlowVelocity)).floatValue = velocity;
            });
        }

        public sealed class GlowPattern
        {
            public string Label;
            public string Guid;
            public Vector2 Tiling;
            public bool Scales;
        }

        public static readonly GlowPattern[] GlowPatterns =
        {
            new GlowPattern { Label = "しま", Guid = "de5f6251a0f24ac0badb75067888b4c4", Tiling = new Vector2(1f, 3f) },
            new GlowPattern { Label = "ハート", Guid = "cd60a3c6a97c4d1a8686d7da21caf008", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "星", Guid = "c37ecd2bf1c04889b8984d345838554f", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "きらきら", Guid = "7d25a6f2afca4d2abc0c795dce3ac9ff", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "十字", Guid = "c581e76561eb482abb048ad55c4a75e6", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "花びら", Guid = "4088fe71c6de41198a917ddea0f592f1", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "しずく", Guid = "03209049c9cd4c6e9ffff9e79c3bcd3c", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "葉っぱ", Guid = "3d25a7b2289c45d7848edb993df2ef39", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "雪", Guid = "f817b48df36440d2b70503b8ca9a95e1", Tiling = new Vector2(5f, 5f), Scales = true },
            new GlowPattern { Label = "稲妻", Guid = "37b5038984154b8cb49566fa2a09cd30", Tiling = new Vector2(5f, 5f), Scales = true },
        };

        public static int GetGlowPattern(GlowParts g)
        {
            if (g == null || g.Setting.FlowMask == null) return -1;
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(g.Setting.FlowMask));
            return Array.FindIndex(GlowPatterns, p => p.Guid == guid);
        }

        public static void SetGlowPattern(GlowParts g, int index)
        {
            if (g == null || index < 0 || index >= GlowPatterns.Length) return;
            var p = GlowPatterns[index];
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(p.Guid));
            if (tex == null) return;
            var tiling = p.Scales ? p.Tiling * g.Setting.PatternScale : p.Tiling;
            WriteSetting(g, so =>
            {
                so.FindProperty(nameof(HairGlowSetting.FlowMask)).objectReferenceValue = tex;
                so.FindProperty(nameof(HairGlowSetting.FlowMaskTiling)).vector2Value = tiling;
            });
        }

        // SerializedObject で書く（Prefab のインスタンスでは上書きとして残すため）
        static void WriteSetting(GlowParts g, Action<SerializedObject> write)
        {
            Undo.RecordObject(g.Setting, UndoLabel);
            var so = new SerializedObject(g.Setting);
            write(so);
            so.ApplyModifiedProperties();
        }

        public static bool IsGlowMaterialOnAvatar(ColoringProduct target, GlowParts g)
        {
            if (target == null || target.Avatar == null || g == null) return false;
            var mats = new HashSet<Material>(g.Setting.TargetMaterials.Where(m => m != null));
            if (mats.Count == 0) return false;
            foreach (var r in target.Avatar.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && mats.Contains(m)) return true;
            return false;
        }
    }
}
#endif
#endif
