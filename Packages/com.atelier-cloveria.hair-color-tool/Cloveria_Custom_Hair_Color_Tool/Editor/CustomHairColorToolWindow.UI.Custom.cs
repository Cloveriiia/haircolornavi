#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace AtelierCloveria.CustomHairColorTool
{
    // Custom がオフの間は最初の画面だけ（タブを見ただけでは何も書かない）
    public sealed partial class CustomHairColorToolWindow
    {
        VisualElement _gateCard, _customContent;
        Label _gateStart, _gateDefault, _gateContinue;
        VisualElement _modesHost, _partsHost;
        readonly List<KeyValuePair<ColoringMode, VisualElement>> _modeEls = new List<KeyValuePair<ColoringMode, VisualElement>>();
        readonly List<KeyValuePair<int, VisualElement>> _partEls = new List<KeyValuePair<int, VisualElement>>();
        readonly List<VisualElement> _partDots = new List<VisualElement>();
        VisualElement _subTabsRow, _colorCard, _panelColor, _panelGlow, _strandPanel;
        readonly List<KeyValuePair<SubTab, VisualElement>> _subTabEls = new List<KeyValuePair<SubTab, VisualElement>>();
        VisualElement _gradHead, _gradPartsBox;
        VisualElement _gradToggleRow, _gradSwitch, _gradBody, _gradBarFrame, _gradBar, _gradPartsRow;
        Label _gradToggleText, _gradCount2, _gradCount3, _gradPartsAllNote, _gradMixedNote, _gradEven;
        readonly VisualElement[] _gradMarks = new VisualElement[2];
        readonly Label[] _gradMarkPcts = new Label[2];
        int _dragMark = -1; // −1＝なし
        int _lastMark = 1; // 3色で印が近いとき、こちらの%だけ出す
        readonly VisualElement[] _gradStops = new VisualElement[3];
        readonly PaintBox[] _gradStopPaints = new PaintBox[3];
        readonly List<KeyValuePair<int, VisualElement>> _gradPartEls = new List<KeyValuePair<int, VisualElement>>();
        // 毛束調整（単色・グラデーション）も同じ部品
        Label _strandSwap, _strandUnpaint, _strandStart, _strandCut, _strandMessage, _strandReset;
        VisualElement _strandDotA, _strandDotB, _strandDotC, _strandDotA2, _strandActions;
        Label _strandArrow, _strandArrowC;
        readonly Label[] _strandCountNums = new Label[4];
        readonly Label[] _strandCountNames = new Label[4];
        readonly VisualElement[] _strandCountBoxes = new VisualElement[4];
        static readonly int[] StrandCountIndex = { 0, 1, 4, 2 };
        string _sigGate, _sigModes, _sigModeState, _sigParts, _sigPartState, _sigStrips, _sigSubTabs, _sigSubState, _sigGradParts, _sigGradBar;

        void ResetCustomSignatures()
        {
            _sigGate = _sigModes = _sigModeState = _sigParts = _sigPartState = _sigStrips = _sigSubTabs = _sigSubState = _sigGradParts = _sigGradBar = null;
        }

        static string StrandTabLabel(ColoringMode mode) => mode == ColoringMode.Default || ColoringModes.Get(mode).IsGradient ? UiText.StrandTabPaint : ColoringModes.Get(mode).Label + UiText.StrandSuffix;

        VisualElement BuildCustomTab()
        {
            var root = new VisualElement();
            root.AddToClassList("chct-custom");

            _gateCard = Card(null);
            _gateCard.AddToClassList("chct-gate");
            _gateContinue = MakeButton(UiText.GateContinue, null, OnGateContinueClicked);
            _gateCard.Add(_gateContinue);
            _gateStart = MakeButton("", "chct-btn--big", OnGateStartClicked);
            _gateCard.Add(_gateStart);
            _gateDefault = MakeButton(UiText.GateStartDefault, null, OnGateDefaultClicked);
            _gateCard.Add(_gateDefault);
            root.Add(_gateCard);

            _customContent = new VisualElement();
            root.Add(_customContent);

            var modeCard = Card(null);
            _modesHost = new VisualElement();
            modeCard.Add(_modesHost);
            _customContent.Add(modeCard);

            _subTabsRow = new VisualElement();
            _subTabsRow.AddToClassList("chct-subtabs");
            _customContent.Add(_subTabsRow);

            // 根元の色を押してすぐ下のリングで決められるよう、グラデの頭と色を1枚のカードに
            _colorCard = Card(null);
            _colorCard.AddToClassList("chct-grad");
            _partsHost = new VisualElement();
            _colorCard.Add(_partsHost);
            _gradHead = BuildGradHead();
            _colorCard.Add(_gradHead);
            _panelColor = AddImgui(_colorCard, DrawColorDecideIMGUI, "chct-color");
            _customContent.Add(_colorCard);
            _strandPanel = BuildStrandPanel();
            _customContent.Add(_strandPanel);
            _panelGlow = BuildGlowPanel();
            _customContent.Add(_panelGlow);

            _customContent.Add(MakeButton("「" + UiText.TabPick + "」に戻る", "chct-btn--warm", OnBackClicked));
            var keep = new VisualElement();
            keep.AddToClassList("chct-keep");
            var keepHead = new Label("作った色は残ります。");
            keepHead.AddToClassList("chct-keep__head");
            keepHead.AddToClassList(HeadingClass);
            keep.Add(keepHead);
            keep.Add(new Label("続きは「" + UiText.TabCustom + "」→「" + UiText.GateContinue + "」から編集できます。"));
            _customContent.Add(keep);
            return root;
        }

        bool SyncCustomTab()
        {
            var gate = !_customOn;
            var changed = false;
            if (_gateCard.style.display != (gate ? DisplayStyle.Flex : DisplayStyle.None)) changed = true;
            Show(_gateCard, gate);
            Show(_customContent, !gate);
            if (gate) return SyncGate() | changed;

            changed |= SyncModes();
            changed |= SyncParts();
            changed |= SyncSubTabs();
            var shown = ShownSubTab;
            Show(_colorCard, shown == SubTab.Color);
            Show(_strandPanel, shown == SubTab.Strand);
            Show(_panelGlow, shown == SubTab.Glow);
            if (shown == SubTab.Color) changed |= SyncGradPanel();
            if (shown == SubTab.Strand) SyncStrandPanel();
            if (shown == SubTab.Glow) SyncGlowPanel();
            return changed;
        }

        // ---- 最初の画面 ----
        // デフォルトの髪・2つ以上オンは null
        PresetColor GateTile()
        {
            var selected = _tiles.Where(t => t.Selected).ToList();
            return selected.Count == 1 ? selected[0] : null;
        }

        bool SyncGate()
        {
            var tile = GateTile();
            var copyable = tile != null && HairColorCore.ColoringModeOfPresetGroup(_target, tile.GroupName).HasValue;
            var baseText = copyable ? GroupShortLabel(tile.GroupName) + "「" + tile.Label + "」" + UiText.GateStartFrom : "";
            var sig = baseText + "|" + _customDiffers;
            if (sig == _sigGate) return false;
            _sigGate = sig;
            _gateContinue.text = _customDiffers ? UiText.GateContinue : UiText.GateContinue + UiText.GateContinueNone;
            _gateContinue.SetEnabled(_customDiffers);
            _gateContinue.EnableInClassList("chct-btn--cont", _customDiffers);
            _gateStart.text = baseText;
            Show(_gateStart, copyable);
            _gateDefault.EnableInClassList("chct-btn--big", !copyable);
            return true;
        }

        void OnGateStartClicked()
        {
            if (_target == null || !_target.IsValid || _customOn) return;
            StartCustomFrom(GateTile());
            SyncUI(false);
        }

        void OnGateDefaultClicked()
        {
            if (_target == null || !_target.IsValid || _customOn) return;
            StartCustomFromDefault();
            SyncUI(false);
        }

        void OnGateContinueClicked()
        {
            if (_target == null || !_target.IsValid || _customOn) return;
            ChooseCustom();
            SyncUI(false);
        }

        // ---- モード ----
        bool SyncModes()
        {
            var changed = false;
            var modes = _target.AvailableModes.ToList();
            var ms = string.Join(",", modes.Select(m => ((int)m).ToString()));
            if (ms != _sigModes)
            {
                _sigModes = ms;
                _sigModeState = null;
                BuildModes(modes);
                changed = true;
            }
            var mstate = ((int)_mode).ToString();
            if (mstate != _sigModeState)
            {
                _sigModeState = mstate;
                foreach (var kv in _modeEls) kv.Value.EnableInClassList("is-selected", kv.Key == _mode);
                changed = true;
            }
            return changed;
        }

        void BuildModes(List<ColoringMode> modes)
        {
            _modesHost.Clear();
            _modeEls.Clear();
            if (modes.Count == 0) return;
            var perRow = modes.Count >= 4 ? (modes.Count + 1) / 2 : modes.Count; // 最小幅でも文字が切れないよう4つ以上は2段
            var box = new VisualElement();
            box.AddToClassList("chct-pills-box");
            for (var start = 0; start < modes.Count; start += perRow)
            {
                var row = new VisualElement();
                row.AddToClassList("chct-pills");
                for (var k = start; k < Mathf.Min(modes.Count, start + perRow); k++)
                {
                    var m = modes[k];
                    var pill = new Label(ColoringModes.Get(m).Label);
                    pill.AddToClassList("chct-pill");
                    pill.AddToClassList(HeadingClass);
                    pill.AddManipulator(new Clickable(() => OnModeClicked(m)));
                    row.Add(pill);
                    _modeEls.Add(new KeyValuePair<ColoringMode, VisualElement>(m, pill));
                }
                box.Add(row);
            }
            _modesHost.Add(box);
        }

        void OnModeClicked(ColoringMode m)
        {
            if (_target == null || !_target.IsValid || !_customOn || _mode == m) return;
            SetMode(m);
            SyncUI(false);
        }

        // ---- 1色目／2色目 ----
        bool SyncParts()
        {
            var changed = false;
            var parts = IsGradientMode ? new List<int>() : VisibleParts(_mode);
            var ps = ((int)_mode) + ":" + string.Join(",", parts.Select(p => p.ToString()));
            if (ps != _sigParts)
            {
                _sigParts = ps;
                _sigPartState = null;
                _sigStrips = null;
                BuildParts(parts);
                changed = true;
            }
            var pstate = _partIndex.ToString();
            if (pstate != _sigPartState)
            {
                _sigPartState = pstate;
                foreach (var kv in _partEls) kv.Value.EnableInClassList("is-selected", kv.Key == _partIndex);
                changed = true;
            }
            // 色が変わったときだけ描き直す（リングをドラッグしている間も追いつく）
            var cs = StripSignature(parts);
            if (cs != _sigStrips)
            {
                _sigStrips = cs;
                foreach (var d in _partDots) d.MarkDirtyRepaint();
                foreach (var kv in _partEls)
                {
                    var clear = PartClear(_mode, kv.Key);
                    var dot = kv.Value.Q<VisualElement>(className: "chct-dot");
                    dot?.EnableInClassList("chct-dot--clear", clear);
                    kv.Value.tooltip = clear ? NoneSwatch.Label + "（" + NoneSwatch.Hint + "）" : "";
                }
            }
            return changed;
        }

        void BuildParts(List<int> parts)
        {
            _partsHost.Clear();
            _partEls.Clear();
            _partDots.Clear();
            Show(_partsHost, parts.Count >= 2);
            if (parts.Count < 2) return;
            var row = new VisualElement();
            row.AddToClassList("chct-colors");
            var mode = _mode;
            foreach (var i in parts)
            {
                var idx = i;
                var item = new VisualElement();
                item.AddToClassList("chct-color");
                var dot = new VisualElement();
                dot.AddToClassList("chct-dot");
                var paint = new PaintBox { Painter = (r, add) => PaintPart(mode, idx, r, add) };
                paint.AddToClassList("chct-fill");
                dot.Add(paint);
                item.Add(dot);
                _partDots.Add(paint);
                var name = new Label(PartLabel(mode, idx));
                name.AddToClassList("chct-color__name");
                name.AddToClassList(HeadingClass);
                item.Add(name);
                item.AddManipulator(new Clickable(() => OnPartClicked(idx)));
                _partEls.Add(new KeyValuePair<int, VisualElement>(idx, item));
                row.Add(item);
            }
            _partsHost.Add(row);
        }

        void OnPartClicked(int index)
        {
            if (_target == null || !_target.IsValid || !_customOn || _partIndex == index) return;
            SetPart(index);
            SyncUI(false);
        }

        // メッシュ調整の「デフォルトの髪にする」の丸と同じ色
        static readonly Color OriginalHairColor = new Color32(0xC9, 0xA2, 0x7A, 0xFF);

        // PaintBox は a を 1 にして描くため、ここで混ぜる
        static Color OverHair(Color c)
        {
            var o = Color.Lerp(OriginalHairColor, c, c.a);
            o.a = 1f;
            return o;
        }

        static Gradient PreviewGradient(GradientShape s) => new Gradient
        {
            colorKeys = HairColorCore.BuildGradientColorKeys(s),
            alphaKeys = HairColorCore.BuildGradientAlphaKeys(s),
        };

        // 色ごとのグラデなら、出している所が全部無色
        bool PartClear(ColoringMode mode, int index)
        {
            if (!ColoringModes.Get(mode).IsGradient && IsColorGradOnFor(mode, index))
            {
                var s = ShapeOf(mode, index);
                return s.RootClear && s.TipClear && (!s.ThreeColors || s.MiddleClear);
            }
            return State(mode, index).Clear;
        }

        void PaintStop(int stop, Rect r, System.Action<Rect, Color> add)
        {
            var s = GradStop(_mode, GradColor, stop);
            if (s.Clear) NoneSwatch.Paint(r, add);
            else add(r, s.Color);
        }

        void PaintPart(ColoringMode mode, int index, Rect r, System.Action<Rect, Color> add)
        {
            if (!ColoringModes.Get(mode).IsGradient && IsColorGradOnFor(mode, index))
            {
                var g = PreviewGradient(DotShape(mode, index));
                Slices(r, 24, i => OverHair(g.Evaluate((i + 0.5f) / 24f)), add);
                return;
            }
            var st = State(mode, index);
            if (st.Clear) NoneSwatch.Paint(r, add);
            else add(r, st.Color);
        }

        // 位置は「色が変わる位置」で選んでいるパーツ（全部なら先頭の Decal）
        GradientShape DotShape(ColoringMode mode, int color)
        {
            var s = ShapeOf(mode, color);
            var g = Grad(mode, color);
            var decals = GradDecalsOf(mode, color);
            var sel = mode == _mode && color == GradColor ? SelectedGradDecal : null;
            var d = sel ?? decals.FirstOrDefault(x => x != null);
            if (d == null) return s;
            var p = PartShape(d, g);
            s.Split = p.Split;
            s.Split1 = p.Split1;
            s.Split2 = p.Split2;
            return s;
        }

        string StripSignature(List<int> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                sb.Append(ColorUtility.ToHtmlStringRGB(State(_mode, p).Color)).Append(State(_mode, p).Clear ? "c" : "");
                if (!IsGradientMode && IsColorGradOnFor(_mode, p))
                {
                    var s = DotShape(_mode, p);
                    sb.Append('g').Append(s.ThreeColors).Append(ColorUtility.ToHtmlStringRGB(s.Root)).Append(ColorUtility.ToHtmlStringRGB(s.Middle))
                      .Append(ColorUtility.ToHtmlStringRGB(s.Tip)).Append(s.Split.ToString("F3")).Append(s.Split1.ToString("F3")).Append(s.Split2.ToString("F3"))
                      .Append(s.RootClear ? 'r' : '-').Append(s.MiddleClear ? 'm' : '-').Append(s.TipClear ? 't' : '-');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        // ---- 下のタブ ----
        // 古い Prefab の単色は「色」だけのため、タブの帯ごと隠れる
        List<SubTab> AvailableSubTabs()
        {
            var list = new List<SubTab> { SubTab.Color };
            if (_target == null || !_target.IsValid) return list;
            if (CurrentPins != null) list.Add(SubTab.Strand);
            if (CurrentGlow != null) list.Add(SubTab.Glow);
            return list;
        }

        // 覚えたタブが今のモードに無ければ「色」を出す（覚えた値は書き換えない）
        // EditorPrefs に「グラデ」（1）が残っている人もここで「色」に倒れる
        SubTab ShownSubTab => AvailableSubTabs().Contains(_subTab) ? _subTab : SubTab.Color;

        static string SubTabLabel(SubTab t, ColoringMode mode)
        {
            switch (t)
            {
                case SubTab.Strand: return StrandTabLabel(mode);
                case SubTab.Glow: return UiText.SubGlow;
                default: return UiText.SubColor;
            }
        }

        bool SyncSubTabs()
        {
            var changed = false;
            var list = AvailableSubTabs();
            var sig = (int)_mode + ":" + string.Join(",", list.Select(t => ((int)t).ToString()));
            if (sig != _sigSubTabs)
            {
                _sigSubTabs = sig;
                _sigSubState = null;
                _subTabsRow.Clear();
                _subTabEls.Clear();
                foreach (var t in list)
                {
                    var tab = t;
                    var el = new Label(SubTabLabel(t, _mode));
                    el.AddToClassList("chct-subtab");
                    el.AddToClassList(HeadingClass);
                    el.AddManipulator(new Clickable(() => OnSubTabClicked((int)tab)));
                    _subTabsRow.Add(el);
                    _subTabEls.Add(new KeyValuePair<SubTab, VisualElement>(t, el));
                }
                Show(_subTabsRow, list.Count >= 2);
                changed = true;
            }
            var shown = ShownSubTab;
            var st = ((int)shown).ToString();
            if (st != _sigSubState)
            {
                _sigSubState = st;
                foreach (var kv in _subTabEls) kv.Value.EnableInClassList("is-selected", kv.Key == shown);
                changed = true;
            }
            return changed;
        }

        void OnSubTabClicked(int tab)
        {
            var t = tab >= 0 && tab <= 3 ? (SubTab)tab : SubTab.Color;
            if (_subTab == t) return;
            _subTab = t;
            SaveUiPrefs();
            SyncUI(false);
        }

        // ---- グラデ ----
        VisualElement BuildGradHead()
        {
            var head = new VisualElement();
            head.AddToClassList("chct-grad-head");

            _gradToggleRow = Row("chct-switch-row");
            _gradToggleText = new Label("");
            _gradToggleText.AddToClassList("chct-switch-row__text");
            _gradToggleRow.Add(_gradToggleText);
            _gradSwitch = new VisualElement();
            _gradSwitch.AddToClassList("chct-switch");
            var knob = new VisualElement();
            knob.AddToClassList("chct-switch__knob");
            _gradSwitch.Add(knob);
            _gradToggleRow.Add(_gradSwitch);
            _gradToggleRow.AddManipulator(new Clickable(OnGradToggleClicked));
            head.Add(_gradToggleRow);

            _gradBody = new VisualElement();
            head.Add(_gradBody);

            // 「2色／3色」の並びと理由がつながるよう「根元から毛先へ」を添える
            var count = Row("chct-row--wrap");
            var lab = new Label("色の数");
            lab.AddToClassList("chct-lab");
            lab.AddToClassList("chct-lab--auto");
            count.Add(lab);
            _gradCount2 = Chip("2色", () => OnGradCountClicked(false));
            _gradCount3 = Chip("3色", () => OnGradCountClicked(true));
            count.Add(_gradCount2);
            count.Add(_gradCount3);
            _gradBody.Add(count);

            // 帯を見る前にどちらが根元か読めるよう上に置く
            var ends = Row("chct-bar-ends");
            ends.Add(Note("根元"));
            ends.Add(Note("毛先"));
            _gradBody.Add(ends);

            // 帯の中には線を引かない（位置は帯の下の印で見せて、印で動かす）
            var barFrame = new VisualElement();
            barFrame.AddToClassList("chct-bar");
            var bar = new PaintBox { Painter = PaintGradBar };
            bar.AddToClassList("chct-fill");
            barFrame.Add(bar);
            barFrame.RegisterCallback<PointerDownEvent>(OnGradBarPointerDown);
            _gradBarFrame = barFrame;
            _gradBar = bar;
            _gradBody.Add(barFrame);

            // 帯の枠は角丸のため中を切り取る。印は別の行に置く
            var marks = new VisualElement();
            marks.AddToClassList("chct-marks");
            for (var i = 0; i < 2; i++)
            {
                var mark = new VisualElement();
                mark.AddToClassList("chct-mark");
                var markKnob = new VisualElement();
                markKnob.AddToClassList("chct-mark__knob");
                markKnob.pickingMode = PickingMode.Ignore;
                mark.Add(markKnob);
                var pct = new Label("");
                pct.AddToClassList("chct-mark__pct");
                pct.pickingMode = PickingMode.Ignore;
                mark.Add(pct);
                RegisterMarkDrag(mark, i);
                _gradMarks[i] = mark;
                _gradMarkPcts[i] = pct;
                marks.Add(mark);
            }
            _gradBody.Add(marks);

            // 印と同じ行で印より下に重ねる（端まで印を動かしたときは印のドラッグが勝つ）
            for (var i = 0; i < 3; i++)
            {
                var stop = i;
                var swatch = new VisualElement();
                swatch.AddToClassList("chct-stop-swatch");
                var paint = new PaintBox { Painter = (r, add) => PaintStop(stop, r, add) };
                paint.AddToClassList("chct-fill");
                swatch.Add(paint);
                swatch.AddManipulator(new Clickable(() => OnStopClicked(stop)));
                swatch.AddToClassList(i == 0 ? "chct-stop-swatch--root" : i == 1 ? "chct-stop-swatch--middle" : "chct-stop-swatch--tip");
                _gradStops[i] = swatch;
                _gradStopPaints[i] = paint;
                marks.Insert(i, swatch);
            }
            _gradMixedNote = Note("パーツごとに位置が違います。印を動かすと全部そろいます。");
            _gradBody.Add(_gradMixedNote);
            _gradPartsBox = BuildGradParts();
            _gradBody.Add(_gradPartsBox);


            var sep = new VisualElement();
            sep.AddToClassList("chct-sep");
            head.Add(sep);
            return head;
        }

        VisualElement BuildGradParts()
        {
            var box = new VisualElement();
            box.AddToClassList("chct-grad-parts");
            _gradPartsRow = new VisualElement();
            _gradPartsRow.AddToClassList("chct-chips");
            box.Add(_gradPartsRow);
            _gradPartsAllNote = Note("耳や前髪だけ境目を変えたい→各パーツを選択し印を動かす。\n全部同時に動かしたい→全パーツを選択し動かす。");
            box.Add(_gradPartsAllNote);
            _gradEven = MakeButton(UiText.ResetSplits, null, OnEvenSplitsClicked);
            box.Add(_gradEven);
            return box;
        }

        // 「色」のタブのときだけ呼ぶ
        bool SyncGradPanel()
        {
            var changed = false;
            var headOn = GradHeadAvailable;
            var editing = GradEditing;
            if (_gradHead.style.display != (headOn ? DisplayStyle.Flex : DisplayStyle.None)) changed = true;
            if (_gradPartsBox.style.display != (editing ? DisplayStyle.Flex : DisplayStyle.None)) changed = true;
            Show(_gradHead, headOn);
            Show(_gradPartsBox, editing);
            if (!headOn) return changed;

            var toggleAvail = !IsGradientMode && CurrentGrad != null && _partIndex < CurrentGrad.ColorCount;
            Show(_gradToggleRow, toggleAvail);
            if (toggleAvail)
            {
                _gradToggleText.text = "グラデーションにする"; // 1色目・2色目は上の丸で見えている
                _gradToggleRow.EnableInClassList("is-on", ColorGradOn);
            }
            if (_gradBody.style.display != (editing ? DisplayStyle.Flex : DisplayStyle.None)) changed = true;
            Show(_gradBody, editing);
            if (!editing) return changed;

            var g = Grad(_mode, GradColor);
            _gradCount2.EnableInClassList("is-selected", !g.Three);
            _gradCount3.EnableInClassList("is-selected", g.Three);
            Show(_gradStops[1], g.Three);
            for (var i = 0; i < 3; i++)
            {
                _gradStops[i].EnableInClassList("is-selected", CurrentStop == i);
                _gradStops[i].tooltip = StopLabel(i) + (GradStop(_mode, GradColor, i).Clear ? "：" + NoneSwatch.Label : "の色");
                _gradStopPaints[i].MarkDirtyRepaint();
            }

            var decals = GradDecals;
            var names = string.Join(",", decals.Select(d => d != null ? d.name : ""));
            if (names != _sigGradParts)
            {
                _sigGradParts = names;
                _gradPartsRow.Clear();
                _gradPartEls.Clear();
                var all = Chip("全パーツ", () => OnGradPartClicked(-1));
                _gradPartsRow.Add(all);
                _gradPartEls.Add(new KeyValuePair<int, VisualElement>(-1, all));
                // Hierarchy の名前のまま（言い換えると辞書に無い名前で日本語とまざる）
                for (var i = 0; i < decals.Count; i++)
                {
                    var idx = i;
                    var chip = Chip(decals[i] != null ? decals[i].name : "", () => OnGradPartClicked(idx));
                    _gradPartsRow.Add(chip);
                    _gradPartEls.Add(new KeyValuePair<int, VisualElement>(i, chip));
                }
                changed = true;
            }
            Show(_gradPartsRow, decals.Count >= 2);
            Show(_gradPartsAllNote, decals.Count >= 2);
            foreach (var kv in _gradPartEls) kv.Value.EnableInClassList("is-selected", kv.Key == _gradPartIndex);
            _gradEven.SetEnabled(!SplitsAtDefault);

            var bs = GradBarSignature();
            if (bs != _sigGradBar)
            {
                _sigGradBar = bs;
                _gradBar.MarkDirtyRepaint();
                changed |= SyncGradMarks(g.Three);
            }
            return changed;
        }

        string GradBarSignature()
        {
            var c = CurrentGradientShape();
            var s = ShownSplits();
            return c.ThreeColors + (GradMixed ? "x" : "-") + _lastMark + ColorUtility.ToHtmlStringRGB(c.Root) + ColorUtility.ToHtmlStringRGB(c.Middle) + ColorUtility.ToHtmlStringRGB(c.Tip)
                   + s.Split.ToString("F3") + s.Split1.ToString("F3") + s.Split2.ToString("F3")
                   + (c.RootClear ? "r" : "-") + (c.MiddleClear ? "m" : "-") + (c.TipClear ? "t" : "-");
        }

        void PaintGradBar(Rect r, System.Action<Rect, Color> add)
        {
            if (_target == null || !_target.IsValid || !_customOn || !GradEditing) return;
            var c = CurrentGradientShape();
            var s = ShownSplits();
            var shape = new GradientShape
            {
                ThreeColors = c.ThreeColors, Root = c.Root, Middle = c.Middle, Tip = c.Tip, Split = s.Split, Split1 = s.Split1, Split2 = s.Split2,
                RootClear = c.RootClear, MiddleClear = c.MiddleClear, TipClear = c.TipClear,
            };
            var g = PreviewGradient(shape);
            Slices(r, 64, i => OverHair(g.Evaluate((i + 0.5f) / 64f)), add);
        }

        // 位置はポインタでなく書いたあとの値から置く（Undo やほかの欄の変更でも追う）
        bool SyncGradMarks(bool three)
        {
            var mixed = GradMixed;
            var changed = false;
            var near = three && Mathf.Abs(SplitMark(1) - SplitMark(0)) < 0.14f;
            for (var i = 0; i < 2; i++)
            {
                var on = i == 0 || three;
                if (_gradMarks[i].style.display != (on ? DisplayStyle.Flex : DisplayStyle.None)) changed = true;
                Show(_gradMarks[i], on);
                if (!on) continue;
                var t = SplitMark(three ? i : 0);
                _gradMarks[i].style.left = Length.Percent(t * 100f);
                _gradMarks[i].EnableInClassList("is-mixed", mixed);
                _gradMarkPcts[i].text = mixed ? "—" : Mathf.RoundToInt(t * 100f) + "%";
                // 狭い幅で枠の外に出ないよう、端では%の字を内側へ寄せる
                _gradMarkPcts[i].EnableInClassList("is-left", t < 0.1f);
                _gradMarkPcts[i].EnableInClassList("is-right", t > 0.9f);
                Show(_gradMarkPcts[i], !near || i == _lastMark);
            }
            if (three) _gradStops[1].style.left = Length.Percent((SplitMark(0) + SplitMark(1)) * 50f);
            Show(_gradMixedNote, mixed);
            return changed;
        }

        void RegisterMarkDrag(VisualElement mark, int index)
        {
            mark.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                EndSplitDrag(); // 前のドラッグの Undo が閉じていなければ閉じる
                _dragMark = index;
                _lastMark = index;
                mark.AddToClassList("is-dragging");
                mark.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            mark.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragMark != index || !mark.HasPointerCapture(e.pointerId)) return;
                var w = _gradBarFrame.worldBound;
                if (w.width < 1f) return;
                DragSplitTo(Grad(_mode, GradColor).Three ? index : 0, (e.position.x - w.x) / w.width);
                SyncUI(false);
            });
            mark.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragMark != index) return;
                if (mark.HasPointerCapture(e.pointerId)) mark.ReleasePointer(e.pointerId);
                FinishMarkDrag(mark);
            });
            // Alt+Tab などで離す合図が来ないままポインタを失う。同じ終わり方にしないと次のドラッグまで Undo がまとまる
            mark.RegisterCallback<PointerCaptureOutEvent>(e => { if (_dragMark == index) FinishMarkDrag(mark); });
            mark.RegisterCallback<PointerCancelEvent>(e => { if (_dragMark == index) FinishMarkDrag(mark); });
        }

        void FinishMarkDrag(VisualElement mark)
        {
            _dragMark = -1;
            mark.RemoveFromClassList("is-dragging");
            EndSplitDrag();
            SyncUI(false);
        }

        void OnGradBarPointerDown(PointerDownEvent e)
        {
            if (e.button != 0 || _target == null || !_target.IsValid || !_customOn || !GradEditing) return;
            var w = _gradBarFrame.worldBound;
            if (w.width < 1f) return;
            var t = (e.position.x - w.x) / w.width;
            var stop = Grad(_mode, GradColor).Three ? (t < SplitMark(0) ? 0 : t < SplitMark(1) ? 1 : 2) : (t < SplitMark(0) ? 0 : 2);
            OnStopClicked(stop);
        }

        void OnGradToggleClicked()
        {
            if (_target == null || !_target.IsValid || !_customOn || IsGradientMode || CurrentGrad == null || _partIndex >= CurrentGrad.ColorCount) return;
            ToggleColorGrad(!ColorGradOn);
            SyncUI(false);
        }

        void OnGradCountClicked(bool three)
        {
            if (_target == null || !_target.IsValid || !_customOn || !GradEditing) return;
            if (Grad(_mode, GradColor).Three == three) return;
            SetGradientThree(_mode, GradColor, three);
            SyncUI(false);
        }

        void OnStopClicked(int stop)
        {
            if (_target == null || !_target.IsValid || !_customOn || !GradEditing || CurrentStop == stop) return;
            if (stop == 1 && !Grad(_mode, GradColor).Three) return;
            if (IsGradientMode) SetPart(stop);
            else
            {
                UnityEditor.Undo.RecordObject(this, HairColorCore.UndoLabel);
                _stopIndex = stop;
            }
            SyncUI(false);
            MarkImguiDirty();
        }

        void OnGradPartClicked(int index)
        {
            if (_target == null || !_target.IsValid || !_customOn) return;
            SelectGradPart(index);
            SyncUI(false);
            MarkImguiDirty();
        }

        void OnEvenSplitsClicked()
        {
            ResetSplits();
            SyncUI(false);
        }

        // ---- メッシュ調整・毛束調整 ----
        VisualElement BuildStrandPanel()
        {
            var card = Card(null);
            card.AddToClassList("chct-strand");
            _strandStart = MakeButton("シーン上で毛束を選ぶ", "chct-btn--big", OnStrandStartClicked);
            card.Add(_strandStart);

            var actions = Row("chct-actions");
            _strandActions = actions;
            _strandSwap = new Label("色を入れ替える");
            var swap = ActionButton(_strandSwap, out var dots);
            _strandDotA = HairDot(0);
            _strandDotB = HairDot(1);
            _strandDotC = HairDot(2);
            dots.Add(_strandDotA);
            _strandArrow = new Label("⇄");
            _strandArrow.AddToClassList("chct-action__arrow");
            dots.Add(_strandArrow);
            dots.Add(_strandDotB);
            _strandArrowC = new Label("→");
            _strandArrowC.AddToClassList("chct-action__arrow");
            dots.Add(_strandArrowC);
            dots.Add(_strandDotC);
            swap.AddManipulator(new Clickable(() => OnPickActionClicked((int)PickAction.Swap)));
            actions.Add(swap);
            _strandUnpaint = new Label("デフォルトの髪にする");
            var unpaint = ActionButton(_strandUnpaint, out var dots2);
            _strandDotA2 = Dot();
            _strandDotA2.style.backgroundColor = OriginalHairColor;
            dots2.Add(_strandDotA2);
            unpaint.AddManipulator(new Clickable(() => OnPickActionClicked((int)PickAction.Unpaint)));
            actions.Add(unpaint);
            card.Add(actions);

            var counts = Row("chct-counts");
            for (var i = 0; i < 4; i++)
            {
                var box = new VisualElement();
                box.AddToClassList("chct-count");
                _strandCountNums[i] = new Label("0");
                _strandCountNums[i].AddToClassList("chct-count__num");
                _strandCountNums[i].AddToClassList(HeadingClass);
                _strandCountNames[i] = new Label("");
                _strandCountNames[i].AddToClassList("chct-count__name");
                box.Add(_strandCountNums[i]);
                box.Add(_strandCountNames[i]);
                _strandCountBoxes[i] = box;
                counts.Add(box);
            }
            card.Add(counts);
            _strandCut = Note("");
            card.Add(_strandCut);
            _strandMessage = new Label("");
            _strandMessage.AddToClassList("chct-info");
            card.Add(_strandMessage);
            _strandReset = MakeButton("", null, OnStrandResetClicked);
            card.Add(_strandReset);
            return card;
        }

        static VisualElement ActionButton(Label name, out VisualElement dots)
        {
            var b = new VisualElement();
            b.AddToClassList("chct-action");
            dots = new VisualElement();
            dots.AddToClassList("chct-action__dots");
            b.Add(dots);
            name.AddToClassList("chct-action__name");
            name.AddToClassList(HeadingClass);
            b.Add(name);
            return b;
        }

        static VisualElement Dot()
        {
            var d = new VisualElement();
            d.AddToClassList("chct-action__dot");
            return d;
        }

        VisualElement HairDot(int index)
        {
            var d = Dot();
            var paint = new PaintBox { Painter = (r, add) => PaintPartOnHair(_mode, index, r, add) };
            paint.AddToClassList("chct-fill");
            d.Add(paint);
            return d;
        }

        void PaintPartOnHair(ColoringMode mode, int index, Rect r, System.Action<Rect, Color> add)
        {
            if (!ColoringModes.Get(mode).IsGradient && IsColorGradOnFor(mode, index))
            {
                PaintPart(mode, index, r, add);
                return;
            }
            var st = State(mode, index);
            add(r, st.Clear ? OriginalHairColor : st.Color);
        }

        // 毛束調整（PinKind.Paint）はクリック＝塗る ⇄ デフォルトの髪のため、2択・色の丸・2色目の枠を出さない
        void SyncStrandPanel()
        {
            var ps = CurrentPins;
            var paint = ps != null && ps.Kind == PinKind.Paint;
            var three = !paint && ps != null && ps.ColorCount > 2;
            Show(_strandActions, !paint);
            if (!paint)
            {
                _strandSwap.parent.EnableInClassList("is-selected", _pickAction == PickAction.Swap);
                _strandUnpaint.parent.EnableInClassList("is-selected", _pickAction == PickAction.Unpaint);
                // 本数は目印の数のため変えない
                var clearA = PartClear(_mode, 0);
                var clearB = PartClear(_mode, 1);
                _strandDotA.Q<PaintBox>().MarkDirtyRepaint();
                _strandDotB.Q<PaintBox>().MarkDirtyRepaint();
                _strandDotA.EnableInClassList("chct-dot--clear", clearA);
                _strandDotB.EnableInClassList("chct-dot--clear", clearB);
                _strandSwap.text = three ? "色を回す" : "色を入れ替える";
                _strandArrow.text = three ? "→" : "⇄";
                Show(_strandArrowC, three);
                Show(_strandDotC, three);
                if (three)
                {
                    _strandDotC.Q<PaintBox>().MarkDirtyRepaint();
                    _strandDotC.EnableInClassList("chct-dot--clear", PartClear(_mode, 2));
                }
            }
            _strandStart.text = _picking ? "毛束選びを完了（Esc）" : "シーン上で毛束を選ぶ";
            _strandStart.EnableInClassList("chct-btn--big", !_picking);
            _strandStart.EnableInClassList("chct-btn--soft", _picking);
            var c = _pickCounts ?? new int[5];
            for (var i = 0; i < 4; i++)
            {
                var k = StrandCountIndex[i];
                Show(_strandCountBoxes[i], paint ? i == 0 || i == 3 : i != 2 || three);
                _strandCountNums[i].text = (k < c.Length ? c[k] : 0).ToString();
                if (paint) _strandCountNames[i].text = i == 0 ? "塗る" : "デフォルトの髪";
                else _strandCountNames[i].text = i < 3 ? PartLabel(_mode, i) + (PartClear(_mode, i) ? "（" + NoneSwatch.Label + "）" : "") : "デフォルトの髪";
            }
            var cut = c.Length > 3 ? c[3] : 0;
            _strandCut.text = cut > 0 ? "真ん中で分かれる毛束 " + cut + " 本（ここでは変えられません）" : "";
            Show(_strandCut, cut > 0);
            var msg = FreshPickMessage;
            _strandMessage.text = msg ?? "";
            Show(_strandMessage, msg != null);
            _strandReset.text = StrandTabLabel(_mode) + "前に戻す";
            _strandReset.SetEnabled(_pickDiffers);
        }

        void OnPickActionClicked(int action)
        {
            SetPickAction(action == 1 ? PickAction.Unpaint : PickAction.Swap);
            SyncUI(false);
        }

        void OnStrandStartClicked()
        {
            if (_picking) StopPicking();
            else StartPicking();
            SyncUI(false);
        }

        void OnStrandResetClicked()
        {
            ResetPick();
            SyncUI(false);
        }

        void OnBackClicked()
        {
            if (_target == null || !_target.IsValid || !_customOn) return;
            LeaveCustom();
            _topTab = TopTab.Pick;
            _topTabDecided = true;
            SaveUiPrefs();
            SyncUI(false);
        }
    }
}
#endif
#endif
