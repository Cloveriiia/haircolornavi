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
        public const string Version = "0.9.2";
        public const string MenuPath = "Tools/Atelier_Cloveria/" + ToolName;
        public const string TexTransToolUrl = "https://ttt.rs64.net/";
        public const string ShopUrl = "https://atelier-cloveria.booth.pm/";
        public const string TestedTexTransTool = "TexTransTool 1.0.x"; // asmdef の versionDefines の範囲とそろえる

        // 焼き込みツール（別アセンブリ）とは型を参照せず、アセンブリ名とメニューのパスだけで結ぶ
        public const string BakedToolAssemblyName = "AtelierCloveria.CustomHairColorBakedTool.Editor";
        public const string BakedToolMenuPath = "Tools/Atelier_Cloveria/Cloveria_Custom_Hair_Color_Baked_Tool";
        public const string BakedMaterialTagPrefix = "ACHBT:"; // 焼き込みツールが複製マテリアルの Importer userData に付ける印
    }

    public enum ColoringMode
    {
        Default = 0,
        Inner = 1,
        TwoTone = 2,
        Mesh = 3,
        Gradation = 4,
    }

    public sealed class ColoringModeInfo
    {
        public ColoringMode Mode;
        public string[] NodeNames;
        public string Label;
        public string[] PartLabels;
        public bool IsGradient;    // Decal 1枚＝色1つではなく、全 Decal に同じグラデを書く
    }

    public static class ColoringModes
    {
        public static readonly ColoringModeInfo[] All =
        {
            // ボタンは短い名前（モードが4つ以上並んでも切れないため）
            new ColoringModeInfo { Mode = ColoringMode.Default, NodeNames = new[] { "Default", "Main" }, Label = "単色", PartLabels = new[] { "色" } },
            new ColoringModeInfo { Mode = ColoringMode.Inner, NodeNames = new[] { "Inner" }, Label = "インナー", PartLabels = new[] { "ベース", "インナーカラー" } },
            new ColoringModeInfo { Mode = ColoringMode.TwoTone, NodeNames = new[] { "2ton" }, Label = "ツートン", PartLabels = new[] { "1色目", "2色目" } },
            new ColoringModeInfo { Mode = ColoringMode.Mesh, NodeNames = new[] { "Mesh" }, Label = "メッシュ", PartLabels = new[] { "1色目", "2色目" } },
            new ColoringModeInfo { Mode = ColoringMode.Gradation, NodeNames = new[] { "Gradation" }, Label = "グラデーション", PartLabels = new[] { "根元", "真ん中", "毛先" }, IsGradient = true },
        };

        public static ColoringModeInfo Get(ColoringMode mode) => All.First(m => m.Mode == mode);
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
    public sealed class ColoringModeParts
    {
        public GameObject Node;
        public List<SingleGradationDecal> Decals = new List<SingleGradationDecal>();
        // 名前が「<色の Decal の名前>_Cut」の Decal。同じ色を置き方の違う2枚で塗る商品用（Decals には入れない）
        public readonly Dictionary<SingleGradationDecal, List<SingleGradationDecal>> Followers = new Dictionary<SingleGradationDecal, List<SingleGradationDecal>>();
        // メッシュ・ツートンだけ。Prefab に無ければ null（その機能は出さない）
        public GlowParts Glow;
        public PerColorGradientParts Grad;
        // 色の Decal が _Selectors の下の OR を参照しているときだけ（無ければ null）
        // 書き出し直した商品は _Selectors/_Custom/ でカスタム専用、古い商品はプリセットと共有（PresetsShare）
        public StrandColorPins Pins;
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

    public sealed class ColoringProduct
    {
        public GameObject Avatar;
        public GameObject Custom;
        public bool FoundInSelection;
        public readonly Dictionary<ColoringMode, ColoringModeParts> ModeParts = new Dictionary<ColoringMode, ColoringModeParts>();
        // Mesh がある商品に残る Custom/Inner。オフにだけする（二重に塗らないため）
        public GameObject RetiredInner;

        public bool IsValid => Custom != null && ModeParts.Count > 0;
        public bool Has(ColoringMode mode) => ModeParts.ContainsKey(mode);
        public IEnumerable<ColoringMode> AvailableModes => ColoringModes.All.Select(m => m.Mode).Where(Has);
        public ColoringMode FirstMode => AvailableModes.First();

        public int DecalCount(ColoringMode mode) => Has(mode) ? ModeParts[mode].Decals.Count : 0;

        public SingleGradationDecal Decal(ColoringMode mode, int index)
        {
            if (!Has(mode)) return null;
            var list = ModeParts[mode].Decals;
            return index < list.Count ? list[index] : null;
        }
    }

    public enum SwatchKind
    {
        Solid = 0,
        TwoTone = 1,
        Gradient = 2,
        ThreeTone = 3,
    }

    public sealed class PresetColor
    {
        public GameObject Group;
        public GameObject Node;
        public string GroupName;
        public string Label;
        public string RelPath;     // 例 "Inner/Sky × Pink"。カスタムをやめたときの戻り先
        public SwatchKind Kind;
        public Color ColorA = Color.white;
        public Color ColorB = Color.white;
        public Color ColorC = Color.white;
        public Color[] Bar;
        public bool Selected;
        public Texture2D Thumbnail;
    }

    public static partial class HairColorCore
    {
        public const string CustomNodeName = "Custom";
        public const string UndoLabel = ToolInfo.ToolName;
        public const int GradientBarSteps = 16;

        const string GradientProperty = "Gradient";
        const string BlendTypeKeyProperty = "BlendTypeKey";

        static bool NameIs(Transform t, string name) => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase);

        public static ColoringProduct FindColoringProduct(GameObject selection)
        {
            // Project のプレハブは使わない（商品ファイル本体を書き換えるため）
            if (selection != null && EditorUtility.IsPersistent(selection)) selection = null;
            if (selection != null)
            {
                var t = FindProductUnder(AvatarLookup.FindAvatarRoot(selection));
                if (t != null) { t.FoundInSelection = true; return t; }
            }

            var roots = new List<GameObject>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
            }
            foreach (var r in roots.OrderByDescending(AvatarLookup.IsAvatarRoot))
            {
                var t = FindProductUnder(r);
                if (t != null) return t;
            }
            return null;
        }

        static ColoringProduct FindProductUnder(GameObject avatar)
        {
            if (avatar == null) return null;
            foreach (var tr in avatar.GetComponentsInChildren<Transform>(true))
            {
                if (!NameIs(tr, CustomNodeName)) continue;
                var target = BuildColoringProduct(avatar, tr.gameObject);
                if (target != null) return target;
            }
            return null;
        }

        static ColoringProduct BuildColoringProduct(GameObject avatar, GameObject custom)
        {
            var target = new ColoringProduct { Avatar = avatar, Custom = custom };
            foreach (var info in ColoringModes.All)
            {
                var node = info.NodeNames.Select(n => FindChild(custom.transform, n)).FirstOrDefault(n => n != null);
                if (node == null) continue;
                var decals = CollectDecals(node);
                if (decals.Count == 0) continue;
                const string FollowerSuffix = "_Cut";
                var modeTarget = new ColoringModeParts { Node = node.gameObject };
                foreach (var d in decals)
                {
                    // グラデーションは全 Decal に同じグラデを書くため分けない
                    var lead = info.IsGradient ? null : modeTarget.Decals.FirstOrDefault(l => d.name == l.name + FollowerSuffix);
                    if (lead == null) { modeTarget.Decals.Add(d); continue; }
                    if (!modeTarget.Followers.TryGetValue(lead, out var list)) modeTarget.Followers[lead] = list = new List<SingleGradationDecal>();
                    list.Add(d);
                }
                target.ModeParts[info.Mode] = modeTarget;
            }
            // Mesh がある商品では Inner を編集しない（メッシュでまかなうため）。Inner だけの古い商品は今までどおり
            if (target.Has(ColoringMode.Mesh) && target.Has(ColoringMode.Inner))
            {
                target.RetiredInner = target.ModeParts[ColoringMode.Inner].Node;
                target.ModeParts.Remove(ColoringMode.Inner);
            }
            foreach (var mode in new[] { ColoringMode.TwoTone, ColoringMode.Mesh })
            {
                if (!target.Has(mode)) continue;
                var mt = target.ModeParts[mode];
                mt.Glow = DetectGlow(custom.transform, mt);
                mt.Grad = DetectColorGrad(custom.transform, mt);
                mt.Pins = DetectPinSides(custom.transform, mt, ColoringModes.Get(mode));
            }
            foreach (var mode in new[] { ColoringMode.Default, ColoringMode.Gradation })
            {
                if (target.Has(mode)) target.ModeParts[mode].Pins = DetectPaintPins(custom.transform, target.ModeParts[mode], ColoringModes.Get(mode));
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

        public static Color GetDecalColor(SingleGradationDecal decal)
        {
            if (decal == null) return Color.white;
            var g = new SerializedObject(decal).FindProperty(GradientProperty).gradientValue;
            if (g == null || g.colorKeys == null || g.colorKeys.Length == 0) return Color.white;
            return g.colorKeys[0].color;
        }

        public static string GetDecalBlendMode(SingleGradationDecal decal)
        {
            if (decal == null) return BlendPresets.Key(BlendPreset.Strong);
            return new SerializedObject(decal).FindProperty(BlendTypeKeyProperty).stringValue;
        }

        // 透明度キーが全部 0 なら無色（TTT は透明度 0 の所に元の画素を残す）
        // Gradient は色キーの a を見ないため、色キーに隠した色が残る（無色をやめると前の色に戻る）
        const float ClearAlpha = 0.5f / 255f;

        public static bool IsDecalColorless(SingleGradationDecal decal)
        {
            var g = GetDecalGradientRaw(decal);
            var keys = g?.alphaKeys;
            if (keys == null || keys.Length == 0) return false;
            return keys.All(k => k.alpha < ClearAlpha);
        }

        public static void SetDecalColor(SingleGradationDecal decal, Color color, bool clear = false)
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

        public static void SetDecalBlendMode(SingleGradationDecal decal, string key)
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
        public static GradientShape GetDecalGradient(SingleGradationDecal decal)
        {
            var shape = new GradientShape();
            var g = GetDecalGradientRaw(decal);
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

        internal static GradientColorKey[] BuildGradientColorKeys(GradientShape s)
            => KeyStops(s).Select(k => new GradientColorKey(k.Value == 0 ? s.Root : k.Value == 1 ? s.Middle : s.Tip, k.Key)).ToArray();

        // 無色が無ければ不透明の2キー（出荷時の Prefab と同じ）
        internal static GradientAlphaKey[] BuildGradientAlphaKeys(GradientShape s)
        {
            bool ClearOf(int stop) => stop == 0 ? s.RootClear : stop == 1 ? s.ThreeColors && s.MiddleClear : s.TipClear;
            var stops = KeyStops(s);
            if (!stops.Any(k => ClearOf(k.Value))) return new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
            return stops.Select(k => new GradientAlphaKey(ClearOf(k.Value) ? 0f : 1f, k.Key)).ToArray();
        }

        public static void SetDecalsGradient(IList<SingleGradationDecal> decals, GradientShape shape)
        {
            var list = decals.Where(d => d != null).ToArray();
            if (list.Length == 0) return;
            var keys = BuildGradientColorKeys(shape);
            var alphas = BuildGradientAlphaKeys(shape);
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

        public static void SetDecalsGradientKeepSplits(IList<SingleGradationDecal> decals, GradientShape colors, Func<SingleGradationDecal, GradientShape> splitsOf)
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
                prop.gradientValue = new Gradient { mode = old.mode, alphaKeys = BuildGradientAlphaKeys(shape), colorKeys = BuildGradientColorKeys(shape) };
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static Gradient GetDecalGradientRaw(SingleGradationDecal decal)
        {
            if (decal == null) return null;
            return new SerializedObject(decal).FindProperty(GradientProperty).gradientValue;
        }

        public static void SetDecalGradientRaw(SingleGradationDecal decal, Gradient g)
        {
            if (decal == null || g == null) return;
            Undo.RecordObject(decal, UndoLabel);
            var so = new SerializedObject(decal);
            so.FindProperty(GradientProperty).gradientValue = g;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetActiveWithUndo(GameObject go, bool active)
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

        const string ThirdColorName = "Hair_Third";

        static readonly string[] GradientSampleNames = { "Hair_Base", "Hair_Back" };

        internal static string PresetColorGroupLabel(string groupName)
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

        public static GameObject GetProductRoot(ColoringProduct target)
        {
            if (target == null || !target.IsValid) return null;
            var parent = target.Custom.transform.parent;
            return parent != null ? parent.gameObject : null;
        }

        public static List<GameObject> FindPresetColorGroups(ColoringProduct target)
        {
            var result = new List<GameObject>();
            var root = GetProductRoot(target);
            if (root == null) return result;
            foreach (var name in PresetGroupNames)
            {
                var g = FindChild(root.transform, name);
                if (g == null || g.gameObject == target.Custom) continue;
                result.Add(g.gameObject);
            }
            return result;
        }

        public static List<PresetColor> ListPresetColors(ColoringProduct target)
        {
            var tiles = new List<PresetColor>();
            foreach (var group in FindPresetColorGroups(target))
            {
                var gt = group.transform;
                for (var i = 0; i < gt.childCount; i++)
                {
                    var c = gt.GetChild(i);
                    var decals = CollectDecals(c);
                    if (decals.Count == 0) continue;
                    var tile = new PresetColor
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

        static void FillSwatch(PresetColor tile, List<SingleGradationDecal> decals)
        {
            switch (tile.GroupName.ToLowerInvariant())
            {
                case "gradation":
                    {
                        tile.Kind = SwatchKind.Gradient;
                        var sample = GradientSampleNames
                            .Select(n => decals.FirstOrDefault(d => NameIs(d.transform, n)))
                            .FirstOrDefault(d => d != null) ?? decals[0];
                        var g = GetDecalGradientRaw(sample);
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
                        tile.ColorA = GetDecalColor(first);
                        tile.ColorB = GetDecalColor(second);
                        tile.ColorC = tile.ColorB;
                        var third = ThirdColorOf(tile.Node.transform, decals);
                        if (third == null) break;
                        tile.Kind = SwatchKind.ThreeTone;
                        tile.ColorC = GetDecalColor(third);
                        break;
                    }
                default:
                    tile.Kind = SwatchKind.Solid; // Decal が複数（髪＋しっぽ など）でも同じ色のため先頭を使う
                    tile.ColorA = GetDecalColor(decals[0]);
                    tile.ColorB = tile.ColorA;
                    tile.ColorC = tile.ColorA;
                    break;
            }
            tile.ColorA.a = 1f;
            tile.ColorB.a = 1f;
            tile.ColorC.a = 1f;
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

        static SingleGradationDecal ThirdColorOf(Transform node, List<SingleGradationDecal> decals)
            => decals.FirstOrDefault(d => NameIs(SideOf(node, d), ThirdColorName));

        static Transform SideOf(Transform node, SingleGradationDecal d)
        {
            var t = d.transform;
            while (t.parent != null && t.parent != node) t = t.parent;
            return t;
        }

        // 包みの名前が Custom の色の Decal と同じときだけ（古い商品の入れ子と取り違えないため）
        public static List<SingleGradationDecal> GetPresetPerColorGradientDecals(PresetColor tile, SingleGradationDecal lead, string customDecalName)
        {
            var side = SideOf(tile.Node.transform, lead);
            if (side.GetComponent<SingleGradationDecal>() != null || !NameIs(side, customDecalName)) return new List<SingleGradationDecal>();
            return CollectDecals(side);
        }

        public static List<SingleGradationDecal> GetPresetColorDecals(PresetColor tile)
            => tile != null && tile.Node != null ? CollectDecals(tile.Node.transform) : new List<SingleGradationDecal>();

        public static bool OrderPresetColorDecals(PresetColor tile, List<SingleGradationDecal> decals, out SingleGradationDecal first, out SingleGradationDecal second)
        {
            first = second = null;
            if (tile == null || tile.Node == null || decals == null || decals.Count < 2) return false;
            OrderTwoColors(tile.Node.transform, decals, out first, out second);
            return first != null && second != null;
        }

        public static bool OrderPresetColorDecals(PresetColor tile, List<SingleGradationDecal> decals, out SingleGradationDecal first, out SingleGradationDecal second, out SingleGradationDecal third)
        {
            third = null;
            if (!OrderPresetColorDecals(tile, decals, out first, out second)) return false;
            third = ThirdColorOf(tile.Node.transform, decals);
            return true;
        }

        public static SingleGradationDecal PickGradientSampleDecal(List<SingleGradationDecal> decals)
        {
            if (decals == null || decals.Count == 0) return null;
            return GradientSampleNames.Select(n => decals.FirstOrDefault(d => NameIs(d.transform, n))).FirstOrDefault(d => d != null) ?? decals[0];
        }

        public static ColoringMode? ColoringModeOfPresetGroup(ColoringProduct target, string groupName)
        {
            if (target == null || string.IsNullOrEmpty(groupName)) return null;
            foreach (var info in ColoringModes.All)
            {
                if (!info.NodeNames.Any(n => string.Equals(n, groupName, StringComparison.OrdinalIgnoreCase))) continue;
                return target.Has(info.Mode) ? info.Mode : (ColoringMode?)null;
            }
            return null;
        }

        // ---- サムネイル ----
        // 置き場は商品プレハブと同じフォルダの Thumbnails/。名前は <グループ>__<色の名前>.png（書き出し側の規則）
        public const string ThumbnailFolderName = "Thumbnails";
        public const string NoneThumbnailFileName = "_None.png";

        // 書き出し側と同じ規則（" × " を先に "_x_" にする）
        internal static string SanitizePresetColorName(string name)
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

        internal static string PresetColorThumbnailFileName(PresetColor tile) => tile.GroupName + "__" + SanitizePresetColorName(tile.Label) + ".png";

        internal static string FindThumbnailFolder(ColoringProduct target)
        {
            var root = GetProductRoot(target);
            if (root == null) return null;
            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (string.IsNullOrEmpty(prefabPath)) return null;
            var dir = System.IO.Path.GetDirectoryName(prefabPath);
            if (string.IsNullOrEmpty(dir)) return null;
            var folder = dir.Replace('\\', '/') + "/" + ThumbnailFolderName;
            return AssetDatabase.IsValidFolder(folder) ? folder : null;
        }

        // Undo のグループは呼ぶ側で切る
        public static void SelectPresetColor(ColoringProduct target, IList<PresetColor> tiles, PresetColor chosen, bool custom)
        {
            if (target == null || !target.IsValid) return;
            var groups = FindPresetColorGroups(target);
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

        internal static GameObject FindInactiveProductAncestor(ColoringProduct target)
        {
            var root = GetProductRoot(target);
            if (root == null) return null;
            for (var t = root.transform; t != null; t = t.parent)
            {
                if (target.Avatar != null && t.gameObject == target.Avatar) break;
                if (!t.gameObject.activeSelf) return t.gameObject;
            }
            return null;
        }

        // 変わったときだけ焼き込みの判定をやり直すため
        internal static int ComputeMaterialSignature(GameObject avatar)
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

        internal static bool HasBakedMaterial(GameObject avatar)
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

        public static void SetColoringMode(ColoringProduct target, ColoringMode mode)
        {
            if (target == null || !target.IsValid) return;
            if (!target.Has(mode)) mode = target.FirstMode;
            foreach (var kv in target.ModeParts) SetActiveWithUndo(kv.Value.Node, kv.Key == mode);
            SetActiveWithUndo(target.RetiredInner, false);
            UpdateGlowAndGradientNodes(target, mode);
        }

        // _Glow・_Grad は Custom/<モード> の兄弟のため、モードを替えても親ごとは消えない
        // 中の Decal は触らない（戻れば前の光・グラデが続く）。使っていなければ親もオフ（発光の設定をマテリアルに入れないため）
        internal static void UpdateGlowAndGradientNodes(ColoringProduct target, ColoringMode mode)
        {
            if (target == null || !target.IsValid) return;
            GameObject glowRoot = null, gradRoot = null;
            bool glowOn = false, gradOn = false;
            foreach (var kv in target.ModeParts)
            {
                var g = kv.Value.Glow;
                if (g != null)
                {
                    var on = kv.Key == mode && GetGlowingColorIndex(g) >= 0;
                    SetActiveWithUndo(g.Node, on);
                    glowRoot = g.Root;
                    glowOn |= on;
                }
                var c = kv.Value.Grad;
                if (c != null)
                {
                    var on = kv.Key == mode && IsAnyPerColorGradientOn(c);
                    SetActiveWithUndo(c.Node, on);
                    gradRoot = c.Root;
                    gradOn |= on;
                }
            }
            if (glowRoot != null) SetActiveWithUndo(glowRoot, glowOn);
            if (gradRoot != null) SetActiveWithUndo(gradRoot, gradOn);
        }

        public static ColoringMode? GetColoringMode(ColoringProduct target)
        {
            if (target == null || !target.IsValid) return null;
            var active = target.AvailableModes.Where(m => target.ModeParts[m].Node.activeSelf).ToList();
            return active.Count == 1 ? active[0] : (ColoringMode?)null;
        }
    }
#endif
}
#endif
