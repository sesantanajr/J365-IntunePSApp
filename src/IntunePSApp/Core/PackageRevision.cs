using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace J365.IntunePSApp.Core
{
    /// <summary>Identifies the settings used to build a package, independently of its output location.</summary>
    public static class PackageRevision
    {
        public const string FileName = "build-revision.json";

        public static string Fingerprint(PackageSettings settings)
        {
            var copy = settings.Clone();
            PackageBuilder.Migrate(copy);
            copy.OutputRoot = null;
            if (string.IsNullOrWhiteSpace(copy.DetectionNamePattern)) copy.DetectionNamePattern = copy.UninstallPattern;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(copy, Json.Options))));
        }

        public static void Save(string folder, PackageSettings settings)
            => Json.Save(Path.Combine(folder, FileName), new Stamp { SettingsHash = Fingerprint(settings) });

        // Legacy packages remain readable. A present but unreadable stamp must never be treated as legacy.
        public static bool Matches(string folder, PackageSettings settings)
        {
            string file = Path.Combine(folder, FileName);
            if (!File.Exists(file)) return true;
            try
            {
                var stamp = JsonSerializer.Deserialize<Stamp>(File.ReadAllText(file), Json.Options);
                return stamp?.Schema == 1 && stamp.SettingsHash == Fingerprint(settings);
            }
            catch { return false; }
        }

        public sealed class Stamp
        {
            public int Schema { get; set; } = 1;
            public string SettingsHash { get; set; }
        }
    }

    /// <summary>Reserves an unused output directory across processes. Never reuses an existing package.</summary>
    public sealed class PackageDirectory : IDisposable
    {
        private readonly FileStream _reservation;
        public string Folder { get; }
        private PackageDirectory(string folder, FileStream reservation) { Folder = folder; _reservation = reservation; }

        public static PackageDirectory Reserve(string root, string name)
        {
            Directory.CreateDirectory(root);
            name = Names.Safe(name);
            UnauthorizedAccessException denied = null;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string folder = Path.Combine(root, UniqueName(name, attempt, DateTime.Now));
                if (Directory.Exists(folder) || File.Exists(folder)) continue;
                FileStream reservation;
                try { reservation = new FileStream(folder + ".build-lock", FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 80 or 183) { continue; }
                // Windows reports access denied while a competing DeleteOnClose handle of the same name is closing: the
                // next name is tried. A real permission problem of the folder fails every attempt and is thrown below.
                catch (UnauthorizedAccessException ex) { denied = ex; continue; }
                try
                {
                    if (Directory.Exists(folder) || File.Exists(folder)) { reservation.Dispose(); continue; }
                    Directory.CreateDirectory(folder);
                    return new PackageDirectory(folder, reservation);
                }
                catch { reservation.Dispose(); throw; }
            }
            if (denied != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(denied).Throw();
            throw new IOException("Could not reserve a unique package directory.");
        }

        /// <summary>
        /// The name of the folder for an attempt: the name itself, then the name with the date and time of the build, then
        /// with a counter ("7-Zip_26.06", "7-Zip_26.06_20261123-080610", "7-Zip_26.06_20261123-080610-2"). Until 2.6 the
        /// second one also carried a GUID of 32 characters, which took the paths inside a package in a OneDrive folder past
        /// the 260 characters IntuneWinAppUtil.exe accepts. The reservation file still arbitrates between processes.
        /// </summary>
        internal static string UniqueName(string name, int attempt, DateTime now)
        {
            if (attempt == 0) return name;
            string stamp = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            return attempt == 1 ? $"{name}_{stamp}" : $"{name}_{stamp}-{attempt}";
        }

        public void Dispose() => _reservation.Dispose();
    }
}
