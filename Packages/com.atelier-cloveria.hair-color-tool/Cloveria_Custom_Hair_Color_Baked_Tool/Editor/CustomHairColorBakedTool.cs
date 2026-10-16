#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHBT_NDMF_PRESENT
#define NDMF_ABSENT
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if AC_CHBT_TTT_PRESENT
using net.rs64.TexTransTool;
using net.rs64.TexTransTool.Decal;
#endif
#if AC_CHBT_TTT_PRESENT && !NDMF_ABSENT
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
#endif
using Object = UnityEngine.Object;

namespace AtelierCloveria.CustomHairColorBakedTool
{
    public static class BakedToolInfo
    {
        public const string ToolName = "Cloveria_Custom_Hair_Color_Baked_Tool";
        public const string Version = "0.3.3";
        public const string MenuPath = "Tools/Atelier_Cloveria/" + ToolName;
        public const string TexTransToolUrl = "https://ttt.rs64.net/";
        public const string NdmfUrl = "https://ndmf.nadena.dev/";

        /// <summary>動作確認済みの版（README と同じ値にする）</summary>
        public const string TestedUnity = "2022.3.22f1";
        public const string TestedTexTransTool = "1.0.2";
        public const string TestedNdmf = "1.14.8";

        public static bool HasTexTransTool
        {
            get
            {
#if AC_CHBT_TTT_PRESENT
                return true;
#else
                return false;
#endif
            }
        }

        public static bool HasNdmf
        {
            get
            {
#if NDMF_ABSENT
                return false;
#else
                return true;
#endif
            }
        }
    }

#if AC_CHBT_TTT_PRESENT
    public sealed class ProductCandidate
    {
        public GameObject Root;
        public int ActiveDecals;
        public int TotalDecals;
        public bool HasCustom;

        public string Label => Root.name + "  （有効な色 " + ActiveDecals + " / " + TotalDecals + "）";
    }

    /// <summary>レンダラーの1スロット分</summary>
    public sealed class ExportTarget
    {
        public Renderer Renderer;
        public int Slot;
        public Material Material;
        public string PropertyName;
        public Texture OriginalTexture;
        public List<Component> Decals = new List<Component>();
        /// <summary>元は空の書き先（光る毛束など）。書き出し時に描かれていれば PNG にする</summary>
        public List<string> EmptyMapProperties = new List<string>();
        /// <summary>例: Default_Sky / Custom_Inner_FFC0CB_FFFFFF</summary>
        public string Label;

        public string DisplayName =>
            (Renderer != null ? Renderer.name : "?") + " [" + Slot + "] " + (Material != null ? Material.name : "?") + " ← " + Label;
    }

    public sealed class ExportResult
    {
        public ExportTarget Target;
        public string PngAssetPath;
        public Vector2Int Size;
        public Vector2Int OriginalSize;
        public string Error;
        // 色が当たらなかったマテリアル（同じメッシュの別マテリアルには付いた）。失敗扱いにせず案内だけ
        public string Note;
        public List<BakedMap> Maps = new List<BakedMap>();
        public bool Success => string.IsNullOrEmpty(Error) && string.IsNullOrEmpty(Note);
        public bool SizeDiffers => Success && OriginalSize.x > 0 && Size != OriginalSize;
    }

    /// <summary>元は空の画像に描かれた1枚と、その段の設定。
    /// 発光はアップロード時にプラグインが複製へ入れるため、焼き込んだマテリアルにも同じ値を入れる</summary>
    public sealed class BakedMap
    {
        public string PropertyName;
        public string PngAssetPath;
        internal byte[] Png;
        internal bool SRGB;
        internal Vector2Int Size;
        internal List<MaterialValue> Values = new List<MaterialValue>();
    }

    /// <summary>画像のときは Vector に Tiling / Offset を入れる</summary>
    internal struct MaterialValue
    {
        public string Name;
        public ShaderPropertyType Type;
        public Vector4 Vector;
        public int Int;
        public Texture Texture;

        public static MaterialValue Read(Material m, string name, ShaderPropertyType type)
        {
            var v = new MaterialValue { Name = name, Type = type };
            switch (type)
            {
                case ShaderPropertyType.Color: v.Vector = m.GetColor(name); break;
                case ShaderPropertyType.Vector: v.Vector = m.GetVector(name); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: v.Vector.x = m.GetFloat(name); break;
                case ShaderPropertyType.Int: v.Int = m.GetInteger(name); break;
                case ShaderPropertyType.Texture:
                    v.Texture = m.GetTexture(name);
                    var s = m.GetTextureScale(name);
                    var o = m.GetTextureOffset(name);
                    v.Vector = new Vector4(s.x, s.y, o.x, o.y);
                    break;
            }
            return v;
        }

        public bool SameAs(MaterialValue other) => Vector == other.Vector && Int == other.Int && Texture == other.Texture;

        public void Write(Material m)
        {
            switch (Type)
            {
                case ShaderPropertyType.Color: m.SetColor(Name, Vector); break;
                case ShaderPropertyType.Vector: m.SetVector(Name, Vector); break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: m.SetFloat(Name, Vector.x); break;
                case ShaderPropertyType.Int: m.SetInteger(Name, Int); break;
                case ShaderPropertyType.Texture:
                    m.SetTexture(Name, Texture);
                    m.SetTextureScale(Name, new Vector2(Vector.x, Vector.y));
                    m.SetTextureOffset(Name, new Vector2(Vector.z, Vector.w));
                    break;
            }
        }
    }

    public sealed class ApplyResult
    {
        public ExportTarget Target;
        public string MaterialAssetPath;
        public List<string> ReplacedProperties = new List<string>();
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error);
    }

    /// <summary>複製マテリアルの importer の userData に残す印（元に戻すときに使う）</summary>
    [Serializable]
    public sealed class BakedMaterialTag
    {
        public string tool;
        public string originalGuid;
        /// <summary>適用時にオフにした商品プレハブの、アバターからのパス</summary>
        public string productPath;
    }

    public sealed class RestoreEntry
    {
        public Renderer Renderer;
        public int Slot;
        public Material Baked;
        public Material Original;
        public string ProductPath;
    }

    /// <summary>今の髪色を NDMF でベイクし、UV 空間の PNG にする。Cloveria_Custom_Hair_Color_Tool には依存しない</summary>
    public static class CustomHairColorBakedTool
    {
        public const string DefaultOutputFolder = "Assets/Atelier_Cloveria/" + BakedToolInfo.ToolName;
        public const string NoticeFileName = "README_BAKED_NOTICE.txt";
        public const string LogPrefix = "[" + BakedToolInfo.ToolName + "] ";
        public const string CustomNodeName = "Custom";
        internal const string MarkerPrefix = "__ACHBT";
        internal const string MarkerSuffix = "__";
        const string CloneSuffix = " (" + BakedToolInfo.ToolName + " temp)";

        // ---- アバターと商品の検出 ----

        // 型名で判定（VRC SDK を参照しないため）
        public static bool IsAvatarDescriptor(Component c) => c != null && c.GetType().Name == "VRCAvatarDescriptor";

        // 型を参照せず名前で判定（TTT の実験的な部品のため。消えてもコンパイルは止まらない）
        static bool IsInheritBreaker(Component c) => c != null && c.GetType().Name == "IsActiveInheritBreaker";

        public static bool IsAvatarRoot(GameObject go) => go != null && go.GetComponents<Component>().Any(IsAvatarDescriptor);

        /// <summary>VRCAvatarDescriptor を持つ最上位。無ければ transform.root</summary>
        public static GameObject FindAvatarRoot(GameObject go)
        {
            if (go == null) return null;
            GameObject found = null;
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (IsAvatarRoot(t.gameObject)) found = t.gameObject;
            }
            return found != null ? found : go.transform.root.gameObject;
        }

        /// <summary>Custom ノードを持つもの（Cloveria の髪色プレハブ）を先に並べる</summary>
        public static List<ProductCandidate> FindProducts(GameObject avatar)
        {
            var list = new List<ProductCandidate>();
            if (avatar == null) return list;
            for (int i = 0; i < avatar.transform.childCount; i++)
            {
                var child = avatar.transform.GetChild(i).gameObject;
                var decals = child.GetComponentsInChildren<SingleGradationDecal>(true);
                if (decals.Length == 0) continue;
                list.Add(new ProductCandidate
                {
                    Root = child,
                    TotalDecals = decals.Length,
                    ActiveDecals = decals.Count(d => IsActiveInDomain(d.gameObject, avatar)),
                    HasCustom = child.GetComponentsInChildren<Transform>(true).Any(t => NameIs(t, CustomNodeName)),
                });
            }
            return list.OrderByDescending(p => p.HasCustom).ThenByDescending(p => p.ActiveDecals).ThenBy(p => p.Root.name).ToList();
        }

        static bool NameIs(Transform t, string name) => string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase);

        // ---- 書き出し対象の検出 ----

        /// <summary>レンダラーはアバター全体から探す（TTT の Auto 選択に合わせる）</summary>
        public static List<ExportTarget> Detect(GameObject avatar, GameObject productRoot, List<string> warnings)
        {
            var targets = new List<ExportTarget>();
            if (avatar == null || productRoot == null) return targets;

            if (!avatar.activeInHierarchy)
                warnings.Add("アバターがオフになっています。Hierarchy でアバターをオンにしてから実行してください。");

            var decals = productRoot.GetComponentsInChildren<SingleGradationDecal>(true)
                .Where(d => d != null && IsActiveInDomain(d.gameObject, avatar))
                .ToList();

            if (decals.Count == 0)
            {
                warnings.Add("オンになっている色がありません。カスタムカラーツールで色を選ぶか、Hierarchy で色のセットの中の色をオンにしてください。");
                return targets;
            }

            var allRenderers = avatar.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is SkinnedMeshRenderer || r is MeshRenderer)
                .ToList();

            var map = new Dictionary<(Renderer, int), ExportTarget>();
            var emptyMaps = new List<(Renderer, int, string)>();

            foreach (var decal in decals)
            {
                string prop = decal.TargetPropertyName;
                if (string.IsNullOrEmpty(prop)) prop = "_MainTex";

                var sel = decal.RendererSelector;
                IEnumerable<Renderer> renderers;
                HashSet<Material> filterMaterials = null;

                if (sel.Mode == RendererSelectMode.Manual)
                {
                    renderers = sel.ManualSelections.Where(r => r != null && r.transform.IsChildOf(avatar.transform));
                }
                else
                {
                    renderers = allRenderers;
                    if (!sel.IsAutoIncludingDisableRenderers)
                        renderers = renderers.Where(r => r.enabled && IsActiveInDomain(r.gameObject, avatar));
                    if (sel.UseMaterialFilteringForAutoSelect)
                    {
                        filterMaterials = new HashSet<Material>(sel.AutoSelectFilterMaterials.Where(m => m != null));
                        if (filterMaterials.Count == 0)
                            warnings.Add("「" + decal.gameObject.name + "」はマテリアルの絞り込みがオンなのに、対象のマテリアルが空です（TexTransTool 側でも何も選ばれません）。");
                    }
                }

                int hit = 0, emptyHit = 0;
                foreach (var r in renderers)
                {
                    var mats = r.sharedMaterials;
                    for (int slot = 0; slot < mats.Length; slot++)
                    {
                        var mat = mats[slot];
                        if (mat == null) continue;
                        if (filterMaterials != null && !filterMaterials.Contains(mat)) continue;
                        if (!mat.HasProperty(prop)) continue;
                        // TTT は書き先の画像が空のマテリアルには描かないため外す
                        // 光る毛束は書き出し時に先に下地が入って描かれるため、書き先だけ控える
                        if (mat.GetTexture(prop) == null)
                        {
                            emptyMaps.Add((r, slot, prop));
                            emptyHit++;
                            continue;
                        }

                        var key = (r, slot);
                        if (!map.TryGetValue(key, out var t))
                        {
                            t = new ExportTarget
                            {
                                Renderer = r,
                                Slot = slot,
                                Material = mat,
                                PropertyName = prop,
                                OriginalTexture = mat.GetTexture(prop),
                            };
                            map[key] = t;
                            targets.Add(t);
                        }
                        else if (t.PropertyName != prop)
                        {
                            warnings.Add(r.name + "[" + slot + "] に、別々の画像（" + t.PropertyName + " / " + prop + "）へ色を付ける設定が混ざっています。" + t.PropertyName + " だけを焼き込みます。");
                            continue;
                        }
                        t.Decals.Add(decal);
                        hit++;
                    }
                }

                if (hit == 0 && emptyHit == 0)
                    warnings.Add("「" + decal.gameObject.name + "」の色を付ける先（髪のメッシュ）が見つかりません（TexTransTool 側でも TargetNotFound になります）。");
            }

            // 光る毛束だけが当たるスロットは色を焼き込まないため触らない
            foreach (var (r, slot, prop) in emptyMaps)
            {
                if (map.TryGetValue((r, slot), out var t) && !t.EmptyMapProperties.Contains(prop)) t.EmptyMapProperties.Add(prop);
            }

            foreach (var t in targets)
            {
                var labels = t.Decals
                    .Select(d => ComputeLabel(d.transform, productRoot.transform))
                    .Distinct()
                    .ToList();
                t.Label = string.Join("+", labels);
            }

            return targets;
        }

        /// <summary>TTT のオン・オフの判定に合わせる（アバター自身のオン・オフは見ない）</summary>
        internal static bool IsActiveInDomain(GameObject entry, GameObject domainRoot)
        {
            var t = entry.transform;
            var rootT = domainRoot != null ? domainRoot.transform : null;
            while (t != null && t != rootT)
            {
                if (t.GetComponents<Component>().Any(IsInheritBreaker)) return true;
                if (!t.gameObject.activeSelf) return false;
                t = t.parent;
            }
            return true;
        }

        /// <summary>例: Default/Sky → Default_Sky、Inner/White × Pink → Inner_White_Pink、
        /// Custom/2ton → Custom_2ton_87CEEB_FFC0CB（色は Decal から読む）</summary>
        internal static string ComputeLabel(Transform decalT, Transform productRoot)
        {
            var chain = new List<Transform>();
            for (var t = decalT; t != null && t != productRoot; t = t.parent) chain.Add(t);
            if (chain.Count == 0 || chain[chain.Count - 1].parent != productRoot) return SanitizeName(decalT.name);
            chain.Reverse();

            var group = chain[0];
            var sub = chain.Count >= 2 ? chain[1] : null;

            if (NameIs(group, CustomNodeName) && sub != null)
            {
                // グラデは同じ Decal がパーツの数だけ並ぶため、3枚以上なら同じ色の並びを1つにまとめる
                // （2枚までの Inner・2ton・Mesh は同じ色でも2つ並べる。ファイル名を変えないため）
                var hex = CollectDecals(sub).Select(KeyColorsHex).ToList();
                if (hex.Count > 2) hex = hex.Distinct().ToList();
                if (hex.Count > 0) return SanitizeName("Custom_" + sub.name + "_" + string.Join("_", hex));
            }

            return sub != null
                ? SanitizeName(group.name) + "_" + SanitizeName(sub.name)
                : SanitizeName(group.name);
        }

        /// <summary>Decal を持つノードで打ち切る（Cloveria_Custom_Hair_Color_Tool と同じ規則）</summary>
        internal static List<SingleGradationDecal> CollectDecals(Transform node)
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

        static string KeyColorsHex(SingleGradationDecal decal)
        {
            var g = decal == null ? null : new SerializedObject(decal).FindProperty("Gradient").gradientValue;
            if (g == null || g.colorKeys == null || g.colorKeys.Length == 0) return ColorUtility.ToHtmlStringRGB(Color.white);
            return string.Join("_", g.colorKeys.OrderBy(k => k.time).Select(k => ColorUtility.ToHtmlStringRGB(k.color)).Distinct());
        }

        public static Color ReadColor(SingleGradationDecal decal)
        {
            if (decal == null) return Color.white;
            var g = new SerializedObject(decal).FindProperty("Gradient").gradientValue;
            if (g == null || g.colorKeys == null || g.colorKeys.Length == 0) return Color.white;
            return g.colorKeys[0].color;
        }

        // ---- 出力先 ----

        public static string AvatarOutputFolder(GameObject avatar)
        {
            return DefaultOutputFolder + "/" + SanitizeName(avatar.name);
        }

        static string Folder(string folder) => folder.Replace('\\', '/').TrimEnd('/');

        public static string PngAssetPath(GameObject avatar, ExportTarget t, string folder)
        {
            return Folder(folder) + "/" +
                   SanitizeName(avatar.name) + "_" + SanitizeName(t.Material.name) + "_" + SanitizeName(t.Label) + ".png";
        }

        public static string MapPngAssetPath(GameObject avatar, ExportTarget t, string property, string folder)
        {
            var png = PngAssetPath(avatar, t, folder);
            return png.Substring(0, png.Length - ".png".Length) + "_" + SanitizeName(property) + ".png";
        }

        public static List<string> OutputPngPaths(GameObject avatar, IEnumerable<ExportTarget> targets, string folder)
        {
            return targets
                .SelectMany(t => new[] { PngAssetPath(avatar, t, folder) }
                    .Concat(t.EmptyMapProperties.Select(p => MapPngAssetPath(avatar, t, p, folder))))
                .Distinct()
                .ToList();
        }

        public static List<string> ExistingOutputs(GameObject avatar, IEnumerable<ExportTarget> targets, string folder)
        {
            return OutputPngPaths(avatar, targets, folder).Where(File.Exists).ToList();
        }

        public static string MaterialAssetPath(GameObject avatar, ExportTarget t, string folder)
        {
            return Folder(folder) + "/" +
                   SanitizeName(t.Material.name) + "_" + SanitizeName(t.Label) + ".mat";
        }

        // ---- マテリアルへの適用 ----

        /// <summary>元テクスチャを参照する全プロパティ（lilToon の _OutlineTex など）を差し替える（TTT の参照の置き換えに合わせる）。
        /// disableProduct は TTT の二重の投影を避けるため</summary>
        public static List<ApplyResult> Apply(GameObject avatar, IList<ExportResult> exported, string folder, GameObject productRoot, bool disableProduct)
        {
            var results = new List<ApplyResult>();
            if (avatar == null || exported == null) return results;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(BakedToolInfo.ToolName + ": Apply");

            var productPath = productRoot != null ? HierarchyPath(productRoot.transform, avatar.transform) : "";
            var created = new Dictionary<string, Material>();
            var replacedByPath = new Dictionary<string, List<string>>();

            foreach (var e in exported)
            {
                if (e == null || !e.Success || string.IsNullOrEmpty(e.PngAssetPath)) continue;
                var t = e.Target;
                var res = new ApplyResult { Target = t };
                results.Add(res);

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(e.PngAssetPath);
                if (tex == null) { res.Error = "保存した画像を読み込めませんでした: " + e.PngAssetPath; continue; }
                // 光の画像が無いまま発光をオンにすると髪全体が光る（lilToon は空の発光の画像を白とみなす）
                var mapTextures = e.Maps.ToDictionary(m => m, m => AssetDatabase.LoadAssetAtPath<Texture2D>(m.PngAssetPath));
                var missing = mapTextures.Where(kv => kv.Value == null).Select(kv => kv.Key.PngAssetPath).ToList();
                if (missing.Count > 0) { res.Error = "保存した光の画像を読み込めませんでした: " + string.Join(", ", missing); continue; }

                var matPath = MaterialAssetPath(avatar, t, folder);
                if (!created.TryGetValue(matPath, out var newMat))
                {
                    var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (existing != null)
                    {
                        // 前回の複製に上書き（参照を保つため）
                        var tmp = new Material(t.Material);
                        EditorUtility.CopySerialized(tmp, existing);
                        Object.DestroyImmediate(tmp);
                        existing.name = Path.GetFileNameWithoutExtension(matPath);
                        newMat = existing;
                    }
                    else
                    {
                        newMat = new Material(t.Material) { name = Path.GetFileNameWithoutExtension(matPath) };
                        AssetDatabase.CreateAsset(newMat, matPath);
                    }

                    var replaced = new List<string>();
                    foreach (var prop in newMat.GetTexturePropertyNames())
                    {
                        if (newMat.GetTexture(prop) == t.OriginalTexture)
                        {
                            newMat.SetTexture(prop, tex);
                            replaced.Add(prop);
                        }
                    }
                    if (!replaced.Contains(t.PropertyName) && newMat.HasProperty(t.PropertyName))
                    {
                        newMat.SetTexture(t.PropertyName, tex);
                        replaced.Add(t.PropertyName);
                    }

                    foreach (var m in e.Maps)
                    {
                        foreach (var v in m.Values) v.Write(newMat);
                        newMat.SetTexture(m.PropertyName, mapTextures[m]);
                        replaced.Add(m.PropertyName);
                    }
                    EditorUtility.SetDirty(newMat);
                    created[matPath] = newMat;
                    replacedByPath[matPath] = replaced;
                    WriteTag(matPath, t.Material, productPath);
                }
                res.MaterialAssetPath = matPath;
                res.ReplacedProperties = replacedByPath[matPath];

                var mats = t.Renderer.sharedMaterials;
                if (t.Slot < mats.Length && (mats[t.Slot] == t.Material || mats[t.Slot] == newMat))
                {
                    if (mats[t.Slot] != newMat)
                    {
                        Undo.RecordObject(t.Renderer, BakedToolInfo.ToolName + ": Apply");
                        mats[t.Slot] = newMat;
                        t.Renderer.sharedMaterials = mats;
                        EditorUtility.SetDirty(t.Renderer);
                    }
                }
                else
                {
                    res.Error = "髪のマテリアルの並びが変わっていたため付けられませんでした（複製マテリアルは保存してあります: " + matPath + "）。";
                }
            }

            if (disableProduct && productRoot != null && results.Any(r => r.Success) && productRoot.activeSelf)
            {
                Undo.RecordObject(productRoot, BakedToolInfo.ToolName + ": Disable product");
                productRoot.SetActive(false);
                EditorUtility.SetDirty(productRoot);
            }

            AssetDatabase.SaveAssets();
            return results;
        }

        // ---- 元に戻す ----

        const string TagPrefix = "ACHBT:";

        static void WriteTag(string matPath, Material original, string productPath)
        {
            var importer = AssetImporter.GetAtPath(matPath);
            if (importer == null) return;
            var tag = new BakedMaterialTag
            {
                tool = BakedToolInfo.ToolName,
                originalGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)),
                productPath = productPath,
            };
            importer.userData = TagPrefix + JsonUtility.ToJson(tag);
            importer.SaveAndReimport();
        }

        public static bool TryReadTag(Material m, out BakedMaterialTag tag)
        {
            tag = null;
            if (m == null) return false;
            var path = AssetDatabase.GetAssetPath(m);
            if (string.IsNullOrEmpty(path)) return false;
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || string.IsNullOrEmpty(importer.userData) || !importer.userData.StartsWith(TagPrefix, StringComparison.Ordinal)) return false;
            try
            {
                tag = JsonUtility.FromJson<BakedMaterialTag>(importer.userData.Substring(TagPrefix.Length));
                return tag != null && !string.IsNullOrEmpty(tag.originalGuid);
            }
            catch { return false; }
        }

        public static List<RestoreEntry> FindApplied(GameObject avatar)
        {
            var list = new List<RestoreEntry>();
            if (avatar == null) return list;
            foreach (var r in avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    if (!TryReadTag(mats[slot], out var tag)) continue;
                    var originalPath = AssetDatabase.GUIDToAssetPath(tag.originalGuid);
                    list.Add(new RestoreEntry
                    {
                        Renderer = r,
                        Slot = slot,
                        Baked = mats[slot],
                        Original = string.IsNullOrEmpty(originalPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(originalPath),
                        ProductPath = tag.productPath,
                    });
                }
            }
            return list;
        }

        /// <summary>複製マテリアルと PNG は消さない（もう一度適用するため）</summary>
        public static int Restore(GameObject avatar, IList<RestoreEntry> entries, bool reenableProduct, List<string> errors)
        {
            if (avatar == null || entries == null || entries.Count == 0) return 0;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(BakedToolInfo.ToolName + ": Restore");

            int restored = 0;
            var productPaths = new HashSet<string>();
            foreach (var e in entries)
            {
                if (e.Renderer == null) continue;
                if (e.Original == null)
                {
                    errors.Add(e.Renderer.name + "[" + e.Slot + "]: 焼き込む前のマテリアルが見つかりません（削除または移動された可能性があります）。");
                    continue;
                }
                var mats = e.Renderer.sharedMaterials;
                if (e.Slot >= mats.Length || mats[e.Slot] != e.Baked) continue;
                Undo.RecordObject(e.Renderer, BakedToolInfo.ToolName + ": Restore");
                mats[e.Slot] = e.Original;
                e.Renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(e.Renderer);
                restored++;
                if (!string.IsNullOrEmpty(e.ProductPath)) productPaths.Add(e.ProductPath);
            }

            if (reenableProduct)
            {
                foreach (var p in productPaths)
                {
                    var t = FindByHierarchyPath(avatar.transform, p);
                    if (t == null || t.gameObject.activeSelf) continue;
                    Undo.RecordObject(t.gameObject, BakedToolInfo.ToolName + ": Enable product");
                    t.gameObject.SetActive(true);
                    EditorUtility.SetDirty(t.gameObject);
                }
            }
            return restored;
        }

        internal static string HierarchyPath(Transform t, Transform root)
        {
            var names = new List<string>();
            for (; t != null && t != root; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        internal static Transform FindByHierarchyPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var cur = root;
            foreach (var name in path.Split('/'))
            {
                Transform next = null;
                for (int i = 0; i < cur.childCount; i++)
                    if (cur.GetChild(i).name == name) { next = cur.GetChild(i); break; }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        internal static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            var sb = new StringBuilder(name.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in name)
            {
                if (invalid.Contains(c) || c == '/' || c == '\\' || c == '×' || char.IsWhiteSpace(c)) sb.Append('_');
                else sb.Append(c);
            }
            var s = sb.ToString();
            while (s.Contains("__")) s = s.Replace("__", "_");
            s = s.Trim('_', '.');
            return string.IsNullOrEmpty(s) ? "unnamed" : s;
        }

        // ---- 書き出し ----

        /// <summary>アバターの複製を NDMF で処理し、TTT が描いた画像を PNG にする</summary>
        public static List<ExportResult> Export(GameObject avatar, IList<ExportTarget> targets, string folder, Action<float, string> progress)
        {
            var results = targets.Select(t => new ExportResult
            {
                Target = t,
                OriginalSize = t.OriginalTexture != null ? new Vector2Int(t.OriginalTexture.width, t.OriginalTexture.height) : Vector2Int.zero,
            }).ToList();
            if (avatar == null || targets.Count == 0) return results;
            progress = progress ?? ((f, s) => { });
#if NDMF_ABSENT
            foreach (var r in results) r.Error = "NDMF が見つかりません。";
            return results;
#else
            var pngBytes = new byte[targets.Count][];
            var pngSRGB = new bool[targets.Count];

            GameObject clone = null;
            NDMFPreview.DisablePreviewDepth++;
            try
            {
                progress(0.1f, "アバターのコピーを作っています…");
                clone = Object.Instantiate(avatar);
                clone.name = avatar.name + CloneSuffix;
                clone.transform.SetParent(null, true);
                clone.transform.position = avatar.transform.position + Vector3.forward * 2f;
                clone.SetActive(true);

                int stripped = StripAvatarOptimizer(clone);
                if (stripped > 0) Debug.Log(LogPrefix + "複製から Avatar Optimizer のコンポーネントを " + stripped + " 件外しました（UV 空間を保つため。元のアバターには触りません）");

                // 名前に印を付けて追跡（Editor アセンブリの MonoBehaviour は AddComponent できないため）
                // NDMF で階層が動いても名前は残る
                var tokenToIndex = new Dictionary<string, int>();
                for (int i = 0; i < targets.Count; i++)
                {
                    var cloneRenderer = FindCounterpart(targets[i].Renderer.transform, avatar.transform, clone.transform);
                    if (cloneRenderer == null)
                    {
                        results[i].Error = "アバターのコピーの中に、対応する髪のメッシュが見つかりませんでした。";
                        continue;
                    }
                    var go = cloneRenderer.gameObject;
                    string token = ExtractToken(go.name);
                    if (token == null)
                    {
                        token = MarkerPrefix + i + MarkerSuffix;
                        go.name += token;
                    }
                    tokenToIndex[token] = i;
                }

                progress(0.3f, "色を髪の画像に描き込んでいます…（数秒〜十数秒かかります）");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                AvatarProcessor.ProcessAvatar(clone);
                sw.Stop();
                Debug.Log(LogPrefix + "NDMF ProcessAvatar: " + sw.ElapsedMilliseconds + " ms");

                progress(0.7f, "画像ファイル（PNG）に変換しています…");
                var foundRenderers = new Dictionary<int, Renderer>();
                foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
                {
                    var token = ExtractToken(r.gameObject.name);
                    if (token != null && tokenToIndex.TryGetValue(token, out var anyIndex))
                    {
                        var origRenderer = targets[anyIndex].Renderer;
                        for (int i = 0; i < targets.Count; i++)
                            if (targets[i].Renderer == origRenderer) foundRenderers[i] = r;
                    }
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    var res = results[i];
                    if (!res.Success) continue;
                    var t = targets[i];

                    if (!foundRenderers.TryGetValue(i, out var renderer) || renderer == null)
                    {
                        res.Error = "描き込んだあとのコピーから、対象の髪のメッシュが見つかりませんでした（ほかのツールで結合された可能性があります）。";
                        continue;
                    }

                    var mats = renderer.sharedMaterials;
                    Material mat = t.Slot < mats.Length ? mats[t.Slot] : null;
                    if (mat == null || !mat.HasProperty(t.PropertyName))
                    {
                        mat = mats.FirstOrDefault(m => m != null && m.HasProperty(t.PropertyName)
                                                       && m.name.StartsWith(t.Material.name, StringComparison.Ordinal));
                    }
                    if (mat == null) { res.Error = "描き込んだあとのマテリアルが見つかりません。"; continue; }

                    var baked = mat.GetTexture(t.PropertyName);
                    if (baked == null) { res.Error = "描き込んだあとの画像（" + t.PropertyName + "）が空です。"; continue; }
                    if (baked == t.OriginalTexture)
                    {
                        res.Error = NothingPaintedError;
                        continue;
                    }

                    try
                    {
                        // TTT はマテリアル単位で描くため、共有されたマテリアルの PNG 化は1回
                        int shared = -1;
                        for (int j = 0; j < i; j++)
                            if (pngBytes[j] != null && targets[j].Material == t.Material && targets[j].Label == t.Label
                                && targets[j].EmptyMapProperties.SequenceEqual(t.EmptyMapProperties))
                            { shared = j; break; }

                        if (shared >= 0)
                        {
                            pngBytes[i] = pngBytes[shared];
                            pngSRGB[i] = pngSRGB[shared];
                            res.Size = results[shared].Size;
                            res.Maps = results[shared].Maps;
                        }
                        else
                        {
                            pngBytes[i] = TexturePngUtil.EncodeToPng(baked, out pngSRGB[i]);
                            res.Size = new Vector2Int(baked.width, baked.height);
                            res.Maps = CaptureEmptyMaps(t, mat);
                        }
                    }
                    catch (Exception ex)
                    {
                        res.Error = "画像（PNG）への変換に失敗: " + ex.Message;
                        Debug.LogException(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                foreach (var r in results) if (r.Success) r.Error = "NDMF 処理中にエラー: " + ex.Message;
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                try { AvatarProcessor.CleanTemporaryAssets(); }
                catch (Exception ex) { Debug.LogWarning(LogPrefix + "一時アセットの掃除に失敗: " + ex.Message); }
                NDMFPreview.DisablePreviewDepth--;
            }

            // 同じメッシュの別マテリアルに色が付いていれば失敗にしない（髪の上に重ねる影などで起きる）
            // メッシュごとに見る（別のメッシュが全部塗れなかったときまで通さないため）
            foreach (var r in results)
            {
                if (r.Error != NothingPaintedError) continue;
                if (!results.Any(o => o.Success && o.Target.Renderer == r.Target.Renderer)) continue;
                r.Error = null;
                r.Note = r.Target.Renderer.name + " の「" + r.Target.Material.name + "」には、いまの色が当たる所がありませんでした。このマテリアルは元のままです（髪の上に重ねる影などではよくあることで、問題ありません）。";
            }

            if (results.All(r => !r.Success)) return results;

            progress(0.9f, "画像ファイルを保存しています…");
            var saveFolder = Folder(folder);
            EnsureFolder(saveFolder);
            WriteNotice(saveFolder);

            var written = new List<int>();
            var writtenPaths = new HashSet<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < targets.Count; i++)
                {
                    var res = results[i];
                    if (!res.Success) continue;
                    var png = PngAssetPath(avatar, targets[i], folder);
                    try
                    {
                        if (writtenPaths.Add(png)) File.WriteAllBytes(png, pngBytes[i]);
                        res.PngAssetPath = png;
                        foreach (var m in res.Maps)
                        {
                            var mapPng = MapPngAssetPath(avatar, targets[i], m.PropertyName, folder);
                            if (writtenPaths.Add(mapPng)) File.WriteAllBytes(mapPng, m.Png);
                            m.PngAssetPath = mapPng;
                        }
                        written.Add(i);
                    }
                    catch (Exception ex)
                    {
                        res.Error = "画像の保存に失敗: " + ex.Message;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            var imported = new HashSet<string>();
            foreach (var i in written)
            {
                var res = results[i];
                if (!imported.Add(res.PngAssetPath)) continue;
                AssetDatabase.ImportAsset(res.PngAssetPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureImporter(res.PngAssetPath, targets[i].OriginalTexture, pngSRGB[i], res.Size);
                foreach (var m in res.Maps)
                {
                    if (!imported.Add(m.PngAssetPath)) continue;
                    AssetDatabase.ImportAsset(m.PngAssetPath, ImportAssetOptions.ForceSynchronousImport);
                    // 取り込みの設定は髪の画像にそろえる（同じ UV で読むため）
                    ConfigureImporter(m.PngAssetPath, targets[i].OriginalTexture, m.SRGB, m.Size);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return results;
#endif
        }

        const string NothingPaintedError = "画像に色が描き込まれませんでした（色が髪に当たっていない可能性があります）。";

        /// <summary>段は書き先から Map を除いた名前（_Emission2ndMap なら _Emission2nd で始まるものと _UseEmission2nd）。
        /// 描かれていない書き先（光らせていない・下地を入れるプラグインが無い）は飛ばす</summary>
        static List<BakedMap> CaptureEmptyMaps(ExportTarget t, Material baked)
        {
            var list = new List<BakedMap>();
            var shader = baked.shader;
            foreach (var prop in t.EmptyMapProperties)
            {
                var tex = baked.GetTexture(prop);
                if (tex == null) continue;
                var m = new BakedMap
                {
                    PropertyName = prop,
                    Png = TexturePngUtil.EncodeToPng(tex, out var srgb),
                    SRGB = srgb,
                    Size = new Vector2Int(tex.width, tex.height),
                };
                var stage = prop.EndsWith("Map", StringComparison.Ordinal) ? prop.Substring(0, prop.Length - 3) : prop;
                var use = "_Use" + stage.TrimStart('_');
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var name = shader.GetPropertyName(i);
                    if (name == prop || !(name.StartsWith(stage, StringComparison.Ordinal) || name == use)) continue;
                    var type = shader.GetPropertyType(i);
                    var v = MaterialValue.Read(baked, name, type);
                    if (!v.SameAs(MaterialValue.Read(t.Material, name, type))) m.Values.Add(v);
                }
                list.Add(m);
            }
            return list;
        }

        /// <summary>AAO は TTT より後にテクスチャをアトラス化・縮小し、UV 空間の PNG が取れなくなるため外す。
        /// asmdef で参照せず namespace で判定</summary>
        internal static int StripAvatarOptimizer(GameObject root)
        {
            int removed = 0;
            for (int pass = 0; pass < 4; pass++)
            {
                int before = removed;
                foreach (var c in root.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    var ns = c.GetType().Namespace ?? "";
                    if (!ns.StartsWith("Anatawa12.AvatarOptimizer", StringComparison.OrdinalIgnoreCase)) continue;
                    try { Object.DestroyImmediate(c); removed++; }
                    catch (Exception ex) { Debug.LogWarning(LogPrefix + c.GetType().Name + " を外せませんでした: " + ex.Message); }
                }
                if (removed == before) break; // RequireComponent で残った分を次の周で外す
            }
            return removed;
        }

        /// <summary>"Hair_front__ACHBT3__" → "__ACHBT3__"。無ければ null</summary>
        internal static string ExtractToken(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int s = name.IndexOf(MarkerPrefix, StringComparison.Ordinal);
            if (s < 0) return null;
            int e = name.IndexOf(MarkerSuffix, s + MarkerPrefix.Length, StringComparison.Ordinal);
            if (e < 0) return null;
            return name.Substring(s, e + MarkerSuffix.Length - s);
        }

        internal static Renderer FindCounterpart(Transform original, Transform originalRoot, Transform cloneRoot)
        {
            var path = new List<int>();
            var t = original;
            while (t != null && t != originalRoot)
            {
                path.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            if (t == null) return null;
            path.Reverse();

            var c = cloneRoot;
            foreach (var idx in path)
            {
                if (idx >= c.childCount) return null;
                c = c.GetChild(idx);
            }
            return c.GetComponent<Renderer>();
        }

        internal static void EnsureFolder(string assetFolder)
        {
            var parts = assetFolder.Replace('\\', '/').Split('/');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        internal static void WriteNotice(string folder)
        {
            var path = folder + "/" + NoticeFileName;
            if (File.Exists(path)) return;
            var text =
                "このフォルダ配下の PNG は " + BakedToolInfo.ToolName + " がお使いの環境内で生成した「あなた専用」のテクスチャです。\n" +
                "元テクスチャの派生物にあたるため、アバターの利用規約に従い、再配布・販売・他者への譲渡はできません。\n" +
                "別の色にしたい場合は、色を選び直してからもう一度書き出してください。\n" +
                "使い方：ツールの「保存した画像を髪に付ける」を使うか、手で入れる場合はメインテクスチャと輪郭線テクスチャの両方に PNG を入れます。その後は色のセット（髪色プレハブ）をオフにしてください。\n" +
                "\n" +
                "The PNG files under this folder were generated locally by " + BakedToolInfo.ToolName + " for your personal use only.\n" +
                "They are derivative works of the avatar's original textures. Do not redistribute, sell, or share them.\n" +
                "To change the color, pick another color and export again.\n" +
                "Usage: use \"Apply to materials\" in the tool, or assign the PNG to BOTH the main texture and the outline texture of the hair material, then turn the hair color prefab off.\n" +
                "\n" +
                "Generated by " + BakedToolInfo.ToolName + " v" + BakedToolInfo.Version + " / Atelier_Cloveria\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path);
        }

        internal static void ConfigureImporter(string pngPath, Texture original, bool isSRGB, Vector2Int size)
        {
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer == null) return;

            TextureImporter src = null;
            if (original != null)
            {
                var srcPath = AssetDatabase.GetAssetPath(original);
                if (!string.IsNullOrEmpty(srcPath)) src = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = isSRGB;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;

            int need = TexturePngUtil.CeilPow2(Mathf.Max(size.x, size.y));
            if (src != null)
            {
                importer.mipmapEnabled = src.mipmapEnabled;
                importer.streamingMipmaps = src.streamingMipmaps;
                importer.textureCompression = src.textureCompression;
                importer.crunchedCompression = src.crunchedCompression;
                importer.compressionQuality = src.compressionQuality;
                importer.wrapMode = src.wrapMode;
                importer.filterMode = src.filterMode;
                importer.anisoLevel = src.anisoLevel;
                importer.maxTextureSize = Mathf.Max(src.maxTextureSize, need);
            }
            else
            {
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = need;
            }

            importer.SaveAndReimport();
        }
    }
#endif // AC_CHBT_TTT_PRESENT
}
#endif // UNITY_EDITOR
