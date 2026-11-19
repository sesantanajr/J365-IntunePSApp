using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace J365.IntunePSApp.Core
{
    /// <summary>Imagens do pacote: logo (256 px), banner do estilo Classic e icone do aplicativo para o Portal da Empresa.</summary>
    public static class ImageTools
    {
        private static Image LoadImage(string path)
        {
            if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ico = new Icon(path, 256, 256);
                return ico.ToBitmap();
            }
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            return new Bitmap(Image.FromStream(ms));
        }

        public static void SaveLogoPng(string source, string destination, int size = 256)
        {
            using var src = LoadImage(source);
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                double scale = Math.Min((double)size / src.Width, (double)size / src.Height);
                int w = (int)Math.Round(src.Width * scale), h = (int)Math.Round(src.Height * scale);
                g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
            }
            bmp.Save(destination, ImageFormat.Png);
        }

        public static Color FromHex(string hex, string fallback = "0A5CA8")
        {
            string h = (hex ?? "").Trim().TrimStart('#');
            if (!Regex.IsMatch(h, "^[0-9A-Fa-f]{6}$")) h = fallback;
            return Color.FromArgb(255, Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }

        public static void SaveBanner(string logoPng, string company, string accentHex, string destination)
        {
            using var bmp = new Bitmap(450, 50);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int x = 12;
                if (!string.IsNullOrEmpty(logoPng) && File.Exists(logoPng))
                {
                    using var logo = LoadImage(logoPng);
                    g.DrawImage(logo, 8, 5, 38, 38);
                    x = 54;
                }
                using (var brush = new SolidBrush(FromHex(accentHex))) g.FillRectangle(brush, 0, 46, 450, 4);
                if (!string.IsNullOrWhiteSpace(company))
                {
                    using var font = new Font("Segoe UI", 17, FontStyle.Bold, GraphicsUnit.Pixel);
                    using var tb = new SolidBrush(Color.FromArgb(255, 32, 36, 44));
                    g.DrawString(company, font, tb, x, 11);
                }
            }
            bmp.Save(destination, ImageFormat.Png);
        }

        public static bool SaveExeIcon(string exePath, string destination)
        {
            try
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint n = NativeMethods.PrivateExtractIcons(exePath, 0, 256, 256, handles, ids, 1, 0);
                if (n == 0 || n == 0xFFFFFFFF || handles[0] == IntPtr.Zero) return false;
                try
                {
                    using var ico = Icon.FromHandle(handles[0]);
                    using var bmp = ico.ToBitmap();
                    bmp.Save(destination, ImageFormat.Png);
                    return true;
                }
                finally { NativeMethods.DestroyIcon(handles[0]); }
            }
            catch { return false; }
        }
    }
}
