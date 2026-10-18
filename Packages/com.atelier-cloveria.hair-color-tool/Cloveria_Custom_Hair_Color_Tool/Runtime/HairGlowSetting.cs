using UnityEngine;
#if AC_CHCT_NDMF_PRESENT
using nadena.dev.ndmf;
#endif

namespace AtelierCloveria.CustomHairColorTool
{
    // ビルド時に NDMF プラグインが髪のマテリアルの複製へ書き込み、この部品を外す（光る毛束と色は発光 Decal 側）
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class HairGlowSetting : MonoBehaviour
#if AC_CHCT_NDMF_PRESENT
        , INDMFEditorOnly
#endif
    {
        public Material[] TargetMaterials = new Material[0];

        public string EmissionPrefix = "_Emission2nd";

        // TargetMaterials と同じ並び。大きさがそのまま発光の画像の大きさになる（TTT は書き先の大きさで描くため）
        // 髪の画像ごとに別の下地にする（同じ下地を指すと TTT が1枚にまとめて描くため）
        public Texture2D[] BlackMaps = new Texture2D[0];

        // lilToon の発光は「発光の画像 × 色」のため、これが光の強さになる
        public float Strength = 0.6f;

        // 髪の画像が Repeat でない商品では FlowMask を空にする
        public Texture2D FlowMask;
        public Vector2 FlowMaskTiling = new Vector2(1f, 3f);
        public bool FlowOn = false;
        public float FlowVelocity = 0.5f; // ＋で画像の上から下へ流れる
        public float PatternScale = 1f;
    }
}
