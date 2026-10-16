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

    public sealed class PinSides
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

    public static partial class CustomHairColorToolCore
    {
        public const string SelectorsNodeName = "_Selectors";

        // 目印の無い古い商品は null（毛束を選ぶ機能を出さない）
        // _Selectors の下なら深さは見ない（書き出し直した商品は _Selectors/_Custom/MeshA など）
        static PinSides DetectPinSides(Transform custom, ModeTarget mt, ModeInfo info)
        {
            if (mt.Decals.Count < 2) return null;
            var product = custom.parent;
            if (product == null) return null;
            var ps = new PinSides();
            var lists = new[] { new List<PinIslandSelector>(), new List<PinIslandSelector>() };
            for (var c = 0; c < 2; c++)
            {
                var d = mt.Decals[c];
                var or = d != null ? d.IslandSelector as IslandSelectorOR : null;
                if (or == null || !UnderSelectors(or.transform, product)) return null;
                ps.Sides[c] = or;
                ps.Decals[c] = d;
                ps.AllDecals.Add(d);
                HairIslandPick.CollectPins(or.transform, lists[c]);
            }
            if (ps.Sides[0] == ps.Sides[1] || lists[0].Count == 0 || lists[1].Count == 0) return null;

            // _Cut 仲間の Decal が参照する目印。ここがオンの毛束はクリックで変えない
            var cut = new List<PinIslandSelector>();
            foreach (var kv in mt.Followers)
            {
                foreach (var f in kv.Value)
                {
                    if (f == null || f.IslandSelector == null) continue;
                    if (f.IslandSelector is IslandSelectorOR fo) { if (fo != ps.Sides[0] && fo != ps.Sides[1]) HairIslandPick.CollectPins(fo.transform, cut); }
                    else if (f.IslandSelector is PinIslandSelector fp) cut.Add(fp);
                }
            }
            ps.AllPins.AddRange(lists[0]);
            ps.AllPins.AddRange(lists[1]);
            ps.Pairs = HairIslandPick.PairPins(lists[0], lists[1], cut);
            ps.PresetsShare = PresetsSharePins(product, info, ps);
            return ps;
        }

        // 古い Prefab ではプリセットもここで書く目印を見る（画面の一言を出し分ける）
        // OR でなく目印で比べる（単色の All とグラデの All/Part_ のように、別の OR でも中の目印が同じことがある）
        static bool PresetsSharePins(Transform product, ModeInfo info, PinSides ps)
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
                HairIslandPick.CollectPins(or.transform, list);
                if (list.Any(mine.Contains)) return true;
            }
            return false;
        }

        // 1枚でも参照していなければ null（単色の Decal が目印を見ない古い Prefab）
        // グラデはパーツごとの Decal が別々の OR を見るため、目印は重ならないように集める
        static PinSides DetectPaintPins(Transform custom, ModeTarget mt, ModeInfo info)
        {
            if (mt == null || mt.Decals.Count == 0) return null;
            var product = custom.parent;
            if (product == null) return null;
            var ps = new PinSides { Kind = PinKind.Paint };
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
                HairIslandPick.CollectPins(or.transform, list);
                foreach (var p in list) if (seen.Add(p)) pins.Add(p);
            }
            if (pins.Count == 0) return null;
            ps.Sides[0] = ps.Groups[0];
            ps.Decals[0] = ps.AllDecals[0];
            ps.AllPins.AddRange(pins);
            ps.Pairs = HairIslandPick.PairPins(pins, new List<PinIslandSelector>(), null);
            ps.PresetsShare = PresetsSharePins(product, info, ps);
            return ps;
        }

        // 再書き出しした商品は同じ、それより前に出荷した商品は違う
        static void LinkPaintPins(HairColorTarget target)
        {
            var a = target.Has(HairMode.Default) ? target.ModeTargets[HairMode.Default].Pins : null;
            var b = target.Has(HairMode.Gradation) ? target.ModeTargets[HairMode.Gradation].Pins : null;
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
        public static int[] CountPinSides(PinSides ps)
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
        public static PickResult ApplyPick(PinSides ps, IEnumerable<int> pairIndices, bool exclude, out int side, IDictionary<int, bool> originals = null, IDictionary<int, bool> beforeUnpaint = null)
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
                    onA = usable[0].A.Select(pin => OriginalPinActive(pin, originals)).FirstOrDefault(v => v.HasValue) ?? true;
                    onB = usable[0].B.Select(pin => OriginalPinActive(pin, originals)).FirstOrDefault(v => v.HasValue) ?? false;
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
        public static bool? OriginalPinActive(PinIslandSelector pin, IDictionary<int, bool> fallback)
        {
            if (pin == null) return null;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(pin.gameObject);
            if (src != null) return src.activeSelf;
            if (fallback != null && fallback.TryGetValue(pin.gameObject.GetInstanceID(), out var on)) return on;
            return null;
        }

        public static int ResetPins(PinSides ps, IDictionary<int, bool> fallback)
        {
            if (ps == null) return 0;
            var writes = new List<(GameObject go, bool on)>();
            foreach (var pin in ps.AllPins)
            {
                var want = OriginalPinActive(pin, fallback);
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
        public static List<Renderer> PickRenderers(HairColorTarget target, PinSides ps, out HashSet<Material> materials)
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

    public sealed partial class CustomHairColorToolWindow
    {
        [SerializeField] List<int> _pinOrigIds = new List<int>(); // Unpack した商品で「最初に戻す」に使う
        [SerializeField] List<bool> _pinOrigOn = new List<bool>();
        Dictionary<int, bool> _pinOrig;
        readonly Dictionary<int, bool> _pinBeforeUnpaint = new Dictionary<int, bool>();

        bool _picking;
        HairMode _pickMode;
        List<PickIsland> _pickIslands;
        PinSides _pickMatchedTo;
        int _pickSignature;
        readonly Dictionary<SceneView, bool> _pickMouseMoveBefore = new Dictionary<SceneView, bool>();
        int _pickHover = -1;
        string _pickMessage;
        double _pickMessageAt;
        [SerializeField] bool _pickHidTools; // スクリプトの読み直し後も戻せるよう保存する
        int[] _pickCounts = new int[4];
        // 選んでいる間は光を止める（毛束を選ぶたびに光の画像を作り直して重いため）
        // 側とモードは保存する（スクリプトの読み直しで終わっても戻せるように）
        [SerializeField] int _pickGlowSide = -1;
        [SerializeField] int _pickGlowMode;
        [System.NonSerialized] HairColorTarget _pickGlowTarget;
        bool _pickDiffers;
        GUIStyle _sceneLabelStyle;
        PickAction _pickAction = PickAction.Swap;

        const double PickMessageSeconds = 4.0;

        PinSides CurrentPins => _target != null && _target.IsValid && _target.Has(_mode) ? _target.ModeTargets[_mode].Pins : null;

        bool PaintPick => CurrentPins != null && CurrentPins.Kind == PinKind.Paint;

        bool PickAvailable => _picking && _target != null && _target.IsValid && _customOn && _mode == _pickMode && CurrentPins != null;

        Dictionary<int, bool> PinOriginals
        {
            get
            {
                if (_pinOrig != null) return _pinOrig;
                _pinOrig = new Dictionary<int, bool>();
                for (var i = 0; i < Mathf.Min(_pinOrigIds.Count, _pinOrigOn.Count); i++) _pinOrig[_pinOrigIds[i]] = _pinOrigOn[i];
                return _pinOrig;
            }
        }

        // つながりの無い目印は最初に見たときの値を覚える（ウィンドウの中だけ）
        void RefreshPick()
        {
            if (!_picking && _pickHidTools) { Tools.hidden = false; _pickHidTools = false; } // 選んでいる途中でスクリプトが読み直された
            if (!_picking && _pickGlowSide >= 0) ResumePickGlow();
            _pickCounts = new int[4];
            _pickDiffers = false;
            if (_target == null || !_target.IsValid) return;
            foreach (var mt in _target.ModeTargets.Values)
            {
                if (mt.Pins == null) continue;
                foreach (var pin in mt.Pins.AllPins)
                {
                    if (pin == null || PrefabUtility.GetCorrespondingObjectFromSource(pin.gameObject) != null) continue;
                    var id = pin.gameObject.GetInstanceID();
                    if (PinOriginals.ContainsKey(id)) continue;
                    PinOriginals[id] = pin.gameObject.activeSelf;
                    _pinOrigIds.Add(id);
                    _pinOrigOn.Add(pin.gameObject.activeSelf);
                }
            }
            var ps = CurrentPins;
            if (ps == null) return;
            _pickCounts = CustomHairColorToolCore.CountPinSides(ps);
            foreach (var pin in ps.AllPins)
            {
                var want = CustomHairColorToolCore.OriginalPinActive(pin, PinOriginals);
                if (want.HasValue && pin.gameObject.activeSelf != want.Value) { _pickDiffers = true; break; }
            }
        }

        // オフだと TTT はその Decal で何も塗らない（単色も目印で塗る毛束を決めるため単色にも効く）
        GameObject SelectorOffNode()
        {
            if (_target == null || !_target.IsValid || !_customOn || !_target.Has(_mode)) return null;
            var product = CustomHairColorToolCore.ProductRoot(_target);
            if (product == null || !product.activeInHierarchy) return null; // 商品ごとオフのときは別の注意（_inactiveNode）
            foreach (var d in _target.ModeTargets[_mode].Decals)
            {
                var sel = d != null ? d.IslandSelector : null;
                if (sel == null || sel.gameObject.activeInHierarchy) continue;
                GameObject top = null;
                for (var t = sel.transform; t != null && t != product.transform; t = t.parent) if (!t.gameObject.activeSelf) top = t.gameObject;
                if (top != null) return top;
            }
            return null;
        }

        string FreshPickMessage => !string.IsNullOrEmpty(_pickMessage) && EditorApplication.timeSinceStartup - _pickMessageAt < PickMessageSeconds ? _pickMessage : null;

        void StartPicking()
        {
            var ps = CurrentPins;
            if (ps == null) return;
            _pickMode = _mode;
            _pickIslands = null;
            _pickMatchedTo = null;
            _pickHover = -1;
            if (!EnsurePickIslands() || _pickIslands.Count == 0)
            {
                SetPickMessage("Scene でクリックできる髪が見つかりません（髪の Renderer がオフになっていないか確かめてください）。");
                return;
            }
            if (!_picking)
            {
                SceneView.duringSceneGui -= OnPickSceneGUI;
                SceneView.duringSceneGui += OnPickSceneGUI;
                EditorApplication.playModeStateChanged -= OnPickPlayModeChanged;
                EditorApplication.playModeStateChanged += OnPickPlayModeChanged; // ドメインを読み直さない設定でも Play に入ったら終える
                if (!Tools.hidden) { Tools.hidden = true; _pickHidTools = true; } // ハンドルにクリックを取られないため（終えたら戻す）
                PausePickGlow();
            }
            _picking = true;
            SetPickMessage(null);
            SceneView.RepaintAll();
            Repaint();
        }

        void StopPicking()
        {
            if (_picking)
            {
                SceneView.duringSceneGui -= OnPickSceneGUI;
                EditorApplication.playModeStateChanged -= OnPickPlayModeChanged;
                if (_pickHidTools) { Tools.hidden = false; _pickHidTools = false; }
            }
            ResumePickGlow();
            foreach (var kv in _pickMouseMoveBefore) if (kv.Key != null) kv.Key.wantsMouseMove = kv.Value;
            _pickMouseMoveBefore.Clear();
            _picking = false;
            _pickHover = -1;
            _pickIslands = null;
            _pickMatchedTo = null;
            SceneView.RepaintAll();
            Repaint();
        }

        void PausePickGlow()
        {
            if (_target == null || !_target.IsValid || !_target.Has(_mode)) return;
            var side = CustomHairColorToolCore.ReadGlowSide(_target.ModeTargets[_mode].Glow);
            if (side < 0) return;
            _pickGlowSide = side;
            _pickGlowMode = (int)_mode;
            _pickGlowTarget = _target;
            CustomHairColorToolCore.SetGlowSide(_target, _mode, -1);
            QueueRefresh(); // Refresh の中から呼ばれることがあるため
        }

        // 選んでいる間に光らせる側を自分で選んだら戻さない
        void ResumePickGlow()
        {
            if (_pickGlowSide < 0) return;
            var side = _pickGlowSide;
            var mode = (HairMode)_pickGlowMode;
            var target = _pickGlowTarget != null && _pickGlowTarget.IsValid ? _pickGlowTarget : _target;
            _pickGlowSide = -1;
            _pickGlowTarget = null;
            if (target == null || !target.IsValid || !target.Has(mode)) return;
            var g = target.ModeTargets[mode].Glow;
            if (g == null || CustomHairColorToolCore.ReadGlowSide(g) >= 0) return;
            CustomHairColorToolCore.SetGlowSide(target, mode, side);
            QueueRefresh();
        }

        void SetPickMessage(string msg)
        {
            _pickMessage = msg;
            _pickMessageAt = EditorApplication.timeSinceStartup;
        }

        bool EnsurePickIslands()
        {
            var ps = CurrentPins;
            if (ps == null) return false;
            var renderers = CustomHairColorToolCore.PickRenderers(_target, ps, out var materials);
            var sig = PickSignature(renderers, _pickIslands);
            if (_pickIslands == null || sig != _pickSignature)
            {
                _pickIslands = HairIslandPick.BuildIslands(renderers, materials);
                _pickMatchedTo = null;
                _pickHover = -1;
                sig = PickSignature(renderers, _pickIslands);
            }
            _pickSignature = sig;
            if (_pickMatchedTo != ps)
            {
                HairIslandPick.RefreshPoints(ps.Pairs); // 組の位置は Refresh 時点の値のため、毛束と同じ時点で取り直す
                HairIslandPick.MatchPairs(_pickIslands, ps.Pairs);
                _pickMatchedTo = ps;
            }
            return true;
        }

        static int PickSignature(List<Renderer> renderers, List<PickIsland> islands)
        {
            unchecked
            {
                var h = 17;
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    h = h * 31 + r.GetInstanceID();
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                    h = h * 31 + (mesh != null ? mesh.GetInstanceID() : 0);
                    foreach (var m in r.sharedMaterials) h = h * 31 + (m != null ? m.GetInstanceID() : 0);
                }
                if (islands == null) return h;
                foreach (var r in islands.Select(i => i.Renderer).Distinct())
                {
                    if (r == null) continue;
                    h = h * 31 + r.transform.localToWorldMatrix.GetHashCode();
                    if (!(r is SkinnedMeshRenderer s)) continue;
                    if (s.rootBone != null) h = h * 31 + s.rootBone.localToWorldMatrix.GetHashCode();
                    foreach (var b in s.bones) if (b != null) h = h * 31 + b.localToWorldMatrix.GetHashCode();
                    var n = s.sharedMesh != null ? s.sharedMesh.blendShapeCount : 0;
                    for (var i = 0; i < n; i++) h = h * 31 + s.GetBlendShapeWeight(i).GetHashCode();
                }
                return h;
            }
        }

        void OnPickPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) StopPicking();
        }

        // Scene のクリックとバッチの確かめの両方から呼ぶ。1クリック＝Undo 1回
        PickResult PickAt(Ray ray, PickAction action)
        {
            if (!EnsurePickIslands()) return PickResult.NoPin;
            var i = HairIslandPick.Raycast(_pickIslands, ray, out _);
            if (i < 0) return PickResult.NoChange;
            return PickIslandAt(i, action == PickAction.Unpaint);
        }

        // Alt＋クリックは何もしない（Scene の回転に譲る。Use もしない）
        bool TryPickClick(Event e, Ray ray)
        {
            if (e == null || e.type != EventType.MouseDown || e.button != 0) return false;
            if (e.alt) return false;
            PickAt(ray, _pickAction);
            try { e.Use(); } catch (System.Exception) { } // バッチの確かめで作った Event でも落ちないように
            return true;
        }

        void SetPickAction(PickAction a)
        {
            if (_pickAction == a) return;
            _pickAction = a;
            SaveUiPrefs();
            SceneView.RepaintAll();
        }

        PickResult PickIslandAt(int islandIndex, bool exclude)
        {
            // Pairs の添字は _pickMatchedTo（EnsurePickIslands のあとの今の PinSides）
            var ps = _pickMatchedTo;
            if (ps == null || ps != CurrentPins || _pickIslands == null || islandIndex < 0 || islandIndex >= _pickIslands.Count) return PickResult.NoPin;
            var isl = _pickIslands[islandIndex];
            var name = isl.Pairs.Count > 0 ? ps.Pairs[isl.Pairs[0]].Name : isl.Renderer != null ? isl.Renderer.name : "";

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            var result = CustomHairColorToolCore.ApplyPick(ps, isl.Pairs, exclude, out var side, PinOriginals, _pinBeforeUnpaint);
            Undo.CollapseUndoOperations(group);

            switch (result)
            {
                case PickResult.Cut:
                    SetPickMessage(name + "：この毛束は真ん中で分かれる毛束なので、ここでは変えられません。");
                    break;
                case PickResult.NoPin:
                    SetPickMessage("この毛束の目印が見つかりません（ポーズ・シェイプキーを初期に戻してから、もう一度「シーン上で毛束を選ぶ」を押してください）。");
                    break;
                case PickResult.NoChange:
                    SetPickMessage(name + "：もう" + SideText(side) + "になっています。");
                    break;
                default:
                    SetPickMessage(name + "：" + (ps.Kind == PinKind.Paint && side == 0 ? "塗るに戻しました" : SideText(side) + "にしました") + "（Ctrl+Z で戻せます）。");
                    _strandNoticePending = ps.Kind == PinKind.Paint ? StrandTabLabel(_mode) + "で選んだ毛束" : Modes.Get(_mode).Label + "の分け方";
                    break;
            }
            SceneView.RepaintAll();
            Refresh();
            Repaint();
            return result;
        }

        string SideText(int side) => PaintPick ? (side == 0 ? "塗る" : "デフォルトの髪") : side == 0 ? PartLabel(_mode, 0) : side == 1 ? PartLabel(_mode, 1) : "デフォルトの髪";

        void ResetPick()
        {
            var ps = CurrentPins;
            if (ps == null) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            var n = CustomHairColorToolCore.ResetPins(ps, PinOriginals);
            Undo.CollapseUndoOperations(group);
            _pinBeforeUnpaint.Clear();
            _strandNoticePending = null;
            SetPickMessage(n > 0 ? StrandTabLabel(_mode) + "前に戻しました（" + n + " か所）。" : StrandTabLabel(_mode) + "前のままです。");
            SceneView.RepaintAll();
            Refresh();
            Repaint();
        }

        string HoverText()
        {
            var ps = _pickMatchedTo;
            if (ps == null || ps != CurrentPins || _pickIslands == null || _pickHover < 0 || _pickHover >= _pickIslands.Count) return null;
            var isl = _pickIslands[_pickHover];
            if (isl.Pairs.Count == 0) return (isl.Renderer != null ? isl.Renderer.name : "") + "（この毛束の目印が見つかりません。ポーズ・シェイプキーを初期に戻してください）";
            var p = ps.Pairs[isl.Pairs[0]];
            if (isl.Pairs.Any(i => ps.Pairs[i].IsCut)) return p.Name + "（真ん中で分かれる毛束。変えられません）";
            if (ps.Kind == PinKind.Paint) return p.Name + (p.OnA ? "（いま 塗る → クリックでデフォルトの髪）" : "（いま デフォルトの髪 → クリックで塗る）");
            var now = p.OnB ? 1 : p.OnA ? 0 : -1;
            if (_pickAction == PickAction.Unpaint) return p.Name + (now < 0 ? "（いま デフォルトの髪 → クリックで前の色に戻す）" : "（いま " + SideText(now) + " → クリックでデフォルトの髪）");
            var next = now == 0 ? 1 : 0;
            return p.Name + "（いま " + SideText(now) + " → クリックで " + SideText(next) + "）";
        }

        GUIStyle SceneLabelStyle
        {
            get
            {
                if (_sceneLabelStyle == null) _sceneLabelStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
                return _sceneLabelStyle;
            }
        }

        void OnPickSceneGUI(SceneView sv)
        {
            if (this == null) { SceneView.duringSceneGui -= OnPickSceneGUI; return; }
            if (!PickAvailable) { StopPicking(); RequestUiSync(); return; }
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); StopPicking(); RequestUiSync(); return; }

            if (!_pickMouseMoveBefore.ContainsKey(sv)) _pickMouseMoveBefore[sv] = sv.wantsMouseMove;
            sv.wantsMouseMove = true; // カーソルを動かすだけで MouseMove を届けるため（終えたら戻す）
            var controlId = GUIUtility.GetControlID(FocusType.Passive); // 毎イベント取る（イベントごとに数がずれないため）
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId); // Scene の選択を取らせない

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                if (EnsurePickIslands())
                {
                    var over = HairIslandPick.Raycast(_pickIslands, HandleUtility.GUIPointToWorldRay(e.mousePosition), out _);
                    if (over != _pickHover) { _pickHover = over; sv.Repaint(); }
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0) TryPickClick(e, HandleUtility.GUIPointToWorldRay(e.mousePosition));

            if (e.type == EventType.Repaint && _pickIslands != null && _pickHover >= 0 && _pickHover < _pickIslands.Count)
                HairIslandPick.DrawIsland(_pickIslands[_pickHover], new Color(1f, 0.9f, 0f, 0.8f), sv.camera.transform.position);

            Handles.BeginGUI();
            var y = 10f;
            void Line(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                var size = SceneLabelStyle.CalcSize(new GUIContent(s));
                GUI.Label(new Rect(10f, y, Mathf.Min(size.x + 8f, sv.position.width - 20f), 24f), s, SceneLabelStyle);
                y += 28f;
            }
            if (PaintPick)
            {
                Line(StrandTabLabel(_mode) + "：髪をクリックして、塗らない毛束（デフォルトの髪）を選択中（Esc で完了）");
                Line("クリック＝塗る ⇄ デフォルトの髪 を切り替える");
            }
            else
            {
                Line(StrandTabLabel(_mode) + "：髪をクリックして分け方を変更中（Esc で完了）");
                Line(_pickAction == PickAction.Unpaint
                    ? "クリック＝デフォルトの髪にする（もう一度クリックで戻す）"
                    : "クリック＝" + PartLabel(_mode, 0) + " ⇄ " + PartLabel(_mode, 1) + " を入れ替える");
            }
            Line(HoverText());
            if (!string.IsNullOrEmpty(_pickMessage) && EditorApplication.timeSinceStartup - _pickMessageAt < PickMessageSeconds) Line(_pickMessage);
            Handles.EndGUI();
        }
    }
}
#endif
#endif
