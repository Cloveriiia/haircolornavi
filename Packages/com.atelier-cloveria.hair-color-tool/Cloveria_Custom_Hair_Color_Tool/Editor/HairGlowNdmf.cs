#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT && AC_CHCT_NDMF_PRESENT
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using net.rs64.TexTransTool.Decal;
using net.rs64.TexTransTool.IslandSelector;
using UnityEngine;

[assembly: ExportsPlugin(typeof(AtelierCloveria.CustomHairColorTool.HairGlowPlugin))]

namespace AtelierCloveria.CustomHairColorTool
{
    // TTT より前に、マテリアルの複製へ黒い下地と発光を設定（TTT は書き先の画像が空のマテリアルには描かないため）
    public sealed class HairGlowPlugin : Plugin<HairGlowPlugin>
    {
        public override string QualifiedName => "atelier-cloveria.custom-hair-color-tool";
        public override string DisplayName => ToolInfo.ToolName;

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .BeforePlugin("net.rs64.tex-trans-tool")
                .Run(HairGlowPass.Instance)
                .PreviewingWith(new HairGlowFilter());
        }
    }

    sealed class HairGlowPass : Pass<HairGlowPass>
    {
        public override string DisplayName => "髪の発光の設定";

        protected override void Execute(BuildContext context)
        {
            var root = context.AvatarRootObject;
            var settings = root.GetComponentsInChildren<HairGlowSetting>(true);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var copies = new Dictionary<Material, Material>();
            foreach (var s in settings)
            {
                if (!HairGlow.ActiveUnder(s.transform, root.transform) || !HairGlow.IsUsable(s)) continue;
                // 元が同じかで見る（先に動いた別のプラグインが複製に差し替えていても当てるため）
                var blackOf = new Dictionary<ObjectReference, Texture2D>();
                for (var i = 0; i < s.TargetMaterials.Length; i++)
                    if (s.TargetMaterials[i] != null) blackOf[ObjectRegistry.GetReference(s.TargetMaterials[i])] = s.BlackMaps[i];
                foreach (var r in renderers)
                {
                    var mats = r.sharedMaterials;
                    var changed = false;
                    for (var i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || !blackOf.TryGetValue(ObjectRegistry.GetReference(m), out var black)) continue;
                        if (!copies.TryGetValue(m, out var copy))
                        {
                            // 焼き込みツールは元の名前で始まるかで探す
                            copy = new Material(m) { name = m.name + "_Glow" };
                            HairGlow.Apply(copy, s, black);
                            context.AssetSaver.SaveAsset(copy);
                            // 登録しないと色の Decal まで当たらなくなる（TTT は対象のマテリアルかをこの台帳で見るため）
                            ObjectRegistry.RegisterReplacedObject(m, copy);
                            copies[m] = copy;
                        }
                        mats[i] = copy;
                        changed = true;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            foreach (var s in settings) Object.DestroyImmediate(s);
        }
    }

    static class HairGlow
    {
        // TTT のオン・オフの判定に合わせる（アバター自身のオン・オフは見ない）
        public static bool ActiveUnder(Transform t, Transform avatar)
        {
            for (; t != null && t != avatar; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        public static bool IsUsable(HairGlowSetting s)
            => s.BlackMaps.Length == s.TargetMaterials.Length && s.BlackMaps.All(b => b != null)
               && (s.EmissionPrefix == "_Emission" || s.EmissionPrefix == "_Emission2nd");

        public static void Apply(Material m, HairGlowSetting s, Texture map)
        {
            var p = s.EmissionPrefix;
            if (m.HasProperty(p + "Map")) m.SetTexture(p + "Map", map);
            SetFloat(m, "_Use" + p.Substring(1), 1f);
            if (m.HasProperty(p + "Color")) m.SetColor(p + "Color", new Color(s.Strength, s.Strength, s.Strength, 1f));
            SetFloat(m, p + "Blend", 1f);
            SetFloat(m, p + "MainStrength", 0f);
            SetVector(m, p + "Blink", new Vector4(0f, 0f, Mathf.PI, 0f));
            SetVector(m, p + "Map_ScrollRotate", Vector4.zero);
            SetFloat(m, p + "Map_UVMode", 0f);
            if (m.HasProperty(p + "BlendMask"))
            {
                // 流さないときはマスクを空に（lilToon は空のマスクを白とみなし全部光る）
                m.SetTexture(p + "BlendMask", s.FlowOn ? s.FlowMask : null);
                m.SetTextureScale(p + "BlendMask", s.FlowMaskTiling);
            }
            SetVector(m, p + "BlendMask_ScrollRotate", new Vector4(0f, s.FlowVelocity, 0f, 0f));
        }

        static void SetFloat(Material m, string name, float v) { if (m.HasProperty(name)) m.SetFloat(name, v); }
        static void SetVector(Material m, string name, Vector4 v) { if (m.HasProperty(name)) m.SetVector(name, v); }
    }

    // TTT のプレビューは元のマテリアル（発光の画像が空）で当て先を決めるため、光る毛束は自前で塗る
    // 塗り方は TTT の Decal に合わせる（並び順に塗り、後ろの Decal が上に乗る）
    sealed class HairGlowFilter : IRenderFilter
    {
        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = ImmutableList.CreateBuilder<RenderGroup>();
            foreach (var root in context.GetAvatarRoots())
            {
                var s = ActiveSetting(context, root);
                if (s == null) continue;
                var targets = context.Observe(s, x => x.TargetMaterials.Where(m => m != null).ToArray(), (a, b) => a.SequenceEqual(b));
                var renderers = context.GetComponentsInChildren<Renderer>(root, true)
                    .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer)
                    .Where(r => context.Observe(r, x => x.sharedMaterials, (a, b) => a.SequenceEqual(b)).Any(m => m != null && targets.Contains(m)))
                    .ToList();
                if (renderers.Count > 0) groups.Add(RenderGroup.For(renderers));
            }
            return groups.ToImmutable();
        }

        static HairGlowSetting ActiveSetting(ComputeContext context, GameObject root)
        {
            foreach (var s in context.GetComponentsInChildren<HairGlowSetting>(root, true))
                if (context.ActiveInHierarchy(s.gameObject) && HairGlow.IsUsable(s)) return s;
            return null;
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context)
        {
            var root = context.GetAvatarRoot(group.Renderers[0].gameObject);
            var s = root != null ? ActiveSetting(context, root) : null;
            if (s == null) return Task.FromResult<IRenderFilterNode>(new HairGlowNode()); // NDMF は返したノードの OnFrame・Dispose を呼ぶため、空でも返す
            context.Observe(s, x => (x.EmissionPrefix, x.Strength, x.FlowMask, x.FlowMaskTiling, x.FlowOn, x.FlowVelocity));
            context.Observe(s, x => x.BlackMaps.ToArray(), (a, b) => a.SequenceEqual(b));

            // 下地ごとに1枚（アップロード時に TTT が下地ごとに1枚描くのに合わせる）
            var node = new HairGlowNode();
            var mapOf = new Dictionary<Material, RenderTexture>();
            for (var i = 0; i < s.TargetMaterials.Length; i++)
            {
                var m = s.TargetMaterials[i];
                if (m == null || mapOf.ContainsKey(m)) continue;
                var black = s.BlackMaps[i];
                var sameBlack = new HashSet<Material>(s.TargetMaterials.Where((t, j) => t != null && s.BlackMaps[j] == black));
                var map = CustomHairColorToolCore.PaintGlowMap(context, s, black.width, group.Renderers, sameBlack);
                node.Maps.Add(map);
                foreach (var t in sameBlack) mapOf[t] = map;
            }
            foreach (var (original, proxy) in proxyPairs)
            {
                var om = original.sharedMaterials;
                var pm = proxy.sharedMaterials;
                for (var i = 0; i < pm.Length && i < om.Length; i++)
                {
                    if (om[i] == null || pm[i] == null || !mapOf.TryGetValue(om[i], out var map)) continue;
                    var copy = new Material(pm[i]) { name = pm[i].name + "_Glow" };
                    HairGlow.Apply(copy, s, map);
                    ObjectRegistry.RegisterReplacedObject(pm[i], copy);
                    node.Materials.Add(copy);
                    pm[i] = copy;
                }
                proxy.sharedMaterials = pm;
                node.ByOriginal[original] = pm;
            }
            return Task.FromResult<IRenderFilterNode>(node);
        }
    }

    sealed class HairGlowNode : IRenderFilterNode
    {
        public readonly List<RenderTexture> Maps = new List<RenderTexture>();
        public readonly List<Material> Materials = new List<Material>();
        public readonly Dictionary<Renderer, Material[]> ByOriginal = new Dictionary<Renderer, Material[]>();

        public RenderAspects WhatChanged => RenderAspects.Material | RenderAspects.Texture;

        // プレビューの複製は毎フレーム元の Renderer の値で上書きされるため、入れ直す
        public void OnFrame(Renderer original, Renderer proxy)
        {
            if (ByOriginal.TryGetValue(original, out var mats)) proxy.sharedMaterials = mats;
        }

        public void Dispose()
        {
            foreach (var m in Materials) Object.DestroyImmediate(m);
            Materials.Clear();
            foreach (var map in Maps) { map.Release(); Object.DestroyImmediate(map); }
            Maps.Clear();
        }
    }

    public static partial class CustomHairColorToolCore
    {
        static Material s_glowPaintMat;

        internal static RenderTexture PaintGlowMap(ComputeContext context, HairGlowSetting s, int size, IEnumerable<Renderer> renderers, ICollection<Material> targets)
        {
            var rt = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "HairGlowPreview", hideFlags = HideFlags.HideAndDontSave };
            rt.Create();

            var layers = new List<(Color color, List<PinIslandSelector> pins)>();
            foreach (var d in CollectDecals(s.transform))
            {
                if (!context.ActiveInHierarchy(d.gameObject)) continue;
                var color = context.Observe(d, x => ReadColor(x));
                var selector = context.Observe(d, x => x.IslandSelector);
                if (selector == null) continue;
                var pins = new List<PinIslandSelector>();
                HairIslandPick.CollectPins(selector.transform, pins);
                pins = pins.Where(p => context.ActiveInHierarchy(p.gameObject)).ToList();
                layers.Add((color, pins));
            }

            var islands = HairIslandPick.BuildIslands(renderers, targets);
            if (s_glowPaintMat == null)
            {
                s_glowPaintMat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                s_glowPaintMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                s_glowPaintMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                s_glowPaintMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                s_glowPaintMat.SetInt("_ZWrite", 0);
                s_glowPaintMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var prev = RenderTexture.active;
            var prevSrgb = GL.sRGBWrite;
            GL.sRGBWrite = linear; // カメラの描画の外では sRGB の変換が効かないことがあるため明示
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            GL.PushMatrix();
            GL.LoadOrtho();
            s_glowPaintMat.SetPass(0);
            foreach (var (color, pins) in layers)
            {
                HairIslandPick.MatchPairs(islands, HairIslandPick.PairPins(pins, new List<PinIslandSelector>(), null));
                GL.Begin(GL.TRIANGLES);
                // TTT が書く画素と同じ値にするため、リニアのときは色をリニアに直す
                GL.Color(linear ? color.linear : color);
                foreach (var isl in islands)
                {
                    if (isl.Pairs.Count == 0) continue;
                    foreach (var uv in isl.TriUv) GL.Vertex3(uv.x, uv.y, 0f);
                }
                GL.End();
            }
            GL.PopMatrix();
            RenderTexture.active = prev;
            GL.sRGBWrite = prevSrgb;
            return rt;
        }
    }
}
#endif
#endif
