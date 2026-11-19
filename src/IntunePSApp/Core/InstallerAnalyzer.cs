using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace J365.IntunePSApp.Core
{
    /// <summary>Analisa instaladores .msi/.exe: metadados, tecnologia, parametros silenciosos e correspondencia no banco de dados.</summary>
    public static class InstallerAnalyzer
    {
        private static readonly Dictionary<string, (string Install, string Uninstall, string Note)> TechDefaults = new()
        {
            ["NSIS"] = ("/S", "/S", "Instalador NSIS: parâmetro /S (silencioso)."),
            ["Inno Setup"] = ("/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", "Instalador Inno Setup: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART."),
            ["WiX Burn"] = ("/quiet /norestart", "", "Instalador WiX Burn (bundle): /quiet /norestart. A desinstalação usa a string silenciosa registrada pelo bundle."),
            ["InstallShield"] = ("/s /v\"/qn REBOOT=ReallySuppress\"", "", "Instalador InstallShield: confira os parâmetros com o fabricante (variam entre versões)."),
            ["Advanced Installer"] = ("/exenoui /qn /norestart", "", "Instalador Advanced Installer: /exenoui /qn."),
            ["Squirrel"] = ("--silent", "", "Instalador Squirrel instala POR USUÁRIO: use o contexto Usuário no Intune ou procure a versão MSI/empresarial."),
            ["Desconhecido"] = ("", "", "Tecnologia não identificada: informe os parâmetros de instalação silenciosa (site do fabricante ou \"instalador.exe /?\")."),
        };

        public static InstallerInfo Analyze(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Arquivo não encontrado.", path);
            var fi = new FileInfo(path);
            string ext = fi.Extension.ToLowerInvariant();
            var info = new InstallerInfo
            {
                Path = fi.FullName,
                FileName = fi.Name,
                SizeMB = Math.Round(fi.Length / 1024d / 1024d, 1)
            };

            if (Signature.IsValid(fi.FullName)) info.Signer = Signature.Signer(fi.FullName).Name ?? "(assinado)";

            switch (ext)
            {
                case ".msi":
                    {
                        info.Type = "MSI";
                        info.Technology = "Windows Installer (MSI)";
                        var (p, template) = MsiReader.Read(fi.FullName);
                        p.TryGetValue("ProductName", out var pn);
                        p.TryGetValue("Manufacturer", out var mf);
                        p.TryGetValue("ProductVersion", out var pv);
                        p.TryGetValue("ProductCode", out var pc);
                        p.TryGetValue("UpgradeCode", out var uc);
                        info.MsiProductName = pn ?? "";
                        info.AppName = Names.CleanAppName(pn);
                        info.AppVendor = mf ?? "";
                        info.AppVersion = Names.TrimVersionZeros(pv ?? "");
                        info.ProductCode = pc ?? "";
                        info.UpgradeCode = uc ?? "";
                        template ??= "";
                        if (template.Contains("x64") || template.Contains("Intel64") || template.Contains("AMD64")) info.AppArch = "x64";
                        else if (template.Contains("Arm64", StringComparison.OrdinalIgnoreCase)) info.AppArch = "arm64";
                        else if (template.Contains("Intel")) info.AppArch = "x86";
                        info.UninstallPattern = Names.NamePattern(pn);
                        info.Notes.Add("MSI: instalação silenciosa automática (/qn REBOOT=ReallySuppress). Use os parâmetros apenas para propriedades extras (ex.: ALLUSERS=1).");
                        break;
                    }
                case ".exe":
                    {
                        info.Type = "EXE";
                        var vi = FileVersionInfo.GetVersionInfo(fi.FullName);
                        info.Technology = DetectTechnology(fi.FullName, vi);
                        string name = FirstNonEmpty(vi.ProductName, vi.FileDescription, Path.GetFileNameWithoutExtension(fi.Name));
                        info.AppName = Names.CleanAppName(name);
                        info.AppVendor = vi.CompanyName ?? "";
                        info.AppVersion = Names.CleanVersion(FirstNonEmpty(vi.ProductVersion, vi.FileVersion));
                        if (string.IsNullOrEmpty(info.AppVersion)) info.AppVersion = Names.CleanVersion(Path.GetFileNameWithoutExtension(fi.Name));
                        string fn = fi.Name.ToLowerInvariant();
                        if (fn.Contains("arm64")) info.AppArch = "arm64";
                        else if (fn.Contains("x64") || fn.Contains("amd64") || fn.Contains("win64") || fn.Contains("64bit") || fn.Contains("64-bit")) info.AppArch = "x64";
                        else if (fn.Contains("x86") || fn.Contains("win32") || fn.Contains("32bit") || fn.Contains("32-bit")) info.AppArch = "x86";
                        else { var pe = PeArchitecture(fi.FullName); if (pe != "x86") info.AppArch = pe; }
                        var td = TechDefaults[info.Technology];
                        info.InstallArgs = td.Install;
                        info.UninstallExeArgs = td.Uninstall;
                        info.Notes.Add(td.Note);
                        info.UninstallPattern = Names.NamePattern(info.AppName);
                        if (info.Technology == "Squirrel") info.InstallBehavior = "User";
                        break;
                    }
                default:
                    throw new NotSupportedException($"Tipo de arquivo não suportado: '{ext}'. Selecione um instalador .msi ou .exe.");
            }
            info.DetectionVersion = info.AppVersion;

            // Correspondencia no banco de dados: hash exato > codigo do produto MSI > nome.
            var catalog = CatalogService.Instance;
            if (catalog.IsLoaded)
            {
                try { info.Sha256 = Hashing.Sha256File(fi.FullName); } catch { }
                var (app, inst) = catalog.FindByHash(info.Sha256);
                string match = app != null ? "hash" : null;
                if (app == null && !string.IsNullOrEmpty(info.ProductCode)) { (app, inst) = catalog.FindByProductCode(info.ProductCode); if (app != null) match = "productcode"; }
                if (app != null)
                {
                    string before = info.Technology;
                    CatalogService.Apply(info, app, inst, match);
                    if (before == "Desconhecido") info.Notes.RemoveAll(n => n == TechDefaults["Desconhecido"].Note);
                    info.Notes.Insert(0, match == "hash"
                        ? $"Instalador reconhecido no banco de dados (hash idêntico): {app.Name} [{app.Id}]. Parâmetros validados pela comunidade winget."
                        : $"Aplicativo reconhecido pelo código do produto MSI: {app.Name} [{app.Id}].");
                }
                else
                {
                    var similar = catalog.FindByName(info.AppName, info.AppVendor, 1).FirstOrDefault();
                    if (similar != null)
                    {
                        info.CatalogId = similar.Id;
                        info.CatalogMatch = "nome";
                        if (similar.Extra?.Processes != null && info.Processes.Count == 0) info.Processes = new List<string>(similar.Extra.Processes);
                        if (!string.IsNullOrEmpty(similar.Extra?.NamePattern)) info.UninstallPattern = similar.Extra.NamePattern;
                        if (!string.IsNullOrEmpty(similar.Extra?.UninstallArgs) && info.Type == "EXE") info.UninstallExeArgs = similar.Extra.UninstallArgs;
                        if (info.Technology == "Desconhecido")
                        {
                            var ci = CatalogService.PickInstaller(similar, info.AppArch == "" ? "x64" : info.AppArch);
                            if (ci != null && !ci.IsMsi && info.Type == "EXE")
                            {
                                info.InstallArgs = CatalogService.SilentArgsFor(ci);
                                info.Technology = CatalogService.TechnologyFor(ci) ?? "Instalador do fabricante";
                                info.Notes.RemoveAll(n => n == TechDefaults["Desconhecido"].Note);
                            }
                        }
                        info.Notes.Insert(0, $"Correspondência provável no banco de dados: {similar.Name} [{similar.Id}]. Confira os parâmetros sugeridos.");
                        if (!string.IsNullOrEmpty(similar.Extra?.Notes)) info.Notes.Add(similar.Extra.Notes);
                    }
                }
            }

            info.Notes.Add(string.IsNullOrEmpty(info.Signer)
                ? "Atenção: o instalador não possui assinatura digital válida. Confirme que ele veio do site oficial do fabricante."
                : $"Assinatura digital válida: {info.Signer}.");
            return info;
        }

        private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        public static string DetectTechnology(string path, FileVersionInfo vi)
        {
            int max = 16 * 1024 * 1024;
            byte[] buf;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int len = (int)Math.Min(fs.Length, max);
                buf = new byte[len];
                int read = 0, n;
                while (read < len && (n = fs.Read(buf, read, len - read)) > 0) read += n;
            }
            string ascii = Encoding.ASCII.GetString(buf);
            string uni = Encoding.Unicode.GetString(buf, 0, buf.Length - (buf.Length % 2));
            string meta = $"{vi.Comments} | {vi.FileDescription} | {vi.ProductName} | {vi.LegalCopyright}";
            bool Has(params string[] needles) => needles.Any(t => ascii.Contains(t, StringComparison.Ordinal) || uni.Contains(t, StringComparison.Ordinal) || meta.Contains(t, StringComparison.Ordinal));

            if (ascii.Contains(".wixburn", StringComparison.Ordinal)) return "WiX Burn";
            if (Has("Inno Setup", "InnoSetupLdrWindow")) return "Inno Setup";
            if (Has("Nullsoft", "NSIS Error", "NullsoftInst")) return "NSIS";
            if (Has("InstallShield")) return "InstallShield";
            if (Has("Advanced Installer", "Caphyon")) return "Advanced Installer";
            if (Has("SquirrelSetup", "Squirrel.Windows", "Squirrel installer")) return "Squirrel";
            return "Desconhecido";
        }

        public static string PeArchitecture(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var br = new BinaryReader(fs);
                fs.Seek(0x3C, SeekOrigin.Begin);
                int pe = br.ReadInt32();
                fs.Seek(pe + 4, SeekOrigin.Begin);
                ushort machine = br.ReadUInt16();
                return machine switch { 0x8664 => "x64", 0x014C => "x86", 0xAA64 => "arm64", _ => "" };
            }
            catch { return ""; }
        }

        /// <summary>Programas instalados neste computador cujo nome corresponde ao padrao (curinga).</summary>
        public static List<InstalledProgram> FindInstalled(string pattern)
        {
            var result = new List<InstalledProgram>();
            var targets = new (RegistryHive Hive, RegistryView View, string Scope)[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64, "Computador (64 bits)"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, "Computador (32 bits)"),
                (RegistryHive.CurrentUser, RegistryView.Default, "Usuário atual")
            };
            var rx = WildcardToRegex(pattern);
            foreach (var t in targets)
            {
                try
                {
                    using var b = RegistryKey.OpenBaseKey(t.Hive, t.View);
                    using var key = b.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key == null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var k = key.OpenSubKey(sub);
                        var dn = k?.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(dn) || !rx.IsMatch(dn)) continue;
                        result.Add(new InstalledProgram
                        {
                            Name = dn,
                            Version = k.GetValue("DisplayVersion") as string ?? "",
                            Publisher = k.GetValue("Publisher") as string ?? "",
                            Type = Convert.ToString(k.GetValue("WindowsInstaller")) == "1" ? "MSI" : "EXE",
                            QuietUninstall = !string.IsNullOrWhiteSpace(k.GetValue("QuietUninstallString") as string),
                            Scope = t.Scope,
                            KeyName = sub
                        });
                    }
                }
                catch { }
            }
            return result.GroupBy(r => r.Name + "|" + r.Version + "|" + r.KeyName).Select(g => g.First()).OrderBy(r => r.Name).ToList();
        }

        public static System.Text.RegularExpressions.Regex WildcardToRegex(string pattern)
        {
            // Semelhante ao -like do PowerShell: * ? e `[ como escape.
            var sb = new StringBuilder("^");
            string p = (pattern ?? "").Replace("`[", "\u0001").Replace("`]", "\u0002");
            foreach (char c in p)
            {
                switch (c)
                {
                    case '*': sb.Append(".*"); break;
                    case '?': sb.Append('.'); break;
                    case '\u0001': sb.Append("\\["); break;
                    case '\u0002': sb.Append("\\]"); break;
                    default: sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString())); break;
                }
            }
            sb.Append('$');
            return new System.Text.RegularExpressions.Regex(sb.ToString(), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        /// <summary>Converte a analise em configuracoes de pacote (mantendo os padroes da empresa).</summary>
        public static void ApplyTo(PackageSettings s, InstallerInfo info)
        {
            s.InstallerPath = info.Path;
            s.InstallerType = info.Type;
            s.CatalogId = info.CatalogMatch == "nome" ? "" : info.CatalogId ?? "";
            s.AppName = info.AppName ?? "";
            s.AppVendor = info.AppVendor ?? "";
            s.AppVersion = info.AppVersion ?? "";
            s.AppArch = info.AppArch ?? "";
            s.InstallArgs = info.InstallArgs ?? "";
            s.MsiProductCode = info.ProductCode ?? "";
            s.UninstallPattern = info.UninstallPattern ?? "";
            s.UninstallExeArgs = info.UninstallExeArgs ?? "";
            s.ProcessesToClose = new List<string>(info.Processes ?? new List<string>());
            s.InstallBehavior = info.InstallBehavior ?? "System";
            s.DetectionMethod = info.DetectionMethod ?? "Registry";
            s.DetectionNamePattern = info.UninstallPattern ?? "";
            s.DetectionVersion = string.IsNullOrEmpty(info.DetectionVersion) ? info.AppVersion : info.DetectionVersion;
            s.DetectionCheckVersion = info.DetectionCheckVersion;
            s.DetectionFilePath = info.DetectionFilePath ?? "";
            s.Description = info.Description ?? "";
            s.Homepage = info.Homepage ?? "";
            if (info.RemovePrevious.HasValue) s.RemovePreviousVersions = info.RemovePrevious.Value;
        }
    }
}
