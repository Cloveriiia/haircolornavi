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
    public static partial class HairColorCore
    {
        internal static readonly Color GlowShotBackground = new Color(0.05f, 0.05f, 0.07f, 1f);
        const float GlowShotAmbient = 0.04f;
        const float GlowShotLight = 0.15f;
        const float GlowShotFov = 20f;

        // 開発用：明かりを変えたあとに例外を投げて、戻るかを確かめる
        static Action s_glowShotTestHook;

        internal static List<Renderer> FindGlowRenderers(ColoringProduct target, GlowParts g)
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
        internal static Texture2D CaptureGlowInDarkRoom(GameObject avatar, IList<Renderer> hair, int size, Texture2D reuse, ICollection<Material> only = null)
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
}
#endif
#endif
