#if UNITY_EDITOR
using System.Linq;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    public static class AvatarLookup
    {
        // 型名で判定（VRC SDK を参照しないため）
        public static bool IsAvatarDescriptor(Component c) => c != null && c.GetType().Name == "VRCAvatarDescriptor";

        public static bool IsAvatarRoot(GameObject go) => go != null && go.GetComponents<Component>().Any(IsAvatarDescriptor);

        // VRCAvatarDescriptor を持つ最上位。無ければ transform.root
        public static GameObject FindAvatarRoot(GameObject go)
        {
            if (go == null) return null;
            GameObject found = null;
            for (var t = go.transform; t != null; t = t.parent)
            {
                if (IsAvatarRoot(t.gameObject)) found = t.gameObject;
            }
            return found != null ? found : go.transform.root.gameObject;
        }
    }
}
#endif
