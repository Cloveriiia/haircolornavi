#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System.Collections.Generic;
using System.Linq;
using net.rs64.TexTransTool.Decal;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    // メッシュ・ツートンの色ごとのグラデ。Prefab：Custom/_Grad/<モード>/<色の Decal の名前>/ の下にパーツごとの Decal
    // グラデにする＝単色の Decal をオフにして包みをオン
    public sealed class ColorGradTarget
    {
        public GameObject Root;
        public GameObject Node;
        public readonly GameObject[] Wrappers = new GameObject[2];
        public readonly List<SingleGradationDecal>[] Decals = { new List<SingleGradationDecal>(), new List<SingleGradationDecal>() };
        // パーツ名→おすすめの色が変わる位置（無ければ均等）
        public Dictionary<string, GradientShape> Suggested = new Dictionary<string, GradientShape>(System.StringComparer.OrdinalIgnoreCase);
    }

    public static partial class CustomHairColorToolCore
    {
        public const string GradNodeName = "_Grad";

        // 古い構造の商品では null（色ごとのグラデを出さない）
        static ColorGradTarget DetectColorGrad(Transform custom, ModeTarget mt)
        {
            if (mt.Decals.Count < 2) return null;
            var root = FindChild(custom, GradNodeName);
            if (root == null) return null;
            var node = FindChild(root, mt.Node.name);
            if (node == null) return null;
            var t = new ColorGradTarget { Root = root.gameObject, Node = node.gameObject };
            for (var c = 0; c < 2; c++)
            {
                var lead = mt.Decals[c];
                // 単色の Decal がモードのノードに付いている商品は対象外（オフにするとモードごと消えるため）
                if (lead == null || lead.gameObject == mt.Node) return null;
                var w = FindChild(node, lead.name);
                if (w == null || w.GetComponent<SingleGradationDecal>() != null) return null;
                var decals = CollectDecals(w);
                if (decals.Count == 0) return null;
                t.Wrappers[c] = w.gameObject;
                t.Decals[c].AddRange(decals);
            }
            t.Suggested = ReadSuggestedSplits(custom);
            return t;
        }

        // 商品の色はパーツごとに位置を変えてある（同じ割合だと短いパーツがすぐ毛先の色になり、つなぎ目で段になるため）
        // 位置はどの色も同じため先頭の色から読む
        static Dictionary<string, GradientShape> ReadSuggestedSplits(Transform custom)
        {
            var map = new Dictionary<string, GradientShape>(System.StringComparer.OrdinalIgnoreCase);
            var product = custom.parent;
            if (product == null) return map;
            var group = Modes.Get(HairMode.Gradation).NodeNames.Select(n => FindChild(product, n)).FirstOrDefault(n => n != null);
            if (group == null) return map;
            for (var i = 0; i < group.childCount; i++)
            {
                var decals = CollectDecals(group.GetChild(i));
                if (decals.Count == 0) continue;
                foreach (var d in decals) map[d.name] = ReadGradient(d);
                break;
            }
            return map;
        }

        // 包みのオン・オフで見る（ウィンドウには覚えない）
        public static bool IsColorGradOn(ColorGradTarget g, int color)
            => g != null && color >= 0 && color < 2 && g.Wrappers[color] != null && g.Wrappers[color].activeSelf;
    }

    public sealed partial class CustomHairColorToolWindow
    {
        ColorGradTarget CurrentGrad => _target != null && _target.IsValid && _target.Has(_mode) ? _target.ModeTargets[_mode].Grad : null;

        bool ColorGradOn => !IsGradientMode && CustomHairColorToolCore.IsColorGradOn(CurrentGrad, _partIndex);

        bool IsColorGradOnFor(HairMode mode, int color)
            => _target != null && _target.Has(mode) && CustomHairColorToolCore.IsColorGradOn(_target.ModeTargets[mode].Grad, color);

        static string StopLabel(int stop) => stop == 0 ? "根元" : stop == 1 ? "真ん中" : "毛先";

        void SyncColorGrad(HairMode mode, int color)
        {
            var g = _target.ModeTargets[mode].Grad;
            if (!CustomHairColorToolCore.IsColorGradOn(g, color)) return;
            SyncGradientFrom(g.Decals[color], mode, color);
            var root = GradStop(mode, color, 0);
            var st = State(mode, color);
            st.Color = root.Color;
            st.Hue = root.Hue;
            st.BlendKey = root.BlendKey;
            st.Clear = root.Clear;
        }

        // ---- パーツごとの色が変わる位置 ----
        // 位置はウィンドウに保存せず Refresh で Decal ごとに読む（Undo のあとは読み直すため）
        // GradState の Split 系は先頭の Decal の値
        readonly Dictionary<SingleGradationDecal, GradientShape> _partShapes = new Dictionary<SingleGradationDecal, GradientShape>();
        int _gradPartIndex = -1;   // −1＝全部
        string _gradPartKey;

        const float SplitSame = 0.005f;

        void RefreshPartShapes()
        {
            _partShapes.Clear();
            foreach (var kv in _target.ModeTargets)
            {
                var mt = kv.Value;
                if (Modes.Get(kv.Key).IsGradient) foreach (var d in mt.Decals) ReadPartShape(d);
                if (mt.Grad == null) continue;
                for (var c = 0; c < 2; c++) foreach (var d in mt.Grad.Decals[c]) ReadPartShape(d);
            }
            var key = _target.Custom.GetInstanceID() + ":" + (int)_mode + ":" + GradColor;
            if (key != _gradPartKey) { _gradPartKey = key; _gradPartIndex = -1; }
            if (_gradPartIndex >= GradDecals.Count) _gradPartIndex = -1;
        }

        void ReadPartShape(SingleGradationDecal d)
        {
            if (d != null) _partShapes[d] = CustomHairColorToolCore.ReadGradient(d);
        }

        List<SingleGradationDecal> GradDecalsOf(HairMode mode, int color)
        {
            if (_target == null || !_target.IsValid || !_target.Has(mode)) return new List<SingleGradationDecal>();
            var mt = _target.ModeTargets[mode];
            if (Modes.Get(mode).IsGradient) return mt.Decals;
            return mt.Grad != null && color >= 0 && color < 2 ? mt.Grad.Decals[color] : new List<SingleGradationDecal>();
        }

        List<SingleGradationDecal> GradDecals => GradDecalsOf(_mode, GradColor);

        GradientShape PartShape(SingleGradationDecal d, GradState g)
        {
            if (d != null && _partShapes.TryGetValue(d, out var s) && s != null) return s;
            s = new GradientShape { ThreeColors = g.Three, Split = g.Split, Split1 = g.Split1, Split2 = g.Split2 };
            if (d != null) _partShapes[d] = s;
            return s;
        }

        SingleGradationDecal SelectedGradDecal
        {
            get
            {
                var decals = GradDecals;
                return _gradPartIndex >= 0 && _gradPartIndex < decals.Count ? decals[_gradPartIndex] : null;
            }
        }

        GradientShape ShownSplits()
        {
            var g = Grad(_mode, GradColor);
            var sel = SelectedGradDecal;
            if (sel != null) return PartShape(sel, g);
            var decals = GradDecals;
            return decals.Count > 0 ? PartShape(decals[0], g) : PartShape(null, g);
        }

        // 「全部」でパーツごとに位置が違う（印の%は「—」）
        bool GradMixed
        {
            get
            {
                if (SelectedGradDecal != null) return false;
                var g = Grad(_mode, GradColor);
                var decals = GradDecals;
                if (decals.Count < 2) return false;
                var a = PartShape(decals[0], g);
                foreach (var d in decals)
                {
                    var s = PartShape(d, g);
                    if (g.Three ? Mathf.Abs(s.Split1 - a.Split1) > SplitSame || Mathf.Abs(s.Split2 - a.Split2) > SplitSame : Mathf.Abs(s.Split - a.Split) > SplitSame) return true;
                }
                return false;
            }
        }

        bool GradEditing => IsGradientMode || ColorGradOn;

        bool GradHeadAvailable => _target != null && _target.IsValid && (IsGradientMode || (CurrentGrad != null && _partIndex <= 1));

        void SelectGradPart(int index)
        {
            var n = GradDecals.Count;
            _gradPartIndex = index >= 0 && index < n ? index : -1;
        }

        const float SplitGap = 0.02f;
        const float SplitMin2 = 0.05f, SplitMax2 = 0.95f;
        const float SplitMin3 = 0.02f, SplitMax3 = 0.98f;

        float SplitMark(int mark)
        {
            var s = ShownSplits();
            if (!Grad(_mode, GradColor).Three) return s.Split;
            return mark == 0 ? s.Split1 : s.Split2;
        }

        // 位置が変わらなければ書かない（押して離しただけで Undo を作らないため）
        void DragSplitTo(int mark, float t)
        {
            if (_target == null || !_target.IsValid || !_customOn || !GradEditing) return;
            var g = Grad(_mode, GradColor);
            var s = ShownSplits();
            var mixed = GradMixed;
            if (g.Three)
            {
                float a = s.Split1, b = s.Split2;
                if (mark == 0) a = Mathf.Min(Mathf.Clamp(t, SplitMin3, SplitMax3 - SplitGap), b - SplitGap);
                else b = Mathf.Max(Mathf.Clamp(t, SplitMin3 + SplitGap, SplitMax3), a + SplitGap);
                if (!mixed && Mathf.Abs(a - s.Split1) < 0.001f && Mathf.Abs(b - s.Split2) < 0.001f) return;
                WriteSplits(0f, a, b);
            }
            else
            {
                var v = Mathf.Clamp(t, SplitMin2, SplitMax2);
                if (!mixed && Mathf.Abs(v - s.Split) < 0.001f) return;
                WriteSplits(v, 0f, 0f);
            }
        }

        // 離す合図が来ずにポインタを失ったときも呼ぶ
        void EndSplitDrag() => OnMouseReleased();

        void WriteSplits(float split, float split1, float split2)
        {
            BeginDragUndo();
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            var g = Grad(_mode, GradColor);
            var decals = GradDecals;
            var sel = SelectedGradDecal;
            void Set(GradientShape s)
            {
                s.ThreeColors = g.Three;
                if (g.Three) { s.Split1 = split1; s.Split2 = split2; }
                else s.Split = split;
            }
            if (sel != null)
            {
                Set(PartShape(sel, g));
                if (decals.Count > 0 && decals[0] == sel) { if (g.Three) { g.Split1 = split1; g.Split2 = split2; } else g.Split = split; }
                PushGradient(sel);
                return;
            }
            foreach (var d in decals) Set(PartShape(d, g));
            if (g.Three) { g.Split1 = split1; g.Split2 = split2; }
            else g.Split = split;
            PushGradient();
        }

        static readonly GradientShape EvenSplits = new GradientShape(); // 共有のため書き換えない

        GradientShape DefaultSplits(HairMode mode, int color, SingleGradationDecal d)
        {
            var g = Modes.Get(mode).IsGradient || !_target.Has(mode) ? null : _target.ModeTargets[mode].Grad;
            return g != null && d != null && g.Suggested.TryGetValue(d.name, out var s) ? s : EvenSplits;
        }

        void SetDefaultSplits(HairMode mode, int color, GradState g)
        {
            var decals = GradDecalsOf(mode, color);
            foreach (var d in decals)
            {
                var s = PartShape(d, g);
                var def = DefaultSplits(mode, color, d);
                s.ThreeColors = g.Three;
                s.Split = def.Split;
                s.Split1 = def.Split1;
                s.Split2 = def.Split2;
            }
            var first = decals.Count > 0 ? DefaultSplits(mode, color, decals[0]) : EvenSplits;
            g.Split = first.Split;
            g.Split1 = first.Split1;
            g.Split2 = first.Split2;
        }

        void ResetSplits()
        {
            if (_target == null || !_target.IsValid || !_customOn || !GradEditing) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            SetDefaultSplits(_mode, GradColor, Grad(_mode, GradColor));
            PushGradient();
            Refresh();
        }

        bool SplitsAtDefault
        {
            get
            {
                var g = Grad(_mode, GradColor);
                foreach (var d in GradDecals)
                {
                    var s = PartShape(d, g);
                    var def = DefaultSplits(_mode, GradColor, d);
                    if (g.Three ? Mathf.Abs(s.Split1 - def.Split1) > SplitSame || Mathf.Abs(s.Split2 - def.Split2) > SplitSame : Mathf.Abs(s.Split - def.Split) > SplitSame) return false;
                }
                return true;
            }
        }

        void ToggleColorGrad(bool on)
        {
            var g = CurrentGrad;
            var lead = CurrentDecal;
            if (g == null || lead == null) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            var c = _partIndex;
            var st = State(_mode, c);
            if (on)
            {
                var gs = Grad(_mode, c);
                gs.Three = false;
                SetDefaultSplits(_mode, c, gs);
                gs.MiddleKnown = false;
                for (var i = 0; i < 3; i++)
                {
                    var s = gs.Stop(i);
                    s.Color = st.Color;
                    s.Hue = st.Hue;
                    s.Tone = st.Tone;
                    s.BlendKey = st.BlendKey;
                    s.Clear = st.Clear;
                }
                _stopIndex = 0;
                SetColorGradNodes(_mode, c, true);
                foreach (var d in g.Decals[c]) CustomHairColorToolCore.ApplyBlendKey(d, st.BlendKey);
                CustomHairColorToolCore.ApplyGradientPerPart(g.Decals[c], ShapeOf(_mode, c), d => PartShape(d, gs));
            }
            else
            {
                var root = GradStop(_mode, c, 0);
                st.Color = root.Color;
                st.Hue = root.Hue;
                st.Tone = root.Tone;
                st.BlendKey = root.BlendKey;
                st.Clear = root.Clear;
                _stopIndex = 0;
                SetColorGradNodes(_mode, c, false);
                foreach (var d in WithFollowers(lead))
                {
                    CustomHairColorToolCore.ApplyColor(d, st.Color, st.Clear);
                    CustomHairColorToolCore.ApplyBlendKey(d, st.BlendKey);
                }
            }
            SyncGlowColorToHair(_mode, c);
            SceneView.RepaintAll();
            Refresh();
        }

        void SetColorGradNodes(HairMode mode, int color, bool on)
        {
            var g = _target.ModeTargets[mode].Grad;
            var lead = _target.Decal(mode, color);
            if (g == null || lead == null) return;
            CustomHairColorToolCore.SetNodeActive(lead.gameObject, !on);
            CustomHairColorToolCore.SetNodeActive(g.Wrappers[color], on);
            CustomHairColorToolCore.SetMode(_target, mode); // _Grad の親のオン・オフもそろえるため
        }
    }
}
#endif
#endif
