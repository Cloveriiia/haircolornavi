#if UNITY_EDITOR
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AtelierCloveria.CustomHairColorTool
{
    public sealed partial class CustomHairColorToolWindow
    {
        VisualElement _glowWarnPanel, _glowPreviewPanel, _glowControls, _glowLit, _glowFlowBox, _glowSpeedBox, _glowFollowRow, _glowSmoothRow;
        Label _glowFollowNote, _glowStrengthValue, _glowSpeedValue, _glowSmoothNote;
        ColorField _glowColorField;
        Slider _glowStrengthSlider, _glowSpeedSlider;
        readonly VisualElement[] _glowSideEls = new VisualElement[3];
        readonly VisualElement[] _glowSideDots = new VisualElement[2];
        readonly VisualElement[] _glowFlowEls = new VisualElement[3];

        VisualElement BuildGlowPanel()
        {
            var card = Card(null);
            card.AddToClassList("chct-grad");
            _glowWarnPanel = AddImgui(card, DrawGlowWarningsIMGUI, "chct-glow-warnings");

            _glowControls = new VisualElement();
            card.Add(_glowControls);
            _glowControls.Add(GlowLabel("光らせる色"));
            var sides = Pills();
            for (var k = -1; k < 2; k++)
            {
                var side = k;
                var pill = new VisualElement();
                pill.AddToClassList("chct-pill");
                pill.AddToClassList("chct-pill--dot");
                if (k >= 0)
                {
                    var dot = new VisualElement();
                    dot.AddToClassList("chct-pill__dot");
                    var paint = new PaintBox { Painter = (r, add) => PaintPartOnHair(_mode, side, r, add) };
                    paint.AddToClassList("chct-fill");
                    dot.Add(paint);
                    pill.Add(dot);
                    _glowSideDots[k] = dot;
                }
                var name = new Label(k < 0 ? "光らせない" : "");
                name.AddToClassList(HeadingClass);
                pill.Add(name);
                pill.AddManipulator(new Clickable(() => OnGlowSideClicked(side)));
                sides.Q(className: "chct-pills").Add(pill);
                _glowSideEls[k + 1] = pill;
            }
            _glowControls.Add(sides);

            _glowLit = new VisualElement();
            _glowControls.Add(_glowLit);
            _glowLit.Add(GlowLabel("光の色"));
            _glowColorField = new ColorField { showAlpha = false, hdr = false, showEyeDropper = true };
            _glowColorField.RegisterValueChangedCallback(e =>
            {
                SetGlowColor(e.newValue);
                RequestUiSync();
            });
            _glowLit.Add(_glowColorField);
            _glowFollowRow = SwitchRow("髪の色と同じにする", OnGlowFollowClicked);
            _glowLit.Add(_glowFollowRow);
            _glowFollowNote = Note("");
            _glowLit.Add(_glowFollowNote);

            _glowLit.Add(GlowLabel("光の強さ"));
            _glowStrengthSlider = new Slider(0f, GlowStrengthMax);
            _glowStrengthValue = new Label();
            _glowStrengthSlider.RegisterValueChangedCallback(e =>
            {
                SetGlowStrength(e.newValue);
                _glowStrengthValue.text = e.newValue.ToString("0.00");
            });
            _glowLit.Add(SliderRow(_glowStrengthSlider, _glowStrengthValue));

            _glowFlowBox = new VisualElement();
            _glowLit.Add(_glowFlowBox);
            _glowFlowBox.Add(GlowLabel("光を流す"));
            var flows = Pills();
            for (var k = 0; k < 3; k++)
            {
                var dir = k;
                var pill = new Label(k == 0 ? "流さない" : k == 1 ? "上から下" : "下から上");
                pill.AddToClassList("chct-pill");
                pill.AddToClassList(HeadingClass);
                pill.AddManipulator(new Clickable(() => OnGlowFlowClicked(dir)));
                flows.Q(className: "chct-pills").Add(pill);
                _glowFlowEls[k] = pill;
            }
            _glowFlowBox.Add(flows);

            _glowSpeedBox = new VisualElement();
            _glowFlowBox.Add(_glowSpeedBox);
            _glowSpeedBox.Add(GlowLabel("流れる速さ"));
            _glowSpeedSlider = new Slider(FlowSpeedMin, FlowSpeedMax);
            _glowSpeedValue = new Label();
            _glowSpeedSlider.RegisterValueChangedCallback(e =>
            {
                SetGlowSpeed(e.newValue);
                _glowSpeedValue.text = e.newValue.ToString("0.00");
            });
            _glowSpeedBox.Add(SliderRow(_glowSpeedSlider, _glowSpeedValue));
            _glowSmoothRow = SwitchRow("シーン上でなめらかに見る", OnFlowSmoothClicked);
            _glowSmoothRow.AddToClassList("chct-switch-row--loud");
            _glowSpeedBox.Add(_glowSmoothRow);
            _glowSmoothNote = new Label("オンの間シーン上の描き直しが増えて重くなるため、確認完了後はオフ推奨です。");
            _glowSmoothNote.AddToClassList("chct-warn");
            _glowSpeedBox.Add(_glowSmoothNote);

            _glowPreviewPanel = AddImgui(card, DrawGlowPreviewIMGUI, "chct-glow-preview");
            card.Add(Note("アップロードすると発光の画像が自動で作成されます。"));
            return card;
        }

        static Label GlowLabel(string text)
        {
            var l = new Label(text);
            l.AddToClassList("chct-glow-label");
            return l;
        }

        static VisualElement Pills()
        {
            var box = new VisualElement();
            box.AddToClassList("chct-pills-box");
            var row = new VisualElement();
            row.AddToClassList("chct-pills");
            box.Add(row);
            return box;
        }

        static VisualElement SwitchRow(string text, Action onClick)
        {
            var row = Row("chct-switch-row");
            var label = new Label(text);
            label.AddToClassList("chct-switch-row__text");
            row.Add(label);
            var sw = new VisualElement();
            sw.AddToClassList("chct-switch");
            var knob = new VisualElement();
            knob.AddToClassList("chct-switch__knob");
            sw.Add(knob);
            row.Add(sw);
            row.AddManipulator(new Clickable(onClick));
            return row;
        }

        static VisualElement SliderRow(Slider slider, Label value)
        {
            var row = Row("chct-slider-row");
            slider.AddToClassList("chct-slider");
            row.Add(slider);
            value.AddToClassList("chct-slider-row__value");
            row.Add(value);
            return row;
        }

        void SyncGlowPanel()
        {
            var ready = CurrentGlow != null && GlowReady;
            var lit = _glowSide >= 0;
            Show(_glowWarnPanel, !ready);
            Show(_glowPreviewPanel, ready && lit);
            Show(_glowControls, ready);
            if (!ready) return;
            for (var k = -1; k < 2; k++) _glowSideEls[k + 1].EnableInClassList("is-selected", _glowSide == k);
            for (var k = 0; k < 2; k++)
            {
                _glowSideDots[k].Q<PaintBox>().MarkDirtyRepaint();
                _glowSideEls[k + 1].Q<Label>().text = PartLabel(_mode, k);
            }
            _glowLit.SetEnabled(lit);

            var follow = GlowFollowHair(_mode);
            var sideClear = lit && State(_mode, _glowSide).Clear;
            _glowColorField.SetValueWithoutNotify(_glowColor);
            _glowColorField.SetEnabled(!follow || sideClear);
            _glowFollowRow.EnableInClassList("is-on", follow);
            _glowFollowNote.text = follow && sideClear ? PartLabel(_mode, _glowSide) + "は「" + NoneSwatch.Label + "」（デフォルトの髪のまま）なので、光の色は自分で決めます。「色」で色を選ぶと、また髪の色に合わせます。" : "";
            Show(_glowFollowNote, follow && sideClear);

            _glowStrengthSlider.SetValueWithoutNotify(_glowStrength);
            _glowStrengthValue.text = _glowStrength.ToString("0.00");

            Show(_glowFlowBox, _glowHasFlow);
            if (!_glowHasFlow) return;
            var dir = !_glowFlowOn ? 0 : _glowFlowVelocity >= 0f ? 1 : 2;
            for (var k = 0; k < 3; k++) _glowFlowEls[k].EnableInClassList("is-selected", dir == k);
            _glowSpeedBox.SetEnabled(_glowFlowOn);
            var speed = Mathf.Abs(_glowFlowVelocity);
            _glowSpeedSlider.SetValueWithoutNotify(speed);
            _glowSpeedValue.text = speed.ToString("0.00");
            _glowSmoothRow.EnableInClassList("is-on", _flowSmooth);
            Show(_glowSmoothNote, _flowSmooth);
        }

        void OnGlowSideClicked(int side)
        {
            if (_target == null || !_target.IsValid || !_customOn || _glowSide == side) return;
            SetGlowSide(side);
            SyncUI(false);
        }

        void OnGlowFollowClicked()
        {
            if (_target == null || !_target.IsValid || !_customOn || _glowSide < 0) return;
            SetGlowFollowHair(!GlowFollowHair(_mode));
            SyncUI(false);
        }

        void OnGlowFlowClicked(int dir)
        {
            if (_target == null || !_target.IsValid || !_customOn || _glowSide < 0) return;
            SetGlowFlow(dir);
            SyncUI(false);
        }

        void OnFlowSmoothClicked()
        {
            if (!_glowFlowOn || _glowSide < 0) return;
            _flowSmooth = !_flowSmooth;
            SceneView.RepaintAll();
            SyncUI(false);
        }
    }
}
#endif
#endif
