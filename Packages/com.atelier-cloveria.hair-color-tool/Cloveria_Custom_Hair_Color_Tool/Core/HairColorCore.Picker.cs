#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System.Collections.Generic;
using System.Linq;
using net.rs64.TexTransTool;
using net.rs64.TexTransTool.Decal;
using net.rs64.TexTransTool.IslandSelector;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    // クリックで毛束の目印のオン・オフを入れ替える（Prefab 本体ではなくシーンのインスタンスの上書き）
    // 1色目・2色目の側は Custom の Decal が参照する OR から取る（Right・MeshA などの名前は見ない）
    public enum PinKind
    {
        TwoSides = 0, // メッシュ・ツートン
        Paint = 1, // 単色・グラデーション
    }

    public sealed class StrandColorPins
    {
        public PinKind Kind = PinKind.TwoSides;
        public readonly IslandSelectorOR[] Sides = new IslandSelectorOR[2]; // Paint では [0] だけ（Decals も同じ）
        public readonly SingleGradationDecal[] Decals = new SingleGradationDecal[2];
        public readonly List<IslandSelectorOR> Groups = new List<IslandSelectorOR>(); // Paint 用（重ならない）
        public readonly List<SingleGradationDecal> AllDecals = new List<SingleGradationDecal>();
        public bool PresetsShare;
        public bool ModesShare;
        public readonly List<PinIslandSelector> AllPins = new List<PinIslandSelector>(); // 真ん中で切る毛束の目印は入れない
        public List<PinPair> Pairs = new List<PinPair>();
    }

    public enum PickAction
    {
        Swap = 0, // デフォルトの髪の毛束は 1色目に戻す
        Unpaint = 1,
    }

    public enum PickResult
    {
        Changed = 0,
        NoChange = 1,
        Cut = 2, // <名前>_Cut の Decal。変えない
        NoPin = 3,
    }

    public static partial class HairColorCore
    {
        public const string SelectorsNodeName = "_Selectors";

        // 目印の無い古い商品は null（毛束を選ぶ機能を出さない）
        // _Selectors の下なら深さは見ない（書き出し直した商品は _Selectors/_Custom/MeshA など）
        static StrandColorPins DetectPinSides(Transform custom, ColoringModeParts mt, ColoringModeInfo info)
        {
            if (mt.Decals.Count < 2) return null;
            var product = custom.parent;
            if (product == null) return null;
            var ps = new StrandColorPins();
            var lists = new[] { new List<PinIslandSelector>(), new List<PinIslandSelector>() };
            for (var c = 0; c < 2; c++)
            {
                var d = mt.Decals[c];
                var or = d != null ? d.IslandSelector as IslandSelectorOR : null;
                if (or == null || !UnderSelectors(or.transform, product)) return null;
                ps.Sides[c] = or;
                ps.Decals[c] = d;
                ps.AllDecals.Add(d);
                StrandPicker.CollectStrandPins(or.transform, lists[c]);
            }
            if (ps.Sides[0] == ps.Sides[1] || lists[0].Count == 0 || lists[1].Count == 0) return null;

            // _Cut 仲間の Decal が参照する目印。ここがオンの毛束はクリックで変えない
            var cut = new List<PinIslandSelector>();
            foreach (var kv in mt.Followers)
            {
                foreach (var f in kv.Value)
                {
                    if (f == null || f.IslandSelector == null) continue;
                    if (f.IslandSelector is IslandSelectorOR fo) { if (fo != ps.Sides[0] && fo != ps.Sides[1]) StrandPicker.CollectStrandPins(fo.transform, cut); }
                    else if (f.IslandSelector is PinIslandSelector fp) cut.Add(fp);
                }
            }
            ps.AllPins.AddRange(lists[0]);
            ps.AllPins.AddRange(lists[1]);
            ps.Pairs = StrandPicker.PairStrandPins(lists[0], lists[1], cut);
            ps.PresetsShare = PresetsSharePins(product, info, ps);
            return ps;
        }

        // 古い Prefab ではプリセットもここで書く目印を見る（画面の一言を出し分ける）
        // OR でなく目印で比べる（単色の All とグラデの All/Part_ のように、別の OR でも中の目印が同じことがある）
        static bool PresetsSharePins(Transform product, ColoringModeInfo info, StrandColorPins ps)
        {
            var group = info.NodeNames.Select(n => FindChild(product, n)).FirstOrDefault(n => n != null);
            if (group == null || ps == null || ps.AllPins.Count == 0) return false;
            var mine = new HashSet<PinIslandSelector>(ps.AllPins);
            var seen = new HashSet<IslandSelectorOR>();
            foreach (var d in CollectDecals(group))
            {
                var or = d != null ? d.IslandSelector as IslandSelectorOR : null;
                if (or == null || !seen.Add(or)) continue;
                var list = new List<PinIslandSelector>();
                StrandPicker.CollectStrandPins(or.transform, list);
                if (list.Any(mine.Contains)) return true;
            }
            return false;
        }

        // 1枚でも参照していなければ null（単色の Decal が目印を見ない古い Prefab）
        // グラデはパーツごとの Decal が別々の OR を見るため、目印は重ならないように集める
        static StrandColorPins DetectPaintPins(Transform custom, ColoringModeParts mt, ColoringModeInfo info)
        {
            if (mt == null || mt.Decals.Count == 0) return null;
            var product = custom.parent;
            if (product == null) return null;
            var ps = new StrandColorPins { Kind = PinKind.Paint };
            var pins = new List<PinIslandSelector>();
            var seen = new HashSet<PinIslandSelector>();
            foreach (var d in mt.Decals)
            {
                var or = d != null ? d.IslandSelector as IslandSelectorOR : null;
                if (or == null || !UnderSelectors(or.transform, product)) return null;
                ps.AllDecals.Add(d);
                if (ps.Groups.Contains(or)) continue;
                ps.Groups.Add(or);
                var list = new List<PinIslandSelector>();
                StrandPicker.CollectStrandPins(or.transform, list);
                foreach (var p in list) if (seen.Add(p)) pins.Add(p);
            }
            if (pins.Count == 0) return null;
            ps.Sides[0] = ps.Groups[0];
            ps.Decals[0] = ps.AllDecals[0];
            ps.AllPins.AddRange(pins);
            ps.Pairs = StrandPicker.PairStrandPins(pins, new List<PinIslandSelector>(), null);
            ps.PresetsShare = PresetsSharePins(product, info, ps);
            return ps;
        }

        // 再書き出しした商品は同じ、それより前に出荷した商品は違う
        static void LinkPaintPins(ColoringProduct target)
        {
            var a = target.Has(ColoringMode.Default) ? target.ModeParts[ColoringMode.Default].Pins : null;
            var b = target.Has(ColoringMode.Gradation) ? target.ModeParts[ColoringMode.Gradation].Pins : null;
            if (a == null || b == null) return;
            var same = a.AllPins.Count == b.AllPins.Count && new HashSet<PinIslandSelector>(a.AllPins).SetEquals(b.AllPins);
            a.ModesShare = same;
            b.ModesShare = same;
        }

        static bool UnderSelectors(Transform t, Transform product)
        {
            for (var p = t; p != null && p != product; p = p.parent)
            {
                if (NameIs(p, SelectorsNodeName)) return p.parent == product;
            }
            return false;
        }

        // 両方オンは 2色目に数える（後ろの Decal が上に乗るため）。Paint は n[0]＝塗る・n[2]＝デフォルトの髪
        public static int[] CountStrandsPerColor(StrandColorPins ps)
        {
            var n = new int[4];
            if (ps == null) return n;
            if (ps.Kind == PinKind.Paint)
            {
                foreach (var p in ps.Pairs)
                {
                    if (p.A.Count == 0) continue;
                    if (p.OnA) n[0]++;
                    else n[2]++;
                }
                return n;
            }
            foreach (var p in ps.Pairs)
            {
                if (p.IsCut) { n[3]++; continue; }
                if (p.A.Count == 0 || p.B.Count == 0) continue;
                if (p.OnB) n[1]++;
                else if (p.OnA) n[0]++;
                else n[2]++;
            }
            return n;
        }

        // Undo のグループは呼ぶ側で切る（1クリック＝1回）。side は書いたあとの側（0＝1色目・1＝2色目・-1＝塗らない）
        // Paint は exclude を見ずに 塗る ⇄ デフォルトの髪 を入れ替える
        public static PickResult ToggleStrandColor(StrandColorPins ps, IEnumerable<int> pairIndices, bool exclude, out int side, IDictionary<int, bool> originals = null, IDictionary<int, bool> beforeUnpaint = null)
        {
            side = -1;
            if (ps == null || pairIndices == null) return PickResult.NoPin;
            var pairs = pairIndices.Where(i => i >= 0 && i < ps.Pairs.Count).Distinct().Select(i => ps.Pairs[i]).ToList();
            if (pairs.Count == 0) return PickResult.NoPin;
            if (pairs.Any(p => p.IsCut)) return PickResult.Cut;
            if (ps.Kind == PinKind.Paint)
            {
                var paint = pairs.Where(p => p.A.Count > 0).ToList();
                if (paint.Count == 0) return PickResult.NoPin;
                var on = !paint[0].OnA;
                side = on ? 0 : -1;
                return WritePins(paint.SelectMany(p => p.A).Where(pin => pin != null && pin.gameObject.activeSelf != on).Select(pin => (pin.gameObject, on)).ToList());
            }
            // 1つの毛束に組が2つ以上ある＝同じ面に重なる毛束。一緒に同じ側にする
            var usable = pairs.Where(p => p.A.Count > 0 && p.B.Count > 0).ToList();
            if (usable.Count == 0) return PickResult.NoPin;

            bool onA, onB;
            var key = usable[0].A.FirstOrDefault(pin => pin != null)?.gameObject.GetInstanceID() ?? 0;
            if (exclude && !usable[0].OnA && !usable[0].OnB)
            {
                if (beforeUnpaint != null && beforeUnpaint.TryGetValue(key, out var wasB))
                {
                    onA = !wasB;
                    onB = wasB;
                    beforeUnpaint.Remove(key);
                }
                else
                {
                    onA = usable[0].A.Select(pin => GetOriginalStrandPinActive(pin, originals)).FirstOrDefault(v => v.HasValue) ?? true;
                    onB = usable[0].B.Select(pin => GetOriginalStrandPinActive(pin, originals)).FirstOrDefault(v => v.HasValue) ?? false;
                    if (!onA && !onB) onA = true;
                }
            }
            else if (exclude)
            {
                if (beforeUnpaint != null) beforeUnpaint[key] = usable[0].OnB;
                onA = false;
                onB = false;
            }
            else if (usable[0].OnB) { onA = true; onB = false; }
            else if (usable[0].OnA) { onA = false; onB = true; }
            else { onA = true; onB = false; }
            side = onA ? 0 : onB ? 1 : -1;

            var writes = new List<(GameObject go, bool on)>();
            foreach (var p in usable)
            {
                foreach (var pin in p.A) if (pin != null && pin.gameObject.activeSelf != onA) writes.Add((pin.gameObject, onA));
                foreach (var pin in p.B) if (pin != null && pin.gameObject.activeSelf != onB) writes.Add((pin.gameObject, onB));
            }
            return WritePins(writes);
        }

        static PickResult WritePins(List<(GameObject go, bool on)> writes)
        {
            if (writes.Count == 0) return PickResult.NoChange;
            Undo.RecordObjects(writes.Select(w => (Object)w.go).ToArray(), UndoLabel);
            foreach (var w in writes)
            {
                w.go.SetActive(w.on);
                RecordInstanceChange(w.go);
            }
            return PickResult.Changed;
        }

        // つながりが無ければ fallback（ウィンドウが最初に見たときの値）
        internal static bool? GetOriginalStrandPinActive(PinIslandSelector pin, IDictionary<int, bool> fallback)
        {
            if (pin == null) return null;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(pin.gameObject);
            if (src != null) return src.activeSelf;
            if (fallback != null && fallback.TryGetValue(pin.gameObject.GetInstanceID(), out var on)) return on;
            return null;
        }

        internal static int ResetStrandColors(StrandColorPins ps, IDictionary<int, bool> fallback)
        {
            if (ps == null) return 0;
            var writes = new List<(GameObject go, bool on)>();
            foreach (var pin in ps.AllPins)
            {
                var want = GetOriginalStrandPinActive(pin, fallback);
                if (want.HasValue && pin.gameObject.activeSelf != want.Value) writes.Add((pin.gameObject, want.Value));
            }
            if (writes.Count == 0) return 0;
            Undo.RecordObjects(writes.Select(w => (Object)w.go).ToArray(), UndoLabel);
            foreach (var w in writes)
            {
                w.go.SetActive(w.on);
                RecordInstanceChange(w.go);
            }
            return writes.Count;
        }

        // materials が null なら全部
        internal static List<Renderer> FindStrandPickRenderers(ColoringProduct target, StrandColorPins ps, out HashSet<Material> materials)
        {
            materials = null;
            var renderers = new List<Renderer>();
            if (target == null || target.Avatar == null || ps == null) return renderers;
            var decals = ps.AllDecals.Count > 0 ? (IEnumerable<SingleGradationDecal>)ps.AllDecals : ps.Decals;
            var manual = decals.Where(d => d != null && d.RendererSelector != null && d.RendererSelector.Mode == RendererSelectMode.Manual).ToList();
            if (manual.Count > 0)
            {
                foreach (var d in manual) renderers.AddRange(d.RendererSelector.ManualSelections.Where(r => r != null));
                return renderers.Distinct().ToList();
            }
            var filter = decals.Where(d => d != null && d.RendererSelector != null && d.RendererSelector.UseMaterialFilteringForAutoSelect).ToList();
            if (filter.Count > 0)
            {
                materials = new HashSet<Material>();
                foreach (var d in filter) foreach (var m in d.RendererSelector.AutoSelectFilterMaterials) if (m != null) materials.Add(m);
            }
            renderers.AddRange(target.Avatar.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy));
            return renderers;
        }
    }
}
#endif
#endif
