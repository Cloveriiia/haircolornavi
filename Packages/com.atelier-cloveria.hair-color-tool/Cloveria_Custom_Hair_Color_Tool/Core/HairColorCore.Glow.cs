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
