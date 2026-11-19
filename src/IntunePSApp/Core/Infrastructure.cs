using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    public static class Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static readonly JsonSerializerOptions Catalog = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static TValue Load<TValue>(string path) where TValue : new()
        {
            try
            {
                if (File.Exists(path)) return JsonSerializer.Deserialize<TValue>(File.ReadAllText(path, Encoding.UTF8), Options) ?? new TValue();
            }
            catch (Exception ex) { Log.Warn(T("Log_FileReadFailed", Path.GetFileName(path), ex.Message)); }
            return new TValue();
        }

        /// <summary>Atomic save: writes to a temporary file and swaps it in.</summary>
        public static void Save<TValue>(string path, TValue value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }
    }

    /// <summary>
    /// Application folders. Portable mode keeps everything next to the executable (IntunePSApp-Data);
    /// the per-user installed copy and read-only locations use %LOCALAPPDATA%\J365\IntunePSApp.
    /// </summary>
    public static class AppPaths
    {
        public static string ExeDir { get; } = AppContext.BaseDirectory.TrimEnd('\\');
        public static string ExePath { get; } = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "J365.IntunePSApp.exe");
        public static string InstallDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "J365 IntunePSApp");
        public static string UserDataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "J365", "IntunePSApp");
        public static string Data { get; }
        public static bool IsPortable { get; }
        public static bool IsInstalledCopy { get; }

        static AppPaths()
        {
            IsInstalledCopy = string.Equals(ExeDir, InstallDir, StringComparison.OrdinalIgnoreCase);
            string env = Environment.GetEnvironmentVariable("J365_INTUNEPSAPP_DATA");
            if (!string.IsNullOrWhiteSpace(env)) { Data = env; IsPortable = true; }
            else if (!IsInstalledCopy && CanWrite(ExeDir)) { Data = Path.Combine(ExeDir, "IntunePSApp-Data"); IsPortable = true; }
            else { Data = UserDataDir; IsPortable = false; }
            Directory.CreateDirectory(Data);
        }

        public static string Tools => Ensure(Path.Combine(Data, "tools"));
        public static string CatalogDir => Ensure(Path.Combine(Data, "catalog"));
        public static string Downloads => Ensure(Path.Combine(Data, "downloads"));
        public static string Branding => Ensure(Path.Combine(Data, "branding"));
        public static string Logs => Ensure(Path.Combine(Data, "logs"));
        public static string Temp => Ensure(Path.Combine(Data, "temp"));
        /// <summary>Portable: next to the data. Installed: Documents (a place the user can find).</summary>
        public static string DefaultOutput => IsPortable
            ? Path.Combine(Data, "Packages")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "J365 IntunePSApp");
        public static string ConfigFile => Path.Combine(Data, "config.json");

        private static string Ensure(string p) { Directory.CreateDirectory(p); return p; }

        private static bool CanWrite(string dir)
        {
            try
            {
                if (dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)) return false;
                string probe = Path.Combine(dir, ".j365-write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Removes leftovers from previous sessions (temporary icons, validation folders). Only entries older than a day:
        /// another instance (a second window, a command-line build) may be using the recent ones right now.
        /// </summary>
        public static void CleanTemp()
        {
            try
            {
                var limit = DateTime.UtcNow.AddDays(-1);
                foreach (var f in Directory.GetFiles(Temp)) { try { if (File.GetLastWriteTimeUtc(f) < limit) File.Delete(f); } catch { } }
                foreach (var d in Directory.GetDirectories(Temp)) { try { if (Directory.GetLastWriteTimeUtc(d) < limit) Directory.Delete(d, true); } catch { } }
            }
            catch { }
        }
    }

    public enum LogLevel { Info, Step, Ok, Warn, Error }

    public class LogEntry
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; }
        public string Text { get; set; }
        public string TimeLabel => Time.ToString("HH:mm:ss");
        public string Glyph => Level switch
        {
            LogLevel.Step => "",
            LogLevel.Ok => "",
            LogLevel.Warn => "",
            LogLevel.Error => "",
            _ => ""
        };
        public string Color => Level switch
        {
            LogLevel.Step => "#2563EB",
            LogLevel.Ok => "#16A34A",
            LogLevel.Warn => "#D97706",
            LogLevel.Error => "#DC2626",
            _ => "#64748B"
        };
    }

    public static class Log
    {
        private static readonly object Sync = new object();
        private static readonly AsyncLocal<string> PackageLog = new AsyncLocal<string>();
        public static event Action<LogEntry> Entry;

        /// <summary>When set, entries written by the current async flow are also appended to this file (build.log of a package).</summary>
        public static string PackageLogFile { get => PackageLog.Value; set => PackageLog.Value = value; }

        public static void Write(string text, LogLevel level = LogLevel.Info)
        {
            var e = new LogEntry { Level = level, Text = text };
            string line = $"{e.Time:yyyy-MM-dd HH:mm:ss}\t{level}\t{text}{Environment.NewLine}";
            lock (Sync)
            {
                try { File.AppendAllText(Path.Combine(AppPaths.Logs, $"app-{DateTime.Now:yyyyMMdd}.log"), line, Encoding.UTF8); } catch { }
                var pkg = PackageLogFile;
                if (!string.IsNullOrEmpty(pkg)) { try { File.AppendAllText(pkg, line, Encoding.UTF8); } catch { } }
            }
            try { Entry?.Invoke(e); } catch { }
        }

        public static void Info(string t) => Write(t, LogLevel.Info);
        public static void Step(string t) => Write(t, LogLevel.Step);
        public static void Ok(string t) => Write(t, LogLevel.Ok);
        public static void Warn(string t) => Write(t, LogLevel.Warn);
        public static void Error(string t) => Write(t, LogLevel.Error);

        /// <summary>Keeps the last 14 daily log files.</summary>
        public static void Prune()
        {
            try
            {
                foreach (var f in new DirectoryInfo(AppPaths.Logs).GetFiles("app-*.log").OrderByDescending(f => f.Name).Skip(14)) f.Delete();
            }
            catch { }
        }
    }

    public static class ConfigStore
    {
        private static AppConfig _current;
        public static AppConfig Current => _current ??= Load();

        private static AppConfig Load()
        {
            var c = Json.Load<AppConfig>(AppPaths.ConfigFile);
            if (string.IsNullOrWhiteSpace(c.OutputFolder)) c.OutputFolder = DefaultOutputFolder();
            if (string.IsNullOrWhiteSpace(c.CatalogUrl)) c.CatalogUrl = AppInfo.DefaultCatalogUrl;
            if (c.CatalogUpdateDays < 1) c.CatalogUpdateDays = 1;
            return c;
        }

        /// <summary>Keeps packages created by 1.x (folder "Pacotes") visible after upgrading.</summary>
        private static string DefaultOutputFolder()
        {
            string legacy = Path.Combine(AppPaths.Data, "Pacotes");
            return Directory.Exists(legacy) ? legacy : AppPaths.DefaultOutput;
        }

        public static void Save()
        {
            try { Json.Save(AppPaths.ConfigFile, Current); }
            catch (Exception ex) { Log.Warn(T("Log_ConfigSaveFailed", ex.Message)); }
        }
    }

    public static class Http
    {
        public static readonly HttpClient Client = Create();

        private static HttpClient Create()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 15
            };
            var c = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.UserAgent.ParseAdd($"J365-IntunePSApp/{AppInfo.Version} (+{AppInfo.RepoUrl})");
            return c;
        }

        /// <summary>GET with a per-request timeout; returns null when the request fails.</summary>
        public static async Task<string> TryGetStringAsync(string url, int timeoutSeconds, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                using var resp = await Client.GetAsync(url, cts.Token);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsStringAsync(cts.Token);
            }
            catch { return null; }
        }

        /// <summary>
        /// Downloads a file with progress, retries transient failures and (optionally) verifies its SHA256.
        /// The file only appears at <paramref name="destination"/> when complete and verified.
        /// </summary>
        public static async Task DownloadAsync(string url, string destination, IProgress<(long read, long? total)> progress, CancellationToken ct, string expectedSha256 = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string tmp = destination + ".part";
            const int attempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadOnceAsync(url, tmp, progress, ct);
                    break;
                }
                catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested && (ex is HttpRequestException || ex is IOException || ex is TimeoutException || ex is OperationCanceledException))
                {
                    Log.Warn(T("Log_DownloadRetry", attempt, attempts - 1, ex.Message));
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
            }
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actual = await Task.Run(() => Hashing.Sha256File(tmp), ct);
                if (!actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmp); } catch { }
                    throw new InvalidDataException(T("Err_HashMismatch", expectedSha256, actual));
                }
            }
            File.Move(tmp, destination, true);
        }

        private static async Task DownloadOnceAsync(string url, string tmp, IProgress<(long read, long? total)> progress, CancellationToken ct)
        {
            using var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long? total = resp.Content.Headers.ContentLength;
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
            var buf = new byte[1 << 16];
            long read = 0;
            var last = DateTime.MinValue;
            while (true)
            {
                // A stalled connection fails after 60 s without data instead of hanging forever.
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(TimeSpan.FromSeconds(60));
                int n;
                try { n = await src.ReadAsync(buf.AsMemory(0, buf.Length), stall.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(T("Err_DownloadStalled")); }
                if (n <= 0) break;
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if ((DateTime.Now - last).TotalMilliseconds > 120) { progress?.Report((read, total)); last = DateTime.Now; }
            }
            progress?.Report((read, total));
        }
    }

    public static class Hashing
    {
        public static string Sha256File(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
    }

    public static class TextFiles
    {
        public static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
        public static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static (string Text, bool HasBom) Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = bom ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3) : Encoding.UTF8.GetString(bytes);
            return (text, bom);
        }

        public static void Write(string path, string text, bool bom) => File.WriteAllText(path, text, bom ? Utf8Bom : Utf8NoBom);
    }

    public static class Names
    {
        private static readonly string[] Palette = { "#0A5CA8", "#0E7490", "#6D28D9", "#B45309", "#BE185D", "#15803D", "#1E3A8A", "#9333EA" };

        public static string Safe(string text)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in text ?? "") sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string r = Regex.Replace(sb.ToString(), @"\s+", "_");
            r = Regex.Replace(r, "_+", "_").Trim('_', '.', ' ');
            return string.IsNullOrEmpty(r) ? "Package" : r;
        }

        /// <summary>"7-Zip 26.06 (x64 edition)" -> "7-Zip"</summary>
        public static string CleanAppName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string n = Regex.Replace(name, @"\s*[\(\[].*$", "");
            n = Regex.Replace(n, @"\s+v?\d+([\.\-_]\d+)+.*$", "");
            n = n.Trim(' ', '-', '_', ',');
            return string.IsNullOrEmpty(n) ? name.Trim() : n;
        }

        /// <summary>Wildcard pattern for "Installed apps": "7-Zip 26.06 (x64)" -> "7-Zip*"</summary>
        public static string NamePattern(string name)
        {
            string n = CleanAppName(name);
            if (string.IsNullOrEmpty(n)) return "";
            n = n.Replace("[", "`[").Replace("]", "`]");
            return n + "*";
        }

        public static string CleanVersion(string text)
        {
            var m = Regex.Match(text ?? "", @"\d+(\.\d+)+");
            if (m.Success) return m.Value;
            m = Regex.Match(text ?? "", @"\d+");
            return m.Success ? m.Value : "";
        }

        /// <summary>"26.06.00.0" -> "26.06" (trailing zero parts do not change version comparisons).</summary>
        public static string TrimVersionZeros(string v)
        {
            var parts = new List<string>((v ?? "").Split('.'));
            while (parts.Count > 2 && Regex.IsMatch(parts[^1], "^0+$")) parts.RemoveAt(parts.Count - 1);
            return string.Join(".", parts);
        }

        public static List<string> ParseProcessList(string text)
        {
            var list = new List<string>();
            foreach (var raw in (text ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = raw.Trim().Trim('"');
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p[..^4];
                if (p.Length > 0 && !list.Exists(x => x.Equals(p, StringComparison.OrdinalIgnoreCase))) list.Add(p);
            }
            return list;
        }

        public static string Initials(string name)
        {
            var words = Regex.Matches(name ?? "", @"[\p{L}\p{N}]+").Select(m => m.Value).ToList();
            if (words.Count == 0) return "?";
            if (words.Count == 1) return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
        }

        public static string AvatarColor(string id)
        {
            int h = 0;
            foreach (char c in id ?? "") h = (h * 31 + c) & 0x7FFFFFFF;
            return Palette[h % Palette.Length];
        }
    }

    public static class VersionUtil
    {
        /// <summary>
        /// Compares versions the way winget does: dot-separated parts, numeric prefix first, then the text suffix
        /// ("1.0" is newer than "1.0-beta"; missing parts count as zero).
        /// </summary>
        public static int Compare(string a, string b)
        {
            var pa = Normalize(a).Split('.');
            var pb = Normalize(b).Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                Split(i < pa.Length ? pa[i] : "0", out long na, out string sa);
                Split(i < pb.Length ? pb[i] : "0", out long nb, out string sb);
                if (na != nb) return na.CompareTo(nb);
                if (sa.Length == 0 && sb.Length > 0) return 1;
                if (sa.Length > 0 && sb.Length == 0) return -1;
                int c = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
            return 0;
        }

        /// <summary>
        /// Drops a prefix that is not part of the number, as winget does: "v2.7.6" is 2.7.6 and "b12532" is 12532.
        /// Only when a digit comes before the first dot ("beta.1" is left alone).
        /// </summary>
        internal static string Normalize(string version)
        {
            string v = (version ?? "").Trim();
            int digit = -1;
            for (int i = 0; i < v.Length; i++) if (char.IsDigit(v[i])) { digit = i; break; }
            int dot = v.IndexOf('.');
            return digit > 0 && (dot < 0 || digit < dot) ? v.Substring(digit) : v;
        }

        private static void Split(string part, out long number, out string suffix)
        {
            int i = 0;
            while (i < part.Length && char.IsDigit(part[i])) i++;
            number = 0;
            if (i > 0) long.TryParse(part.AsSpan(0, Math.Min(i, 18)), out number);
            suffix = part[i..];
        }
    }
}
