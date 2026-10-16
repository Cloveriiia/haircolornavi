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
    // Prefab：Custom/_Glow/<モード> に HairGlowSetting、その下に発光の Decal「<色の Decal の名前>_Glow」2枚（Padding 0・オフで出荷）
    // 発光の設定をマテリアルに入れるのは自前の NDMF プラグイン（TTT より先に動く）
    public sealed class GlowTarget
    {
        public GameObject Root;
        public GameObject Node;
        public readonly SingleGradationDecal[] Decals = new SingleGradationDecal[2];
        public HairGlowSetting Setting;
        public string Prefix => Setting.EmissionPrefix; // "_Emission" か "_Emission2nd"
    }

    public static partial class CustomHairColorToolCore
    {
        public const string GlowNodeName = "_Glow";
        public const string GlowSuffix = "_Glow";

        static GlowTarget DetectGlow(Transform custom, ModeTarget mt)
        {
            if (mt.Decals.Count < 2) return null;
            var root = FindChild(custom, GlowNodeName);
            if (root == null) return null;
            var node = FindChild(root, mt.Node.name);
            if (node == null) return null;
            var t = new GlowTarget { Root = root.gameObject, Node = node.gameObject };
            var decals = CollectDecals(node);
            for (var c = 0; c < 2; c++)
            {
                var lead = mt.Decals[c];
                t.Decals[c] = lead != null ? decals.FirstOrDefault(d => NameIs(d.transform, lead.name + GlowSuffix)) : null;
                if (t.Decals[c] == null) return null;
            }
            t.Setting = node.GetComponent<HairGlowSetting>();
            if (t.Setting == null) return null;
            return t;
        }

        // 光らせるときも2枚ともオンにし、反対側に黒を書く
        // 色と同じく後ろの Decal が上に乗るため、両側をまたぐ毛束でも色と光がずれない（Padding 0 だけでははみ出した）
        static bool IsBlack(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b)) < 0.5f / 255f;

        // 0＝1色目・1＝2色目・-1＝光らせない
        public static int ReadGlowSide(GlowTarget g)
        {
            if (g == null) return -1;
            var on = new bool[2];
            for (var c = 0; c < 2; c++) on[c] = g.Decals[c] != null && g.Decals[c].gameObject.activeSelf;
            if (on[0] && on[1])
            {
                if (IsBlack(ReadColor(g.Decals[0])) && !IsBlack(ReadColor(g.Decals[1]))) return 1;
                return 0;
            }
            if (on[0]) return 0;
            if (on[1]) return 1;
            return -1;
        }

        // 光らせないときも両方に光の色を残す（次に同じ色で光らせるため）
        public static void SetGlowSide(HairColorTarget target, HairMode mode, int side)
        {
            if (target == null || !target.Has(mode)) return;
            var g = target.ModeTargets[mode].Glow;
            if (g == null) return;
            var light = ReadGlowColor(g);
            for (var c = 0; c < 2; c++)
            {
                if (g.Decals[c] == null) continue;
                SetNodeActive(g.Decals[c].gameObject, side >= 0);
                ApplyColor(g.Decals[c], side < 0 || c == side ? light : Color.black);
            }
            SetMode(target, mode);
        }

        public static Color ReadGlowColor(GlowTarget g)
        {
            if (g == null) return Color.white;
            var side = ReadGlowSide(g);
            if (side >= 0) return ReadColor(g.Decals[side]);
            var a = ReadColor(g.Decals[0]);
            return IsBlack(a) ? ReadColor(g.Decals[1]) : a;
        }

        // 真っ黒は反対側の印のため、ほんの少し明るくする
        public static void ApplyGlowColor(GlowTarget g, Color c)
        {
            if (g == null) return;
            c.a = 1f;
            if (IsBlack(c)) c = new Color(1f / 255f, 1f / 255f, 1f / 255f, 1f);
            var side = ReadGlowSide(g);
            for (var i = 0; i < 2; i++)
                if (g.Decals[i] != null && (side < 0 || i == side)) ApplyColor(g.Decals[i], c);
        }

        // lilToon の発光は「発光の画像 × 色」のため、色の明るさが強さになる
        public static float ReadGlowStrength(GlowTarget g) => g != null ? g.Setting.Strength : 0.6f;

        public static void ApplyGlowStrength(GlowTarget g, float s)
        {
            if (g == null) return;
            WriteSetting(g, so => so.FindProperty(nameof(HairGlowSetting.Strength)).floatValue = s);
        }

        // ---- 流れる光 ----
        // マスクは髪の画像のサンプラーで読むため、髪の画像が Repeat でないと流れない
        public const float FlowSpeedDefault = 0.5f;

        public static bool HasFlow(HairColorTarget target, GlowTarget g)
        {
            if (g == null || g.Setting.FlowMask == null) return false;
            foreach (var m in g.Setting.TargetMaterials)
            {
                var tex = m != null ? m.mainTexture : null;
                if (tex != null && tex.wrapMode != TextureWrapMode.Repeat) return false;
            }
            return true;
        }

        public static bool ReadGlowFlowOn(GlowTarget g) => g != null && g.Setting.FlowOn;

        // ＋＝髪の画像の上→下、−＝下→上
        public static float ReadGlowFlowVelocity(GlowTarget g) => g != null ? g.Setting.FlowVelocity : FlowSpeedDefault;

        public static void ApplyGlowFlow(GlowTarget g, bool on, float velocity)
        {
            if (g == null) return;
            WriteSetting(g, so =>
            {
                so.FindProperty(nameof(HairGlowSetting.FlowOn)).boolValue = on;
                so.FindProperty(nameof(HairGlowSetting.FlowVelocity)).floatValue = velocity;
            });
        }

        // SerializedObject で書く（Prefab のインスタンスでは上書きとして残すため）
        static void WriteSetting(GlowTarget g, Action<SerializedObject> write)
        {
            Undo.RecordObject(g.Setting, UndoLabel);
            var so = new SerializedObject(g.Setting);
            write(so);
            so.ApplyModifiedProperties();
        }

        public static bool GlowMaterialHit(HairColorTarget target, GlowTarget g)
        {
            if (target == null || target.Avatar == null || g == null) return false;
            var mats = new HashSet<Material>(g.Setting.TargetMaterials.Where(m => m != null));
            if (mats.Count == 0) return false;
            foreach (var r in target.Avatar.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && mats.Contains(m)) return true;
            return false;
        }
    }

    public sealed partial class CustomHairColorToolWindow
    {
        const float GlowStrengthMax = 2f;
        const float GlowStrengthDefault = 0.6f;
        const float FlowSpeedMin = 0.1f;
        const float FlowSpeedMax = 2f;
        const double GlowWriteInterval = 0.3; // ドラッグ中の書き込みの間引き（発光マップのプレビューの作り直しが重いため）

        [SerializeField] bool[] _glowFollowHair = new bool[Modes.All.Length];

        int _glowSide = -1;
        Color _glowColor = Color.white;
        float _glowStrength = GlowStrengthDefault;
        bool _glowHasFlow;
        bool _glowFlowOn;
        float _glowFlowVelocity = CustomHairColorToolCore.FlowSpeedDefault;
        bool _glowMaterialHit;
        bool _glowPending;
        bool _glowColorPending;
        Color _glowColorPendingValue;
        HairMode _glowColorPendingMode;
        double _glowLastWrite;

        GlowTarget CurrentGlow => _target != null && _target.IsValid && _target.Has(_mode) ? _target.ModeTargets[_mode].Glow : null;

        bool GlowFollowHair(HairMode mode)
        {
            if (_glowFollowHair == null || _glowFollowHair.Length < Modes.All.Length) Array.Resize(ref _glowFollowHair, Modes.All.Length);
            return _glowFollowHair[(int)mode];
        }

        void RefreshGlow()
        {
            var g = CurrentGlow;
            if (g == null) return;
            _glowSide = CustomHairColorToolCore.ReadGlowSide(g);
            _glowColor = CustomHairColorToolCore.ReadGlowColor(g);
            if (!_glowPending) // 間引き中の値を、書く前の値で上書きしない
            {
                _glowStrength = CustomHairColorToolCore.ReadGlowStrength(g);
                _glowFlowVelocity = CustomHairColorToolCore.ReadGlowFlowVelocity(g);
            }
            _glowHasFlow = CustomHairColorToolCore.HasFlow(_target, g);
            _glowFlowOn = _glowHasFlow && CustomHairColorToolCore.ReadGlowFlowOn(g);
            _glowMaterialHit = CustomHairColorToolCore.GlowMaterialHit(_target, g);
        }

        // throttle：リングのドラッグ中はつまみと同じく間引く
        void SyncGlowColorToHair(HairMode mode, int color, bool throttle = false)
        {
            if (_target == null || !_target.Has(mode)) return;
            var g = _target.ModeTargets[mode].Glow;
            if (g == null || !GlowFollowHair(mode) || CustomHairColorToolCore.ReadGlowSide(g) != color) return;
            if (State(mode, color).Clear) return; // 無色には写す色が無いため前のまま
            if (!throttle)
            {
                _glowColorPending = false;
                CustomHairColorToolCore.ApplyGlowColor(g, State(mode, color).Color);
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
                var cg = _target != null && _target.Has(_glowColorPendingMode) ? _target.ModeTargets[_glowColorPendingMode].Glow : null;
                if (cg != null) CustomHairColorToolCore.ApplyGlowColor(cg, _glowColorPendingValue);
            }
            if (_glowPending)
            {
                _glowPending = false;
                var g = CurrentGlow;
                if (g != null)
                {
                    CustomHairColorToolCore.ApplyGlowStrength(g, _glowStrength);
                    if (_glowHasFlow) CustomHairColorToolCore.ApplyGlowFlow(g, _glowFlowOn, _glowFlowVelocity);
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
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            _glowColor = c;
            CustomHairColorToolCore.ApplyGlowColor(g, c);
            SceneView.RepaintAll();
        }

        void SetGlowStrength(float s)
        {
            BeginDragUndo();
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            _glowStrength = s;
            QueueGlowWrite();
        }

        void SetGlowSpeed(float speed)
        {
            BeginDragUndo();
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            _glowFlowVelocity = _glowFlowVelocity < 0f ? -speed : speed;
            QueueGlowWrite();
        }

        void SetGlowSide(int side)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            FlushGlowPending();
            CustomHairColorToolCore.SetGlowSide(_target, _mode, side);
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
            if (!_flowSmooth || _glowSide < 0 || !_glowHasFlow || !_glowFlowOn) return;
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
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            FlushGlowPending();
            var speed = Mathf.Abs(_glowFlowVelocity);
            if (speed < FlowSpeedMin) speed = CustomHairColorToolCore.FlowSpeedDefault;
            _glowFlowOn = dir != 0;
            if (dir != 0) _glowFlowVelocity = dir == 1 ? speed : -speed;
            CustomHairColorToolCore.ApplyGlowFlow(g, _glowFlowOn, _glowFlowVelocity);
            SceneView.RepaintAll();
            Refresh();
        }

        void SetGlowFollowHair(bool on)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CustomHairColorToolCore.UndoLabel);
            Undo.RecordObject(this, CustomHairColorToolCore.UndoLabel);
            GlowFollowHair(_mode);
            _glowFollowHair[(int)_mode] = on;
            if (on && _glowSide >= 0) SyncGlowColorToHair(_mode, _glowSide);
            SceneView.RepaintAll();
            Refresh();
        }
    }
}
#endif
#endif
