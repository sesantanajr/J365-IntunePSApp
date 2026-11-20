using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>Analyses .msi/.exe installers: metadata, installer technology, silent switches and catalog match.</summary>
    public static class InstallerAnalyzer
    {
        public const string TechUnknown = "Unknown";
        public const string TechVendor = "Vendor";
        public const string TechMsi = "MSI";

        /// <summary>Default silent switches per installer technology.</summary>
        private static readonly Dictionary<string, (string Install, string Uninstall)> TechDefaults = new()
        {
            ["NSIS"] = ("/S", "/S"),
            ["Inno Setup"] = ("/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART", "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"),
            ["WiX Burn"] = ("/quiet /norestart", ""),
            ["InstallShield"] = ("/s /v\"/qn REBOOT=ReallySuppress\"", ""),
            ["Advanced Installer"] = ("/exenoui /qn /norestart", ""),
            ["Squirrel"] = ("--silent", ""),
            [TechUnknown] = ("", ""),
        };

        /// <summary>Signatures found in the installer stub, checked in order (first match wins).</summary>
        private static readonly (string Tech, string[] Markers)[] TechSignatures =
        {
            ("Inno Setup", new[] { "Inno Setup", "InnoSetupLdrWindow" }),
            ("NSIS", new[] { "Nullsoft", "NSIS Error", "NullsoftInst" }),
            ("InstallShield", new[] { "InstallShield" }),
            ("Advanced Installer", new[] { "Advanced Installer", "Caphyon" }),
            ("Squirrel", new[] { "SquirrelSetup", "Squirrel.Windows", "Squirrel installer" }),
        };

        public static string TechnologyLabel(string tech) => tech switch
        {
            TechUnknown => T("Tech_Unknown"),
            TechVendor => T("Tech_Vendor"),
            TechMsi => "Windows Installer (MSI)",
            null or "" => "",
            _ => tech
        };

        private static string TechNote(string tech) => tech switch
        {
            "NSIS" => T("Tech_Note_NSIS"),
            "Inno Setup" => T("Tech_Note_Inno"),
            "WiX Burn" => T("Tech_Note_Burn"),
            "InstallShield" => T("Tech_Note_InstallShield"),
            "Advanced Installer" => T("Tech_Note_AdvancedInstaller"),
            "Squirrel" => T("Tech_Note_Squirrel"),
            _ => T("Tech_Note_Unknown")
        };

        public static InstallerInfo Analyze(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(T("Err_FileNotFound", path), path);
            var fi = new FileInfo(path);
            string ext = fi.Extension.ToLowerInvariant();
            var info = new InstallerInfo
            {
                Path = fi.FullName,
                FileName = fi.Name,
                SizeMB = Math.Round(fi.Length / 1024d / 1024d, 1)
            };

            if (Signature.IsValid(fi.FullName)) info.Signer = Signature.Signer(fi.FullName).Name ?? T("Analysis_Signed");

            string unknownNote = null;
            switch (ext)
            {
                case ".msi":
                    {
                        info.Type = "MSI";
                        info.Technology = TechMsi;
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
                        info.Notes.Add(T("Analysis_MsiSilent"));
                        var transforms = Directory.GetFiles(fi.DirectoryName, "*.mst").Select(Path.GetFileName).ToList();
                        if (transforms.Count > 0) info.Notes.Add(T("Analysis_MstFound", string.Join(", ", transforms), transforms[0]));
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
                        info.AppArch = ArchitectureFromName(fi.Name) ?? (PeArchitecture(fi.FullName) is string pe && pe != "x86" ? pe : "");
                        var td = TechDefaults[info.Technology];
                        info.InstallArgs = td.Install;
                        info.UninstallExeArgs = td.Uninstall;
                        string note = TechNote(info.Technology);
                        if (info.Technology == TechUnknown) unknownNote = note;
                        info.Notes.Add(note);
                        info.UninstallPattern = Names.NamePattern(info.AppName);
                        if (info.Technology == "Squirrel") info.InstallBehavior = "User";
                        break;
                    }
                default:
                    throw new NotSupportedException(T("Err_UnsupportedFile", ext));
            }
            info.DetectionVersion = info.AppVersion;

            // Catalog match: identical hash > MSI product code > name.
            var catalog = CatalogService.Instance;
            if (catalog.IsLoaded)
            {
                try { info.Sha256 = Hashing.Sha256File(fi.FullName); } catch { }
                var (app, inst) = catalog.FindByHash(info.Sha256);
                string match = app != null ? "hash" : null;
                if (app == null && !string.IsNullOrEmpty(info.ProductCode))
                {
                    (app, inst) = catalog.FindByProductCode(info.ProductCode);
                    if (app != null) match = "productcode";
                }
                if (app != null)
                {
                    CatalogService.Apply(info, app, inst, match);
                    if (unknownNote != null) info.Notes.Remove(unknownNote);
                    info.Notes.Insert(0, match == "hash" ? T("Analysis_MatchHash", app.Name, app.Id) : T("Analysis_MatchProductCode", app.Name, app.Id));
                }
                else
                {
                    var similar = catalog.FindByName(info.AppName, info.AppVendor, 1).FirstOrDefault();
                    if (similar != null) ApplySimilar(info, similar, unknownNote);
                }
            }

            info.Notes.Add(string.IsNullOrEmpty(info.Signer) ? T("Analysis_NotSigned") : T("Analysis_SignedBy", info.Signer));
            return info;
        }

        /// <summary>A catalog entry with the same name: only fills what the file itself could not tell.</summary>
        private static void ApplySimilar(InstallerInfo info, CatalogApp similar, string unknownNote)
        {
            info.CatalogId = similar.Id;
            info.CatalogMatch = "name";
            var x = similar.Extra;
            if (x?.Processes != null && info.Processes.Count == 0) info.Processes = new List<string>(x.Processes);
            // The pattern of a look-alike is only taken when it really matches this product: an installer of Python 3.12
            // must not inherit "Python 3.13*" from the entry of 3.13 (Intune would never detect it, and 3.13 would be removed).
            if (!string.IsNullOrEmpty(x?.NamePattern))
            {
                var rx = WildcardToRegex(x.NamePattern);
                if (new[] { info.MsiProductName, info.AppName }.Any(n => !string.IsNullOrEmpty(n) && rx.IsMatch(n))) info.UninstallPattern = x.NamePattern;
            }
            if (!string.IsNullOrEmpty(x?.UninstallArgs) && info.Type == "EXE") info.UninstallExeArgs = x.UninstallArgs;
            if (info.Technology == TechUnknown && info.Type == "EXE")
            {
                var ci = CatalogService.PickInstaller(similar, string.IsNullOrEmpty(info.AppArch) ? "x64" : info.AppArch, preferMsi: false);
                if (ci != null && !ci.IsMsi)
                {
                    info.InstallArgs = CatalogService.SilentArgsFor(ci);
                    info.Technology = CatalogService.TechnologyFor(ci) ?? TechVendor;
                    if (unknownNote != null) info.Notes.Remove(unknownNote);
                }
            }
            if (string.IsNullOrEmpty(info.Description)) info.Description = similar.LocalizedDescription;
            if (string.IsNullOrEmpty(info.Homepage)) info.Homepage = similar.Homepage;
            info.Notes.Insert(0, T("Analysis_MatchName", similar.Name, similar.Id));
            if (!string.IsNullOrEmpty(x?.LocalizedNotes)) info.Notes.Add(x.LocalizedNotes);
        }

        private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        private static string ArchitectureFromName(string fileName)
        {
            string fn = fileName.ToLowerInvariant();
            if (fn.Contains("arm64")) return "arm64";
            if (Regex.IsMatch(fn, @"x64|amd64|win64|64-?bit|x86_64")) return "x64";
            if (Regex.IsMatch(fn, @"x86|win32|32-?bit|ia32")) return "x86";
            return null;
        }

        /// <summary>
        /// Identifies the installer framework by scanning the first 16 MB of the file for known ASCII/UTF-16 markers
        /// (vectorised byte search, no string conversion of the binary).
        /// </summary>
        public static string DetectTechnology(string path, FileVersionInfo vi)
        {
            byte[] buf;
            int read = 0;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int len = (int)Math.Min(fs.Length, 16 * 1024 * 1024);
                buf = new byte[len];
                int n;
                while (read < len && (n = fs.Read(buf, read, len - read)) > 0) read += n;
            }
            string meta = $"{vi?.Comments} | {vi?.FileDescription} | {vi?.ProductName} | {vi?.LegalCopyright}";

            if (ContainsBytes(buf, read, Encoding.ASCII.GetBytes(".wixburn"))) return "WiX Burn";
            foreach (var (tech, markers) in TechSignatures)
            {
                foreach (var marker in markers)
                {
                    if (meta.Contains(marker, StringComparison.Ordinal)
                        || ContainsBytes(buf, read, Encoding.ASCII.GetBytes(marker))
                        || ContainsBytes(buf, read, Encoding.Unicode.GetBytes(marker)))
                        return tech;
                }
            }
            return TechUnknown;
        }

        private static bool ContainsBytes(byte[] data, int length, byte[] pattern) => data.AsSpan(0, length).IndexOf(pattern) >= 0;

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

        /// <summary>Programs installed on this computer whose display name matches the wildcard pattern.</summary>
        public static List<InstalledProgram> FindInstalled(string pattern)
        {
            var result = new List<InstalledProgram>();
            var targets = new (RegistryHive Hive, RegistryView View, string ScopeKey)[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64, "Installed_ScopeMachine64"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, "Installed_ScopeMachine32"),
                (RegistryHive.CurrentUser, RegistryView.Default, "Installed_ScopeUser")
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
                            Scope = T(t.ScopeKey),
                            KeyName = sub,
                            DisplayIcon = k.GetValue("DisplayIcon") as string ?? ""
                        });
                    }
                }
                catch { }
            }
            return result.GroupBy(r => r.Name + "|" + r.Version + "|" + r.KeyName).Select(g => g.First()).OrderBy(r => r.Name).ToList();
        }

        /// <summary>Same semantics as PowerShell's -like: * and ? wildcards, `[ and `] as literal brackets.</summary>
        public static Regex WildcardToRegex(string pattern)
        {
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
                    default: sb.Append(Regex.Escape(c.ToString())); break;
                }
            }
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }

        /// <summary>Copies an analysis into package settings (company defaults already in <paramref name="s"/> are kept).</summary>
        public static void ApplyTo(PackageSettings s, InstallerInfo info)
        {
            s.InstallerPath = info.Path;
            s.InstallerType = info.Type;
            s.CatalogId = info.IsCatalogExact ? info.CatalogId ?? "" : "";
            s.CatalogVersion = info.IsCatalogExact ? info.CatalogVersion ?? "" : "";
            s.AppName = info.AppName ?? "";
            s.AppVendor = info.AppVendor ?? "";
            s.AppVersion = info.AppVersion ?? "";
            s.AppArch = info.AppArch ?? "";
            s.InstallArgs = info.InstallArgs ?? "";
            s.ExtraSuccessCodes = info.SuccessCodes ?? "";
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
            s.PrivacyUrl = info.PrivacyUrl ?? "";
            if (info.RemovePrevious.HasValue) s.RemovePreviousVersions = info.RemovePrevious.Value;
        }

        /// <summary>
        /// Replaces only the installer-specific fields of an existing project (new version of the same app),
        /// keeping every customisation: messages, actions, branding, processes and detection method.
        /// </summary>
        public static void ApplyNewVersion(PackageSettings s, InstallerInfo info)
        {
            // The new version comes in another format (an .exe became an .msi, or the reverse): the switches of the old
            // installer mean nothing to the new one (msiexec fails with 1639 on "/VERYSILENT") and the product code is stale.
            if (!string.IsNullOrEmpty(s.InstallerType) && !string.Equals(s.InstallerType, info.Type, StringComparison.OrdinalIgnoreCase))
            {
                s.InstallArgs = info.InstallArgs ?? "";
                s.UninstallExeArgs = info.UninstallExeArgs ?? "";
                s.MsiProductCode = info.ProductCode ?? "";
                if (s.DetectionMethod == "ProductCode" && string.IsNullOrEmpty(s.MsiProductCode)) s.DetectionMethod = "Registry";
            }
            s.InstallerPath = info.Path;
            s.InstallerType = info.Type;
            if (info.IsCatalogExact) { s.CatalogId = info.CatalogId ?? s.CatalogId; s.CatalogVersion = info.CatalogVersion ?? ""; }
            else s.CatalogVersion = "";
            s.AppVersion = info.AppVersion ?? s.AppVersion;
            if (!string.IsNullOrEmpty(info.AppArch)) s.AppArch = info.AppArch;
            if (!string.IsNullOrEmpty(info.ProductCode)) s.MsiProductCode = info.ProductCode;
            if (string.IsNullOrWhiteSpace(s.InstallArgs)) s.InstallArgs = info.InstallArgs ?? "";
            s.DetectionVersion = string.IsNullOrEmpty(info.DetectionVersion) ? info.AppVersion : info.DetectionVersion;
        }
    }
}
