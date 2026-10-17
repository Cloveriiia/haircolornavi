#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System.Collections.Generic;
using System.Linq;
using net.rs64.TexTransTool.IslandSelector;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    // 開発用の HairIslandPicker から必要な所だけ写したもの（_Dev は出荷しないため参照しない）

    public sealed class StrandIsland
    {
        public Renderer Renderer;
        public int SubMesh;
        public Vector3[] TriWorld;   // 3つで1三角形
        public Vector2[] TriUv;
        public Bounds Bounds;
        public readonly List<int> Pairs = new List<int>();
    }

    // 1色目（Right / MeshA）と2色目（Left / MeshB）に、同じ所へ刺した目印が1本ずつある
    public sealed class PinPair
    {
        public Vector3 Point;
        public string Name;
        public readonly List<PinIslandSelector> A = new List<PinIslandSelector>();
        public readonly List<PinIslandSelector> B = new List<PinIslandSelector>();
        public readonly List<PinIslandSelector> Cut = new List<PinIslandSelector>(); // 真ん中で切る毛束の目印（_Cut 仲間の Decal が参照する側）
        public readonly List<Vector3> Alt = new List<Vector3>();

        public bool OnA => A.Any(p => p != null && p.gameObject.activeSelf);
        public bool OnB => B.Any(p => p != null && p.gameObject.activeSelf);
        public bool IsCut => Cut.Any(p => p != null && p.gameObject.activeSelf);
    }

    public static class StrandPicker
    {
        // 原点から遠い所は float の丸めでずれるため、距離の内側でなく「いちばん近いもの」で選ぶ（上限 1 mm）
        public const float MatchDistance = 0.001f;
        const float TieDistance = 0.00002f; // 同じ面に重なる毛束は一緒に結ぶ（0.02 mm）

        // 目印は重心＋法線×off から長さ 2×off で刺してあるため、線分の真ん中が重心になる（長さは TTT と同じ計算）
        public static Vector3 PinMidpoint(PinIslandSelector pin)
        {
            var t = pin.transform;
            return t.position + t.forward * (t.lossyScale.z * pin.IslandSelectorRange * 0.5f);
        }

        // TTT の OR と同じく直下の子だけを見て、子が OR ならその中も見る（入れ子の Part_<パーツ> も拾う）
        public static void CollectStrandPins(Transform or, List<PinIslandSelector> result)
        {
            if (or == null) return;
            for (var i = 0; i < or.childCount; i++)
            {
                var c = or.GetChild(i);
                var pin = c.GetComponent<PinIslandSelector>();
                if (pin != null) { result.Add(pin); continue; }
                if (c.GetComponent<IslandSelectorOR>() != null) CollectStrandPins(c, result);
            }
        }

        // 「+」付きはシェイプキーで動いた形の上に刺した追加の目印（例：Hair_Bangs_007+Ahoge_SideON）
        // 刺した所に毛束が無い形もあるため、位置でなく「+」の前の名前で元の組に入れる
        public static bool IsExtraPin(PinIslandSelector pin) => pin != null && pin.gameObject.name.IndexOf('+') >= 0;

        static string BaseName(PinIslandSelector pin) => pin.gameObject.name.Substring(0, pin.gameObject.name.IndexOf('+'));

        public static List<PinPair> PairStrandPins(List<PinIslandSelector> a, List<PinIslandSelector> b, List<PinIslandSelector> cut)
        {
            var pairs = new List<PinPair>();
            var cell = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x * 1000f), Mathf.FloorToInt(p.y * 1000f), Mathf.FloorToInt(p.z * 1000f));

            // ます目は 1 mm のため、隣のますまで見れば上限の内側は全部見られる
            PinPair Find(Vector3 p)
            {
                var k = Key(p);
                PinPair best = null;
                var bestD = MatchDistance * MatchDistance;
                for (var x = -1; x <= 1; x++)
                    for (var y = -1; y <= 1; y++)
                        for (var z = -1; z <= 1; z++)
                        {
                            if (!cell.TryGetValue(new Vector3Int(k.x + x, k.y + y, k.z + z), out var list)) continue;
                            foreach (var i in list)
                            {
                                var d = (pairs[i].Point - p).sqrMagnitude;
                                if (d <= bestD) { bestD = d; best = pairs[i]; }
                            }
                        }
                return best;
            }

            void Add(PinIslandSelector pin, int side)
            {
                if (pin == null) return;
                var p = PinMidpoint(pin);
                var pair = Find(p);
                if (pair == null)
                {
                    pair = new PinPair { Point = p, Name = pin.gameObject.name };
                    var k = Key(p);
                    if (!cell.TryGetValue(k, out var list)) cell[k] = list = new List<int>();
                    list.Add(pairs.Count);
                    pairs.Add(pair);
                }
                (side == 0 ? pair.A : side == 1 ? pair.B : pair.Cut).Add(pin);
            }

            var sides = new[] { a, b, cut ?? new List<PinIslandSelector>() };
            for (var s = 0; s < sides.Length; s++)
                foreach (var p in sides[s]) if (!IsExtraPin(p)) Add(p, s);

            var byName = new Dictionary<string, PinPair>();
            foreach (var pair in pairs) if (!byName.ContainsKey(pair.Name)) byName[pair.Name] = pair;
            for (var s = 0; s < sides.Length; s++)
            {
                foreach (var p in sides[s])
                {
                    if (!IsExtraPin(p)) continue;
                    if (!byName.TryGetValue(BaseName(p), out var pair)) { Add(p, s); continue; }
                    (s == 0 ? pair.A : s == 1 ? pair.B : pair.Cut).Add(p);
                    pair.Alt.Add(PinMidpoint(p));
                }
            }
            return pairs;
        }

        // アバターを動かした・Undo で戻したときのため、毛束を作り直すときに一緒に呼ぶ
        public static void RefreshPairPoints(List<PinPair> pairs)
        {
            if (pairs == null) return;
            foreach (var p in pairs)
            {
                var all = p.A.Concat(p.B).Concat(p.Cut).Where(x => x != null).ToList();
                var pin = all.FirstOrDefault(x => !IsExtraPin(x)) ?? all.FirstOrDefault();
                if (pin != null) p.Point = PinMidpoint(pin);
                p.Alt.Clear();
                foreach (var x in all) if (IsExtraPin(x) && x != pin) p.Alt.Add(PinMidpoint(x));
            }
        }

        // materials が null なら全サブメッシュ
        public static List<StrandIsland> BuildStrandIslands(IEnumerable<Renderer> renderers, ICollection<Material> materials)
        {
            var result = new List<StrandIsland>();
            foreach (var r in renderers.Distinct())
            {
                if (r == null || !(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                var mesh = GetMesh(r);
                if (mesh == null) continue;
                var mats = r.sharedMaterials;
                Vector3[] world = null;
                Vector2[] uvs = null;
                for (var sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
                {
                    if (materials != null && (mats[sub] == null || !materials.Contains(mats[sub]))) continue;
                    if (world == null) { world = BakeWorldVertices(r, mesh); uvs = mesh.uv; }
                    var tris = mesh.GetTriangles(sub);
                    foreach (var g in SplitIslands(mesh.vertexCount, uvs, tris))
                    {
                        var isl = new StrandIsland { Renderer = r, SubMesh = sub, TriWorld = new Vector3[g.Count], TriUv = new Vector2[g.Count] };
                        for (var i = 0; i < g.Count; i++)
                        {
                            isl.TriWorld[i] = world[g[i]];
                            isl.TriUv[i] = uvs.Length > g[i] ? uvs[g[i]] : Vector2.zero;
                        }
                        var b = new Bounds(isl.TriWorld[0], Vector3.zero);
                        foreach (var v in isl.TriWorld) b.Encapsulate(v);
                        isl.Bounds = b;
                        result.Add(isl);
                    }
                }
            }
            return result;
        }

        public static void MatchIslandsToPairs(List<StrandIsland> islands, List<PinPair> pairs)
        {
            foreach (var isl in islands) isl.Pairs.Clear();
            if (pairs == null) return;
            var dist = new float[islands.Count];
            for (var pi = 0; pi < pairs.Count; pi++)
            {
                var best = Nearest(islands, pairs[pi].Point, dist);
                for (var ai = 0; best >= MatchDistance && ai < pairs[pi].Alt.Count; ai++) best = Nearest(islands, pairs[pi].Alt[ai], dist);
                if (best >= MatchDistance) continue; // ポーズ・シェイプキーが書き出したときと違う
                for (var ii = 0; ii < islands.Count; ii++)
                    if (dist[ii] <= best + TieDistance) islands[ii].Pairs.Add(pi);
            }
        }

        static float Nearest(List<StrandIsland> islands, Vector3 p, float[] dist)
        {
            var best = MatchDistance;
            for (var ii = 0; ii < islands.Count; ii++)
            {
                dist[ii] = float.MaxValue;
                var b = islands[ii].Bounds;
                b.Expand(MatchDistance * 2f);
                if (!b.Contains(p)) continue;
                var t = islands[ii].TriWorld;
                for (var k = 0; k + 2 < t.Length; k += 3) dist[ii] = Mathf.Min(dist[ii], ((t[k] + t[k + 1] + t[k + 2]) / 3f - p).magnitude);
                best = Mathf.Min(best, dist[ii]);
            }
            return best;
        }

        public static int RaycastStrand(List<StrandIsland> islands, Ray ray, out float distance)
        {
            var best = -1;
            distance = float.MaxValue;
            for (var i = 0; i < islands.Count; i++)
            {
                if (!islands[i].Bounds.IntersectRay(ray, out var near) || near > distance) continue;
                var t = islands[i].TriWorld;
                for (var k = 0; k + 2 < t.Length; k += 3)
                {
                    if (RayTriangle(ray, t[k], t[k + 1], t[k + 2], out var d) && d < distance) { distance = d; best = i; }
                }
            }
            return best;
        }

        // Möller–Trumbore（裏表どちらからでも当たる）
        static bool RayTriangle(Ray r, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(r.direction, e2);
            var det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            var inv = 1f / det;
            var s = r.origin - a;
            var u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            var v = Vector3.Dot(r.direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        // 焼き方と行列は TTT と目印の書き出しに合わせる（違うと目印と毛束がずれて見つからない）
        static Vector3[] BakeWorldVertices(Renderer r, Mesh mesh)
        {
            Vector3[] local;
            Matrix4x4 m;
            if (r is SkinnedMeshRenderer smr)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                local = baked.vertices;
                Object.DestroyImmediate(baked);
                if (smr.bones.Length > 0) m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                else if (smr.rootBone == null) m = smr.localToWorldMatrix;
                else m = smr.rootBone.localToWorldMatrix;
            }
            else
            {
                local = mesh.vertices;
                m = r.transform.localToWorldMatrix;
            }
            var w = new Vector3[local.Length];
            for (var i = 0; i < local.Length; i++) w[i] = m.MultiplyPoint3x4(local[i]);
            return w;
        }

        // TTT が毛束を区切る規則に合わせ、UV が完全一致の頂点を同じとみなす
        static List<List<int>> SplitIslands(int vertexCount, Vector2[] uvs, int[] tris)
        {
            var parent = new int[vertexCount];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            void Union(int a, int b)
            {
                a = Find(a); b = Find(b);
                if (a != b) parent[a] = b;
            }
            var keyToVertex = new Dictionary<Vector2, int>();
            foreach (var v in tris)
            {
                var uv = uvs.Length > v ? uvs[v] : Vector2.zero;
                if (keyToVertex.TryGetValue(uv, out var same)) Union(v, same);
                else keyToVertex[uv] = v;
            }
            for (var i = 0; i < tris.Length; i += 3)
            {
                Union(tris[i], tris[i + 1]);
                Union(tris[i], tris[i + 2]);
            }
            var byRoot = new Dictionary<int, List<int>>();
            var result = new List<List<int>>();
            for (var i = 0; i < tris.Length; i += 3)
            {
                var k = Find(tris[i]);
                if (!byRoot.TryGetValue(k, out var list)) { list = new List<int>(); byRoot[k] = list; result.Add(list); }
                list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
            }
            return result;
        }

        // ---- Scene に毛束を描く ----
        static Material s_mat;

        public static void DrawStrandOutline(StrandIsland isl, Color c, Vector3 cam)
        {
            if (isl == null) return;
            if (s_mat == null)
            {
                s_mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
                s_mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                s_mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                s_mat.SetInt("_ZWrite", 0);
            }
            s_mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            s_mat.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.TRIANGLES);
            GL.Color(c);
            // 髪の面とのちらつきを防ぐため、カメラ側に少し浮かせる
            foreach (var v in isl.TriWorld) GL.Vertex(v + (cam - v).normalized * 0.0008f);
            GL.End();
            GL.PopMatrix();
        }
    }
}
#endif
#endif
