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
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string folder = Path.Combine(root, attempt == 0 ? name : $"{name}_{DateTime.Now:yyyyMMdd-HHmmss}_{Guid.NewGuid():N}");
                if (Directory.Exists(folder) || File.Exists(folder)) continue;
                FileStream reservation;
                try { reservation = new FileStream(folder + ".build-lock", FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 80 or 183) { continue; }
                // Windows can report access denied while a competing DeleteOnClose handle is closing.
                // Retry with a unique name once; a real directory permission error then propagates.
                catch (UnauthorizedAccessException) when (attempt == 0) { continue; }
                try
                {
                    if (Directory.Exists(folder) || File.Exists(folder)) { reservation.Dispose(); continue; }
                    Directory.CreateDirectory(folder);
                    return new PackageDirectory(folder, reservation);
                }
                catch { reservation.Dispose(); throw; }
            }
            throw new IOException("Could not reserve a unique package directory.");
        }

        public void Dispose() => _reservation.Dispose();
    }
}
