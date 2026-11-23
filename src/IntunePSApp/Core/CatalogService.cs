using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Application database: generated daily from the public microsoft/winget-pkgs repository by this project's
    /// GitHub Actions workflow and enriched with the curated knowledge in catalog/curated.json.
    /// </summary>
    public class CatalogService
    {
        private const string CatalogFileName = "catalog.json.gz";
        private const string MetaFileName = "catalog-meta.json";

        public static CatalogService Instance { get; } = new CatalogService();

        private Dictionary<string, (CatalogApp App, CatalogInstaller Inst)> _byHash = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (CatalogApp App, CatalogInstaller Inst)> _byCode = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CatalogApp> _byId = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CuratedEntry> _curated = new(StringComparer.OrdinalIgnoreCase);

        public List<CatalogApp> Apps { get; private set; } = new List<CatalogApp>();
        public DateTime? Generated { get; private set; }
        public string Commit { get; private set; }
        public bool IsLoaded => Apps.Count > 0;
        public string CachePath => Path.Combine(AppPaths.CatalogDir, CatalogFileName);
        public event Action Changed;

        public async Task LoadAsync()
        {
            LoadCurated();
            if (File.Exists(CachePath))
            {
                try { await Task.Run(() => LoadFile(CachePath)); }
                catch (Exception ex) { Log.Warn(T("Log_CatalogInvalid", ex.Message)); }
            }
            Changed?.Invoke();
        }

        public bool NeedsUpdateCheck(AppConfig cfg)
        {
            // A copy that could not be read (damaged, cut short) is downloaded again at once instead of leaving the
            // catalog empty until the next scheduled check.
            if (!File.Exists(CachePath) || !IsLoaded) return true;
            var last = cfg.LastCatalogCheck ?? File.GetLastWriteTime(CachePath);
            return (DateTime.Now - last).TotalDays >= Math.Max(1, cfg.CatalogUpdateDays);
        }

        /// <summary>
        /// The database gives the installer addresses and the SHA256 they are checked against, so it only comes over
        /// HTTPS (or from this computer, for tests and a local mirror): over plain HTTP anyone on the way could swap both.
        /// </summary>
        public static bool IsAllowedUrl(string url)
            => Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));

        /// <summary>
        /// Downloads the latest database. Unless forced, the tiny metadata file is checked first so the 3.5 MB
        /// catalog is only downloaded when it really changed. Returns true when new data was installed.
        /// </summary>
        public async Task<bool> UpdateAsync(string url, IProgress<(long, long?)> progress, CancellationToken ct, bool force = false)
        {
            if (!IsAllowedUrl(url)) throw new InvalidDataException(T("Err_CatalogUrlHttps", url));
            if (!force && IsLoaded && Generated.HasValue)
            {
                var remote = await RemoteGeneratedAsync(url, ct);
                if (remote.HasValue && remote.Value <= Generated.Value.ToUniversalTime().AddMinutes(1))
                {
                    MarkChecked();
                    Log.Info(T("Log_CatalogUpToDate"));
                    return false;
                }
            }
            string tmp = Path.Combine(AppPaths.CatalogDir, "catalog.download.gz");
            Log.Step(T("Log_CatalogDownloading"));
            await Http.DownloadAsync(url, tmp, progress, ct);
            await Task.Run(() => LoadFile(tmp), ct); // validates before replacing the local copy
            File.Move(tmp, CachePath, true);
            MarkChecked();
            Log.Ok(T("Log_CatalogUpdated", Apps.Count));
            Changed?.Invoke();
            return true;
        }

        private static void MarkChecked()
        {
            ConfigStore.Current.LastCatalogCheck = DateTime.Now;
            ConfigStore.Save();
        }

        private static async Task<DateTime?> RemoteGeneratedAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(url) || !url.EndsWith(CatalogFileName, StringComparison.OrdinalIgnoreCase)) return null;
            string json = await Http.TryGetStringAsync(url[..^CatalogFileName.Length] + MetaFileName, 20, ct);
            if (json == null) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("generated").GetDateTime().ToUniversalTime();
            }
            catch { return null; }
        }

        /// <summary>Imports a manually downloaded catalog.json.gz (offline use).</summary>
        public async Task ImportAsync(string file)
        {
            await Task.Run(() => LoadFile(file));
            File.Copy(file, CachePath, true);
            MarkChecked();
            Changed?.Invoke();
        }

        private void LoadFile(string path)
        {
            CatalogFile data;
            using (var fs = File.OpenRead(path))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                data = JsonSerializer.Deserialize<CatalogFile>(gz, Json.Catalog);
            }
            if (data?.Apps == null || data.Apps.Count == 0) throw new InvalidDataException(T("Err_CatalogEmpty"));
            // A database written in a newer format would be read wrongly: say so instead of failing every day with "invalid".
            if (data.Schema > SupportedSchema) throw new InvalidDataException(T("Err_CatalogNewer"));
            var byHash = new Dictionary<string, (CatalogApp, CatalogInstaller)>(StringComparer.OrdinalIgnoreCase);
            var byCode = new Dictionary<string, (CatalogApp, CatalogInstaller)>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<string, CatalogApp>(StringComparer.OrdinalIgnoreCase);
            var apps = new List<CatalogApp>(data.Apps.Count);
            foreach (var a in data.Apps)
            {
                if (string.IsNullOrEmpty(a.Id)) continue;
                a.Name ??= a.Id;
                a.Installers ??= new List<CatalogInstaller>();
                if (_curated.TryGetValue(a.Id, out var cur)) a.Extra = Merge(a.Extra, cur);
                a.NameLower = a.Name.ToLowerInvariant();
                a.NameWords = new HashSet<string>(Regex.Split(a.NameLower, @"[^\p{L}\p{N}\+#]+").Where(w => w.Length > 0));
                a.SearchText = string.Join(" ", new[] { a.Name, a.Id, a.Publisher, a.Moniker, a.Tags == null ? null : string.Join(" ", a.Tags) }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();
                byId[a.Id] = a;
                apps.Add(a);
                foreach (var i in a.Installers)
                {
                    // Manifests often list one file under several rows (x64 and arm64, user and machine scope):
                    // the row that fits a managed x64 fleet is the one a hash lookup answers with.
                    if (!string.IsNullOrEmpty(i.Sha256) && (!byHash.TryGetValue(i.Sha256, out var known) || HashRank(i) < HashRank(known.Item2))) byHash[i.Sha256] = (a, i);
                    if (IsGuid(i.ProductCode)) byCode[i.ProductCode] = (a, i);
                    if (i.Arp != null) foreach (var e in i.Arp) if (IsGuid(e.ProductCode)) byCode[e.ProductCode] = (a, i);
                }
            }
            Apps = apps;
            _byHash = byHash; _byCode = byCode; _byId = byId;
            Generated = data.Generated;
            Commit = data.Commit;
        }

        private static bool IsGuid(string s) => !string.IsNullOrEmpty(s) && s.StartsWith("{");

        /// <summary>Newest format of the database this version understands (CatalogFile.Schema).</summary>
        public const int SupportedSchema = 2;

        private static bool IsArm(CatalogInstaller i) => (i.Arch ?? "").StartsWith("arm", StringComparison.OrdinalIgnoreCase);

        /// <summary>Lower is better: machine scope before user scope, x64/x86 before ARM.</summary>
        private static int HashRank(CatalogInstaller i) => (i.Scope == "user" ? 2 : 0) + (IsArm(i) ? 1 : 0);

        /// <summary>The downloaded catalog wins; the curation embedded in the app fills whatever is missing.</summary>
        private static CuratedEntry Merge(CuratedEntry fromCatalog, CuratedEntry embedded)
        {
            if (fromCatalog == null) return embedded;
            fromCatalog.NotesEn ??= embedded.NotesEn;
            fromCatalog.Notes ??= embedded.Notes;
            fromCatalog.Processes ??= embedded.Processes;
            fromCatalog.NamePattern ??= embedded.NamePattern;
            fromCatalog.UninstallArgs ??= embedded.UninstallArgs;
            return fromCatalog;
        }

        private void LoadCurated()
        {
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("curated.json");
                if (s == null) return;
                var f = JsonSerializer.Deserialize<CuratedFile>(s, Json.Options);
                if (f?.Apps != null) _curated = new Dictionary<string, CuratedEntry>(f.Apps, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { Log.Warn(T("Log_CuratedInvalid", ex.Message)); }
        }

        public CatalogApp ById(string id) => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var a) ? a : null;

        public (CatalogApp App, CatalogInstaller Inst) FindByHash(string sha) => sha != null && _byHash.TryGetValue(sha, out var r) ? r : (null, null);

        public (CatalogApp App, CatalogInstaller Inst) FindByProductCode(string pc) => pc != null && _byCode.TryGetValue(pc, out var r) ? r : (null, null);

        /// <summary>
        /// Dependencies declared by the winget manifest for one installer (the ones common to every installer plus
        /// its own, for example the x64 runtime), resolved against the catalog when possible.
        /// </summary>
        public List<DependencyInfo> DependenciesOf(CatalogApp app, CatalogInstaller installer)
        {
            var list = new List<DependencyInfo>();
            if (app == null) return list;
            var ids = (app.Dependencies ?? new List<string>()).Concat(installer?.Dependencies ?? new List<string>());
            foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
                list.Add(new DependencyInfo { Id = id, App = ById(id) });
            return list;
        }

        /// <summary>Dependencies of the installer that best matches a package (architecture and MSI/EXE).</summary>
        public List<DependencyInfo> DependenciesOf(string appId, string arch, string installerType)
        {
            var app = ById(appId);
            if (app == null) return new List<DependencyInfo>();
            bool? preferMsi = installerType == "MSI" ? true : installerType == "EXE" ? false : (bool?)null;
            var installer = PickInstaller(app, string.IsNullOrEmpty(arch) ? ConfigStore.Current.PreferredArch : arch, ConfigStore.Current.PreferredLocale, preferMsi);
            return DependenciesOf(app, installer);
        }

        public List<CatalogApp> Search(string query, bool supportedOnly, int max = 400)
        {
            IEnumerable<CatalogApp> src = Apps;
            if (supportedOnly) src = src.Where(a => a.IsSupported);
            var q = (query ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0)
                return src.Where(a => a.HasCuration).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
            var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return src.Where(a => tokens.All(t => a.SearchText.Contains(t)))
                .Select(a => (App: a, Score: Score(a, q, tokens)))
                .OrderBy(x => x.Score).ThenBy(x => x.App.HasCuration ? 0 : 1).ThenBy(x => x.App.Name.Length).ThenBy(x => x.App.Name, StringComparer.OrdinalIgnoreCase)
                .Take(max).Select(x => x.App).ToList();
        }

        /// <summary>
        /// Lower is better. A whole word of the name beats a piece of a longer word: "edge" finds Microsoft Edge before
        /// EdgeEver, "teams" finds Microsoft Teams before TeamSpeak. The winget moniker ("ssms", "vscode") counts as the name.
        /// </summary>
        internal static int Score(CatalogApp a, string q, string[] tokens)
        {
            if (a.NameLower == q || string.Equals(a.Moniker, q, StringComparison.OrdinalIgnoreCase)) return 0;
            if (a.NameWords != null && tokens.All(a.NameWords.Contains)) return a.NameLower.StartsWith(q) ? 1 : 2;
            if (a.Id.Equals(q, StringComparison.OrdinalIgnoreCase)) return 2;
            if (a.NameLower.StartsWith(q)) return 3;
            if (a.NameLower.Contains(q)) return 4;
            if (a.Id.Contains(q, StringComparison.OrdinalIgnoreCase)) return 5;
            return 6;
        }

        /// <summary>
        /// Other applications of the database whose name (or the name they register in "Installed apps") the pattern also
        /// matches: "KeePass*" finds KeePassXC. On a device where one of them is installed it would be uninstalled before
        /// the installation, and it would satisfy the detection rule. Translations of the same package are not listed.
        /// </summary>
        public List<string> OthersMatching(string pattern, string catalogId, int max = 5)
        {
            var found = new List<string>();
            if (!IsLoaded || string.IsNullOrWhiteSpace(pattern) || Regex.Replace(pattern, @"[\*\?\s]", "").Length < 2) return found;
            var rx = InstallerAnalyzer.WildcardToRegex(pattern.Trim());
            string family = string.IsNullOrEmpty(catalogId) ? null : catalogId + ".";
            foreach (var a in Apps)
            {
                if (string.Equals(a.Id, catalogId, StringComparison.OrdinalIgnoreCase)) continue;
                // Mozilla.Firefox.pt-BR is the same product as Mozilla.Firefox.
                if (family != null && a.Id.StartsWith(family, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(a.Id.Substring(family.Length), @"^[a-z]{2}(-[A-Za-z]{2,4})?$")) continue;
                var names = (a.Installers ?? new List<CatalogInstaller>()).SelectMany(i => i.Arp ?? new List<ArpEntry>()).Select(e => e.DisplayName).Where(n => !string.IsNullOrEmpty(n)).ToList();
                if (names.Count == 0) names.Add(a.Name);
                if (!names.Any(rx.IsMatch)) continue;
                if (!found.Contains(a.Name, StringComparer.OrdinalIgnoreCase)) found.Add(a.Name);
                if (found.Count >= max) break;
            }
            return found;
        }

        public List<CatalogApp> FindByName(string name, string publisher, int max = 3)
        {
            var n = Names.CleanAppName(name ?? "").ToLowerInvariant();
            if (n.Length < 2) return new List<CatalogApp>();
            string pub = (publisher ?? "").ToLowerInvariant().Split(' ').FirstOrDefault() ?? "";
            return Apps.Where(a => a.NameLower == n || a.NameLower.StartsWith(n + " ") || a.NameLower.StartsWith(n + " ("))
                .OrderBy(a => a.NameLower == n ? 0 : 1)
                .ThenBy(a => pub.Length > 0 && (a.Publisher ?? "").Contains(pub, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(a => a.HasCuration ? 0 : 1)
                .Take(max).ToList();
        }

        /// <summary>Best installer for Intune: preferred architecture, machine scope, MSI when available, preferred language.</summary>
        public static CatalogInstaller PickInstaller(CatalogApp app, string prefArch = "x64", string prefLocale = "pt-BR", bool? preferMsi = null)
        {
            prefArch = string.IsNullOrEmpty(prefArch) ? "x64" : prefArch;
            bool armTarget = prefArch.StartsWith("arm", StringComparison.OrdinalIgnoreCase);
            // An installer the target cannot run is never an answer: ARM builds on x64/x86, x64 builds on x86.
            var list = (app?.Installers ?? new List<CatalogInstaller>()).Where(i => i.IsSupported
                && (armTarget || !IsArm(i)) && !(prefArch == "x86" && i.Arch == "x64")).ToList();
            if (list.Count == 0) return null;
            prefLocale = string.IsNullOrEmpty(prefLocale) ? "en-US" : prefLocale;
            string lang = prefLocale.Split('-')[0];
            int ArchRank(CatalogInstaller i) => string.Equals(i.Arch, prefArch, StringComparison.OrdinalIgnoreCase) ? 0 : i.Arch == "neutral" || string.IsNullOrEmpty(i.Arch) ? 1 : i.Arch == "x86" ? 2 : 3;
            int ScopeRank(CatalogInstaller i) => i.Scope == "machine" ? 0 : string.IsNullOrEmpty(i.Scope) ? 1 : 2;
            int LocRank(CatalogInstaller i) => string.Equals(i.Locale, prefLocale, StringComparison.OrdinalIgnoreCase) ? 0 : (i.Locale ?? "").StartsWith(lang, StringComparison.OrdinalIgnoreCase) ? 1 : string.IsNullOrEmpty(i.Locale) ? 2 : string.Equals(i.Locale, "en-US", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
            int TypeRank(CatalogInstaller i) => preferMsi == false ? (i.IsMsi ? 1 : 0) : (i.IsMsi ? 0 : 1);
            return list.OrderBy(ArchRank).ThenBy(ScopeRank).ThenBy(TypeRank).ThenBy(LocRank).First();
        }

        /// <summary>Silent switches for a catalog installer (winget defaults when the manifest does not state them).</summary>
        public static string SilentArgsFor(CatalogInstaller i)
        {
            if (i == null) return "";
            // MSI: the quiet/log switches are added by the toolkit, but the properties a manifest puts in "Silent"
            // (IACCEPTMSODBCSQLLICENSETERMS=YES, ACCEPTLICENSE=YES...) are needed for the installation to run at all.
            if (i.IsMsi)
            {
                string custom = (i.Custom ?? "").Trim();
                var extra = MsiProperties(i.Silent).Where(p => custom.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0);
                return (string.Join(" ", extra) + " " + custom).Trim();
            }
            string b = i.Silent;
            if (string.IsNullOrWhiteSpace(b))
            {
                b = i.Type switch
                {
                    "inno" => "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                    "nullsoft" => "/S",
                    "burn" => "/quiet /norestart",
                    _ => ""
                };
            }
            // Inno Setup installers that offer both install modes take the one of the row on the command line, as winget does:
            // without /CURRENTUSER a per-user row starts in administrative mode, which fails (or asks for elevation) when
            // the package runs in the user context. Installers with a single mode ignore the switch.
            string given = b + " " + i.Custom;
            bool hasMode = given.IndexOf("/ALLUSERS", StringComparison.OrdinalIgnoreCase) >= 0 || given.IndexOf("/CURRENTUSER", StringComparison.OrdinalIgnoreCase) >= 0;
            if (i.Type == "inno" && !hasMode && i.Scope == "machine") b += " /ALLUSERS";
            else if (i.Type == "inno" && !hasMode && i.Scope == "user") b += " /CURRENTUSER";
            return (b + " " + (i.Custom ?? "")).Trim();
        }

        /// <summary>The switches of a command line, a quoted part kept with its switch (/v"/qn REBOOT=ReallySuppress").</summary>
        internal static List<string> Switches(string text)
            => Regex.Matches(text ?? "", "(?:[^\\s\"]+|\"[^\"]*\")+").Select(m => m.Value).ToList();

        /// <summary>
        /// What is left of an msiexec command line once the interface, restart and log switches are removed:
        /// the properties and anything else the manifest asks for.
        /// </summary>
        internal static List<string> MsiProperties(string switches)
        {
            var kept = new List<string>();
            var tokens = Regex.Matches(switches ?? "", "(?:[^\\s\"]+|\"[^\"]*\")+").Select(m => m.Value).ToList();
            for (int k = 0; k < tokens.Count; k++)
            {
                string t = tokens[k];
                if (Regex.IsMatch(t, @"^[/-](q[nbrf]?[\+\-!]*|quiet|passive|norestart|promptrestart|forcerestart)$", RegexOptions.IgnoreCase)) continue;
                // /l*v <file> and /log <file>: the switch and its file name.
                if (Regex.IsMatch(t, @"^[/-](l[a-z\*\+!]*|log)$", RegexOptions.IgnoreCase)) { k++; continue; }
                kept.Add(t);
            }
            return kept;
        }

        public static string UninstallArgsFor(CatalogInstaller i) => i?.Type switch
        {
            "inno" => "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            "nullsoft" => "/S",
            _ => ""
        };

        public static string TechnologyFor(CatalogInstaller i) => i?.Type switch
        {
            "inno" => "Inno Setup",
            "nullsoft" => "NSIS",
            "burn" => "WiX Burn",
            "wix" => "MSI",
            "msi" => "MSI",
            _ => null
        };

        /// <summary>Applies catalog knowledge to an installer analysis.</summary>
        public static void Apply(InstallerInfo info, CatalogApp app, CatalogInstaller inst, string match)
        {
            if (app == null) return;
            info.CatalogId = app.Id;
            info.CatalogMatch = match;
            // Many vendors keep the MSI product code from one version to the next: the same code does not mean the same
            // version. An older file then keeps its own version, so detection finds it and the "newer version" notice shows.
            string arpOfMatch = inst?.Arp?.FirstOrDefault(e => !string.IsNullOrEmpty(e.DisplayVersion))?.DisplayVersion;
            // Only decided when the manifest states the version the installer registers: some MSI packages number their
            // versions differently from the database, and those are the same release.
            bool sameVersion = match != "productcode" || string.IsNullOrEmpty(info.AppVersion) || arpOfMatch == null
                || VersionUtil.Compare(info.AppVersion, app.Version) == 0 || VersionUtil.Compare(info.AppVersion, arpOfMatch) == 0;
            info.CatalogVersion = !info.IsCatalogExact ? null : sameVersion ? app.Version : info.AppVersion;
            info.AppName = Names.CleanAppName(app.Name);
            if (!string.IsNullOrEmpty(app.Publisher)) info.AppVendor = app.Publisher;
            // MSI packages keep their own ProductVersion, unless the database has the same version written in full
            // ("2.8.0" instead of the trimmed "2.8").
            if (info.IsCatalogExact && sameVersion && !string.IsNullOrEmpty(app.Version)
                && (info.Type != "MSI" || string.IsNullOrEmpty(info.AppVersion) || VersionUtil.Compare(app.Version, info.AppVersion) == 0))
                info.AppVersion = app.Version;
            info.Description = app.LocalizedDescription;
            info.Homepage = app.Homepage;
            info.PrivacyUrl = app.PrivacyUrl;

            string arpName = null, arpVersion = null;
            if (inst != null)
            {
                // A plain "exe" row that states no silent switch says nothing about the installer, but the file itself may:
                // the analysis recognises NSIS, Inno Setup, InstallShield, Advanced Installer and Squirrel stubs. Their
                // switches are kept, with the custom switches of the row (a third of the rows without switches, sampled in 2.6).
                string args = SilentArgsFor(inst);
                string detected = info.Type == "EXE" ? InstallerAnalyzer.DefaultInstallSwitches(info.Technology) : "";
                if (string.Equals(inst.Type, "exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(inst.Silent) && detected.Length > 0)
                {
                    string custom = (inst.Custom ?? "").Trim();
                    var missing = Switches(detected).Where(t => !Switches(custom).Contains(t, StringComparer.OrdinalIgnoreCase));
                    args = (string.Join(" ", missing) + " " + custom).Trim();
                }
                info.InstallArgs = args;
                var ua = UninstallArgsFor(inst);
                if (!string.IsNullOrEmpty(ua)) info.UninstallExeArgs = ua;
                if (info.Type == "EXE") info.Technology = TechnologyFor(inst) ?? (info.Technology == InstallerAnalyzer.TechUnknown ? InstallerAnalyzer.TechVendor : info.Technology);
                if (inst.Scope == "user") info.InstallBehavior = "User";
                // The row the user chose says the architecture. A row found by hash or product code only fills it in when
                // the file itself did not tell: one file is often listed for x64 and for ARM64.
                if (!string.IsNullOrEmpty(inst.Arch) && inst.Arch != "neutral" && (match == "download" || string.IsNullOrEmpty(info.AppArch))) info.AppArch = inst.Arch;
                arpName = inst.Arp?.FirstOrDefault(e => !string.IsNullOrEmpty(e.DisplayName))?.DisplayName;
                arpVersion = sameVersion ? arpOfMatch : null;
                // Exit codes the manifest declares as success (besides 0).
                if (inst.SuccessCodes != null && inst.SuccessCodes.Count > 0) info.SuccessCodes = string.Join(", ", inst.SuccessCodes.Where(c => c != 0).Distinct());
                // A setup program that installs a Windows Installer product (Adobe Reader, InstallShield setups): the
                // catalog gives its product code, so the package removes it by that code (msiexec /x) without switches.
                // Bundles (burn), NSIS, Inno Setup and Squirrel register keys of their own, never an MSI: only plain "exe"
                // rows, and only when the file itself is not one of those either.
                if (info.Type == "EXE" && string.IsNullOrEmpty(info.ProductCode) && string.Equals(inst.Type, "exe", StringComparison.OrdinalIgnoreCase)
                    && info.Technology is not ("NSIS" or "Inno Setup" or "WiX Burn" or "Squirrel")
                    && Guid.TryParseExact((inst.ProductCode ?? "").Trim(), "B", out var msiCode) && msiCode != Guid.Empty)
                {
                    info.ProductCode = msiCode.ToString("B").ToUpperInvariant();
                    info.Notes.Add(T("Analysis_ExeMsiCode", info.ProductCode));
                }
            }
            info.UninstallPattern = Names.NamePattern(arpName ?? (string.IsNullOrEmpty(info.MsiProductName) ? app.Name : info.MsiProductName));
            info.DetectionVersion = arpVersion ?? info.AppVersion;

            var x = app.Extra;
            if (x == null) return;
            if (x.Processes != null && x.Processes.Count > 0) info.Processes = new List<string>(x.Processes);
            if (!string.IsNullOrEmpty(x.NamePattern)) info.UninstallPattern = x.NamePattern;
            if (!string.IsNullOrEmpty(x.UninstallArgs)) info.UninstallExeArgs = x.UninstallArgs;
            if (!string.IsNullOrEmpty(x.InstallArgs) && info.Type == "EXE") info.InstallArgs = x.InstallArgs;
            if (!string.IsNullOrEmpty(x.DetectionFile)) { info.DetectionMethod = "File"; info.DetectionFilePath = x.DetectionFile; }
            if (x.CheckVersion.HasValue) info.DetectionCheckVersion = x.CheckVersion.Value;
            if (x.RemovePrevious.HasValue) info.RemovePrevious = x.RemovePrevious.Value;
            if (!string.IsNullOrEmpty(x.LocalizedNotes) && !info.Notes.Contains(x.LocalizedNotes)) info.Notes.Add(x.LocalizedNotes);
        }

        // ------------------------------------------------------------------ downloads

        /// <summary>Folder + file name where an installer of the database is stored locally.</summary>
        public static string LocalInstallerPath(CatalogApp app, CatalogInstaller inst)
        {
            string ext = inst.IsMsi ? ".msi" : ".exe";
            string name = "";
            try { name = Uri.UnescapeDataString(Path.GetFileName(new Uri(inst.Url).AbsolutePath)); } catch { }
            name = string.Concat((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (string.IsNullOrWhiteSpace(name) || name.Length < 5 || !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                name = $"{Names.Safe(app.Name)}_{Names.Safe(app.Version)}_{inst.Arch}{ext}";
            // x64 and ARM64 rows (or machine and user) often share the file name: each variant gets its own folder.
            string variant = string.Join("-", new[] { inst.Arch, inst.Scope, inst.Locale }.Where(v => !string.IsNullOrWhiteSpace(v)));
            return Path.Combine(AppPaths.Downloads, Names.Safe(app.Id), Names.Safe(app.Version ?? "latest"), Names.Safe(variant.Length > 0 ? variant : "any"), name);
        }

        /// <summary>
        /// Downloads an installer from the vendor URL recorded in the database and verifies its SHA256.
        /// A file already downloaded and still matching the hash is reused.
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(CatalogApp app, CatalogInstaller inst, IProgress<(long, long?)> progress, CancellationToken ct)
        {
            string dest = LocalInstallerPath(app, inst);
            bool cached = File.Exists(dest) && !string.IsNullOrEmpty(inst.Sha256)
                && (await Task.Run(() => Hashing.Sha256File(dest), ct)).Equals(inst.Sha256, StringComparison.OrdinalIgnoreCase);
            if (!cached)
            {
                Log.Step(T("Log_Downloading", inst.Url));
                await Http.DownloadAsync(inst.Url, dest, progress, ct, inst.Sha256);
                Log.Ok(T("Log_DownloadVerified", Path.GetFileName(dest)));
            }
            return dest;
        }
    }
}
