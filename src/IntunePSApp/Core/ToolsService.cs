using System;
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
    /// Downloads and validates the two external dependencies: the PSAppDeployToolkit v4 template
    /// (official GitHub release) and Microsoft's IntuneWinAppUtil.exe (Win32 Content Prep Tool).
    /// </summary>
    public static class ToolsService
    {
        private const string FallbackVersion = "4.1.8";
        private const string FallbackUrl = "https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases/download/4.1.8/PSAppDeployToolkit_Template_v4.zip";
        private const string ReleasesApi = "https://api.github.com/repos/PSAppDeployToolkit/PSAppDeployToolkit/releases?per_page=20";
        private const string IntuneWinAppUtilUrl = "https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool/raw/master/IntuneWinAppUtil.exe";

        public static string PsadtBase => Path.Combine(AppPaths.Tools, "PSAppDeployToolkit");
        public static string IntuneWinAppUtilPath => Path.Combine(AppPaths.Tools, "IntuneWinAppUtil.exe");

        public static string LocalPsadt()
        {
            if (!Directory.Exists(PsadtBase)) return null;
            return Directory.GetDirectories(PsadtBase)
                .Where(d => File.Exists(Path.Combine(d, "Invoke-AppDeployToolkit.ps1")) && File.Exists(Path.Combine(d, "PSAppDeployToolkit", "PSAppDeployToolkit.psd1")))
                .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0))
                .FirstOrDefault();
        }

        public static string PsadtVersion => LocalPsadt() is string p ? Path.GetFileName(p) : null;

        /// <summary>Publishers of the official PSAppDeployToolkit releases (the 4.x files are signed by Patch My PC).</summary>
        private static readonly string[] PsadtPublishers = { "Patch My PC", "PSAppDeployToolkit" };

        /// <summary>
        /// Checks the module and the launcher of a toolkit copy: a valid signature of the toolkit publisher. Returns the
        /// signer; throws when a file is missing, unsigned, changed after signing or signed by someone else. Called when
        /// a template is installed and before every build, so a copy changed on disk never reaches a package.
        /// </summary>
        public static string VerifyPsadt(string root)
        {
            string psm1 = Path.Combine(root, "PSAppDeployToolkit", "PSAppDeployToolkit.psm1");
            if (!File.Exists(psm1)) throw new InvalidDataException(T("Err_PsadtModuleMissing"));
            string signer = null;
            foreach (var file in new[] { psm1, Path.Combine(root, "Invoke-AppDeployToolkit.exe") })
            {
                if (!File.Exists(file)) continue;
                if (!Signature.IsValid(file)) throw new InvalidDataException(T("Err_PsadtSignature", Path.GetFileName(file)));
                string name = Signature.Signer(file).Name ?? "";
                if (!PsadtPublishers.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException(T("Err_PsadtPublisher", Path.GetFileName(file), name));
                signer ??= name;
            }
            return signer;
        }

        /// <summary>
        /// The toolkit for a build, checked again first. A copy that no longer passes (changed on disk or damaged since it
        /// was installed) is removed and installed again from the official release, then checked once more, so one
        /// damaged copy does not make every build fail.
        /// </summary>
        public static async Task<string> EnsureVerifiedPsadtAsync(CancellationToken ct, IProgress<(long, long?)> progress = null)
        {
            string template = await EnsurePsadtAsync(ct, progress);
            try
            {
                await Task.Run(() => VerifyPsadt(template), ct);
                return template;
            }
            catch (InvalidDataException ex)
            {
                Log.Warn(T("Log_PsadtReinstall", Path.GetFileName(template), ex.Message));
                await Task.Run(() => Directory.Delete(template, true), ct);
            }
            template = await EnsurePsadtAsync(ct, progress);
            await Task.Run(() => VerifyPsadt(template), ct);
            return template;
        }

        public static bool HasIntuneWinAppUtil => File.Exists(IntuneWinAppUtilPath);

        public static async Task<string> EnsurePsadtAsync(CancellationToken ct, IProgress<(long, long?)> progress = null)
        {
            var local = LocalPsadt();
            if (local != null) return local;
            // Offline use: a template zip dropped into the tools folder is installed without any download.
            var zip = Directory.GetFiles(AppPaths.Tools, "PSAppDeployToolkit_Template_v4*.zip").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            if (zip != null)
            {
                Log.Info(T("Log_PsadtLocalZip", Path.GetFileName(zip)));
                return InstallFromZip(zip);
            }
            return await UpdatePsadtAsync(ct, progress);
        }

        /// <summary>Latest stable 4.x release of the toolkit template, or null when GitHub cannot be reached.</summary>
        public static async Task<(string Version, string Url)?> GetLatestPsadtReleaseAsync(CancellationToken ct)
        {
            string json = await Http.TryGetStringAsync(ReleasesApi, 30, ct);
            if (json == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var r in doc.RootElement.EnumerateArray())
                {
                    if (r.GetProperty("prerelease").GetBoolean() || r.GetProperty("draft").GetBoolean()) continue;
                    string tag = r.GetProperty("tag_name").GetString() ?? "";
                    if (!Regex.IsMatch(tag, @"^v?4\.")) continue;
                    foreach (var a in r.GetProperty("assets").EnumerateArray())
                    {
                        string name = a.GetProperty("name").GetString() ?? "";
                        if (Regex.IsMatch(name, @"^PSAppDeployToolkit_Template_v4.*\.zip$", RegexOptions.IgnoreCase))
                            return (tag.TrimStart('v'), a.GetProperty("browser_download_url").GetString());
                    }
                }
            }
            catch { }
            return null;
        }

        public static async Task<string> UpdatePsadtAsync(CancellationToken ct, IProgress<(long, long?)> progress = null)
        {
            var latest = await GetLatestPsadtReleaseAsync(ct);
            if (latest == null) Log.Warn(T("Log_PsadtGitHubFailed", FallbackVersion));
            string ver = latest?.Version ?? FallbackVersion;
            string url = latest?.Url ?? FallbackUrl;

            var local = LocalPsadt();
            if (local != null && Path.GetFileName(local) == ver) { Log.Ok(T("Log_PsadtUpToDate", ver)); return local; }
            // Without GitHub's answer we never replace a working local copy with the (possibly older) fallback.
            if (local != null && latest == null) return local;
            Log.Step(T("Log_PsadtDownloading", ver));
            string zipPath = Path.Combine(AppPaths.Tools, $"PSAppDeployToolkit_Template_v4_{ver}.zip");
            await Http.DownloadAsync(url, zipPath, progress, ct);
            var dest = await Task.Run(() => InstallFromZip(zipPath), ct);
            try { File.Delete(zipPath); } catch { }
            Log.Ok(T("Log_PsadtReady", Path.GetFileName(dest)));
            return dest;
        }

        private static string InstallFromZip(string zip)
        {
            string tmp = Path.Combine(AppPaths.Tools, "_tmp_" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(zip, tmp, true);
                var launcher = Directory.GetFiles(tmp, "Invoke-AppDeployToolkit.ps1", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidDataException(T("Err_NotPsadtTemplate"));
                string root = Path.GetDirectoryName(launcher);
                string psd1 = Path.Combine(root, "PSAppDeployToolkit", "PSAppDeployToolkit.psd1");
                if (!File.Exists(psd1)) throw new InvalidDataException(T("Err_PsadtModuleMissing"));
                var m = Regex.Match(File.ReadAllText(psd1), @"ModuleVersion\s*=\s*'([^']+)'");
                string version = m.Success ? m.Groups[1].Value : "4.0.0";
                if (!version.StartsWith("4.")) throw new InvalidDataException(T("Err_PsadtUnsupported", version));
                // The toolkit runs as SYSTEM on every device: only the official release, signed by its publisher, goes in.
                Log.Info(T("Log_PsadtSignatureOk", VerifyPsadt(root)));
                ValidateTemplate(root, version);
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) Unblock(f);
                Directory.CreateDirectory(PsadtBase);
                string dest = Path.Combine(PsadtBase, version);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.Move(root, dest);
                return dest;
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        /// <summary>Makes sure this application can build packages from a template before it replaces the current one.</summary>
        private static void ValidateTemplate(string root, string version)
        {
            try
            {
                var (tpl, _) = TextFiles.Read(Path.Combine(root, "Invoke-AppDeployToolkit.ps1"));
                var probe = new PackageSettings { AppName = "Probe", AppVersion = "1.0", InstallerType = "EXE", UninstallPattern = "Probe*" };
                ScriptGenerator.DeployScript(tpl, probe, "setup.exe");
                string config = File.ReadAllText(Path.Combine(root, "Config", "config.psd1"));
                foreach (var key in new[] { "CompanyName", "LogPath", "DialogStyle", "FluentAccentColor", "LanguageOverride" })
                    if (!Regex.IsMatch(config, @"(?m)^\s*" + key + @"\s*=")) throw new InvalidDataException(key);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException)
            {
                throw new InvalidDataException(T("Err_PsadtIncompatible", version), ex);
            }
        }

        /// <summary>Returns the path of a Microsoft-signed IntuneWinAppUtil.exe, or null (the built-in packager is used instead).</summary>
        public static async Task<string> EnsureIntuneWinAppUtilAsync(CancellationToken ct, IProgress<(long, long?)> progress = null)
        {
            string exe = IntuneWinAppUtilPath;
            if (!File.Exists(exe))
            {
                try
                {
                    Log.Step(T("Log_IwauDownloading"));
                    await Http.DownloadAsync(IntuneWinAppUtilUrl, exe, progress, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Log.Warn(T("Log_IwauDownloadFailed", ex.Message));
                    try { File.Delete(exe); } catch { }
                    return null;
                }
            }
            var signer = Signature.Signer(exe);
            if (!Signature.IsValid(exe) || signer.Subject == null || !signer.Subject.Contains("O=Microsoft Corporation"))
            {
                Log.Warn(T("Log_IwauSignatureInvalid"));
                try { File.Delete(exe); } catch { }
                return null;
            }
            Unblock(exe);
            return exe;
        }

        private static void Unblock(string file)
        {
            try { File.Delete(file + ":Zone.Identifier"); } catch { }
        }

        public static string WindowsPowerShell
        {
            get
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string p = Path.Combine(sys, Environment.Is64BitProcess ? "System32" : "Sysnative", "WindowsPowerShell", "v1.0", "powershell.exe");
                return File.Exists(p) ? p : "powershell.exe";
            }
        }

        public static bool HasWindowsPowerShell => File.Exists(WindowsPowerShell);

        /// <summary>
        /// Starts Windows PowerShell 5.1 for one of our own generated scripts. PSModulePath is cleared so modules of
        /// PowerShell 7 never leak into the 5.1 session (that breaks Get-AuthenticodeSignature and others).
        /// </summary>
        public static ProcessStartInfo PowerShellStartInfo(string arguments)
        {
            var psi = new ProcessStartInfo(WindowsPowerShell, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["PSModulePath"] = "";
            return psi;
        }

        /// <summary>
        /// Runs Windows PowerShell and returns its exit code and standard output. Both streams are drained while the
        /// process runs (a full error pipe would otherwise block it) and the process tree is stopped at the time limit.
        /// </summary>
        public static (int ExitCode, string Output, bool TimedOut) RunPowerShell(ProcessStartInfo psi, int timeoutMs = 60000)
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException(WindowsPowerShell);
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-1, "", true);
            }
            p.WaitForExit(); // flushes the redirected streams
            _ = stderr.GetAwaiter().GetResult();
            return (p.ExitCode, stdout.GetAwaiter().GetResult(), false);
        }
    }

    /// <summary>Checks the syntax of generated scripts with the Windows PowerShell 5.1 parser (the engine that runs the package).</summary>
    /// <summary>
    /// Windows App Control (WDAC) policies with script rules - Smart App Control among them - run unsigned scripts in
    /// Constrained Language Mode: the .NET calls of the detection script, of the local tests and of the publishing script
    /// are refused there. The language mode is read once, so those features say why instead of failing obscurely.
    /// </summary>
    public static class PowerShellPolicy
    {
        private static string _mode;
        private static readonly object Gate = new object();

        /// <summary>FullLanguage, ConstrainedLanguage, or Unknown when PowerShell could not be asked.</summary>
        public static string LanguageMode
        {
            get
            {
                lock (Gate)
                {
                    if (_mode != null) return _mode;
                    try
                    {
                        var psi = ToolsService.PowerShellStartInfo("-NoProfile -NonInteractive -Command \"$ExecutionContext.SessionState.LanguageMode\"");
                        var run = ToolsService.RunPowerShell(psi, 30000);
                        string last = (run.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
                        _mode = run.TimedOut || last.Length == 0 ? "Unknown" : last;
                    }
                    catch { _mode = "Unknown"; }
                    return _mode;
                }
            }
        }

        public static bool IsRestricted => LanguageMode.Equals("ConstrainedLanguage", StringComparison.OrdinalIgnoreCase);

        /// <summary>Throws the explanation when unsigned scripts cannot run with .NET calls on this computer.</summary>
        public static void EnsureFullLanguage()
        {
            if (IsRestricted) throw new InvalidOperationException(T("Err_PowerShellRestricted"));
        }
    }

    public static class PowerShellValidator
    {
        /// <summary>Instant feedback while typing: unbalanced braces. Returns null when nothing looks wrong.</summary>
        public static string QuickCheck(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            int depth = 0, n = code.Length;
            for (int i = 0; i < n; i++)
            {
                char c = code[i];
                char next = i + 1 < n ? code[i + 1] : '\0';
                // Braces inside strings and comments are text, not code: -replace '\{', '' is valid PowerShell.
                if (c == '@' && (next == '\'' || next == '"'))
                {
                    int end = code.IndexOf("\n" + next + "@", i + 2, StringComparison.Ordinal);
                    if (end < 0) return null; // unfinished here-string: the real parser reports it when the package is generated
                    i = end + 2;
                }
                else if (c == '\'')
                {
                    for (i++; i < n; i++)
                    {
                        if (code[i] != '\'') continue;
                        if (i + 1 < n && code[i + 1] == '\'') { i++; continue; } // '' is an escaped quote
                        break;
                    }
                }
                else if (c == '"')
                {
                    for (i++; i < n && code[i] != '"'; i++) if (code[i] == '`') i++;
                }
                else if (c == '<' && next == '#')
                {
                    int end = code.IndexOf("#>", i + 2, StringComparison.Ordinal);
                    if (end < 0) return null;
                    i = end + 1;
                }
                else if (c == '#') { while (i < n && code[i] != '\n') i++; }
                else if (c == '`') i++;
                else if (c == '{') depth++;
                else if (c == '}') { if (--depth < 0) return T("Val_BraceUnopened"); }
            }
            return depth != 0 ? T("Val_BraceUnbalanced") : null;
        }

        /// <summary>Parses the file without running it. Returns one "line: message" entry per syntax error.</summary>
        public static string[] Validate(string file)
        {
            try
            {
                // The last line proves the parser really ran: when PowerShell refuses the command (Constrained Language
                // Mode, a blocked or missing file) there is no output at all, which must not read as "no syntax errors".
                const string done = "J365-PARSE-DONE";
                const string script = "$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseFile($env:J365_PS_FILE,[ref]$t,[ref]$e);" +
                                      "foreach($x in $e){'{0}: {1}' -f $x.Extent.StartLineNumber,$x.Message};'" + done + "'";
                var psi = ToolsService.PowerShellStartInfo("-NoProfile -NonInteractive -Command \"" + script + "\"");
                psi.Environment["J365_PS_FILE"] = file;
                var run = ToolsService.RunPowerShell(psi);
                if (run.TimedOut) throw new TimeoutException(ToolsService.WindowsPowerShell);
                var lines = run.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (!lines.Remove(done)) throw new InvalidOperationException(lines.FirstOrDefault() ?? ("exit code " + run.ExitCode));
                return lines.ToArray();
            }
            catch (Exception ex)
            {
                Log.Warn(T("Log_SyntaxCheckSkipped", ex.Message));
                return Array.Empty<string>();
            }
        }
    }
}
