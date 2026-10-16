#if UNITY_EDITOR
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    public enum Tone
    {
        None = 0,
        Vivid = 1,
        Pastel = 2,
        Dull = 3,
        Dark = 4,
    }

    // HSV の S / V に足すオフセット（調整はここだけ）
    public static class ToneTable
    {
        public const float BaseSaturation = 0.65f;
        public const float BaseValue = 0.85f;

        public const float VividSaturationOffset = +0.35f;
        public const float VividValueOffset = +0.15f;

        public const float PastelSaturationOffset = -0.35f;
        public const float PastelValueOffset = +0.15f;

        public const float DullSaturationOffset = -0.30f;
        public const float DullValueOffset = -0.20f;

        public const float DarkSaturationOffset = +0.05f;
        public const float DarkValueOffset = -0.45f;

        public static readonly Tone[] Order = { Tone.None, Tone.Vivid, Tone.Pastel, Tone.Dull, Tone.Dark };

        public static string Label(Tone tone)
        {
            switch (tone)
            {
                case Tone.Vivid: return "ビビッド";
                case Tone.Pastel: return "パステル";
                case Tone.Dull: return "くすみ";
                case Tone.Dark: return "ダーク";
                default: return "標準";
            }
        }

        public static void GetOffset(Tone tone, out float saturationOffset, out float valueOffset)
        {
            switch (tone)
            {
                case Tone.Vivid: saturationOffset = VividSaturationOffset; valueOffset = VividValueOffset; break;
                case Tone.Pastel: saturationOffset = PastelSaturationOffset; valueOffset = PastelValueOffset; break;
                case Tone.Dull: saturationOffset = DullSaturationOffset; valueOffset = DullValueOffset; break;
                case Tone.Dark: saturationOffset = DarkSaturationOffset; valueOffset = DarkValueOffset; break;
                default: saturationOffset = 0f; valueOffset = 0f; break;
            }
        }

        public static void GetSV(Tone tone, out float saturation, out float value)
        {
            GetOffset(tone, out var sOff, out var vOff);
            saturation = Mathf.Clamp01(BaseSaturation + sOff);
            value = Mathf.Clamp01(BaseValue + vOff);
        }

        public static Color Compose(float hue, Tone tone)
        {
            GetSV(tone, out var s, out var v);
            var c = Color.HSVToRGB(Mathf.Repeat(hue, 1f), s, v, false);
            c.a = 1f;
            return c;
        }

        // 四角で点を動かしたあとはどのトーンにも当たらないことが多い
        public static Tone? Match(float saturation, float value)
        {
            foreach (var tone in Order)
            {
                GetSV(tone, out var s, out var v);
                if (Mathf.Abs(s - saturation) < 0.02f && Mathf.Abs(v - value) < 0.02f) return tone;
            }
            return null;
        }
    }
}
#endif
