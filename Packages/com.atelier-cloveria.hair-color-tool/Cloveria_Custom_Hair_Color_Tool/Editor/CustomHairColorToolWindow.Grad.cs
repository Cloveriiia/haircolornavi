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
    public sealed partial class CustomHairColorToolWindow
    {
        PerColorGradientParts CurrentGrad => _target != null && _target.IsValid && _target.Has(_mode) ? _target.ModeParts[_mode].Grad : null;

        bool ColorGradOn => !IsGradientMode && HairColorCore.IsPerColorGradientOn(CurrentGrad, _partIndex);

        bool IsColorGradOnFor(ColoringMode mode, int color)
            => _target != null && _target.Has(mode) && HairColorCore.IsPerColorGradientOn(_target.ModeParts[mode].Grad, color);

        static string StopLabel(int stop) => stop == 0 ? "根元" : stop == 1 ? "真ん中" : "毛先";

        void SyncColorGrad(ColoringMode mode, int color)
        {
            var g = _target.ModeParts[mode].Grad;
            if (!HairColorCore.IsPerColorGradientOn(g, color)) return;
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
            foreach (var kv in _target.ModeParts)
            {
                var mt = kv.Value;
                if (ColoringModes.Get(kv.Key).IsGradient) foreach (var d in mt.Decals) ReadPartShape(d);
                if (mt.Grad == null) continue;
                foreach (var list in mt.Grad.Decals) foreach (var d in list) ReadPartShape(d);
            }
            var key = _target.Custom.GetInstanceID() + ":" + (int)_mode + ":" + GradColor;
            if (key != _gradPartKey) { _gradPartKey = key; _gradPartIndex = -1; }
            if (_gradPartIndex >= GradDecals.Count) _gradPartIndex = -1;
        }

        void ReadPartShape(SingleGradationDecal d)
        {
            if (d != null) _partShapes[d] = HairColorCore.GetDecalGradient(d);
        }

        List<SingleGradationDecal> GradDecalsOf(ColoringMode mode, int color)
        {
            if (_target == null || !_target.IsValid || !_target.Has(mode)) return new List<SingleGradationDecal>();
            var mt = _target.ModeParts[mode];
            if (ColoringModes.Get(mode).IsGradient) return mt.Decals;
            return mt.Grad != null && color >= 0 && color < mt.Grad.ColorCount ? mt.Grad.Decals[color] : new List<SingleGradationDecal>();
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
            Undo.RecordObject(this, HairColorCore.UndoLabel);
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

        GradientShape DefaultSplits(ColoringMode mode, int color, SingleGradationDecal d)
        {
            var g = ColoringModes.Get(mode).IsGradient || !_target.Has(mode) ? null : _target.ModeParts[mode].Grad;
            return g != null && d != null && g.Suggested.TryGetValue(d.name, out var s) ? s : EvenSplits;
        }

        void SetDefaultSplits(ColoringMode mode, int color, GradState g)
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
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
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
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
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
                foreach (var d in g.Decals[c]) HairColorCore.SetDecalBlendMode(d, st.BlendKey);
                HairColorCore.SetDecalsGradientKeepSplits(g.Decals[c], ShapeOf(_mode, c), d => PartShape(d, gs));
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
                    HairColorCore.SetDecalColor(d, st.Color, st.Clear);
                    HairColorCore.SetDecalBlendMode(d, st.BlendKey);
                }
            }
            SyncGlowColorToHair(_mode, c);
            SceneView.RepaintAll();
            Refresh();
        }

        void SetColorGradNodes(ColoringMode mode, int color, bool on)
        {
            var g = _target.ModeParts[mode].Grad;
            var lead = _target.Decal(mode, color);
            if (g == null || lead == null) return;
            HairColorCore.SetActiveWithUndo(lead.gameObject, !on);
            HairColorCore.SetActiveWithUndo(g.Wrappers[color], on);
            HairColorCore.SetColoringMode(_target, mode); // _Grad の親のオン・オフもそろえるため
        }
    }
}
#endif
#endif
