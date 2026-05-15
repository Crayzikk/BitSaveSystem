using System.Collections;
using System.IO;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// Корутина, яка чекає WaitForEndOfFrame, робить ScreenCapture.CaptureScreenshotAsTexture(),
    /// стискає в JPG (~50 КБ за замовчуванням) і зберігає у тій самій папці, що й сейв.
    /// </summary>
    public static class ScreenshotService
    {
        public static IEnumerator CaptureAndSave(string folder, string fileNameNoExt,
                                                 int jpgQuality = 60, int maxWidth = 0)
        {
            yield return new WaitForEndOfFrame();

            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                if (maxWidth > 0 && tex.width > maxWidth)
                {
                    tex = ResizeTexture(tex, maxWidth);
                }

                byte[] jpg = tex.EncodeToJPG(Mathf.Clamp(jpgQuality, 1, 100));
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, fileNameNoExt + ".jpg");
                File.WriteAllBytes(path, jpg);
            }
            finally
            {
                Object.Destroy(tex);
            }
        }

        private static Texture2D ResizeTexture(Texture2D src, int maxWidth)
        {
            float ratio = (float)maxWidth / src.width;
            int w = maxWidth;
            int h = Mathf.RoundToInt(src.height * ratio);

            var rt = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGB24, false);
            dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }
    }
}
