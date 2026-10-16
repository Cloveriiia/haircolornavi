#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !TTT_ABSENT
using net.rs64.TexTransTool.Decal;
#endif

namespace AtelierCloveria.CustomHairColorTool
{
    public static class ToolInfo
    {
        public const string ToolName = "Cloveria_Custom_Hair_Color_Tool";
        public const string Version = "0.6.8";
        public const string MenuPath = "Tools/Atelier_Cloveria/" + ToolName;
        public const string TexTransToolUrl = "https://ttt.rs64.net/";
        public const string ShopUrl = "https://atelier-cloveria.booth.pm/";
        public const string TestedTexTransTool = "TexTransTool 1.0.x"; // asmdef の versionDefines の範囲とそろえる

        // 焼き込みツール（別アセンブリ）とは型を参照せず、アセンブリ名とメニューのパスだけで結ぶ
        public const string BakedToolAssemblyName = "AtelierCloveria.CustomHairColorBakedTool.Editor";
        public const string BakedToolMenuPath = "Tools/Atelier_Cloveria/Cloveria_Custom_Hair_Color_Baked_Tool";
        public const string BakedMaterialTagPrefix = "ACHBT:"; // 焼き込みツールが複製マテリアルの Importer userData に付ける印
    }

    public enum HairMode
    {
        Default = 0,
        Inner = 1,
        TwoTone = 2,
        Mesh = 3,
        Gradation = 4,
    }

    public sealed class ModeInfo
    {
        public HairMode Mode;
        public string[] NodeNames;
        public string Label;
        public string[] PartLabels;
        public bool IsGradient;    // Decal 1枚＝色1つではなく、全 Decal に同じグラデを書く
    }

    public static class Modes
    {
        public static readonly ModeInfo[] All =
        {
            // ボタンは短い名前（モードが4つ以上並んでも切れないため）
            new ModeInfo { Mode = HairMode.Default, NodeNames = new[] { "Default", "Main" }, Label = "単色", PartLabels = new[] { "色" } },
            new ModeInfo { Mode = HairMode.Inner, NodeNames = new[] { "Inner" }, Label = "インナー", PartLabels = new[] { "ベース", "インナーカラー" } },
            new ModeInfo { Mode = HairMode.TwoTone, NodeNames = new[] { "2ton" }, Label = "ツートン", PartLabels = new[] { "1色目", "2色目" } },
            new ModeInfo { Mode = HairMode.Mesh, NodeNames = new[] { "Mesh" }, Label = "メッシュ", PartLabels = new[] { "1色目", "2色目" } },
            new ModeInfo { Mode = HairMode.Gradation, NodeNames = new[] { "Gradation" }, Label = "グラデーション", PartLabels = new[] { "根元", "真ん中", "毛先" }, IsGradient = true },
        };

        public static ModeInfo Get(HairMode mode) => All.First(m => m.Mode == mode);
    }

    public enum BlendPreset
    {
        Strong = 0,
        Natural = 1,
        HueOnly = 2,
        Overwrite = 3,
    }

    public static class BlendPresets
    {
        public static readonly BlendPreset[] Order = { BlendPreset.Strong, BlendPreset.Natural, BlendPreset.HueOnly, BlendPreset.Overwrite };

        public static string Key(BlendPreset p)
        {
            switch (p)
            {
                case BlendPreset.Natural: return "SoftLight";
                case BlendPreset.HueOnly: return "Hue";
                case BlendPreset.Overwrite: return "Normal";
                default: return "LinearLight";
            }
        }

        public static string Label(BlendPreset p)
        {
            switch (p)
            {
                case BlendPreset.Natural: return "自然";
                case BlendPreset.HueOnly: return "色相";
                case BlendPreset.Overwrite: return "上書き";
                default: return "しっかり";
            }
        }

        public static BlendPreset? FromKey(string key)
        {
            foreach (var p in Order) if (Key(p) == key) return p;
            return null;
        }

        // ライト系は分けて出す（明るい髪だと白っぽくなりやすいため）
        public static readonly KeyValuePair<string, KeyValuePair<string, string>[]>[] MoreGroups =
        {
            Group("ライト系", More("Overlay", "オーバーレイ"), More("HardLight", "ハードライト"), More("VividLight", "ビビッドライト"), More("PinLight", "ピンライト")),
            Group("そのほか", More("Color", "カラー"), More("Mul", "乗算"), More("Screen", "スクリーン")),
        };

        static KeyValuePair<string, string> More(string key, string label) => new KeyValuePair<string, string>(key, label);
        static KeyValuePair<string, KeyValuePair<string, string>[]> Group(string name, params KeyValuePair<string, string>[] items) => new KeyValuePair<string, KeyValuePair<string, string>[]>(name, items);

        public static string MoreLabel(string key) => MoreGroups.SelectMany(g => g.Value).Where(p => p.Key == key).Select(p => p.Value).FirstOrDefault();

        public static bool IsLight(string key) => MoreGroups[0].Value.Any(p => p.Key == key);

        public static string MoreButtonLabel(string key) => (MoreLabel(key) ?? "その他") + " ▾";
    }

#if !TTT_ABSENT
    public sealed class ModeTarget
    {
        public GameObject Node;
        public List<SingleGradationDecal> Decals = new List<SingleGradationDecal>();
        // 名前が「<色の Decal の名前>_Cut」の Decal。同じ色を置き方の違う2枚で塗る商品用（Decals には入れない）
        public readonly Dictionary<SingleGradationDecal, List<SingleGradationDecal>> Followers = new Dictionary<SingleGradationDecal, List<SingleGradationDecal>>();
        // メッシュ・ツートンだけ。Prefab に無ければ null（その機能は出さない）
        public GlowTarget Glow;
        public ColorGradTarget Grad;
        // 色の Decal が _Selectors の下の OR を参照しているときだけ（無ければ null）
        // 書き出し直した商品は _Selectors/_Custom/ でカスタム専用、古い商品はプリセットと共有（PresetsShare）
        public PinSides Pins;
    }

    // 位置は 0＝根元、1＝毛先
    public sealed class GradientShape
    {
        public bool ThreeColors;
        public Color Root = Color.white, Middle = Color.white, Tip = Color.white;
        public float Split = 0.5f;   // 2色のとき
        public float Split1 = 0.33f; // 3色：根元側の境目
        public float Split2 = 0.66f; // 3色：毛先側の境目
        // 無色＝塗らない（透明度キー 0）
        public bool RootClear, MiddleClear, TipClear;
    }

    public sealed class HairColorTarget
    {
        public GameObject Avatar;
        public GameObject Custom;
        public bool FoundInSelection;
        public readonly Dictionary<HairMode, ModeTarget> ModeTargets = new Dictionary<HairMode, ModeTarget>();
        // Mesh がある商品に残る Custom/Inner。オフにだけする（二重に塗らないため）
        public GameObject RetiredInner;

        public bool IsValid => Custom != null && ModeTargets.Count > 0;
        public bool Has(HairMode mode) => ModeTargets.ContainsKey(mode);
        public IEnumerable<HairMode> AvailableModes => Modes.All.Select(m => m.Mode).Where(Has);
        public HairMode FirstMode => AvailableModes.First();

        public int DecalCount(HairMode mode) => Has(mode) ? ModeTargets[mode].Decals.Count : 0;

        public SingleGradationDecal Decal(HairMode mode, int index)
        {
            if (!Has(mode)) return null;
            var list = ModeTargets[mode].Decals;
            return index < list.Count ? list[index] : null;
        }
    }

    public enum SwatchKind
    {
        Solid = 0,
        TwoTone = 1,
        Gradient = 2,
    }

    public sealed class PresetTile
    {
        public GameObject Group;
        public GameObject Node;
        public string GroupName;
        public string Label;
        public string RelPath;     // 例 "Inner/Sky × Pink"。カスタムをやめたときの戻り先
        public SwatchKind Kind;
        public Color ColorA = Color.white;
        public Color ColorB = Color.white;
        public Color[] Bar;
        public bool Selected;
        public Texture2D Thumbnail;
    }

    public static partial class CustomHairColorToolCore
    {
        public const string CustomNodeName = "Custom";
        public const string UndoLabel = ToolInfo.ToolName;
        public const int GradientBarSteps = 16;

        const string GradientProperty = "Gradient";
        const string BlendTypeKeyProperty = "BlendTypeKey";

        static bool NameIs(Transform t, string name) => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase);

        // 型名で判定（VRC SDK を参照しないため）
        public static bool IsAvatarDescriptor(Component c) => c != null && c.GetType().Name == "VRCAvatarDescriptor";

        public static GameObject FindAvatarRoot(GameObject go)
        {
            if (go == null) return null;
            GameObject found = null;
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (t.GetComponents<Component>().Any(IsAvatarDescriptor)) found = t.gameObject;
            }
            return found != null ? found : go.transform.root.gameObject;
        }

        public static HairColorTarget Detect(GameObject selection)
        {
            // Project のプレハブは使わない（商品ファイル本体を書き換えるため）
            if (selection != null && EditorUtility.IsPersistent(selection)) selection = null;
            if (selection != null)
            {
                var t = FindUnder(FindAvatarRoot(selection));
                if (t != null) { t.FoundInSelection = true; return t; }
            }

            var roots = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
            }
            foreach (var r in roots.OrderByDescending(g => g.GetComponents<Component>().Any(IsAvatarDescriptor)))
            {
                var t = FindUnder(r);
                if (t != null) return t;
            }
            return null;
        }

        static HairColorTarget FindUnder(GameObject avatar)
        {
            if (avatar == null) return null;
            foreach (var tr in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (!NameIs(tr, CustomNodeName)) continue;
                var target = BuildTarget(avatar, tr.gameObject);
                if (target != null) return target;
            }
            return null;
        }

        static HairColorTarget BuildTarget(GameObject avatar, GameObject custom)
        {
            var target = new HairColorTarget { Avatar = avatar, Custom = custom };
            foreach (var info in Modes.All)
            {
                var node = info.NodeNames.Select(n => FindChild(custom.transform, n)).FirstOrDefault(n => n != null);
                if (node == null) continue;
                var decals = CollectDecals(node);
                if (decals.Count == 0) continue;
                const string FollowerSuffix = "_Cut";
                var modeTarget = new ModeTarget { Node = node.gameObject };
                foreach (var d in decals)
                {
                    // グラデーションは全 Decal に同じグラデを書くため分けない
                    var lead = info.IsGradient ? null : modeTarget.Decals.FirstOrDefault(l => d.name == l.name + FollowerSuffix);
                    if (lead == null) { modeTarget.Decals.Add(d); continue; }
                    if (!modeTarget.Followers.TryGetValue(lead, out var list)) modeTarget.Followers[lead] = list = new List<SingleGradationDecal>();
                    list.Add(d);
                }
                target.ModeTargets[info.Mode] = modeTarget;
            }
            // Mesh がある商品では Inner を編集しない（メッシュでまかなうため）。Inner だけの古い商品は今までどおり
            if (target.Has(HairMode.Mesh) && target.Has(HairMode.Inner))
            {
                target.RetiredInner = target.ModeTargets[HairMode.Inner].Node;
                target.ModeTargets.Remove(HairMode.Inner);
            }
            foreach (var mode in new[] { HairMode.TwoTone, HairMode.Mesh })
            {
                if (!target.Has(mode)) continue;
                var mt = target.ModeTargets[mode];
                mt.Glow = DetectGlow(custom.transform, mt);
                mt.Grad = DetectColorGrad(custom.transform, mt);
                mt.Pins = DetectPinSides(custom.transform, mt, Modes.Get(mode));
            }
            foreach (var mode in new[] { HairMode.Default, HairMode.Gradation })
            {
                if (target.Has(mode)) target.ModeTargets[mode].Pins = DetectPaintPins(custom.transform, target.ModeTargets[mode], Modes.Get(mode));
            }
            LinkPaintPins(target);
            return target.IsValid ? target : null;
        }

        static Transform FindChild(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                if (NameIs(parent.GetChild(i), name)) return parent.GetChild(i);
            }
            return null;
        }

        // Decal を持つノードで打ち切る（その下は IslandSelector のため）
        static List<SingleGradationDecal> CollectDecals(Transform node)
        {
            var result = new List<SingleGradationDecal>();
            Collect(node, result);
            return result;
        }

        static void Collect(Transform node, List<SingleGradationDecal> result)
        {
            var self = node.GetComponent<SingleGradationDecal>();
            if (self != null) { result.Add(self); return; }
            for (var i = 0; i < node.childCount; i++) Collect(node.GetChild(i), result);
        }

        public static Color ReadColor(SingleGradationDecal decal)
        {
            if (decal == null) return Color.white;
            var g = new SerializedObject(decal).FindProperty(GradientProperty).gradientValue;
            if (g == null || g.colorKeys == null || g.colorKeys.Length == 0) return Color.white;
            return g.colorKeys[0].color;
        }

        public static string ReadBlendKey(SingleGradationDecal decal)
        {
            if (decal == null) return BlendPresets.Key(BlendPreset.Strong);
            return new SerializedObject(decal).FindProperty(BlendTypeKeyProperty).stringValue;
        }

        // 透明度キーが全部 0 なら無色（TTT は透明度 0 の所に元の画素を残す）
        // Gradient は色キーの a を見ないため、色キーに隠した色が残る（無色をやめると前の色に戻る）
        const float ClearAlpha = 0.5f / 255f;

        public static bool ReadClear(SingleGradationDecal decal)
        {
            var g = ReadGradientRaw(decal);
            var keys = g?.alphaKeys;
            if (keys == null || keys.Length == 0) return false;
            return keys.All(k => k.alpha < ClearAlpha);
        }

        public static void ApplyColor(SingleGradationDecal decal, Color color, bool clear = false)
        {
            if (decal == null) return;
            Undo.RecordObject(decal, UndoLabel);
            var so = new SerializedObject(decal);
            var a = clear ? 0f : 1f;
            so.FindProperty(GradientProperty).gradientValue = new Gradient
            {
                mode = GradientMode.Blend,
                colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(a, 0f), new GradientAlphaKey(a, 1f) },
            };
            so.ApplyModifiedProperties();
        }

        public static void ApplyBlendKey(SingleGradationDecal decal, string key)
        {
            if (decal == null || string.IsNullOrEmpty(key)) return;
            Undo.RecordObject(decal, UndoLabel);
            var so = new SerializedObject(decal);
            so.FindProperty(BlendTypeKeyProperty).stringValue = key;
            so.ApplyModifiedProperties();
        }

        // ---- グラデーション ----
        // キーの置き方は色 Prefab の書き出しに合わせる
        // 読むときは端の2キーずつの中点で割り戻す（真ん中の2キーが重なって3キーでも成り立つ）
        public static GradientShape ReadGradient(SingleGradationDecal decal)
        {
            var shape = new GradientShape();
            var g = ReadGradientRaw(decal);
            var keys = g?.colorKeys?.OrderBy(k => k.time).ToArray();
            if (keys == null || keys.Length == 0) return shape;
            shape.Root = keys[0].color;
            shape.Tip = keys[keys.Length - 1].color;
            // 透明度キーは色キーと同じ時刻に置いてある
            bool ClearAt(float t) => g.Evaluate(t).a < 0.5f;
            shape.RootClear = ClearAt(keys[0].time);
            shape.TipClear = ClearAt(keys[keys.Length - 1].time);
            if (keys.Length >= 3)
            {
                shape.ThreeColors = true;
                shape.Middle = keys[1].color;
                shape.MiddleClear = ClearAt(keys[1].time);
                shape.Split1 = (keys[0].time + keys[1].time) / 2f;
                shape.Split2 = (keys[keys.Length - 2].time + keys[keys.Length - 1].time) / 2f;
                shape.Split = (shape.Split1 + shape.Split2) / 2f;
            }
            else
            {
                shape.Split = keys.Length == 2 ? (keys[0].time + keys[1].time) / 2f : 0.5f;
                shape.Middle = Color.Lerp(shape.Root, shape.Tip, 0.5f);
            }
            return shape;
        }

        // 0＝根元・1＝真ん中・2＝毛先。色キーと透明度キーを同じ時刻に置くため1か所で決める
        static List<KeyValuePair<float, int>> KeyStops(GradientShape s)
        {
            if (s.ThreeColors)
            {
                var a = Mathf.Clamp(s.Split1, 0.02f, 0.96f);
                var b = Mathf.Clamp(s.Split2, a + 0.02f, 0.98f);
                var half = (b - a) / 2f;
                float d1 = Mathf.Min(a, half), d2 = Mathf.Min(1f - b, half);
                var keys = new List<KeyValuePair<float, int>> { new KeyValuePair<float, int>(a - d1, 0), new KeyValuePair<float, int>(a + d1, 1) };
                if (b - d2 > a + d1 + 1e-4f) keys.Add(new KeyValuePair<float, int>(b - d2, 1));
                keys.Add(new KeyValuePair<float, int>(b + d2, 2));
                return keys;
            }
            var t = Mathf.Clamp(s.Split, 0.05f, 0.95f);
            var w = Mathf.Min(1f, 2f * t, 2f * (1f - t));
            return new List<KeyValuePair<float, int>> { new KeyValuePair<float, int>(t - w / 2f, 0), new KeyValuePair<float, int>(t + w / 2f, 2) };
        }

        public static GradientColorKey[] BuildKeys(GradientShape s)
            => KeyStops(s).Select(k => new GradientColorKey(k.Value == 0 ? s.Root : k.Value == 1 ? s.Middle : s.Tip, k.Key)).ToArray();

        // 無色が無ければ不透明の2キー（出荷時の Prefab と同じ）
        public static GradientAlphaKey[] BuildAlphaKeys(GradientShape s)
        {
            bool ClearOf(int stop) => stop == 0 ? s.RootClear : stop == 1 ? s.ThreeColors && s.MiddleClear : s.TipClear;
            var stops = KeyStops(s);
            if (!stops.Any(k => ClearOf(k.Value))) return new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
            return stops.Select(k => new GradientAlphaKey(ClearOf(k.Value) ? 0f : 1f, k.Key)).ToArray();
        }

        public static void ApplyGradient(IList<SingleGradationDecal> decals, GradientShape shape)
        {
            var list = decals.Where(d => d != null).ToArray();
            if (list.Length == 0) return;
            var keys = BuildKeys(shape);
            var alphas = BuildAlphaKeys(shape);
            Undo.RecordObjects(list, UndoLabel);
            foreach (var d in list)
            {
                var so = new SerializedObject(d);
                var prop = so.FindProperty(GradientProperty);
                var old = prop.gradientValue ?? new Gradient();
                prop.gradientValue = new Gradient { mode = old.mode, alphaKeys = alphas, colorKeys = keys };
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static void ApplyGradientPerPart(IList<SingleGradationDecal> decals, GradientShape colors, Func<SingleGradationDecal, GradientShape> splitsOf)
        {
            var list = decals.Where(d => d != null).ToArray();
            if (list.Length == 0 || colors == null) return;
            Undo.RecordObjects(list, UndoLabel);
            foreach (var d in list)
            {
                var s = splitsOf != null ? splitsOf(d) : null;
                var shape = new GradientShape
                {
                    ThreeColors = colors.ThreeColors,
                    Root = colors.Root,
                    Middle = colors.Middle,
                    Tip = colors.Tip,
                    Split = s != null ? s.Split : colors.Split,
                    Split1 = s != null ? s.Split1 : colors.Split1,
                    Split2 = s != null ? s.Split2 : colors.Split2,
                    RootClear = colors.RootClear,
                    MiddleClear = colors.MiddleClear,
                    TipClear = colors.TipClear,
                };
                var so = new SerializedObject(d);
                var prop = so.FindProperty(GradientProperty);
                var old = prop.gradientValue ?? new Gradient();
                prop.gradientValue = new Gradient { mode = old.mode, alphaKeys = BuildAlphaKeys(shape), colorKeys = BuildKeys(shape) };
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static Gradient ReadGradientRaw(SingleGradationDecal decal)
        {
            if (decal == null) return null;
            return new SerializedObject(decal).FindProperty(GradientProperty).gradientValue;
        }

        public static void ApplyGradientRaw(SingleGradationDecal decal, Gradient g)
        {
            if (decal == null || g == null) return;
            Undo.RecordObject(decal, UndoLabel);
            var so = new SerializedObject(decal);
            so.FindProperty(GradientProperty).gradientValue = g;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetNodeActive(GameObject go, bool active)
        {
            if (go == null || go.activeSelf == active) return;
            Undo.RecordObject(go, UndoLabel);
            go.SetActive(active);
            RecordInstanceChange(go);
        }

        // 上書きをすぐ記録する（フレームを待たずに Undo と上書きをそろえるため）
        static void RecordInstanceChange(GameObject go)
        {
            if (go != null && PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        }

        // ---- プリセット ----
        // この名前のノードだけを触る（_Selectors など、ほかの兄弟は読み書きしない）
        static readonly string[] PresetGroupNames = { "Default", "Main", "Inner", "2ton", "Mesh", "Gradation" };

        // 親の名前「A × B」から決められないときの1色目
        static readonly string[] FirstColorNames = { "Main", "Base", "Hair_Main" };

        static readonly string[] GradientSampleNames = { "Hair_Base", "Hair_Back" };

        public static string GroupLabel(string groupName)
        {
            switch ((groupName ?? "").ToLowerInvariant())
            {
                case "default": return "単色（Default）";
                case "main": return "単色（Main）";
                case "inner": return "インナーカラー（Inner）";
                case "2ton": return "ツートン（2ton）";
                case "mesh": return "メッシュ（Mesh）";
                case "gradation": return "グラデーション（Gradation）";
                default: return groupName;
            }
        }

        public static GameObject ProductRoot(HairColorTarget target)
        {
            if (target == null || !target.IsValid) return null;
            var parent = target.Custom.transform.parent;
            return parent != null ? parent.gameObject : null;
        }

        public static List<GameObject> FindPresetGroups(HairColorTarget target)
        {
            var result = new List<GameObject>();
            var root = ProductRoot(target);
            if (root == null) return result;
            foreach (var name in PresetGroupNames)
            {
                var g = FindChild(root.transform, name);
                if (g == null || g.gameObject == target.Custom) continue;
                result.Add(g.gameObject);
            }
            return result;
        }

        public static List<PresetTile> BuildTiles(HairColorTarget target)
        {
            var tiles = new List<PresetTile>();
            foreach (var group in FindPresetGroups(target))
            {
                var gt = group.transform;
                for (var i = 0; i < gt.childCount; i++)
                {
                    var c = gt.GetChild(i);
                    var decals = CollectDecals(c);
                    if (decals.Count == 0) continue;
                    var tile = new PresetTile
                    {
                        Group = group,
                        Node = c.gameObject,
                        GroupName = group.name,
                        Label = c.name,
                        RelPath = group.name + "/" + c.name,
                        Selected = group.activeSelf && c.gameObject.activeSelf,
                    };
                    FillSwatch(tile, decals);
                    tiles.Add(tile);
                }
            }
            return tiles;
        }

        static void FillSwatch(PresetTile tile, List<SingleGradationDecal> decals)
        {
            switch (tile.GroupName.ToLowerInvariant())
            {
                case "gradation":
                    {
                        tile.Kind = SwatchKind.Gradient;
                        var sample = GradientSampleNames
                            .Select(n => decals.FirstOrDefault(d => NameIs(d.transform, n)))
                            .FirstOrDefault(d => d != null) ?? decals[0];
                        var g = ReadGradientRaw(sample);
                        tile.Bar = new Color[GradientBarSteps];
                        for (var i = 0; i < GradientBarSteps; i++)
                        {
                            var c = g != null ? g.Evaluate((i + 0.5f) / GradientBarSteps) : Color.white;
                            c.a = 1f;
                            tile.Bar[i] = c;
                        }
                        tile.ColorA = tile.Bar[0];
                        tile.ColorB = tile.Bar[GradientBarSteps - 1];
                        break;
                    }
                case "inner":
                case "2ton":
                case "mesh":
                    {
                        if (decals.Count < 2) goto default;
                        tile.Kind = SwatchKind.TwoTone;
                        OrderTwoColors(tile.Node.transform, decals, out var first, out var second);
                        tile.ColorA = ReadColor(first);
                        tile.ColorB = ReadColor(second);
                        break;
                    }
                default:
                    tile.Kind = SwatchKind.Solid; // Decal が複数（髪＋しっぽ など）でも同じ色のため先頭を使う
                    tile.ColorA = ReadColor(decals[0]);
                    tile.ColorB = tile.ColorA;
                    break;
            }
            tile.ColorA.a = 1f;
            tile.ColorB.a = 1f;
        }

        // 2色の並び：親の名前「A × B」→ Main/Base/Hair_Main → Hierarchy の順
        // 比べるのは色ノードの直下の子（同じ包みの中の2枚を1色目・2色目にしないため）
        static void OrderTwoColors(Transform node, List<SingleGradationDecal> decals, out SingleGradationDecal first, out SingleGradationDecal second)
        {
            var parts = node.name.Split('×');
            if (parts.Length == 2)
            {
                var a = parts[0].Trim();
                var b = parts[1].Trim();
                var da = decals.FirstOrDefault(d => NameIs(SideOf(node, d), a));
                var db = decals.FirstOrDefault(d => da != null && SideOf(node, d) != SideOf(node, da) && NameIs(SideOf(node, d), b));
                if (da != null && db != null) { first = da; second = db; return; }
            }
            var named = decals.FirstOrDefault(d => FirstColorNames.Any(n => NameIs(SideOf(node, d), n)));
            if (named != null) { first = named; second = decals.First(d => SideOf(node, d) != SideOf(node, named)); return; }
            var head = decals[0];
            first = head;
            second = decals.FirstOrDefault(d => SideOf(node, d) != SideOf(node, head)) ?? decals[1];
        }

        static Transform SideOf(Transform node, SingleGradationDecal d)
        {
            var t = d.transform;
            while (t.parent != null && t.parent != node) t = t.parent;
            return t;
        }

        // 包みの名前が Custom の色の Decal と同じときだけ（古い商品の入れ子と取り違えないため）
        public static List<SingleGradationDecal> PresetColorGrad(PresetTile tile, SingleGradationDecal lead, string customDecalName)
        {
            var side = SideOf(tile.Node.transform, lead);
            if (side.GetComponent<SingleGradationDecal>() != null || !NameIs(side, customDecalName)) return new List<SingleGradationDecal>();
            return CollectDecals(side);
        }

        public static List<SingleGradationDecal> PresetDecals(PresetTile tile)
            => tile != null && tile.Node != null ? CollectDecals(tile.Node.transform) : new List<SingleGradationDecal>();

        public static bool OrderPresetColors(PresetTile tile, List<SingleGradationDecal> decals, out SingleGradationDecal first, out SingleGradationDecal second)
        {
            first = second = null;
            if (tile == null || tile.Node == null || decals == null || decals.Count < 2) return false;
            OrderTwoColors(tile.Node.transform, decals, out first, out second);
            return first != null && second != null;
        }

        public static SingleGradationDecal GradientSampleOf(List<SingleGradationDecal> decals)
        {
            if (decals == null || decals.Count == 0) return null;
            return GradientSampleNames.Select(n => decals.FirstOrDefault(d => NameIs(d.transform, n))).FirstOrDefault(d => d != null) ?? decals[0];
        }

        public static HairMode? ModeOfGroup(HairColorTarget target, string groupName)
        {
            if (target == null || string.IsNullOrEmpty(groupName)) return null;
            foreach (var info in Modes.All)
            {
                if (!info.NodeNames.Any(n => string.Equals(n, groupName, StringComparison.OrdinalIgnoreCase))) continue;
                return target.Has(info.Mode) ? info.Mode : (HairMode?)null;
            }
            return null;
        }

        // ---- サムネイル ----
        // 置き場は商品プレハブと同じフォルダの Thumbnails/。名前は <グループ>__<色の名前>.png（書き出し側の規則）
        public const string ThumbnailFolderName = "Thumbnails";
        public const string NoneThumbnailFileName = "_None.png";

        // 書き出し側と同じ規則（" × " を先に "_x_" にする）
        public static string SanitizePresetName(string name)
        {
            var s = (name ?? "").Replace(" × ", "_x_");
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                var ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';
                sb.Append(ok ? ch : '_');
            }
            return sb.ToString();
        }

        public static string ThumbnailFileName(PresetTile tile) => tile.GroupName + "__" + SanitizePresetName(tile.Label) + ".png";

        public static string ThumbnailFolder(HairColorTarget target)
        {
            var root = ProductRoot(target);
            if (root == null) return null;
            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrEmpty(prefabPath)) return null;
            var dir = System.IO.Path.GetDirectoryName(prefabPath);
            if (string.IsNullOrEmpty(dir)) return null;
            var folder = dir.Replace('\\', '/') + "/" + ThumbnailFolderName;
            return AssetDatabase.IsValidFolder(folder) ? folder : null;
        }

        // Undo のグループは呼ぶ側で切る
        public static void ApplySelection(HairColorTarget target, IList<PresetTile> tiles, PresetTile chosen, bool custom)
        {
            if (target == null || !target.IsValid) return;
            var groups = FindPresetGroups(target);
            foreach (var t in tiles) if (t.Group != null && !groups.Contains(t.Group)) groups.Add(t.Group);

            var touched = new List<UnityEngine.Object>();
            touched.AddRange(groups);
            touched.AddRange(tiles.Select(t => t.Node).Where(n => n != null));
            touched.Add(target.Custom);
            Undo.RecordObjects(touched.ToArray(), UndoLabel);

            foreach (var g in groups) if (!g.activeSelf) { g.SetActive(true); RecordInstanceChange(g); }
            foreach (var t in tiles)
            {
                if (t.Node == null) continue;
                var on = t == chosen;
                if (t.Node.activeSelf != on) { t.Node.SetActive(on); RecordInstanceChange(t.Node); }
            }
            if (target.Custom.activeSelf != custom) { target.Custom.SetActive(custom); RecordInstanceChange(target.Custom); }
        }

        public static GameObject FindInactiveAncestor(HairColorTarget target)
        {
            var root = ProductRoot(target);
            if (root == null) return null;
            for (var t = root.transform; t != null; t = t.parent)
            {
                if (target.Avatar != null && t.gameObject == target.Avatar) break;
                if (!t.gameObject.activeSelf) return t.gameObject;
            }
            return null;
        }

        // 変わったときだけ焼き込みの判定をやり直すため
        public static int MaterialSignature(GameObject avatar)
        {
            if (avatar == null) return 0;
            unchecked
            {
                var h = 17;
                foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        h = h * 31 + (m != null ? m.GetInstanceID() : 0);
                return h;
            }
        }

        public static bool HasBakedMaterial(GameObject avatar)
        {
            if (avatar == null) return false;
            var seen = new HashSet<string>();
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var path = AssetDatabase.GetAssetPath(m);
                    if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                    var importer = AssetImporter.GetAtPath(path);
                    if (importer == null || string.IsNullOrEmpty(importer.userData)) continue;
                    if (importer.userData.StartsWith(ToolInfo.BakedMaterialTagPrefix, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        public static void SetMode(HairColorTarget target, HairMode mode)
        {
            if (target == null || !target.IsValid) return;
            if (!target.Has(mode)) mode = target.FirstMode;
            foreach (var kv in target.ModeTargets) SetNodeActive(kv.Value.Node, kv.Key == mode);
            SetNodeActive(target.RetiredInner, false);
            SyncExtraNodes(target, mode);
        }

        // _Glow・_Grad は Custom/<モード> の兄弟のため、モードを替えても親ごとは消えない
        // 中の Decal は触らない（戻れば前の光・グラデが続く）。使っていなければ親もオフ（発光の設定をマテリアルに入れないため）
        public static void SyncExtraNodes(HairColorTarget target, HairMode mode)
        {
            if (target == null || !target.IsValid) return;
            GameObject glowRoot = null, gradRoot = null;
            bool glowOn = false, gradOn = false;
            foreach (var kv in target.ModeTargets)
            {
                var g = kv.Value.Glow;
                if (g != null)
                {
                    var on = kv.Key == mode && ReadGlowSide(g) >= 0;
                    SetNodeActive(g.Node, on);
                    glowRoot = g.Root;
                    glowOn |= on;
                }
                var c = kv.Value.Grad;
                if (c != null)
                {
                    var on = kv.Key == mode && (IsColorGradOn(c, 0) || IsColorGradOn(c, 1));
                    SetNodeActive(c.Node, on);
                    gradRoot = c.Root;
                    gradOn |= on;
                }
            }
            if (glowRoot != null) SetNodeActive(glowRoot, glowOn);
            if (gradRoot != null) SetNodeActive(gradRoot, gradOn);
        }

        public static HairMode? ReadMode(HairColorTarget target)
        {
            if (target == null || !target.IsValid) return null;
            var active = target.AvailableModes.Where(m => target.ModeTargets[m].Node.activeSelf).ToList();
            return active.Count == 1 ? active[0] : (HairMode?)null;
        }
    }
#endif

    public sealed partial class CustomHairColorToolWindow : EditorWindow
    {
        [MenuItem(ToolInfo.MenuPath)]
        public static void Open()
        {
            var w = GetWindow<CustomHairColorToolWindow>(ToolInfo.ToolName);
            w.minSize = new Vector2(360, 620);
            w.Show();
        }

#if TTT_ABSENT
        void DrawTttMissingIMGUI()
        {
            GUILayout.Space(4);
#if AC_CHCT_TTT_INSTALLED
            // 確かめた範囲の外の版（TTT は同じマイナー版の間しか互換を約束していないため）
            GUILayout.Label("この版の TexTransTool では、まだ動作を確かめていません（" + ToolInfo.TestedTexTransTool + " で確かめています）。\nThis TexTransTool version has not been tested yet.", ImguiSkin.Warn);
            GUILayout.Label("このツールの更新をお待ちください。お問い合わせは Atelier Cloveria へお願いします（TexTransTool の作者さんではなく）。", ImguiSkin.Note);
#else
            GUILayout.Label("TexTransTool が必要です。\nTexTransTool is required.", ImguiSkin.Warn);
            EditorGUILayout.SelectableLabel(ToolInfo.TexTransToolUrl, ImguiSkin.Text, GUILayout.Height(18));
            if (GUILayout.Button("導入ページを開く / Open download page", ImguiSkin.Button, GUILayout.Height(26)))
            {
                Application.OpenURL(ToolInfo.TexTransToolUrl);
            }
#endif
        }
#else
        [Serializable]
        sealed class PartState
        {
            public float Hue;
            public Tone Tone = Tone.None;
            public Color Color = Color.white;
            public string BlendKey = "LinearLight";
            public bool Clear;
        }

        // グラデーションのモードの色は State(Gradation, 0〜2)、メッシュ・ツートンの色ごとのグラデは Stops に持つ
        [Serializable]
        sealed class GradState
        {
            public bool Three;
            public float Split = 0.5f;
            public float Split1 = 0.33f;
            public float Split2 = 0.66f;
            public bool MiddleKnown;     // まだなら3色にしたとき根元と毛先の間の色にする
            public PartState[] Stops = new PartState[3];

            public PartState Stop(int i)
            {
                if (Stops == null || Stops.Length < 3) Array.Resize(ref Stops, 3);
                if (Stops[i] == null) Stops[i] = new PartState();
                return Stops[i];
            }
        }

        [Serializable]
        sealed class OriginalValue
        {
            public int DecalInstanceId;
            public Color Color;
            public string BlendKey;
            public Gradient Gradient;
            public bool Clear;
        }

        sealed class TileEntry
        {
            public string Label;
            public PresetTile Preset; // null＝デフォルトの髪
            public Texture2D Thumbnail;
        }

        const int PartsPerMode = 3;
        const string NoneLabel = "デフォルトの髪";

        [SerializeField] HairMode _mode = HairMode.Default;
        [SerializeField] int _partIndex;
        // 古い _states は読まない（PartsPerMode を 2→3 にして添字の並びが変わったため）
        [SerializeField] PartState[] _partStates = new PartState[Modes.All.Length * PartsPerMode];
        [SerializeField] GradState[] _gradStates = new GradState[Modes.All.Length * PartsPerMode];
        [SerializeField] int _stopIndex;
        [SerializeField] int _lastCustomId;     // 替わったら MiddleKnown を捨てる（前のアバターの色を持ち越さないため）
        [SerializeField] Vector2 _scroll;
        [SerializeField] List<OriginalValue> _originals = new List<OriginalValue>();
        // 「色を選ぶ」に戻る先（RelPath）。空＝色を付けない
        [SerializeField] string _presetBeforeCustom = "";

        HairColorTarget _target;
        int _dragUndoGroup = -1;
        bool _showDetail;

        // Refresh で読む（毎フレーム Hierarchy や SerializedObject を見ないため）
        List<PresetTile> _tiles = new List<PresetTile>();
        int _selectedCount;
        bool _customOn;
        bool _anyGroupOff;         // 古い版の「カスタム有効」のまま保存されたシーン
        GameObject _inactiveNode;
        bool _bakedMaterialFound;
        bool _hasThumbnails;
        Texture2D _noneThumbnail;
        InfoState _infoState;
        string _infoNeed;
        string _productName;
        bool _customDiffers;       // 「前のカスタムの続きから」を出すか
        readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>();

        static bool? s_bakedToolPresent;
        static bool BakedToolPresent
        {
            get
            {
                if (!s_bakedToolPresent.HasValue)
                {
                    s_bakedToolPresent = AppDomain.CurrentDomain.GetAssemblies()
                        .Any(a => a.GetName().Name == ToolInfo.BakedToolAssemblyName);
                }
                return s_bakedToolPresent.Value;
            }
        }

        PartState State(HairMode mode, int index)
        {
            var need = Modes.All.Length * PartsPerMode;
            if (_partStates == null || _partStates.Length < need) Array.Resize(ref _partStates, need);
            var i = (int)mode * PartsPerMode + index;
            if (_partStates[i] == null) _partStates[i] = new PartState();
            return _partStates[i];
        }

        GradState Grad(HairMode mode, int color)
        {
            var need = Modes.All.Length * PartsPerMode;
            if (_gradStates == null || _gradStates.Length < need) Array.Resize(ref _gradStates, need);
            var i = (int)mode * PartsPerMode + (Modes.Get(mode).IsGradient ? 0 : color);
            if (_gradStates[i] == null) _gradStates[i] = new GradState();
            return _gradStates[i];
        }

        PartState GradStop(HairMode mode, int color, int stop) => Modes.Get(mode).IsGradient ? State(mode, stop) : Grad(mode, color).Stop(stop);

        GradientShape ShapeOf(HairMode mode, int color)
        {
            var g = Grad(mode, color);
            return new GradientShape
            {
                ThreeColors = g.Three,
                Root = GradStop(mode, color, 0).Color,
                Middle = GradStop(mode, color, 1).Color,
                Tip = GradStop(mode, color, 2).Color,
                Split = g.Split,
                Split1 = g.Split1,
                Split2 = g.Split2,
                RootClear = GradStop(mode, color, 0).Clear,
                MiddleClear = GradStop(mode, color, 1).Clear,
                TipClear = GradStop(mode, color, 2).Clear,
            };
        }

        PartState Current => ColorGradOn ? Grad(_mode, _partIndex).Stop(_stopIndex) : State(_mode, _partIndex);
        bool IsGradientMode => Modes.Get(_mode).IsGradient;
        SingleGradationDecal CurrentDecal => _target != null && _target.IsValid && !IsGradientMode ? _target.Decal(_mode, _partIndex) : null;
        List<SingleGradationDecal> ModeDecals => _target != null && _target.Has(_mode) ? _target.ModeTargets[_mode].Decals : new List<SingleGradationDecal>();

        int CurrentStop
        {
            get => IsGradientMode ? _partIndex : _stopIndex;
            set { if (IsGradientMode) _partIndex = value; else _stopIndex = value; }
        }

        IEnumerable<SingleGradationDecal> WithFollowers(SingleGradationDecal decal) => WithFollowers(_mode, decal);

        IEnumerable<SingleGradationDecal> WithFollowers(HairMode mode, SingleGradationDecal decal)
        {
            yield return decal;
            if (_target == null || !_target.Has(mode) || !_target.ModeTargets[mode].Followers.TryGetValue(decal, out var list)) yield break;
            foreach (var d in list) if (d != null) yield return d;
        }

        List<int> VisibleParts(HairMode mode)
        {
            if (Modes.Get(mode).IsGradient) return Grad(mode, 0).Three ? new List<int> { 0, 1, 2 } : new List<int> { 0, 2 };
            var n = _target != null ? Mathf.Min(_target.DecalCount(mode), PartsPerMode) : 1;
            return Enumerable.Range(0, Mathf.Max(1, n)).ToList();
        }

        int GradColor => IsGradientMode ? 0 : _partIndex;

        GradientShape CurrentGradientShape() => ShapeOf(_mode, GradColor);

        // wantsMouseMove は要らない（ホバーは USS の :hover のため）
        void OnEnable()
        {
            LoadUiPrefs();
            _recent = RecentColors.Load();
            Undo.undoRedoPerformed += OnUndoRedo;
            Refresh();
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            FlushRecentPending();
            FlushGlowPending();
            StopPicking();
            DisposeGlowPreview();
        }

        void OnFocus() { _recent = RecentColors.Load(); } // ほかのウィンドウで増えた色も出すため

        // 最近の色に入れるのは手が止まってから（ドラッグ途中の色で埋めないため）
        // 光のつまみは書き込みを間引いているため、最後の値をここで書く
        void Update()
        {
            if (_recentPending && EditorApplication.timeSinceStartup - _recentPendingAt > RecentSettleSeconds) FlushRecentPending();
            if (AnyGlowPending && EditorApplication.timeSinceStartup - _glowLastWrite >= GlowWriteInterval) FlushGlowPending();
            TickGlowShot();
            TickFlowRepaint();
        }

        void OnSelectionChange() { Refresh(); Repaint(); }
        void OnHierarchyChange() { QueueRefresh(); }
        void OnProjectChange() { HairColoringInfos.Forget(); QueueRefresh(); }
        void OnUndoRedo() { _pickMessage = null; Refresh(); Repaint(); } // 「〜にしました」は Undo で古くなるため消す

        // 読むだけ（Hierarchy には書かない）
        void Refresh()
        {
            RefreshState();
            _refreshedSinceSync = true;
            RequestUiSync();
        }

        void RefreshState()
        {
            _tiles = new List<PresetTile>();
            _selectedCount = 0;
            _customOn = false;
            _anyGroupOff = false;
            _inactiveNode = null;
            _bakedMaterialFound = false;
            _hasThumbnails = false;
            _noneThumbnail = null;

            _infoState = InfoState.Ok;
            _infoNeed = null;
            _productName = null;

            _target = CustomHairColorToolCore.Detect(Selection.activeGameObject);
            if (_target == null || !_target.IsValid) return;
            _infoState = HairColoringInfos.Check(CustomHairColorToolCore.ProductRoot(_target), out var info);
            if (_infoState != InfoState.Ok)
            {
                _infoNeed = info != null ? info.MinToolVersion : null;
                _target = null;
                return;
            }
            _productName = info != null ? info.ProductName : null;

            var customId = _target.Custom.GetInstanceID();
            if (customId != _lastCustomId)
            {
                _lastCustomId = customId;
                StopPicking();
                if (_gradStates != null) foreach (var g in _gradStates) if (g != null) g.MiddleKnown = false;
            }

            foreach (var mode in _target.AvailableModes)
            {
                if (Modes.Get(mode).IsGradient) { SyncGradient(mode); continue; }
                for (var i = 0; i < PartsPerMode; i++)
                {
                    var decal = _target.Decal(mode, i);
                    if (decal == null) continue;
                    RememberOriginal(decal);
                    SyncFromDecal(State(mode, i), decal);
                    SyncColorGrad(mode, i);
                }
            }

            var mode2 = CustomHairColorToolCore.ReadMode(_target);
            if (mode2.HasValue) _mode = mode2.Value;
            if (!_target.Has(_mode)) _mode = _target.FirstMode;
            var parts = VisibleParts(_mode);
            if (!parts.Contains(_partIndex)) _partIndex = parts[0];
            if (_stopIndex < 0 || _stopIndex > 2 || (_stopIndex == 1 && !IsGradientMode && !Grad(_mode, _partIndex).Three)) _stopIndex = 0;

            _tiles = CustomHairColorToolCore.BuildTiles(_target);
            LoadThumbnails();
            _customOn = _target.Custom.activeSelf;
            _selectedCount = _tiles.Count(t => t.Selected) + (_customOn ? 1 : 0);
            _anyGroupOff = CustomHairColorToolCore.FindPresetGroups(_target).Any(g => !g.activeSelf);
            _inactiveNode = CustomHairColorToolCore.FindInactiveAncestor(_target);
            var sig = CustomHairColorToolCore.MaterialSignature(_target.Avatar);
            if (sig != _materialSig) { _materialSig = sig; _bakedCache = CustomHairColorToolCore.HasBakedMaterial(_target.Avatar); }
            _bakedMaterialFound = _bakedCache;
            RefreshGlow();
            RefreshPick();
            if (_picking && !PickAvailable) StopPicking();
            RefreshPartShapes();
            _customDiffers = ComputeCustomDiffers();
            DecideFirstTopTab();
        }

        // 最初に見た値か Prefab の値と1つでも違うか（グラデの Decal も見る）
        bool ComputeCustomDiffers()
        {
            foreach (var kv in _target.ModeTargets)
            {
                var mt = kv.Value;
                var decals = new List<SingleGradationDecal>(mt.Decals);
                if (mt.Grad != null) { decals.AddRange(mt.Grad.Decals[0]); decals.AddRange(mt.Grad.Decals[1]); }
                foreach (var d in decals)
                {
                    if (d == null) continue;
                    var o = FindOriginal(d);
                    if (o != null && (CustomHairColorToolCore.ReadBlendKey(d) != o.BlendKey || !SameGradient(CustomHairColorToolCore.ReadGradientRaw(d), o.Gradient))) return true;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(d);
                    if (src != null && (CustomHairColorToolCore.ReadBlendKey(d) != CustomHairColorToolCore.ReadBlendKey(src) || !SameGradient(CustomHairColorToolCore.ReadGradientRaw(d), CustomHairColorToolCore.ReadGradientRaw(src)))) return true;
                }
                if (mt.Grad != null && (CustomHairColorToolCore.IsColorGradOn(mt.Grad, 0) || CustomHairColorToolCore.IsColorGradOn(mt.Grad, 1))) return true;
            }
            return false;
        }

        static bool SameGradient(Gradient a, Gradient b)
        {
            if (a == null || b == null) return a == b;
            var ka = a.colorKeys;
            var kb = b.colorKeys;
            if (ka.Length != kb.Length) return false;
            for (var i = 0; i < ka.Length; i++)
                if (!ColorEquals(ka[i].color, kb[i].color) || Mathf.Abs(ka[i].time - kb[i].time) > 1e-3f) return false;
            // 透明度キーも比べる（無色にしただけでも違いとするため）
            var aa = a.alphaKeys;
            var ab = b.alphaKeys;
            if (aa.Length != ab.Length) return false;
            for (var i = 0; i < aa.Length; i++)
                if (Mathf.Abs(aa[i].alpha - ab[i].alpha) > 1e-3f || Mathf.Abs(aa[i].time - ab[i].time) > 1e-3f) return false;
            return true;
        }

        int _materialSig;
        bool _bakedCache;
        bool _refreshQueued;

        // Refresh の中だけで読む（OnGUI では読まない）
        void LoadThumbnails()
        {
            var folder = CustomHairColorToolCore.ThumbnailFolder(_target);
            if (folder == null) return;
            foreach (var t in _tiles) t.Thumbnail = LoadThumbnail(folder + "/" + CustomHairColorToolCore.ThumbnailFileName(t));
            _noneThumbnail = LoadThumbnail(folder + "/" + CustomHairColorToolCore.NoneThumbnailFileName);
            _hasThumbnails = _noneThumbnail != null || _tiles.Any(t => t.Thumbnail != null);
        }

        // 消えた・読み直された Texture（== null）は読み直す
        Texture2D LoadThumbnail(string assetPath)
        {
            if (_thumbCache.TryGetValue(assetPath, out var cached) && cached != null) return cached;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            _thumbCache[assetPath] = tex;
            return tex;
        }

        // 読み直しは1フレームに1回（Hierarchy の変更が1フレームに多数届くため）
        void QueueRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                _refreshQueued = false;
                if (this == null) return;
                Refresh();
                Repaint();
            };
        }

        // 先頭の Decal だけ読む（全 Decal に同じグラデを書くため）
        void SyncGradient(HairMode mode)
        {
            var decals = _target.ModeTargets[mode].Decals;
            foreach (var d in decals) RememberOriginal(d);
            SyncGradientFrom(decals, mode, 0);
        }

        void SyncGradientFrom(IList<SingleGradationDecal> decals, HairMode mode, int color)
        {
            var first = decals.FirstOrDefault(d => d != null);
            if (first == null) return;
            var g = Grad(mode, color);
            var shape = CustomHairColorToolCore.ReadGradient(first);
            g.Three = shape.ThreeColors;
            if (shape.ThreeColors) { g.Split1 = shape.Split1; g.Split2 = shape.Split2; }
            else g.Split = shape.Split;
            SyncColor(GradStop(mode, color, 0), shape.Root);
            SyncColor(GradStop(mode, color, 2), shape.Tip);
            GradStop(mode, color, 0).Clear = shape.RootClear; // SyncColor は色が同じだと何もしないため別に入れる
            GradStop(mode, color, 2).Clear = shape.TipClear;
            if (shape.ThreeColors) { SyncColor(GradStop(mode, color, 1), shape.Middle); GradStop(mode, color, 1).Clear = shape.MiddleClear; g.MiddleKnown = true; }
            var blend = CustomHairColorToolCore.ReadBlendKey(first);
            for (var i = 0; i < PartsPerMode; i++) GradStop(mode, color, i).BlendKey = blend;
        }

        void RememberOriginal(SingleGradationDecal decal)
        {
            var id = decal.GetInstanceID();
            if (_originals.Any(o => o.DecalInstanceId == id)) return;
            _originals.Add(new OriginalValue
            {
                DecalInstanceId = id,
                Color = CustomHairColorToolCore.ReadColor(decal),
                BlendKey = CustomHairColorToolCore.ReadBlendKey(decal),
                Gradient = CustomHairColorToolCore.ReadGradientRaw(decal),
                Clear = CustomHairColorToolCore.ReadClear(decal),
            });
        }

        OriginalValue FindOriginal(SingleGradationDecal decal)
        {
            if (decal == null) return null;
            var id = decal.GetInstanceID();
            return _originals.FirstOrDefault(o => o.DecalInstanceId == id);
        }

        static void SyncFromDecal(PartState st, SingleGradationDecal decal)
        {
            SyncColor(st, CustomHairColorToolCore.ReadColor(decal));
            st.BlendKey = CustomHairColorToolCore.ReadBlendKey(decal);
            st.Clear = CustomHairColorToolCore.ReadClear(decal);
        }

        static void SyncColor(PartState st, Color c)
        {
            if (ColorEquals(c, st.Color)) return;
            st.Color = c;
            Color.RGBToHSV(c, out var h, out _, out _);
            st.Hue = h;
        }

        static bool ColorEquals(Color a, Color b)
        {
            const float eps = 1f / 512f;
            return Mathf.Abs(a.r - b.r) < eps && Mathf.Abs(a.g - b.g) < eps && Mathf.Abs(a.b - b.b) < eps;
        }

        // IMGUIContainer の先頭と画面全体の MouseUp から呼ぶ
        void OnMouseReleased()
        {
            FlushGlowPending();
            if (_ringDragged) { _ringDragged = false; if (!Current.Clear) PushRecent(Current.Color); }
            if (_dragUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(_dragUndoGroup);
                _dragUndoGroup = -1;
            }
        }

        static void SectionHeader(string text)
        {
            GUILayout.Label(text, ImguiSkin.Bold);
        }

        static string PartLabel(HairMode mode, int index)
        {
            var labels = Modes.Get(mode).PartLabels;
            return index < labels.Length ? labels[index] : (index + 1) + "色目";
        }

        string AutoFoundMessage()
        {
            if (_target == null || !_target.IsValid || _target.FoundInSelection) return null;
            return Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject)
                ? "選択中のアバターに髪色の設定が見つからなかったため、Scene 内から自動で見つけました。別のアバターにしたいときは、Hierarchy でそのアバターを選んでください。"
                : "Hierarchy で選んでいないので、シーンの中から自動で見つけました。別のアバターにしたいときは、そのアバターを Hierarchy で選んでください。";
        }

        static string BuildNotFoundMessage()
        {
            var sel = Selection.activeGameObject;
            if (sel != null && EditorUtility.IsPersistent(sel)) sel = null;
            if (sel == null)
            {
                return "髪色の「" + CustomHairColorToolCore.CustomNodeName + "」が見つかりません。\n" +
                       "Hierarchy でアバターを選んでください。髪色プレハブがアバターの中（子）に入っているかも確認してください。";
            }
            var avatar = CustomHairColorToolCore.FindAvatarRoot(sel);
            return "アバター「" + avatar.name + "」の中に髪色の「" + CustomHairColorToolCore.CustomNodeName + "」が見つかりません。\n" +
                   "髪色プレハブをアバターの中（子）に入れてから、「もう一度さがす」を押してください。";
        }

        static string GetHierarchyPath(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // ---- 状態の注意 ----
        void DrawStateWarnings()
        {
            // HelpBox はテーマの色で描くため自前の枠にする
            if (_bakedMaterialFound)
            {
                GUILayout.Label(BakedToolPresent
                        ? "焼き込んだマテリアルが髪に設定されています。色を変えるときは、先に焼き込みツールで「マテリアルを元に戻す」を押してください。"
                        : "焼き込んだマテリアルが髪に設定されています。色を変えるときは、焼き込みツールを入れて「マテリアルを元に戻す」を押すか、髪のマテリアルを元のものに戻してください。",
                    ImguiSkin.Warn);
                if (BakedToolPresent && GUILayout.Button("焼き込みツールを開く", ImguiSkin.Button, GUILayout.Height(26))) OpenBakedTool();
            }

            if (_inactiveNode != null)
            {
                if (_bakedMaterialFound) GUILayout.Space(4);
                var isProduct = _inactiveNode == CustomHairColorToolCore.ProductRoot(_target);
                var msg = isProduct
                    ? "色のセット「" + _inactiveNode.name + "」がオフになっています（焼き込みツールを使ったあとなど）。オンにしないと、ここで選んだ色は髪に出ません。"
                    : "アバターの中の「" + _inactiveNode.name + "」がオフになっているので、色が髪に出ません。";
                GUILayout.Label(msg, ImguiSkin.Warn);
                using (new EditorGUI.DisabledScope(_bakedMaterialFound)) // 焼き込んだ色に重ねて塗らないため
                if (GUILayout.Button("「" + _inactiveNode.name + "」をオンにする", ImguiSkin.Button, GUILayout.Height(26)))
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
                    CustomHairColorToolCore.SetNodeActive(_inactiveNode, true);
                    SceneView.RepaintAll();
                    Refresh();
                }
            }

            // 単色でも見る（単色も目印で塗る毛束を決めるため）
            var selOff = SelectorOffNode();
            if (selOff != null)
            {
                if (_bakedMaterialFound || _inactiveNode != null) GUILayout.Space(4);
                GUILayout.Label("毛束の指定「" + selOff.name + "」がオフになっているので、" + Modes.Get(_mode).Label + "の色が髪に出ません。Hierarchy で「" + selOff.name + "」をオンにしてください。", ImguiSkin.Warn);
            }
        }

        // ---- 色を選ぶ ----
        void ChoosePreset(PresetTile tile)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            CustomHairColorToolCore.ApplySelection(_target, _tiles, tile, false);
            SceneView.RepaintAll();
            Refresh();
        }

        // 「前のカスタムの続きから」もこれ（色は写さない）
        void ChooseCustom()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            ChooseCustomCore();
            SceneView.RepaintAll();
            Refresh();
        }

        // Undo のグループは呼ぶ側
        void ChooseCustomCore()
        {
            if (!_customOn)
            {
                // 戻り先はオフ→オンのときだけ覚える（押し直しで上書きしないため）
                var selected = _tiles.Where(t => t.Selected).ToList();
                if (selected.Count == 0) selected = _tiles.Where(t => t.Node != null && t.Node.activeSelf).ToList();
                _presetBeforeCustom = selected.Count == 1 ? selected[0].RelPath : "";
            }
            CustomHairColorToolCore.ApplySelection(_target, _tiles, null, true);
            CustomHairColorToolCore.SetMode(_target, _mode);
        }

        // プリセットのモードと色を Custom に写してからカスタムをオンにする（Undo 1回）
        // モードが商品に無い色（引退した Inner など）は写さない
        void StartCustomFrom(PresetTile tile)
        {
            if (_target == null || !_target.IsValid) return;
            var mode = tile != null ? CustomHairColorToolCore.ModeOfGroup(_target, tile.GroupName) : null;
            if (!mode.HasValue) { ChooseCustom(); return; }
            var m = mode.Value;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            if (_mode != m) StopPicking();
            _mode = m;
            _partIndex = 0;
            _stopIndex = 0;
            _subTab = SubTab.Color;
            SaveUiPrefs();
            CopyPresetToCustom(tile, m);
            ChooseCustomCore();
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
            Refresh();
        }

        void StartCustomFromDefault()
        {
            if (_target == null || !_target.IsValid || !_target.Has(HairMode.Default)) { ChooseCustom(); return; }
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            if (_mode != HairMode.Default) StopPicking();
            _mode = HairMode.Default;
            _partIndex = 0;
            _stopIndex = 0;
            _subTab = SubTab.Color;
            SaveUiPrefs();
            var decals = _target.ModeTargets[HairMode.Default].Decals;
            for (var c = 0; c < decals.Count; c++)
            {
                if (decals[c] == null) continue;
                foreach (var d in WithFollowers(HairMode.Default, decals[c])) CustomHairColorToolCore.ApplyColor(d, CustomHairColorToolCore.ReadColor(d), true);
                if (c < PartsPerMode) State(HairMode.Default, c).Clear = true;
            }
            ChooseCustomCore();
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
            Refresh();
        }

        // Undo のグループは呼ぶ側
        void CopyPresetToCustom(PresetTile tile, HairMode m)
        {
            var src = CustomHairColorToolCore.PresetDecals(tile);
            if (src.Count == 0 || !_target.Has(m)) return;
            var info = Modes.Get(m);
            var mt = _target.ModeTargets[m];
            if (info.IsGradient)
            {
                var sample = CustomHairColorToolCore.GradientSampleOf(src);
                foreach (var d in mt.Decals)
                {
                    if (d == null) continue;
                    var same = src.FirstOrDefault(s => string.Equals(s.name, d.name, StringComparison.OrdinalIgnoreCase)) ?? sample;
                    var g = CustomHairColorToolCore.ReadGradientRaw(same);
                    if (g != null) CustomHairColorToolCore.ApplyGradientRaw(d, g);
                    CustomHairColorToolCore.ApplyBlendKey(d, CustomHairColorToolCore.ReadBlendKey(same));
                }
                for (var i = 0; i < PartsPerMode; i++) State(m, i).Tone = Tone.None;
                return;
            }

            SingleGradationDecal[] from;
            if (info.PartLabels.Length >= 2 && CustomHairColorToolCore.OrderPresetColors(tile, src, out var first, out var second)) from = new[] { first, second };
            else from = new[] { src[0] };
            for (var c = 0; c < mt.Decals.Count; c++)
            {
                var lead = mt.Decals[c];
                if (lead == null) continue;
                var s = from[Mathf.Min(c, from.Length - 1)];
                var color = CustomHairColorToolCore.ReadColor(s);
                color.a = 1f;
                var key = CustomHairColorToolCore.ReadBlendKey(s);
                // 単色の Decal にも根元の色を書く（グラデをオフにしたとき根元の色に戻すため）
                var grad = c < 2 && mt.Grad != null ? CustomHairColorToolCore.PresetColorGrad(tile, s, lead.name) : new List<SingleGradationDecal>();
                bool gradOn = grad.Count > 0 && mt.Grad.Decals[c].Count > 0;
                if (c < 2 && mt.Grad != null && CustomHairColorToolCore.IsColorGradOn(mt.Grad, c) != gradOn) SetColorGradNodes(m, c, gradOn);
                if (gradOn)
                {
                    foreach (var d in mt.Grad.Decals[c])
                    {
                        var same = grad.FirstOrDefault(x => string.Equals(x.name, d.name, StringComparison.OrdinalIgnoreCase)) ?? grad[0];
                        var g = CustomHairColorToolCore.ReadGradientRaw(same);
                        if (g != null) CustomHairColorToolCore.ApplyGradientRaw(d, g);
                        CustomHairColorToolCore.ApplyBlendKey(d, CustomHairColorToolCore.ReadBlendKey(same));
                    }
                }
                // 無色は根元の透明度で見る（色キーには隠した色が残っているため）
                var clear = CustomHairColorToolCore.ReadGradient(s).RootClear;
                foreach (var d in WithFollowers(m, lead))
                {
                    CustomHairColorToolCore.ApplyColor(d, color, clear);
                    CustomHairColorToolCore.ApplyBlendKey(d, key);
                }
                if (c >= PartsPerMode) continue;
                var st = State(m, c);
                SyncColor(st, color);
                st.Clear = clear;
                st.Tone = Tone.None;
                st.BlendKey = key;
                if (c < 2) SyncGlowColorToHair(m, c);
            }
        }

        // 覚えていない（古い版で保存したシーン）ときは、オンの色のノードが1つだけならそこ
        PresetTile BackTile()
        {
            if (!string.IsNullOrEmpty(_presetBeforeCustom)) return _tiles.FirstOrDefault(t => t.RelPath == _presetBeforeCustom);
            var on = _tiles.Where(t => t.Node != null && t.Node.activeSelf).ToList();
            return on.Count == 1 ? on[0] : null;
        }

        void LeaveCustom()
        {
            var back = BackTile();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            _presetBeforeCustom = "";
            CustomHairColorToolCore.ApplySelection(_target, _tiles, back, false);
            SceneView.RepaintAll();
            Refresh();
        }

        // ---- カスタムする ----
        // 枠は UI Toolkit のカードが描くため、ここでは囲まない
        void DrawColorDecideIMGUI()
        {
            if (_target == null || !_target.IsValid || !_customOn) return;
            var width = ImguiWidth; // ウィンドウの幅ではない
            SectionHeader("色");
            // なしの間も下は触れる（触ったら色を塗るに戻すため）
            // DisabledScope だと押せず戻れないため、薄くするのは GUI.color で
            var clear = ClearAvailable && Current.Clear;
            var keep = GUI.color;
            if (clear) GUI.color = new Color(keep.r, keep.g, keep.b, keep.a * 0.45f);
            try
            {
                DrawRing();
                GUILayout.Space(4);
                DrawTone();
                GUILayout.Space(4);
                using (new EditorGUI.DisabledScope(clear)) DrawBlend(); // 乗せ方はなしの間は効かないため押せなくする
                GUILayout.Space(6);
                DrawRecentSection(width);
            }
            finally { GUI.color = keep; }
        }

        static void OpenBakedTool()
        {
            if (!EditorApplication.ExecuteMenuItem(ToolInfo.BakedToolMenuPath))
            {
                Debug.LogWarning("[" + ToolInfo.ToolName + "] 焼き込みツールのメニューが見つかりません：" + ToolInfo.BakedToolMenuPath);
            }
        }

        void SetGradientThree(HairMode mode, int color, bool on)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            var g = Grad(mode, color);
            var decals = GradDecalsOf(mode, color);
            if (on)
            {
                foreach (var d in decals) { var s = PartShape(d, g); var def = DefaultSplits(mode, color, d); s.Split1 = def.Split1; s.Split2 = def.Split2; }
                var first = decals.Count > 0 ? DefaultSplits(mode, color, decals[0]) : EvenSplits;
                g.Split1 = first.Split1;
                g.Split2 = first.Split2;
                var mid = GradStop(mode, color, 1);
                if (!g.MiddleKnown)
                {
                    mid.Color = Color.Lerp(GradStop(mode, color, 0).Color, GradStop(mode, color, 2).Color, 0.5f);
                    Color.RGBToHSV(mid.Color, out var h, out _, out _);
                    mid.Hue = h;
                    mid.Tone = Tone.None;
                    mid.Clear = false;
                    g.MiddleKnown = true;
                }
                mid.BlendKey = GradStop(mode, color, 0).BlendKey;
            }
            else
            {
                g.Split = Mathf.Clamp((g.Split1 + g.Split2) / 2f, 0.05f, 0.95f);
                foreach (var d in decals) { var s = PartShape(d, g); s.Split = Mathf.Clamp((s.Split1 + s.Split2) / 2f, 0.05f, 0.95f); }
                if (Modes.Get(mode).IsGradient) { if (_partIndex == 1) _partIndex = 0; }
                else if (_stopIndex == 1) _stopIndex = 0;
            }
            g.Three = on;
            foreach (var d in decals) PartShape(d, g).ThreeColors = on;
            PushGradient();
        }

        // まとめるのは手を離したとき（OnMouseReleased）
        void BeginDragUndo()
        {
            if (_dragUndoGroup >= 0) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            _dragUndoGroup = Undo.GetCurrentGroup();
        }

        void SetMode(HairMode mode)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            StopPicking(); // 選べるのは今のモードの毛束だけのため
            _mode = mode;
            _partIndex = 0;
            _stopIndex = 0;
            CustomHairColorToolCore.SetMode(_target, mode);
            SceneView.RepaintAll();
            Refresh();
        }

        void SetPart(int index)
        {
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            _partIndex = index;
            _stopIndex = 0;
            Refresh();
        }

        void DrawRing()
        {
            var st = Current;
            var size = Mathf.Clamp(ImguiWidth - 12f, 160f, 260f);
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(true));
            var hue = st.Hue;
            Color.RGBToHSV(st.Color, out _, out var sat, out var val); // 色合いは st.Hue から（灰色・黒で色合いを失わないため）
            var e = Event.current;
            var clearClick = st.Clear && e.type == EventType.MouseDown && e.button == 0 && HueRing.Hits(rect, e.mousePosition); // Draw がイベントを使う前に見る
            var moved = HueRing.Draw(rect, ref hue, ref sat, ref val);
            DrawRingCorners(rect);
            if (!moved)
            {
                if (clearClick) SetClear(false);
                return;
            }

            BeginDragUndo();
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            st.Hue = hue;
            var c = Color.HSVToRGB(Mathf.Repeat(hue, 1f), sat, val, false);
            c.a = 1f;
            st.Color = c;
            st.Tone = ToneTable.Match(sat, val) ?? Tone.None;
            st.Clear = false;
            _ringDragged = true;
            PushColor();
        }

        void DrawTone()
        {
            var st = Current;
            GUILayout.Label("トーン", ImguiSkin.Bold);
            var cells = GridCells(ToneTable.Order.Length);
            for (var i = 0; i < ToneTable.Order.Length; i++)
            {
                var tone = ToneTable.Order[i];
                var on = ToneOf(st) == tone;
                var pressed = GUI.Toggle(cells[i], on, ToneTable.Label(tone), ButtonStyle(i, ToneTable.Order.Length));
                // 選ばれているトーンを押したときは、無色の間だけ色をつけるに戻す
                if (pressed == on || (on && !st.Clear)) continue;
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
                st.Tone = tone;
                st.Color = ToneTable.Compose(st.Hue, st.Tone);
                st.Clear = false;
                PushColor();
                PushRecent(st.Color);
            }
        }

        [NonSerialized] float _blendCellWidth;

        void DrawBlend()
        {
            var st = Current;
            GUILayout.Label("色の重ね方（元の髪の影とのなじませ方）", ImguiSkin.Bold);
            var count = BlendPresets.Order.Length + 1;
            var cells = GridCells(count);
            var current = BlendPresets.FromKey(st.BlendKey);
            for (var i = 0; i < BlendPresets.Order.Length; i++)
            {
                var p = BlendPresets.Order[i];
                var on = current.HasValue && current.Value == p;
                if (GUI.Toggle(cells[i], on, BlendPresets.Label(p), ButtonStyle(i, count)) && !on) SetBlend(BlendPresets.Key(p));
            }
            // 列の幅に入らない長い名前は「その他」のままにして、下に名前を出す
            var moreName = BlendPresets.MoreLabel(st.BlendKey);
            var moreText = BlendPresets.MoreButtonLabel(st.BlendKey);
            // 前の描画で測った幅で決める（Layout と Repaint で行の数を変えないため）
            var fits = _blendCellWidth <= 0f || ImguiSkin.Button.CalcSize(new GUIContent(moreText)).x <= _blendCellWidth;
            if (Event.current.type == EventType.Repaint) _blendCellWidth = cells[count - 1].width;
            if (!fits) moreText = BlendPresets.MoreButtonLabel(null);
            var moreOn = moreName != null;
            if (GUI.Toggle(cells[count - 1], moreOn, moreText, ButtonStyle(count - 1, count)) != moreOn) ShowBlendMenu(cells[count - 1]);
            if (moreOn && !fits) GUILayout.Label("いまの重ね方：" + moreName, ImguiSkin.Note);

            if (BlendPresets.IsLight(st.BlendKey))
                GUILayout.Label("ライト系は、明るい髪だと白っぽくなったり、グラデーションの境目に色の線が出たりすることがあります。", ImguiSkin.Note);
            if (!BlendPresets.FromKey(st.BlendKey).HasValue && BlendPresets.MoreLabel(st.BlendKey) == null && !string.IsNullOrEmpty(st.BlendKey))
            {
                GUILayout.Label("いまの設定：" + st.BlendKey + "（この画面のボタンにない設定です）", ImguiSkin.Note);
            }
        }

        void ShowBlendMenu(Rect r)
        {
            var menu = new GenericMenu();
            var current = Current.BlendKey;
            for (var g = 0; g < BlendPresets.MoreGroups.Length; g++)
            {
                if (g > 0) menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent(BlendPresets.MoreGroups[g].Key));
                foreach (var p in BlendPresets.MoreGroups[g].Value)
                {
                    var key = p.Key;
                    menu.AddItem(new GUIContent(p.Value), current == key, () =>
                    {
                        SetBlend(key);
                        MarkImguiDirty();
                    });
                }
            }
            menu.DropDown(r);
        }

        void SetBlend(string key)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            Current.BlendKey = key;
            PushBlend();
        }

        static GUIStyle ButtonStyle(int index, int count) => ImguiSkin.Button;

        // 同じ幅で1行に並べる（字の長さで幅が変わらないため）
        static Rect[] GridCells(int count)
        {
            var style = ImguiSkin.Button;
            var height = style.CalcSize(new GUIContent("標準")).y;
            var row = GUILayoutUtility.GetRect(GUIContent.none, style, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            var gap = style.margin.left + style.margin.right;
            var w = (row.width - gap * (count - 1)) / count;
            var cells = new Rect[count];
            for (var i = 0; i < count; i++) cells[i] = new Rect(row.x + i * (w + gap), row.y, w, row.height);
            return cells;
        }

        // only：その Decal だけに書く（ほかのパーツが読み直しで揺れないため）
        // _Cut の Decal は真ん中で切る毛束を単色で塗るため、根元の色を書く
        void PushGradient(SingleGradationDecal only = null)
        {
            CustomHairColorToolCore.SetMode(_target, _mode);
            var g = Grad(_mode, GradColor);
            if (IsGradientMode)
            {
                CustomHairColorToolCore.ApplyGradientPerPart(only != null ? new[] { only } : (IList<SingleGradationDecal>)ModeDecals, CurrentGradientShape(), d => PartShape(d, g));
                SceneView.RepaintAll();
                return;
            }
            var grad = _target.ModeTargets[_mode].Grad;
            if (grad == null || _partIndex > 1) return;
            var shape = CurrentGradientShape();
            CustomHairColorToolCore.ApplyGradientPerPart(only != null ? new[] { only } : (IList<SingleGradationDecal>)grad.Decals[_partIndex], shape, d => PartShape(d, g));
            // 単色の側は根元の色にそろえる（帯と光の追従に使うため）
            var root = GradStop(_mode, _partIndex, 0);
            var st = State(_mode, _partIndex);
            st.Color = root.Color;
            st.Hue = root.Hue;
            st.Clear = root.Clear;
            var lead = CurrentDecal;
            if (lead != null && _target.ModeTargets[_mode].Followers.TryGetValue(lead, out var followers))
                foreach (var d in followers) if (d != null) CustomHairColorToolCore.ApplyColor(d, root.Color, root.Clear);
            SyncGlowColorToHair(_mode, _partIndex, throttle: true);
            SceneView.RepaintAll();
        }

        void PushColor()
        {
            if (IsGradientMode || ColorGradOn) { PushGradient(); return; }
            var decal = CurrentDecal;
            if (decal == null) return;
            CustomHairColorToolCore.SetMode(_target, _mode);
            foreach (var d in WithFollowers(decal)) CustomHairColorToolCore.ApplyColor(d, Current.Color, Current.Clear);
            SyncGlowColorToHair(_mode, _partIndex, throttle: true);
            SceneView.RepaintAll();
        }

        // ---- 無色 ----
        bool ClearAvailable => _target != null && _target.IsValid && _customOn
                               && (IsGradientMode || ColorGradOn || CurrentDecal != null);

        void SetClear(bool clear)
        {
            if (!ClearAvailable || Current.Clear == clear) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            Current.Clear = clear;
            PushColor();
        }

        void PushBlend()
        {
            if (IsGradientMode)
            {
                // 混ぜ方は全部の色で共通（Decal ごとに1つしか持てないため）
                CustomHairColorToolCore.SetMode(_target, _mode);
                for (var i = 0; i < PartsPerMode; i++) State(_mode, i).BlendKey = Current.BlendKey;
                foreach (var d in ModeDecals) CustomHairColorToolCore.ApplyBlendKey(d, Current.BlendKey);
                SceneView.RepaintAll();
                return;
            }
            var decal = CurrentDecal;
            if (decal == null) return;
            var key = Current.BlendKey;
            CustomHairColorToolCore.SetMode(_target, _mode);
            if (ColorGradOn)
            {
                // オフの単色の Decal にも書く（グラデをやめたときにそろえるため）
                for (var i = 0; i < PartsPerMode; i++) GradStop(_mode, _partIndex, i).BlendKey = key;
                State(_mode, _partIndex).BlendKey = key;
                foreach (var d in _target.ModeTargets[_mode].Grad.Decals[_partIndex]) CustomHairColorToolCore.ApplyBlendKey(d, key);
            }
            foreach (var d in WithFollowers(decal)) CustomHairColorToolCore.ApplyBlendKey(d, key);
            SceneView.RepaintAll();
        }

        // ---- リングの四隅と最近使った色 ----
        const double RecentSettleSeconds = 0.8;
        const float RecentSwatchSize = 24f;
        List<Color> _recent = new List<Color>();
        bool _ringDragged;
        bool _recentPending;
        Color _recentPendingColor;
        double _recentPendingAt;
        static GUIStyle s_noneLabel;

        static void CornerLabel(Rect under, string text)
        {
            if (s_noneLabel == null) s_noneLabel = new GUIStyle(ImguiSkin.Note) { alignment = TextAnchor.UpperCenter, wordWrap = false, padding = new RectOffset(), margin = new RectOffset() };
            GUI.Label(new Rect(under.x - 10f, under.yMax + 1f, under.width + 20f, 14f), text, s_noneLabel);
        }

        void DrawRingCorners(Rect rect)
        {
            var st = Current;
            var side = Mathf.Min(rect.width, rect.height);
            var sq = new Rect(rect.x + (rect.width - side) * 0.5f, rect.y + (rect.height - side) * 0.5f, side, side);
            const float swatch = 22f;
            Color? picked = null;

            var cur = new Rect(sq.x, sq.y, swatch + 22f, swatch); // 右端にスポイトのボタンが付くため
            EditorGUI.BeginChangeCheck();
            var c = EditorGUI.ColorField(cur, GUIContent.none, st.Color, true, false, false);
            if (EditorGUI.EndChangeCheck()) picked = c;
            CornerLabel(new Rect(cur.x, cur.y, swatch, swatch), "今の色");

            if (ClearAvailable)
            {
                var none = new Rect(sq.xMax - swatch, sq.y, swatch, swatch);
                var keep = GUI.color;
                if (st.Clear) GUI.color = Color.white;
                NoneSwatch.Draw(none, st.Clear);
                CornerLabel(none, NoneSwatch.Label);
                GUI.color = keep;
                EditorGUIUtility.AddCursorRect(none, MouseCursor.Link);
                var tip = st.Clear ? NoneSwatch.Label + "（もう一度押すと前の色に戻ります）" : NoneSwatch.Label + "（" + NoneSwatch.Hint + "）";
                if (GUI.Button(none, new GUIContent("", tip), GUIStyle.none)) SetClear(!st.Clear);
            }

            const float codeWidth = 64f;
            var code = new Rect(Mathf.Min(sq.xMax - codeWidth + 14f, rect.xMax - codeWidth), sq.yMax - 18f, codeWidth, 18f);
            GUI.Label(new Rect(code.x - 12f, code.y, 12f, code.height), "#", ImguiSkin.Label);
            var now = ColorUtility.ToHtmlStringRGB(st.Color);
            var typed = EditorGUI.DelayedTextField(code, now);
            if (typed != now && ColorUtility.TryParseHtmlString("#" + typed.Trim().TrimStart('#'), out var fromCode)) picked = fromCode;

            if (picked.HasValue) ApplyPickedColor(picked.Value);
        }

        void PushRecent(Color c)
        {
            _recentPending = false;
            RecentColors.Push(c);
            _recent = RecentColors.Load();
            Repaint();
        }

        void FlushRecentPending()
        {
            if (!_recentPending) return;
            PushRecent(_recentPendingColor);
        }

        void ApplyPickedColor(Color p)
        {
            var st = Current;
            p.a = 1f;
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            st.Color = p;
            Color.RGBToHSV(p, out var h, out var sat, out var val);
            if (sat > 0f) st.Hue = h; // 灰色・黒・白は色合いを持たないため前のまま
            st.Tone = ToneTable.Match(sat, val) ?? Tone.None;
            st.Clear = false;
            PushColor();
            _recentPending = true;
            _recentPendingColor = p;
            _recentPendingAt = EditorApplication.timeSinceStartup;
        }

        static Tone? ToneOf(PartState st)
        {
            Color.RGBToHSV(st.Color, out _, out var sat, out var val);
            return ToneTable.Match(sat, val);
        }

        void DrawRecentSection(float width)
        {
            var st = Current;
            if (_recent.Count == 0) return;
            GUILayout.Label("最近使った色", ImguiSkin.Bold);
            var perRow = Mathf.Max(1, Mathf.FloorToInt((width - 28f) / (RecentSwatchSize + 4f)));
            int clickedIndex = -1;
            bool alt = false;
            for (var start = 0; start < _recent.Count; start += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var i = start; i < Mathf.Min(_recent.Count, start + perRow); i++)
                    {
                        var r = GUILayoutUtility.GetRect(RecentSwatchSize, RecentSwatchSize, GUILayout.Width(RecentSwatchSize), GUILayout.Height(RecentSwatchSize));
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.4f));
                            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), _recent[i]);
                        }
                        EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                        var tip = "#" + ColorUtility.ToHtmlStringRGB(_recent[i]) + "（クリックでこの色にする。Alt＋クリックで最近の色から外す）";
                        if (GUI.Button(r, new GUIContent("", tip), GUIStyle.none)) { clickedIndex = i; alt = Event.current.alt; }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            GUILayout.Label("クリックでこの色にします。Alt＋クリックで最近の色から外します。", ImguiSkin.Note);
            if (clickedIndex < 0) return;
            if (alt)
            {
                RecentColors.RemoveAt(clickedIndex);
                _recent = RecentColors.Load();
                Repaint();
                return;
            }
            var pick = _recent[clickedIndex];
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            st.Color = pick;
            Color.RGBToHSV(pick, out var hue, out _, out _);
            st.Hue = hue;
            st.Tone = Tone.None;
            st.Clear = false;
            PushColor();
        }

#endif
    }
}
#endif
