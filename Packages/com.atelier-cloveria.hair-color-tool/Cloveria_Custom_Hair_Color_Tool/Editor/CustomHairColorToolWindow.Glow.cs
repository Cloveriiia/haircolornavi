#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System;
using System.Collections.Generic;
using System.Linq;
using net.rs64.TexTransTool;
using net.rs64.TexTransTool.Decal;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    public sealed partial class CustomHairColorToolWindow
    {
        const float GlowStrengthMax = 2f;
        const float GlowStrengthDefault = 0.6f;
        const float FlowSpeedMin = 0.1f;
        const float FlowSpeedMax = 2f;
        const double GlowWriteInterval = 0.3; // ドラッグ中の書き込みの間引き（発光マップのプレビューの作り直しが重いため）

        [SerializeField] bool[] _glowFollowHair = new bool[ColoringModes.All.Length];

        int _glowingColorIndex = -1;
        Color _glowColor = Color.white;
        float _glowStrength = GlowStrengthDefault;
        bool _glowHasFlow;
        bool _glowFlowOn;
        int _glowPattern = -1;
        bool _glowFlowReverse;
        float _glowFlowSpeed;
        bool _glowMaterialHit;
        bool _glowPending;
        bool _glowColorPending;
        Color _glowColorPendingValue;
        ColoringMode _glowColorPendingMode;
        double _glowLastWrite;

        GlowParts CurrentGlow => _target != null && _target.IsValid && _target.Has(_mode) ? _target.ModeParts[_mode].Glow : null;

        bool GlowFollowHair(ColoringMode mode)
        {
            if (_glowFollowHair == null || _glowFollowHair.Length < ColoringModes.All.Length) Array.Resize(ref _glowFollowHair, ColoringModes.All.Length);
            return _glowFollowHair[(int)mode];
        }

        void RefreshGlow()
        {
            var g = CurrentGlow;
            if (g == null) return;
            _glowingColorIndex = HairColorCore.GetGlowingColorIndex(g);
            _glowColor = HairColorCore.GetGlowColor(g);
            if (!_glowPending) // 間引き中の値を、書く前の値で上書きしない
            {
                _glowStrength = HairColorCore.GetGlowStrength(g);
                _glowFlowSpeed = HairColorCore.GetGlowFlowSpeed(g);
            }
            _glowHasFlow = HairColorCore.CanFlowGlow(_target, g);
            _glowFlowOn = _glowHasFlow && HairColorCore.IsGlowFlowOn(g);
            _glowFlowReverse = HairColorCore.IsGlowFlowReverse(g);
            _glowPattern = HairColorCore.GetGlowPattern(g);
            _glowMaterialHit = HairColorCore.IsGlowMaterialOnAvatar(_target, g);
        }

        // throttle：リングのドラッグ中はつまみと同じく間引く
        void SyncGlowColorToHair(ColoringMode mode, int color, bool throttle = false)
        {
            if (_target == null || !_target.Has(mode)) return;
            var g = _target.ModeParts[mode].Glow;
            if (g == null || !GlowFollowHair(mode) || HairColorCore.GetGlowingColorIndex(g) != color) return;
            if (State(mode, color).Clear) return; // 無色には写す色が無いため前のまま
            if (!throttle)
            {
                _glowColorPending = false;
                HairColorCore.SetGlowColor(g, State(mode, color).Color);
                return;
            }
            _glowColorPending = true;
            _glowColorPendingValue = State(mode, color).Color;
            _glowColorPendingMode = mode;
            if (EditorApplication.timeSinceStartup - _glowLastWrite >= GlowWriteInterval) FlushGlowPending();
        }

        bool AnyGlowPending => _glowPending || _glowColorPending;

        void FlushGlowPending()
        {
            if (!AnyGlowPending) return;
            if (_glowColorPending)
            {
                _glowColorPending = false;
                var cg = _target != null && _target.Has(_glowColorPendingMode) ? _target.ModeParts[_glowColorPendingMode].Glow : null;
                if (cg != null) HairColorCore.SetGlowColor(cg, _glowColorPendingValue);
            }
            if (_glowPending)
            {
                _glowPending = false;
                var g = CurrentGlow;
                if (g != null)
                {
                    HairColorCore.SetGlowStrength(g, _glowStrength);
                    if (_glowHasFlow) HairColorCore.SetGlowFlowSpeed(g, _glowFlowSpeed);
                }
            }
            _glowLastWrite = EditorApplication.timeSinceStartup;
            SceneView.RepaintAll();
        }

        void QueueGlowWrite()
        {
            _glowPending = true;
            if (EditorApplication.timeSinceStartup - _glowLastWrite >= GlowWriteInterval) FlushGlowPending();
        }

        // ---- 光らせる ----
        // 確かめた範囲の NDMF が無いとアップロードしても光らない（発光の設定を入れるのが NDMF のプラグインのため）
#if AC_CHCT_NDMF_PRESENT
        static readonly bool NdmfReady = true;
#else
        static readonly bool NdmfReady = false;
#endif

        bool GlowReady => NdmfReady && _glowMaterialHit;

        void DrawGlowWarningsIMGUI()
        {
            if (_target == null || !_target.IsValid || !_customOn || CurrentGlow == null) return;
            if (!NdmfReady) GUILayout.Label("光らせる機能には NDMF（1.7 以上・2.0 未満）が必要です。いまの環境では、アップロードしても光りません。", ImguiSkin.Warn);
            else if (!_glowMaterialHit) GUILayout.Label("髪のマテリアルが商品の想定と違うので光らせられません（髪のマテリアルを差し替えているときなど）。", ImguiSkin.Warn);
        }

        void DrawGlowPreviewIMGUI()
        {
            if (_target == null || !_target.IsValid || !_customOn || CurrentGlow == null || !GlowReady) return;
            DrawGlowPreview(ImguiWidth);
        }

        void SetGlowColor(Color c)
        {
            var g = CurrentGlow;
            if (g == null) return;
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            _glowColor = c;
            HairColorCore.SetGlowColor(g, c);
            SceneView.RepaintAll();
        }

        void SetGlowStrength(float s)
        {
            BeginDragUndo();
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            _glowStrength = s;
            QueueGlowWrite();
        }

        void SetGlowSpeed(float speed)
        {
            BeginDragUndo();
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            _glowFlowSpeed = speed;
            QueueGlowWrite();
        }

        void SetGlowingColorIndex(int side)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            FlushGlowPending();
            HairColorCore.SetGlowingColorIndex(_target, _mode, side);
            if (side >= 0) SyncGlowColorToHair(_mode, side);
            SceneView.RepaintAll();
            Refresh();
        }

        // 編集中の Scene は何か変わったときしか描き直さないため、流れる光が止まって見える
        // スイッチがオンで流している間だけ描き直す（Scene の Always Refresh は触らない）
        const double FlowRepaintInterval = 1.0 / 60.0;
        double _flowLastRepaint;
        [NonSerialized] bool _flowSmooth;

        void TickFlowRepaint()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_topTab != TopTab.Custom || _subTab != SubTab.Glow || CurrentGlow == null) return;
            if (!_flowSmooth || _glowingColorIndex < 0 || !_glowHasFlow || !_glowFlowOn) return;
            var now = EditorApplication.timeSinceStartup;
            if (now - _flowLastRepaint < FlowRepaintInterval) return;
            _flowLastRepaint = now;
            SceneView.RepaintAll();
        }

        // 0＝流さない、1＝上から下、2＝下から上
        void SetGlowFlow(int dir)
        {
            var g = CurrentGlow;
            if (g == null) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            FlushGlowPending();
            _glowFlowOn = dir != 0;
            if (dir != 0) _glowFlowReverse = dir == 2;
            HairColorCore.SetGlowFlow(g, _glowFlowOn, _glowFlowReverse);
            SceneView.RepaintAll();
            Refresh();
        }

        void SetGlowPattern(int index)
        {
            var g = CurrentGlow;
            if (g == null) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            FlushGlowPending();
            HairColorCore.SetGlowPattern(g, index);
            SceneView.RepaintAll();
            Refresh();
        }

        void SetGlowFollowHair(bool on)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            GlowFollowHair(_mode);
            _glowFollowHair[(int)_mode] = on;
            if (on && _glowingColorIndex >= 0) SyncGlowColorToHair(_mode, _glowingColorIndex);
            SceneView.RepaintAll();
            Refresh();
        }
    }
}
#endif
#endif
