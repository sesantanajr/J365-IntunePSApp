using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace J365.IntunePSApp.Core
{
    /// <summary>Baixa e valida o PSAppDeployToolkit (template v4) e o IntuneWinAppUtil.exe oficial da Microsoft.</summary>
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
        public static bool HasIntuneWinAppUtil => File.Exists(IntuneWinAppUtilPath);

        public static async Task<string> EnsurePsadtAsync(CancellationToken ct)
        {
            var local = LocalPsadt();
            if (local != null) return local;
            var zip = Directory.GetFiles(AppPaths.Tools, "PSAppDeployToolkit_Template_v4*.zip").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
            if (zip != null)
            {
                Log.Info($"Usando o template do PSADT encontrado em tools: {Path.GetFileName(zip)}");
                return InstallFromZip(zip);
            }
            return await UpdatePsadtAsync(ct);
        }

        public static async Task<string> UpdatePsadtAsync(CancellationToken ct)
        {
            string url = null, ver = null;
            try
            {
                using var resp = await Http.Client.GetAsync(ReleasesApi, ct);
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                foreach (var r in doc.RootElement.EnumerateArray())
                {
                    if (r.GetProperty("prerelease").GetBoolean() || r.GetProperty("draft").GetBoolean()) continue;
                    string tag = r.GetProperty("tag_name").GetString() ?? "";
                    if (!Regex.IsMatch(tag, @"^v?4\.")) continue;
                    foreach (var a in r.GetProperty("assets").EnumerateArray())
                    {
                        string name = a.GetProperty("name").GetString() ?? "";
                        if (Regex.IsMatch(name, @"^PSAppDeployToolkit_Template_v4.*\.zip$", RegexOptions.IgnoreCase))
                        {
                            url = a.GetProperty("browser_download_url").GetString();
                            ver = tag.TrimStart('v');
                            break;
                        }
                    }
                    if (url != null) break;
                }
            }
            catch (Exception ex) { Log.Warn($"Não foi possível consultar o GitHub do PSADT ({ex.Message}). Usando a versão {FallbackVersion}."); }
            url ??= FallbackUrl; ver ??= FallbackVersion;

            var local = LocalPsadt();
            if (local != null && Path.GetFileName(local) == ver) { Log.Ok($"PSAppDeployToolkit {ver} já está atualizado."); return local; }
            Log.Step($"Baixando PSAppDeployToolkit {ver} (GitHub oficial)...");
            string zipPath = Path.Combine(AppPaths.Tools, $"PSAppDeployToolkit_Template_v4_{ver}.zip");
            await Http.DownloadAsync(url, zipPath, null, ct);
            var dest = InstallFromZip(zipPath);
            try { File.Delete(zipPath); } catch { }
            Log.Ok($"PSAppDeployToolkit {Path.GetFileName(dest)} pronto.");
            return dest;
        }

        private static string InstallFromZip(string zip)
        {
            string tmp = Path.Combine(AppPaths.Tools, "_tmp_" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(zip, tmp, true);
                var launcher = Directory.GetFiles(tmp, "Invoke-AppDeployToolkit.ps1", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidDataException("O arquivo não é um template do PSAppDeployToolkit v4.");
                string root = Path.GetDirectoryName(launcher);
                string psd1 = Path.Combine(root, "PSAppDeployToolkit", "PSAppDeployToolkit.psd1");
                if (!File.Exists(psd1)) throw new InvalidDataException("Módulo PSAppDeployToolkit não encontrado no template.");
                var m = Regex.Match(File.ReadAllText(psd1), @"ModuleVersion\s*=\s*'([^']+)'");
                string version = m.Success ? m.Groups[1].Value : "4.0.0";
                if (!version.StartsWith("4.")) throw new InvalidDataException($"Versão do PSAppDeployToolkit não suportada: {version}.");
                string psm1 = Path.Combine(root, "PSAppDeployToolkit", "PSAppDeployToolkit.psm1");
                if (!Signature.IsValid(psm1)) Log.Warn("Atenção: a assinatura digital do módulo PSADT não foi validada.");
                else Log.Info($"Assinatura do PSADT válida ({Signature.Signer(psm1).Name}).");
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

        public static async Task<string> EnsureIntuneWinAppUtilAsync(CancellationToken ct)
        {
            string exe = IntuneWinAppUtilPath;
            if (!File.Exists(exe))
            {
                try
                {
                    Log.Step("Baixando IntuneWinAppUtil.exe (Microsoft Win32 Content Prep Tool)...");
                    await Http.DownloadAsync(IntuneWinAppUtilUrl, exe, null, ct);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Não foi possível baixar o IntuneWinAppUtil.exe ({ex.Message}). Será usado o empacotador interno.");
                    try { File.Delete(exe); } catch { }
                    return null;
                }
            }
            var signer = Signature.Signer(exe);
            if (!Signature.IsValid(exe) || signer.Subject == null || !signer.Subject.Contains("O=Microsoft Corporation"))
            {
                Log.Warn("IntuneWinAppUtil.exe sem assinatura válida da Microsoft: arquivo descartado. Será usado o empacotador interno.");
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
    }

    /// <summary>Valida a sintaxe dos scripts gerados com o parser do Windows PowerShell 5.1 (o mesmo que executara o pacote).</summary>
    public static class PowerShellValidator
    {
        public static string QuickCheck(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            int depth = 0;
            foreach (char c in code) { if (c == '{') depth++; else if (c == '}') depth--; if (depth < 0) return "chave '}' sem abertura."; }
            return depth != 0 ? "chaves { } não estão balanceadas." : null;
        }

        public static string[] Validate(string file)
        {
            try
            {
                string script = "$t=$null;$e=$null;[void][System.Management.Automation.Language.Parser]::ParseFile($env:J365_PS_FILE,[ref]$t,[ref]$e);" +
                                "foreach($x in $e){'{0}: {1}' -f $x.Extent.StartLineNumber,$x.Message}";
                var psi = new ProcessStartInfo(ToolsService.WindowsPowerShell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.Environment["J365_PS_FILE"] = file;
                psi.Environment["PSModulePath"] = "";
                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(60000);
                return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception ex)
            {
                Log.Warn($"Validação de sintaxe ignorada: {ex.Message}");
                return Array.Empty<string>();
            }
        }
    }
}
