#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    public sealed class HairColoringInfo : ScriptableObject
    {
        public GameObject Prefab;
        public string ProductName = "";
        public string BoothUrl = "";
        public string MinToolVersion = "";
    }

    public enum InfoState
    {
        Ok = 0,
        Missing = 1,
        ToolTooOld = 2,
    }

    public static class HairColoringInfos
    {
        static readonly string[] WithoutInfo =
        {
            "Shiroa_Hair_Coloring",
            "Eku_Hair_Coloring",
            "Mayo_Hair_Coloring_v2",
            "Nedux_Hair_Coloring",
            "Nedux_Hair_Coloring_v2",
            "Michella_Hair_Coloring",
            "Ichigo_Hair_Coloring_v2",
            "Kogomi_Hair_Coloring",
            "Yugachan_Hair_Coloring",
            "Ellel_Hair_Coloring",
        };

        static List<HairColoringInfo> _infos;

        public static void Forget() => _infos = null;

        public static InfoState Check(GameObject productRoot, out HairColoringInfo info)
        {
            info = null;
            var name = productRoot != null ? productRoot.name : "";
            if (WithoutInfo.Any(n => SameName(n, name))) return InfoState.Ok;
            if (_infos == null)
            {
                _infos = AssetDatabase.FindAssets("t:" + nameof(HairColoringInfo))
                    .Select(g => AssetDatabase.LoadAssetAtPath<HairColoringInfo>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(c => c != null)
                    .ToList();
            }
            info = _infos.FirstOrDefault(c => c != null && c.Prefab != null && SameName(c.Prefab.name, name));
            if (info == null) return InfoState.Missing;
            return IsOlder(ToolInfo.Version, info.MinToolVersion) ? InfoState.ToolTooOld : InfoState.Ok;
        }

        static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static bool IsOlder(string tool, string min) => Version.TryParse(min, out var m) && Version.TryParse(tool, out var t) && t < m;
    }
}
#endif
