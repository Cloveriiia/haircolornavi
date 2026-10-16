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
    // 「暗い部屋での見え方」。親の無い隠しカメラで撮る（NDMF のプレビューが差し替えて写るため）
    // 明かりを消すのは1回の呼び出しの中だけで、finally で必ず戻す（保存・Undo・リロードが間に入らないため）
    public static partial class CustomHairColorToolCore
    {
        internal static readonly Color GlowShotBackground = new Color(0.05f, 0.05f, 0.07f, 1f);
        const float GlowShotAmbient = 0.04f;
        const float GlowShotLight = 0.15f;
        const float GlowShotFov = 20f;

        // 開発用：明かりを変えたあとに例外を投げて、戻るかを確かめる
        static Action s_glowShotTestHook;

        public static List<Renderer> GlowRenderers(HairColorTarget target, GlowTarget g)
        {
            var list = new List<Renderer>();
            if (target == null || target.Avatar == null || g == null) return list;
            var mats = new HashSet<Material>(g.Setting.TargetMaterials.Where(m => m != null));
            if (mats.Count == 0) return list;
            foreach (var r in target.Avatar.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.sharedMaterials.Any(m => m != null && mats.Contains(m))) list.Add(r);
            }
            return list;
        }

        // 頂点から取る（SkinnedMeshRenderer の bounds は体ぜんぶを含むことが多く、髪が小さく写るため）
        static Bounds HairBounds(IList<Renderer> hair, ICollection<Material> only)
        {
            var any = false;
            var b = new Bounds();
            void Add(Vector3 p)
            {
                if (any) b.Encapsulate(p);
                else { b = new Bounds(p, Vector3.zero); any = true; }
            }
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (var r in hair)
                {
                    Mesh mesh = null;
                    var m = r.transform.localToWorldMatrix;
                    if (r is SkinnedMeshRenderer smr)
                    {
                        if (smr.sharedMesh == null) continue;
                        smr.BakeMesh(baked);
                        mesh = baked;
                        if (smr.bones.Length > 0) m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                        else if (smr.rootBone != null) m = smr.rootBone.localToWorldMatrix;
                    }
                    else if (r is MeshRenderer)
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        mesh = mf != null ? mf.sharedMesh : null;
                    }
                    if (mesh == null || !mesh.isReadable && !(r is SkinnedMeshRenderer))
                    {
                        Add(r.bounds.min);
                        Add(r.bounds.max);
                        continue;
                    }
                    var verts = mesh.vertices;
                    var mats = r.sharedMaterials;
                    for (var sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        if (only != null && (sub >= mats.Length || mats[sub] == null || !only.Contains(mats[sub]))) continue;
                        foreach (var i in mesh.GetIndices(sub)) if (i < verts.Length) Add(m.MultiplyPoint3x4(verts[i]));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
            if (!any)
            {
                b = hair[0].bounds;
                for (var i = 1; i < hair.Count; i++) b.Encapsulate(hair[i].bounds);
            }
            return b;
        }

        // アバターの Transform は触らず、カメラを髪の中心のまわりに回す
        public static Texture2D CaptureDarkRoom(GameObject avatar, IList<Renderer> hair, int size, Texture2D reuse, ICollection<Material> only = null)
        {
            if (avatar == null || hair == null || hair.Count == 0) return reuse;
            var b = HairBounds(hair, only);
            var center = b.center;
            var fwd = Vector3.ProjectOnPlane(avatar.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            var radius = Mathf.Max(0.01f, Mathf.Max(new Vector2(b.extents.x, b.extents.z).magnitude, b.extents.y) * 1.1f);
            var dist = radius / Mathf.Tan(GlowShotFov * 0.5f * Mathf.Deg2Rad) + radius;

            var tex = reuse;
            if (tex == null || tex.width != size * 2 || tex.height != size)
            {
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                tex = new Texture2D(size * 2, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "CHCT_GlowShot", wrapMode = TextureWrapMode.Clamp };
            }

            GameObject camGo = null, lightGo = null;
            Camera cam = null;
            RenderTexture rt = null;
            var prevActive = RenderTexture.active;
            var sceneLights = new List<Light>();
            var ambientMode = RenderSettings.ambientMode;
            var ambientLight = RenderSettings.ambientLight;
            var ambientProbe = RenderSettings.ambientProbe;
            var ambientTouched = false;
            try
            {
                camGo = EditorUtility.CreateGameObjectWithHideFlags("__CHCT_GlowShotCamera", HideFlags.HideAndDontSave, typeof(Camera));
                cam = camGo.GetComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = GlowShotBackground;
                cam.fieldOfView = GlowShotFov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = dist * 4f + 5f;
                cam.allowHDR = false;
                lightGo = EditorUtility.CreateGameObjectWithHideFlags("__CHCT_GlowShotLight", HideFlags.HideAndDontSave, typeof(Light));
                var light = lightGo.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = GlowShotLight;
                light.color = Color.white;
                light.shadows = LightShadows.None;
                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
                cam.targetTexture = rt;

                foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    if (l == null || l == light || !l.enabled) continue;
                    sceneLights.Add(l); // 先に覚える（途中で失敗しても戻すため）
                    l.enabled = false;
                }
                ambientTouched = true;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * GlowShotAmbient;
                // ambientProbe も差し替える（Skybox の環境光は ambientMode を替えても計算し直されないため）
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(Color.white * GlowShotAmbient);
                RenderSettings.ambientProbe = sh;
                s_glowShotTestHook?.Invoke();

                for (var k = 0; k < 2; k++)
                {
                    var side = k == 0 ? fwd : -fwd;
                    cam.transform.SetPositionAndRotation(center + side * dist, Quaternion.LookRotation(-side, Vector3.up));
                    light.transform.rotation = Quaternion.LookRotation(-side - Vector3.up * 0.5f, Vector3.up);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, size, size), k * size, 0, false);
                    RenderTexture.active = prevActive;
                }
                tex.Apply(false);
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (ambientTouched)
                {
                    RenderSettings.ambientMode = ambientMode;
                    RenderSettings.ambientLight = ambientLight;
                    RenderSettings.ambientProbe = ambientProbe;
                }
                foreach (var l in sceneLights) if (l != null) l.enabled = true;
                if (cam != null) cam.targetTexture = null;
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (lightGo != null) UnityEngine.Object.DestroyImmediate(lightGo);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
            return tex;
        }
    }

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
            sb.Append(_target.Avatar.GetInstanceID()).Append('|').Append((int)_mode).Append('|').Append(_glowSide).Append('|')
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
            if (_glowSide < 0 || !_glowMaterialHit || _target == null || !_target.IsValid) return;
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
                EditorGUI.DrawRect(r, CustomHairColorToolCore.GlowShotBackground);
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
            if (g == null || _glowSide < 0) return false;
            var hair = CustomHairColorToolCore.GlowRenderers(_target, g);
            var mats = new HashSet<Material>(g.Setting.TargetMaterials.Where(m => m != null));
            if (hair.Count == 0) return false;
            try
            {
                var size = _glowBig != null ? GlowShotSizeLarge : GlowShotSize;
                _glowShot = CustomHairColorToolCore.CaptureDarkRoom(_target.Avatar, hair, size, _glowShot, mats);
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
            EditorGUI.DrawRect(all, CustomHairColorToolCore.GlowShotBackground);
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
