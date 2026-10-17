#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace AtelierCloveria.CustomHairColorTool
{
    public sealed partial class CustomHairColorToolWindow
    {
        // 書き込みの間引き（0.3 秒）より長く、NDMF のプレビューの作り直しが追いつく待ち
        const double GlowShotDelay = 1.0;
        const int GlowShotSize = 256;
        const int GlowShotSizeLarge = 768; // 別ウィンドウで大きく見るとき
        const float GlowShotMaxWidth = 480f;

        [NonSerialized] Texture2D _glowShot;
        [NonSerialized] GlowPreviewWindow _glowBig;
        [NonSerialized] string _glowShotWant;
        [NonSerialized] double _glowShotDue = -1;
        [NonSerialized] string _glowShotError;

        // Hierarchy の変更でなく値で見る（隠しカメラや NDMF のプロキシで Hierarchy が変わり、撮り直しが止まらなくなるため）
        string GlowShotSignature()
        {
            var sb = new StringBuilder();
            sb.Append(_target.Avatar.GetInstanceID()).Append('|').Append((int)_mode).Append('|').Append(_glowingColorIndex).Append('|')
              .Append(ColorUtility.ToHtmlStringRGB(_glowColor)).Append('|').Append(_glowStrength.ToString("F3")).Append('|')
              .Append(_glowFlowOn).Append('|').Append(_glowFlowVelocity.ToString("F2")).Append('|');
            var parts = VisibleParts(_mode);
            sb.Append(StripSignature(parts));
            foreach (var p in parts) sb.Append(State(_mode, p).BlendKey).Append(',');
            if (_pickCounts != null) sb.Append('|').Append(string.Join(",", _pickCounts));
            return sb.ToString();
        }

        static bool GlowShotBlocked => EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null;

        void DrawGlowPreview(float width)
        {
            if (_glowingColorIndex < 0 || !_glowMaterialHit || _target == null || !_target.IsValid) return;
            GUILayout.Space(8);
            GUILayout.Label("暗い部屋での見え方", ImguiSkin.Bold);
            if (GlowShotBlocked)
            {
                GUILayout.Label("再生中・Prefab の編集中は見本を出せません。", ImguiSkin.Note);
                return;
            }
            var sig = GlowShotSignature();
            if (sig != _glowShotWant)
            {
                _glowShotWant = sig;
                ScheduleGlowRecapture();
            }

            var w = Mathf.Max(80f, Mathf.Min(width - 8f, GlowShotMaxWidth));
            var r = GUILayoutUtility.GetRect(w, w * 0.5f, GUILayout.Width(w), GUILayout.Height(w * 0.5f));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, HairColorCore.GlowShotBackground);
                if (_glowShot != null) GUI.DrawTexture(r, _glowShot, ScaleMode.ScaleToFit, false);
                else GUI.Label(r, "見本を撮っています…", ImguiSkin.Note);
            }
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Zoom);
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                OpenGlowPreviewWindow();
                Event.current.Use();
            }
            if (GUILayout.Button("大きく見る（別のウィンドウ）", ImguiSkin.Button, GUILayout.Height(26))) OpenGlowPreviewWindow();
            GUILayout.Label("左が正面、右が後ろ。明かりを落とした部屋での見え方です。", ImguiSkin.Note);
            GUILayout.Label("Scene\u00A0の明かりは変わりません。設定を変えると少しあとに撮り直します。", ImguiSkin.Note);
            if (!string.IsNullOrEmpty(_glowShotError)) GUILayout.Label(_glowShotError, ImguiSkin.Warn);
        }

        void ScheduleGlowRecapture() => _glowShotDue = EditorApplication.timeSinceStartup + GlowShotDelay;

        // EditorApplication.update は使わない（ほかの拡張の例外で後ろが呼ばれないことがあるため）
        void TickGlowShot()
        {
            if (_glowShotDue < 0 || EditorApplication.timeSinceStartup < _glowShotDue) return;
            _glowShotDue = -1;
            CaptureGlowPreview();
            MarkImguiDirty();
            if (_glowBig != null) _glowBig.Repaint();
        }

        internal Texture2D GlowShot => _glowShot;

        void OpenGlowPreviewWindow()
        {
            _glowBig = GlowPreviewWindow.Open(this);
            _glowShotDue = EditorApplication.timeSinceStartup;
        }

        // IMGUI の描画の中では撮らない
        bool CaptureGlowPreview()
        {
            _glowShotError = null;
            if (_target == null || !_target.IsValid || GlowShotBlocked) return false;
            var g = CurrentGlow;
            if (g == null || _glowingColorIndex < 0) return false;
            var hair = HairColorCore.FindGlowRenderers(_target, g);
            var mats = new HashSet<Material>(g.Setting.TargetMaterials.Where(m => m != null));
            if (hair.Count == 0) return false;
            try
            {
                var size = _glowBig != null ? GlowShotSizeLarge : GlowShotSize;
                _glowShot = HairColorCore.CaptureGlowInDarkRoom(_target.Avatar, hair, size, _glowShot, mats);
                return true;
            }
            catch (Exception e)
            {
                _glowShotError = "見本を撮れませんでした（Scene の色や明かりは変わっていません）。";
                Debug.LogWarning("[" + ToolInfo.ToolName + "] 暗い部屋の見本を撮れませんでした：" + e);
                return false;
            }
        }

        void DisposeGlowPreview()
        {
            _glowShotDue = -1;
            if (_glowBig != null) _glowBig.Close();
            _glowBig = null;
            if (_glowShot != null) DestroyImmediate(_glowShot);
            _glowShot = null;
            _glowShotWant = null;
        }
    }

    // ここでは撮らず、ツールの画面が撮った画像を出す
    sealed class GlowPreviewWindow : EditorWindow
    {
        const float NoteHeight = 40f;

        [NonSerialized] CustomHairColorToolWindow _owner;

        public static GlowPreviewWindow Open(CustomHairColorToolWindow owner)
        {
            var w = GetWindow<GlowPreviewWindow>(true, "暗い部屋での見え方");
            w._owner = owner;
            w.minSize = new Vector2(400, 240);
            w.Show();
            return w;
        }

        void OnGUI()
        {
            var all = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(all, HairColorCore.GlowShotBackground);
            var shot = _owner != null ? _owner.GlowShot : null;
            if (shot != null) GUI.DrawTexture(new Rect(8, 8, all.width - 16, all.height - 16 - NoteHeight), shot, ScaleMode.ScaleToFit, false);
            // スクリプトの読み直しで _owner が切れるため、開き直してもらう
            var note = _owner != null
                ? "左が正面、右が後ろ。ウィンドウの端をドラッグすると大きさを変えられます。\n光らせるの設定を変えると、少しあとに撮り直します。"
                : "カスタムカラーツールの「大きく見る」から開き直してください。";
            GUI.Label(new Rect(8, all.height - NoteHeight, all.width - 16, NoteHeight), note, ImguiSkin.Note);
        }
    }
}
#endif
#endif
