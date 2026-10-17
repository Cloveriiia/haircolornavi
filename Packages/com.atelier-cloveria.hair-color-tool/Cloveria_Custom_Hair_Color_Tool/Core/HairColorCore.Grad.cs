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
    public sealed class PerColorGradientParts
    {
        public GameObject Root;
        public GameObject Node;
        public readonly GameObject[] Wrappers;
        public readonly List<SingleGradationDecal>[] Decals;
        // パーツ名→おすすめの色が変わる位置（無ければ均等）
        public Dictionary<string, GradientShape> Suggested = new Dictionary<string, GradientShape>(System.StringComparer.OrdinalIgnoreCase);

        public int ColorCount => Wrappers.Length;

        public PerColorGradientParts(int colorCount)
        {
            Wrappers = new GameObject[colorCount];
            Decals = Enumerable.Range(0, colorCount).Select(_ => new List<SingleGradationDecal>()).ToArray();
        }
    }

    public static partial class HairColorCore
    {
        public const string GradNodeName = "_Grad";

        // 古い構造の商品では null（色ごとのグラデを出さない）
        static PerColorGradientParts DetectColorGrad(Transform custom, ColoringModeParts mt)
        {
            if (mt.Decals.Count < 2) return null;
            var root = FindChild(custom, GradNodeName);
            if (root == null) return null;
            var node = FindChild(root, mt.Node.name);
            if (node == null) return null;
            var t = new PerColorGradientParts(mt.Decals.Count) { Root = root.gameObject, Node = node.gameObject };
            for (var c = 0; c < t.ColorCount; c++)
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
            var group = ColoringModes.Get(ColoringMode.Gradation).NodeNames.Select(n => FindChild(product, n)).FirstOrDefault(n => n != null);
            if (group == null) return map;
            for (var i = 0; i < group.childCount; i++)
            {
                var decals = CollectDecals(group.GetChild(i));
                if (decals.Count == 0) continue;
                foreach (var d in decals) map[d.name] = GetDecalGradient(d);
                break;
            }
            return map;
        }

        // 包みのオン・オフで見る（ウィンドウには覚えない）
        public static bool IsPerColorGradientOn(PerColorGradientParts g, int colorIndex)
            => g != null && colorIndex >= 0 && colorIndex < g.ColorCount && g.Wrappers[colorIndex] != null && g.Wrappers[colorIndex].activeSelf;

        public static bool IsAnyPerColorGradientOn(PerColorGradientParts g)
            => g != null && Enumerable.Range(0, g.ColorCount).Any(c => IsPerColorGradientOn(g, c));
    }
}
#endif
#endif
