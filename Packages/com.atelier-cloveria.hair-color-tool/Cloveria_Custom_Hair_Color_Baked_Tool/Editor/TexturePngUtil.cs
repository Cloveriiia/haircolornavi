#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace AtelierCloveria.CustomHairColorBakedTool
{
    /// <summary>圧縮済み・読み取り不可・RenderTexture も PNG にする。
    /// Linear のプロジェクトで sRGB の画像を Linear の RT に Blit すると暗く保存されるため、RT の ReadWrite を元に合わせる</summary>
    internal static class TexturePngUtil
    {
        public static bool IsSRGB(Texture tex)
        {
            if (tex == null) return true;
            try
            {
                return GraphicsFormatUtility.IsSRGBFormat(tex.graphicsFormat);
            }
            catch
            {
                return true;
            }
        }

        public static byte[] EncodeToPng(Texture tex, out bool isSRGB)
        {
            if (tex == null) throw new ArgumentNullException(nameof(tex));
            isSRGB = IsSRGB(tex);

            int w = tex.width;
            int h = tex.height;

            if (tex is Texture2D t2d && t2d.isReadable &&
                (t2d.format == TextureFormat.RGBA32 || t2d.format == TextureFormat.ARGB32 || t2d.format == TextureFormat.RGB24))
            {
                return t2d.EncodeToPNG();
            }

            RenderTexture rt = null;
            Texture2D readable = null;
            var prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(
                    w, h, 0,
                    RenderTextureFormat.ARGB32,
                    isSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);

                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;

                readable = new Texture2D(w, h, TextureFormat.RGBA32, false, linear: !isSRGB);
                readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readable.Apply(false, false);

                return readable.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        public static int CeilPow2(int v)
        {
            int p = 32;
            while (p < v && p < 16384) p <<= 1;
            return p;
        }
    }
}
#endif
