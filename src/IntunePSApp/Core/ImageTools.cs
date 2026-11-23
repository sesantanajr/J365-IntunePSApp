using System;
using System.Collections.Generic;
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
        public const string IconInstalled = "installed";
        public const string IconGenerated = "generated";

        /// <summary>Embedded cabinets larger than this are not copied out of an .msi just to read an icon.</summary>
        private const long MaxCabinetBytes = 700L * 1024 * 1024;

        /// <summary>
        /// Writes the icon of the application as a 256 px PNG and tells where it came from. Order: the icon chosen by the
        /// user; the icon of the installer (.exe resources; for an .msi its Icon table, otherwise the program its shortcuts
        /// open, read from the cabinet of the package); the icon of the same application already installed on this
        /// computer (<paramref name="installedPattern"/>, its DisplayIcon); and, when there is none, a tile with the
        /// initials. A package therefore always has an icon and nothing has to be downloaded for it.
        /// </summary>
        public static string SaveAppIcon(string customIcon, string installerPath, string appName, string colorKey, string destination, string installedPattern = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
            if (!string.IsNullOrWhiteSpace(customIcon) && File.Exists(customIcon) && SaveIconFromFile(customIcon, destination)) return IconCustom;
            if (!string.IsNullOrWhiteSpace(installerPath) && File.Exists(installerPath) && SaveInstallerIconCached(installerPath, destination)) return IconInstaller;
            if (!string.IsNullOrWhiteSpace(installedPattern) && SaveInstalledIcon(installedPattern, destination)) return IconInstalled;
            SaveInitialsIcon(appName, Names.AvatarColor(string.IsNullOrEmpty(colorKey) ? appName : colorKey), destination);
            return IconGenerated;
        }

        /// <summary>
        /// <see cref="SaveInstallerIcon"/> with a cache: reading the program out of a large .msi takes a few seconds, and
        /// the editor and the build ask for the same icon. The key is the name, size and date of the file (a copy of the
        /// installer inside a package folder keeps all three), so a new download of the installer is read again.
        /// </summary>
        public static bool SaveInstallerIconCached(string installerPath, string destination)
        {
            string png = null, none = null;
            try
            {
                var fi = new FileInfo(installerPath);
                // v3: the generic icon of setup programs is no longer taken as the icon of the application (2.6).
                string id = $"{fi.Name.ToLowerInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|v3";
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
                png = Path.Combine(AppPaths.IconCache, hash + ".png");
                none = Path.Combine(AppPaths.IconCache, hash + ".none");
                if (File.Exists(png)) { File.Copy(png, destination, true); return true; }
                if (File.Exists(none)) return false;
            }
            catch { png = none = null; }

            string work = Path.Combine(AppPaths.Temp, $"icon_{Guid.NewGuid():N}.png");
            try
            {
                bool ok = SaveInstallerIcon(installerPath, work);
                try
                {
                    if (ok && png != null) File.Copy(work, png, true);
                    else if (!ok && none != null) File.WriteAllText(none, installerPath);
                }
                catch { }
                if (ok) File.Copy(work, destination, true);
                return ok;
            }
            catch { return false; }
            finally { try { File.Delete(work); } catch { } }
        }

        /// <summary>Icon of an installer: the resources of an .exe; for an .msi the product icon, else the icon of the program it installs.</summary>
        public static bool SaveInstallerIcon(string installerPath, string destination)
        {
            if (!Path.GetExtension(installerPath).Equals(".msi", StringComparison.OrdinalIgnoreCase)) return SaveAppExeIcon(installerPath, destination);
            return SaveMsiTableIcon(installerPath, destination) || SaveMsiProgramIcon(installerPath, destination);
        }

        private static bool SaveMsiTableIcon(string msiPath, string destination)
        {
            var icon = MsiReader.ReadIcon(msiPath);
            if (icon == null) return false;
            // The stream is an .ico or an .exe/.dll: Windows reads either from a file with the right extension.
            string ext = Path.GetExtension(icon.Value.Name ?? "").ToLowerInvariant();
            if (ext != ".ico" && ext != ".exe" && ext != ".dll") ext = LooksLikeIco(icon.Value.Data) ? ".ico" : ".exe";
            string temp = Path.Combine(AppPaths.Temp, $"msiicon_{Guid.NewGuid():N}{ext}");
            try
            {
                File.WriteAllBytes(temp, icon.Value.Data);
                return SaveAppExeIcon(temp, destination);
            }
            catch { return false; }
            finally { try { File.Delete(temp); } catch { } }
        }

        /// <summary>
        /// An .msi without an icon of its own (7-Zip, for example): the program its shortcuts open is read from the
        /// cabinet of the package - embedded in the .msi or next to it - with expand.exe, and its icon is used.
        /// Nothing is installed and nothing is downloaded.
        /// </summary>
        public static bool SaveMsiProgramIcon(string msiPath, string destination)
        {
            string work = Path.Combine(AppPaths.Temp, "msiprog_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(work);
                var cabinets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in MsiReader.IconCandidates(msiPath))
                {
                    if (string.IsNullOrEmpty(f.Cabinet)) continue;
                    if (!cabinets.TryGetValue(f.Cabinet, out string cab))
                    {
                        if (f.Cabinet.StartsWith("#", StringComparison.Ordinal))
                        {
                            cab = Path.Combine(work, $"embedded{cabinets.Count}.cab");
                            if (!MsiReader.SaveStream(msiPath, f.Cabinet.Substring(1), cab, MaxCabinetBytes)) cab = null;
                        }
                        else
                        {
                            cab = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(msiPath)) ?? "", f.Cabinet);
                            if (!File.Exists(cab)) cab = null;
                        }
                        cabinets[f.Cabinet] = cab;
                    }
                    if (cab == null) continue;
                    string exe = ExpandFromCabinet(cab, f.Key, work);
                    if (exe != null && SaveAppExeIcon(exe, destination)) return true;
                }
                return false;
            }
            catch { return false; }
            finally { try { Directory.Delete(work, true); } catch { } }
        }

        /// <summary>Extracts one file of a cabinet (MSI cabinets name their files by the key of the File table).</summary>
        private static string ExpandFromCabinet(string cab, string key, string work)
        {
            if (!Regex.IsMatch(key ?? "", @"^[\w.\-]+$")) return null;
            string outDir = Path.Combine(work, "x" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "expand.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = work
            };
            psi.ArgumentList.Add("-F:" + key);
            psi.ArgumentList.Add(cab);
            psi.ArgumentList.Add(outDir);
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return null;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(120000)) { try { p.Kill(true); } catch { } return null; }
            string file = Path.Combine(outDir, key);
            if (!File.Exists(file)) return null;
            // Keys often have no extension: Windows reads the icons of a program through a name it recognises.
            if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return file;
            File.Move(file, file + ".exe");
            return file + ".exe";
        }

        /// <summary>
        /// The icon of the application already installed on this computer: the DisplayIcon of the first program in
        /// "Installed apps" whose name matches the pattern (for example "C:\Program Files\7-Zip\7zFM.exe",0).
        /// </summary>
        public static bool SaveInstalledIcon(string pattern, string destination)
        {
            try
            {
                foreach (var p in InstallerAnalyzer.FindInstalled(pattern))
                {
                    var (file, index) = ParseIconLocation(p.DisplayIcon);
                    if (file.Length == 0 || !File.Exists(file)) continue;
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    // Inno Setup points DisplayIcon at its uninstaller, which carries the icon of the setup program.
                    bool ok = ext == ".exe" || ext == ".dll" ? SaveAppExeIcon(file, destination, index) : SaveIconFromFile(file, destination) && !RejectGeneric(destination);
                    if (ok) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// "C:\App\app.exe",0 → (C:\App\app.exe, 0). Environment variables are expanded; a negative index is a resource id.
        /// </summary>
        public static (string Path, int Index) ParseIconLocation(string value)
        {
            string v = Environment.ExpandEnvironmentVariables((value ?? "").Trim());
            int index = 0;
            var m = Regex.Match(v, @"^(?<p>.*?)\s*,\s*(?<i>-?\d{1,9})\s*$");
            if (m.Success)
            {
                v = m.Groups["p"].Value;
                index = int.Parse(m.Groups["i"].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            return (v.Trim().Trim('"').Trim(), index);
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

        // ------------------------------------------------------------------ generic setup icon

        /// <summary>
        /// The default icon of setup programs, a computer with a CD: Inno Setup (SetupClassicIcon) and the WiX Burn
        /// bootstrapper use it when the vendor gives none (WinMerge, .NET runtimes, Visual C++ Redistributable...). It says
        /// nothing about the application, so it never becomes the icon of the Company Portal. The reference is a 16x16 RGB
        /// thumbnail on white, measured from those installers; other icons of applications measured at least 63 from it.
        /// </summary>
        internal static readonly byte[] GenericSetupThumbnail = Convert.FromBase64String(
            "////////////////////////4eHht7e5sK+0sK+0r6+0sK+0sK+0s7O1kZGRwcHB////////////////////////ysrJhofBO2u/NXvDSoXDPYXDN2u/hojDoqKiiYmJ////////////////////////ysrJgIPBWNL3ff3/ff3/Wtr9IKLXgYLAoqKiiYmJ////////////////+Pj4+Pj4xcXEgYTBeO/3fP3/Wtn9H6faAZHJgYLAoqKiiYmJ////////////3t7ex8fHvr6+V1dWf4K+du73Wtr9H6jaA5jOInDqgIDCoqKiiYmJ+Pj4+Pj429vbc3NzYmJiSklKTk5NhojBKXa+KGO3D1WqGEq2Izq/hobCoqKiiYmJsLCwxMTEwsLCiYmJSUhJlpWYSkpK0NDTzs3Tzs3Tzs3Tzs7Tzs7T0tLVmpqah4eH9fX1hISEfn5+RkZGmZibm5qdLi4uTU1NSkpLSkpKTExMT09PT09PT09PXV1dU1NT+Pj4yMjI+fn5gICAmpmcm5qdYWJjnbaou72Lqal4l5eVqqqqurq6u7u72NjYSEhI+Pj4ysrK////goKClJOVjo+RP5SsXaim6O2LzMxqrKypiomKQUFBZGRksLCwb29v+Pj4ysrK/v7+gYGBjIuMio6NUtfMQam/jL6XvL2LsK+x39/fXl5ehoaGkJCQ9PT0+Pj4wMDA6OjoeHh4i4uLkpKSrMe6fLyxVn19eXl3y8vMqqqqdnZ28/Pz////////+Pj4v7+/6OjoeHh4i4qLlpaWvr2+sK+wfX17gIWDtbW1qampf39/8/Pz////////+Pj4v7+/6OjoeHh4iIiIbm5usrKyvLy7zdOaOLSyu+3X3OHfm5ub+fn5////////+Pj4ubm54ODgdHR0cHBwzc3Nq6urwsK14uWcUcvKMLW0h8qryMrJ////////////+Pj4kZGRj4+Pj4+P8/Pz////1tbWpqaan598Wol+XISQysvK////////////////");

        /// <summary>Average difference (0-255 per channel) under which an icon is the generic setup icon (it measured up to 9).</summary>
        internal const double GenericSetupMaxDistance = 24;

        /// <summary>True when the image (a PNG written by this class) is the generic icon of setup programs.</summary>
        public static bool IsGenericSetupIcon(string png)
        {
            try
            {
                using var img = LoadImage(png);
                return ThumbnailDistance(Thumbnail(img), GenericSetupThumbnail) < GenericSetupMaxDistance;
            }
            catch { return false; }
        }

        /// <summary>16x16 RGB thumbnail on white (the image is drawn at 256 px first, as Windows extracts icons).</summary>
        internal static byte[] Thumbnail(Image src)
        {
            using var big = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(big))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, 256, 256);
            }
            // Two steps of 4x: each one averages its area, as a thumbnail should.
            using var mid = Reduce(big, 64);
            using var small = Reduce(mid, 16);
            var bytes = new byte[16 * 16 * 3];
            int i = 0;
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    var c = small.GetPixel(x, y);
                    bytes[i++] = c.R; bytes[i++] = c.G; bytes[i++] = c.B;
                }
            return bytes;
        }

        private static Bitmap Reduce(Image src, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);   // no dark border from outside the image
            g.DrawImage(src, new Rectangle(0, 0, size, size), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attributes);
            return bmp;
        }

        internal static double ThumbnailDistance(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0) return double.MaxValue;
            long sum = 0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
            return (double)sum / a.Length;
        }

        /// <summary>Removes a PNG that holds the generic setup icon. True when it was one.</summary>
        private static bool RejectGeneric(string png)
        {
            if (!IsGenericSetupIcon(png)) return false;
            try { File.Delete(png); } catch { }
            return true;
        }

        /// <summary>The icon of a program that stands for the application: never the generic icon of setup programs.</summary>
        private static bool SaveAppExeIcon(string exePath, string destination, int index = 0)
            => SaveExeIcon(exePath, destination, index) && !RejectGeneric(destination);

        public static bool SaveExeIcon(string exePath, string destination, int index = 0)
        {
            try
            {
                var handles = new IntPtr[1];
                var ids = new uint[1];
                uint n = NativeMethods.PrivateExtractIcons(exePath, index, 256, 256, handles, ids, 1, 0);
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
