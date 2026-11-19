using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

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

        public static T Load<T>(string path) where T : new()
        {
            try
            {
                if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Options) ?? new T();
            }
            catch (Exception ex) { Log.Warn($"Não foi possível ler {Path.GetFileName(path)}: {ex.Message}"); }
            return new T();
        }

        public static void Save<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }
    }

    /// <summary>Pastas do aplicativo. Modo portatil: tudo fica ao lado do .exe (IntunePSApp-Data).</summary>
    public static class AppPaths
    {
        public static string ExeDir { get; } = AppContext.BaseDirectory.TrimEnd('\\');
        public static string Data { get; }
        public static bool IsPortable { get; }

        static AppPaths()
        {
            string env = Environment.GetEnvironmentVariable("J365_INTUNEPSAPP_DATA");
            string portable = Path.Combine(ExeDir, "IntunePSApp-Data");
            if (!string.IsNullOrWhiteSpace(env)) { Data = env; IsPortable = true; }
            else if (CanWrite(ExeDir)) { Data = portable; IsPortable = true; }
            else { Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "J365", "IntunePSApp"); IsPortable = false; }
            Directory.CreateDirectory(Data);
        }

        public static string Tools => Ensure(Path.Combine(Data, "tools"));
        public static string CatalogDir => Ensure(Path.Combine(Data, "catalog"));
        public static string Downloads => Ensure(Path.Combine(Data, "downloads"));
        public static string Branding => Ensure(Path.Combine(Data, "branding"));
        public static string Logs => Ensure(Path.Combine(Data, "logs"));
        public static string DefaultOutput => Path.Combine(Data, "Pacotes");
        public static string ConfigFile => Path.Combine(Data, "config.json");
        public static string Temp => Ensure(Path.Combine(Data, "temp"));

        private static string Ensure(string p) { Directory.CreateDirectory(p); return p; }

        private static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".j365-write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return !dir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
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
    }

    public static class ConfigStore
    {
        private static AppConfig _current;
        public static AppConfig Current => _current ??= Load();

        private static AppConfig Load()
        {
            var c = Json.Load<AppConfig>(AppPaths.ConfigFile);
            if (string.IsNullOrWhiteSpace(c.OutputFolder)) c.OutputFolder = AppPaths.DefaultOutput;
            if (string.IsNullOrWhiteSpace(c.CatalogUrl)) c.CatalogUrl = AppInfo.DefaultCatalogUrl;
            return c;
        }

        public static void Save() => Json.Save(AppPaths.ConfigFile, Current);
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
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd($"J365-IntunePSApp/{AppInfo.Version} (+{AppInfo.RepoUrl})");
            return c;
        }

        /// <summary>Baixa um arquivo com progresso e (opcionalmente) confere o SHA256.</summary>
        public static async Task DownloadAsync(string url, string destination, IProgress<(long read, long? total)> progress, CancellationToken ct, string expectedSha256 = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string tmp = destination + ".part";
            using (var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                var buf = new byte[1 << 16];
                long read = 0; int n; var last = DateTime.MinValue;
                while ((n = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    if ((DateTime.Now - last).TotalMilliseconds > 120) { progress?.Report((read, total)); last = DateTime.Now; }
                }
                progress?.Report((read, total));
            }
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actual = Hashing.Sha256File(tmp);
                if (!actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tmp);
                    throw new InvalidDataException($"O arquivo baixado não confere com o hash SHA256 do catálogo (esperado {expectedSha256}, obtido {actual}). Download descartado por segurança.");
                }
            }
            File.Move(tmp, destination, true);
        }
    }

    public static class Hashing
    {
        public static string Sha256File(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs));
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
        public static string Safe(string text)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in text ?? "") sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string r = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", "_");
            r = System.Text.RegularExpressions.Regex.Replace(r, "_+", "_").Trim('_', '.', ' ');
            return string.IsNullOrEmpty(r) ? "Pacote" : r;
        }

        /// <summary>"7-Zip 26.06 (x64 edition)" -> "7-Zip"</summary>
        public static string CleanAppName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string n = System.Text.RegularExpressions.Regex.Replace(name, @"\s*[\(\[].*$", "");
            n = System.Text.RegularExpressions.Regex.Replace(n, @"\s+v?\d+([\.\-_]\d+)+.*$", "");
            n = n.Trim(' ', '-', '_', ',');
            return string.IsNullOrEmpty(n) ? name.Trim() : n;
        }

        /// <summary>Padrao (curinga) para "Aplicativos instalados": "7-Zip 26.06 (x64)" -> "7-Zip*"</summary>
        public static string NamePattern(string name)
        {
            string n = CleanAppName(name);
            if (string.IsNullOrEmpty(n)) return "";
            n = n.Replace("[", "`[").Replace("]", "`]");
            return n + "*";
        }

        public static string CleanVersion(string text)
        {
            var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\d+(\.\d+)+");
            if (m.Success) return m.Value;
            m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\d+");
            return m.Success ? m.Value : "";
        }

        public static string TrimVersionZeros(string v)
        {
            var parts = new List<string>((v ?? "").Split('.'));
            while (parts.Count > 2 && System.Text.RegularExpressions.Regex.IsMatch(parts[^1], "^0+$")) parts.RemoveAt(parts.Count - 1);
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
    }
}
