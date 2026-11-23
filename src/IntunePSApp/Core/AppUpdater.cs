using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Updates the application from its GitHub release while it is open: downloads the release zip, checks its SHA256
    /// against SHA256SUMS.txt of the same release, takes the new executable out of it, puts it in place of the running
    /// one and restarts. A running executable cannot be overwritten, but it can be renamed: the current file becomes
    /// "J365.IntunePSApp.exe.old" (removed at the next start) and the new one takes its name. Settings, the database,
    /// the tools and the packages are not touched.
    /// </summary>
    public static class AppUpdater
    {
        public const string ExeName = "J365.IntunePSApp.exe";

        public sealed class Release
        {
            public string Version { get; set; } = "";
            public string PageUrl { get; set; } = "";
            public string ZipName { get; set; } = "";
            public string ZipUrl { get; set; } = "";
            public long ZipSize { get; set; }
            public string SumsUrl { get; set; } = "";
            /// <summary>Where the version was read: <see cref="SourceFiles"/>, <see cref="SourceApi"/> or <see cref="SourcePage"/>.</summary>
            public string Source { get; set; } = "";
        }

        public const string SourceFiles = "files", SourceApi = "api", SourcePage = "page";

        /// <summary>Release API. J365_UPDATE_API points it elsewhere (an internal mirror, tests).</summary>
        public static string ApiUrl
            => Environment.GetEnvironmentVariable("J365_UPDATE_API") is { Length: > 0 } url ? url : AppInfo.LatestReleaseApi;

        /// <summary>A mirror set with J365_UPDATE_API answers alone: the pages of GitHub are not asked.</summary>
        private static bool UsesCustomApi => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("J365_UPDATE_API"));

        /// <summary>"https://github.com/owner/repo/releases/tag/v2.6.0" → "2.6.0"; null for any other address.</summary>
        public static string VersionFromReleaseUrl(string url)
        {
            var m = Regex.Match(url ?? "", @"/releases/tag/v?(?<v>\d+(\.\d+){1,3})/?(\?.*)?$", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value : null;
        }

        /// <summary>A release known by its version only: its files have the names every release of this project uses.</summary>
        public static Release ReleaseFromVersion(string version)
        {
            string zip = $"J365-IntunePSApp-{version}-win-x64.zip", files = $"{AppInfo.RepoUrl}/releases/download/v{version}/";
            return new Release { Version = version, PageUrl = $"{AppInfo.RepoUrl}/releases/tag/v{version}", ZipName = zip, ZipUrl = files + zip, SumsUrl = files + "SHA256SUMS.txt" };
        }

        /// <summary>
        /// The latest version from the page of the latest release (github.com redirects it to the tag): no API, so no limit
        /// of 60 requests an hour per address, and reachable where a proxy only lets github.com through. Null when unknown.
        /// </summary>
        public static async Task<string> LatestVersionFromPageAsync(CancellationToken ct)
            => VersionFromReleaseUrl(await Http.FinalUrlAsync(AppInfo.ReleasesUrl, 30, ct));

        /// <summary>
        /// The release named by a SHA256SUMS.txt: the line of the application zip ("HASH  J365-IntunePSApp-2.7.0-win-x64.zip")
        /// gives the version, and the files have the names every release of this project uses. Null when it lists no zip.
        /// </summary>
        public static Release ReleaseFromSums(string sums)
        {
            foreach (var line in (sums ?? "").Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^[0-9A-Fa-f]{64}\s+\*?J365-IntunePSApp-(?<v>\d+(\.\d+){1,3})-win-x64\.zip$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var r = ReleaseFromVersion(m.Groups["v"].Value);
                r.Source = SourceFiles;
                return r;
            }
            return null;
        }

        /// <summary>False when this copy cannot replace itself (read-only folder): the release page is offered instead.</summary>
        public static bool CanUpdateInPlace => CanWrite(Path.GetDirectoryName(AppPaths.ExePath));

        /// <summary>
        /// The latest release, asked past the caches of the network, from three places in turn:
        /// 1. the SHA256SUMS.txt of the latest release (github.com/…/releases/latest/download/SHA256SUMS.txt). It is not the
        ///    API, so it has no limit of 60 requests an hour per address — a company network shares one address with every
        ///    colleague, and copies up to 2.6.0 said "up to date" whenever that limit was reached. It names the zip, so it
        ///    also proves the release has its files;
        /// 2. the API of GitHub (it also gives the size of the zip);
        /// 3. the page of the latest release, which redirects to its tag.
        /// A mirror set with J365_UPDATE_API answers alone. Throws, saying why each one failed, when none answers.
        /// </summary>
        public static async Task<Release> GetLatestAsync(CancellationToken ct)
        {
            var errors = new List<string>();
            if (!UsesCustomApi)
            {
                var (sums, sumsError) = await Http.GetStringAsync(AppInfo.LatestSumsUrl, 30, ct, noCache: true);
                ct.ThrowIfCancellationRequested();
                if (ReleaseFromSums(sums) is Release fromFiles) return fromFiles;
                errors.Add("github.com: " + (sumsError ?? T("Update_ErrNoHash", "J365-IntunePSApp-*-win-x64.zip")));
            }

            var (json, error) = await Http.GetStringAsync(ApiUrl, 30, ct, noCache: true);
            ct.ThrowIfCancellationRequested();
            if (json != null)
            {
                try
                {
                    var r = ParseRelease(json);
                    r.Source = SourceApi;
                    return r;
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is KeyNotFoundException) { error = ex.Message; }
            }
            errors.Add(UsesCustomApi ? error : "api.github.com: " + error);

            if (!UsesCustomApi && await LatestVersionFromPageAsync(ct) is string version)
            {
                Log.Info(T("Log_UpdateFromPage", version, string.Join("; ", errors)));
                var r = ReleaseFromVersion(version);
                r.Source = SourcePage;
                return r;
            }
            ct.ThrowIfCancellationRequested();
            string why = string.Join("; ", errors.Where(e => !string.IsNullOrWhiteSpace(e)));
            throw new IOException(T("Update_ErrNoInfo") + (why.Length == 0 ? "" : $" ({why})"));
        }

        /// <summary>The size of the release zip when its source did not say it (a HEAD request); 0 when unknown.</summary>
        public static async Task<long> ZipSizeAsync(Release r, CancellationToken ct)
            => r.ZipSize > 0 ? r.ZipSize : await Http.ContentLengthAsync(r.ZipUrl, 20, ct) ?? 0;

        /// <summary>Reads a GitHub release (JSON of /releases/latest): version, the win-x64 zip and SHA256SUMS.txt.</summary>
        public static Release ParseRelease(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var r = new Release
            {
                Version = (Str(root, "tag_name") ?? "").Trim().TrimStart('v', 'V'),
                PageUrl = Str(root, "html_url") ?? AppInfo.ReleasesUrl
            };
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = Str(a, "name") ?? "", url = Str(a, "browser_download_url") ?? "";
                    if (Regex.IsMatch(name, @"^J365-IntunePSApp-.+-win-x64\.zip$", RegexOptions.IgnoreCase))
                    {
                        r.ZipName = name;
                        r.ZipUrl = url;
                        r.ZipSize = a.TryGetProperty("size", out var size) && size.TryGetInt64(out long n) ? n : 0;
                    }
                    else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) r.SumsUrl = url;
                }
            }
            if (r.Version.Length == 0 || !Regex.IsMatch(r.Version, @"^\d+(\.\d+){1,3}$")) throw new InvalidDataException(T("Update_ErrNoInfo"));
            if (r.ZipUrl.Length == 0 || r.SumsUrl.Length == 0) throw new InvalidDataException(T("Update_ErrNoAsset", r.Version));
            return r;
        }

        private static string Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>SHA256 of a file in a SHA256SUMS.txt ("HASH  name" or "HASH *name"), or null.</summary>
        public static string HashFor(string sums, string fileName)
        {
            foreach (var line in (sums ?? "").Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^([0-9A-Fa-f]{64})\s+\*?(.+)$");
                if (m.Success && m.Groups[2].Value.Trim().Equals(fileName, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value.ToUpperInvariant();
            }
            return null;
        }

        /// <summary>
        /// Downloads and checks the release, and leaves the new executable next to <paramref name="target"/> (…exe.new):
        /// in the same folder, so putting it in place is a rename. A zip already downloaded and verified is reused.
        /// </summary>
        public static async Task<(string NewExe, string Zip)> DownloadAsync(Release r, string target, IProgress<(long, long?)> progress, CancellationToken ct)
        {
            string sums = await Http.TryGetStringAsync(r.SumsUrl, 30, ct);
            ct.ThrowIfCancellationRequested();
            string expected = HashFor(sums, r.ZipName) ?? throw new InvalidDataException(T("Update_ErrNoHash", r.ZipName));
            string work = Path.Combine(AppPaths.Temp, "update-" + Names.Safe(r.Version));
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, r.ZipName);
            bool ready = File.Exists(zip) && (await Task.Run(() => Hashing.Sha256File(zip), ct)).Equals(expected, StringComparison.OrdinalIgnoreCase);
            if (!ready) await Http.DownloadAsync(r.ZipUrl, zip, progress, ct, expected);
            ct.ThrowIfCancellationRequested();
            string newExe = target + ".new";
            await Task.Run(() => ExtractExe(zip, newExe, r.Version), ct);
            return (newExe, zip);
        }

        /// <summary>Takes the executable out of the release zip and checks that it is the announced version.</summary>
        public static void ExtractExe(string zip, string destination, string version)
        {
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException(T("Update_ErrNoExe", Path.GetFileName(zip)));
            string part = destination + ".part";
            try
            {
                entry.ExtractToFile(part, true);
                var info = FileVersionInfo.GetVersionInfo(part);
                string found = (info.ProductVersion ?? "").Split('+')[0].Trim();
                if (found.Length == 0) found = info.FileVersion ?? "";
                if (VersionUtil.Compare(found, version) != 0) throw new InvalidDataException(T("Update_ErrVersion", found, version));
                // Once releases are code-signed, an update must carry a valid signature of the same publisher, or of one
                // of the publishers of this project (the signing service may change): a signed copy is never replaced by
                // an unsigned or foreign file.
                string running = AppPaths.ExePath;
                if (Path.GetFileName(running).Equals(ExeName, StringComparison.OrdinalIgnoreCase) && Signature.IsValid(running))
                {
                    var current = Signature.Signer(running);
                    if (!Signature.IsValid(part) || !IsTrustedPublisher(Signature.Signer(part), current))
                        throw new InvalidDataException(T("Update_ErrSignature", current.Name));
                }
                File.Move(part, destination, true);
            }
            finally { try { File.Delete(part); } catch { } }
        }

        /// <summary>
        /// The signer of a new executable is accepted when it is the signer of the running one, or one of the publishers
        /// of the project's releases (<see cref="AppInfo.TrustedPublishers"/>).
        /// </summary>
        public static bool IsTrustedPublisher((string Name, string Subject) signer, (string Name, string Subject) current)
        {
            if (!string.IsNullOrEmpty(signer.Subject) && string.Equals(signer.Subject, current.Subject, StringComparison.OrdinalIgnoreCase)) return true;
            // Compared without accents and punctuation: "Open Source Developer, Sergio Sant'Ana Junior" (Certum) is the author.
            string name = Comparable(signer.Name);
            return name.Length > 0 && AppInfo.TrustedPublishers.Select(Comparable).Any(p => p.Length > 0 && (name == p || name.EndsWith(p, StringComparison.Ordinal)));
        }

        private static string Comparable(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (text ?? "").Normalize(System.Text.NormalizationForm.FormD))
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>
        /// Puts <paramref name="newFile"/> in place of <paramref name="target"/>. When the target is running it cannot be
        /// overwritten: it is renamed to "….old" (removed at the next start) and the new file takes its name. On failure
        /// the original stays where it was.
        /// </summary>
        public static void Replace(string newFile, string target)
        {
            if (!File.Exists(target)) { File.Move(newFile, target); return; }
            try { File.Move(newFile, target, true); return; }                 // not running: a plain replace
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            string old = OldName(target);
            File.Move(target, old);                                            // a running executable can be renamed
            try { File.Move(newFile, target); }
            catch
            {
                try { File.Move(old, target); } catch { }
                throw;
            }
        }

        private static string OldName(string target)
        {
            string old = target + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            // Still there: an older copy is still running from it. A second name keeps both.
            return File.Exists(old) ? $"{target}.{DateTime.UtcNow.Ticks}.old" : old;
        }

        /// <summary>
        /// The files that come with the executable in the release zip (README, CHANGELOG, licences) are refreshed when the
        /// folder already has them (a portable copy unzipped by the user). Nothing new is added to the folder.
        /// </summary>
        public static void RefreshCompanionFiles(string zip, string folder)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zip);
                foreach (var e in archive.Entries)
                {
                    if (e.Name.Length == 0 || e.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase) || e.FullName.Contains('/')) continue;
                    string dest = Path.Combine(folder, e.Name);
                    if (!File.Exists(dest)) continue;
                    try { e.ExtractToFile(dest, true); } catch { }
                }
            }
            catch { }
        }

        /// <summary>Starts the new version (it shows "updated from …" and removes the old file).</summary>
        public static void Restart(string exe, string previousVersion)
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
            psi.ArgumentList.Add("--updated");
            psi.ArgumentList.Add(previousVersion);
            Process.Start(psi);
        }

        /// <summary>
        /// Removes what an update left behind: the previous executable (….old), an unfinished download (….new) and the
        /// zips of earlier updates. The previous process may still be closing, so it tries again for a few seconds.
        /// </summary>
        public static Task CleanupAsync()
        {
            return Task.Run(async () =>
            {
                var folders = new[] { Path.GetDirectoryName(AppPaths.ExePath), AppPaths.InstallDir }
                    .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                // A download this process starts later (….new) is not a leftover.
                DateTime started;
                try { started = Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch { started = DateTime.UtcNow; }
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    bool left = false;
                    foreach (var dir in folders)
                    {
                        IEnumerable<string> files;
                        try
                        {
                            files = Directory.GetFiles(dir, ExeName + "*.old")
                                .Concat(Directory.GetFiles(dir, ExeName + ".new*").Where(f => File.GetLastWriteTimeUtc(f) < started))
                                .ToList();
                        }
                        catch { continue; }
                        foreach (var f in files)
                        {
                            try { File.Delete(f); } catch { left = true; }
                        }
                    }
                    if (!left) break;
                    await Task.Delay(2000);
                }
                try
                {
                    foreach (var d in Directory.GetDirectories(AppPaths.Temp, "update-*"))
                        try { Directory.Delete(d, true); } catch { }
                }
                catch { }
            });
        }

        /// <summary>Running copies of an executable (other than this process).</summary>
        public static List<Process> RunningCopies(string exe)
        {
            var list = new List<Process>();
            string full = Path.GetFullPath(exe);
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
            {
                try
                {
                    if (p.Id == Environment.ProcessId) { p.Dispose(); continue; }
                    string file = p.MainModule?.FileName ?? "";
                    // A copy renamed by an earlier update (….exe.old) still counts.
                    if (file.Equals(full, StringComparison.OrdinalIgnoreCase) || file.StartsWith(full + ".", StringComparison.OrdinalIgnoreCase)) list.Add(p);
                    else p.Dispose();
                }
                catch { p.Dispose(); }
            }
            return list;
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, ".j365-update-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }
}
