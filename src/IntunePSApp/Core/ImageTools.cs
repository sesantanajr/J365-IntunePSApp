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

        // ------------------------------------------------------------------ application icon (Intune / Company Portal)

        public const string IconCustom = "custom";
        public const string IconInstaller = "installer";
        public const string IconGenerated = "generated";

        /// <summary>
        /// Writes the icon of the application as a 256 px PNG and tells where it came from. Order: the icon chosen by the
        /// user, the icon inside the installer (.exe resources or the Icon table of an .msi) and, when there is none,
        /// a tile with the initials of the application. A package therefore always has an icon and nothing has to be
        /// downloaded for it.
        /// </summary>
        public static string SaveAppIcon(string customIcon, string installerPath, string appName, string colorKey, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (!string.IsNullOrWhiteSpace(customIcon) && File.Exists(customIcon) && SaveIconFromFile(customIcon, destination)) return IconCustom;
            if (!string.IsNullOrWhiteSpace(installerPath) && File.Exists(installerPath) && SaveInstallerIcon(installerPath, destination)) return IconInstaller;
            SaveInitialsIcon(appName, Names.AvatarColor(string.IsNullOrEmpty(colorKey) ? appName : colorKey), destination);
            return IconGenerated;
        }

        /// <summary>Icon of an installer: the resources of an .exe, or the product icon stored in an .msi.</summary>
        public static bool SaveInstallerIcon(string installerPath, string destination)
        {
            if (!Path.GetExtension(installerPath).Equals(".msi", StringComparison.OrdinalIgnoreCase)) return SaveExeIcon(installerPath, destination);
            var icon = MsiReader.ReadIcon(installerPath);
            if (icon == null) return false;
            // The stream is an .ico or an .exe/.dll: Windows reads either from a file with the right extension.
            string ext = Path.GetExtension(icon.Value.Name ?? "").ToLowerInvariant();
            if (ext != ".ico" && ext != ".exe" && ext != ".dll") ext = LooksLikeIco(icon.Value.Data) ? ".ico" : ".exe";
            string temp = Path.Combine(AppPaths.Temp, $"msiicon_{Guid.NewGuid():N}{ext}");
            try
            {
                File.WriteAllBytes(temp, icon.Value.Data);
                return SaveExeIcon(temp, destination);
            }
            catch { return false; }
            finally { try { File.Delete(temp); } catch { } }
        }

        private static bool LooksLikeIco(byte[] data) => data.Length > 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0;

        /// <summary>An image (.png, .jpg, .bmp, .gif), an .ico or a program (.exe, .dll) as a 256 px PNG.</summary>
        public static bool SaveIconFromFile(string source, string destination)
        {
            try
            {
                string ext = Path.GetExtension(source).ToLowerInvariant();
                if (ext == ".exe" || ext == ".dll") return SaveExeIcon(source, destination);
                SaveLogoPng(source, destination, 256);
                return true;
            }
            catch { return false; }
        }

        /// <summary>A rounded tile with the initials of the application (the same look as the catalog list).</summary>
        public static void SaveInitialsIcon(string appName, string colorHex, string destination)
        {
            const int size = 256;
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var path = RoundedRectangle(new RectangleF(8, 8, size - 16, size - 16), 44))
                using (var fill = new SolidBrush(FromHex(colorHex)))
                    g.FillPath(fill, path);
                string text = Names.Initials(appName);
                using var font = new Font("Segoe UI", text.Length > 1 ? 104 : 124, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, font, Brushes.White, new RectangleF(0, 0, size, size - 6), format);
            }
            bmp.Save(destination, ImageFormat.Png);
        }

        private static GraphicsPath RoundedRectangle(RectangleF r, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
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
