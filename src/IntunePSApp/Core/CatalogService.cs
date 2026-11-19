using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Banco de dados de aplicativos: gerado diariamente a partir do repositorio publico microsoft/winget-pkgs
    /// (GitHub Actions deste projeto) e enriquecido com a curadoria (catalog/curated.json).
    /// </summary>
    public class CatalogService
    {
        public static CatalogService Instance { get; } = new CatalogService();

        private Dictionary<string, (CatalogApp App, CatalogInstaller Inst)> _byHash = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (CatalogApp App, CatalogInstaller Inst)> _byCode = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CatalogApp> _byId = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CuratedEntry> _curated = new(StringComparer.OrdinalIgnoreCase);

        public List<CatalogApp> Apps { get; private set; } = new List<CatalogApp>();
        public DateTime? Generated { get; private set; }
        public string Commit { get; private set; }
        public bool IsLoaded => Apps.Count > 0;
        public string CachePath => Path.Combine(AppPaths.CatalogDir, "catalog.json.gz");
        public event Action Changed;

        public async Task LoadAsync()
        {
            LoadCurated();
            if (!File.Exists(CachePath)) { Changed?.Invoke(); return; }
            try { await Task.Run(() => LoadFile(CachePath)); }
            catch (Exception ex) { Log.Warn($"Banco de dados local inválido: {ex.Message}. Atualize o catálogo."); }
            Changed?.Invoke();
        }

        public bool NeedsUpdate(AppConfig cfg)
        {
            if (!File.Exists(CachePath)) return true;
            var last = cfg.LastCatalogCheck ?? File.GetLastWriteTime(CachePath);
            return (DateTime.Now - last).TotalDays >= Math.Max(1, cfg.CatalogUpdateDays);
        }

        /// <summary>Baixa a versao mais recente do banco de dados.</summary>
        public async Task UpdateAsync(string url, IProgress<(long, long?)> progress, CancellationToken ct)
        {
            string tmp = Path.Combine(AppPaths.CatalogDir, "catalog.download.gz");
            Log.Step("Baixando o banco de dados de aplicativos...");
            await Http.DownloadAsync(url, tmp, progress, ct);
            await Task.Run(() => LoadFile(tmp), ct); // valida antes de substituir
            File.Move(tmp, CachePath, true);
            ConfigStore.Current.LastCatalogCheck = DateTime.Now;
            ConfigStore.Save();
            Log.Ok($"Banco de dados atualizado: {Apps.Count:N0} aplicativos.");
            Changed?.Invoke();
        }

        /// <summary>Importa um catalog.json.gz baixado manualmente (uso offline).</summary>
        public async Task ImportAsync(string file)
        {
            await Task.Run(() => LoadFile(file));
            File.Copy(file, CachePath, true);
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
            if (data?.Apps == null || data.Apps.Count == 0) throw new InvalidDataException("Arquivo de catálogo vazio.");
            var byHash = new Dictionary<string, (CatalogApp, CatalogInstaller)>(StringComparer.OrdinalIgnoreCase);
            var byCode = new Dictionary<string, (CatalogApp, CatalogInstaller)>(StringComparer.OrdinalIgnoreCase);
            var byId = new Dictionary<string, CatalogApp>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in data.Apps)
            {
                if (string.IsNullOrEmpty(a.Id)) continue;
                a.Installers ??= new List<CatalogInstaller>();
                if (a.Extra == null && _curated.TryGetValue(a.Id, out var cur)) a.Extra = cur;
                a.NameLower = (a.Name ?? "").ToLowerInvariant();
                a.SearchText = string.Join(" ", new[] { a.Name, a.Id, a.Publisher, a.Moniker, a.Tags == null ? null : string.Join(" ", a.Tags) }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant();
                byId[a.Id] = a;
                foreach (var i in a.Installers)
                {
                    if (!string.IsNullOrEmpty(i.Sha256)) byHash[i.Sha256] = (a, i);
                    if (!string.IsNullOrEmpty(i.ProductCode) && i.ProductCode.StartsWith("{")) byCode[i.ProductCode] = (a, i);
                    if (i.Arp != null) foreach (var e in i.Arp) if (!string.IsNullOrEmpty(e.ProductCode) && e.ProductCode.StartsWith("{")) byCode[e.ProductCode] = (a, i);
                }
            }
            Apps = data.Apps.Where(a => !string.IsNullOrEmpty(a.Id)).ToList();
            _byHash = byHash; _byCode = byCode; _byId = byId;
            Generated = data.Generated;
            Commit = data.Commit;
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
            catch (Exception ex) { Log.Warn($"Curadoria embutida inválida: {ex.Message}"); }
        }

        public CatalogApp ById(string id) => id != null && _byId.TryGetValue(id, out var a) ? a : null;

        public (CatalogApp App, CatalogInstaller Inst) FindByHash(string sha) => sha != null && _byHash.TryGetValue(sha, out var r) ? r : (null, null);

        public (CatalogApp App, CatalogInstaller Inst) FindByProductCode(string pc) => pc != null && _byCode.TryGetValue(pc, out var r) ? r : (null, null);

        public List<CatalogApp> Search(string query, bool supportedOnly, int max = 400)
        {
            IEnumerable<CatalogApp> src = Apps;
            if (supportedOnly) src = src.Where(a => a.IsSupported);
            var q = (query ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0)
            {
                return src.Where(a => a.HasCuration).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
            }
            var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return src.Where(a => tokens.All(t => a.SearchText.Contains(t)))
                .Select(a => (App: a, Score: Score(a, q)))
                .OrderBy(x => x.Score).ThenBy(x => x.App.Name.Length).ThenBy(x => x.App.Name, StringComparer.OrdinalIgnoreCase)
                .Take(max).Select(x => x.App).ToList();
        }

        private static int Score(CatalogApp a, string q)
        {
            int s;
            if (a.NameLower == q) s = 0;
            else if (a.NameLower.StartsWith(q)) s = 1;
            else if (a.Id.Equals(q, StringComparison.OrdinalIgnoreCase)) s = 1;
            else if (a.NameLower.Contains(q)) s = 2;
            else if (a.Id.ToLowerInvariant().Contains(q)) s = 3;
            else s = 4;
            if (!a.HasCuration) s += 1;
            return s;
        }

        public List<CatalogApp> FindByName(string name, string publisher, int max = 3)
        {
            var n = Names.CleanAppName(name ?? "").ToLowerInvariant();
            if (n.Length < 2) return new List<CatalogApp>();
            var pub = (publisher ?? "").ToLowerInvariant();
            return Apps.Where(a => a.NameLower == n || a.NameLower.StartsWith(n + " ") || a.NameLower.StartsWith(n + " ("))
                .OrderBy(a => a.NameLower == n ? 0 : 1)
                .ThenBy(a => pub.Length > 0 && (a.Publisher ?? "").ToLowerInvariant().Contains(pub.Split(' ')[0]) ? 0 : 1)
                .ThenBy(a => a.HasCuration ? 0 : 1)
                .Take(max).ToList();
        }

        /// <summary>Escolhe o melhor instalador: arquitetura preferida, escopo maquina, MSI quando houver, idioma preferido.</summary>
        public static CatalogInstaller PickInstaller(CatalogApp app, string prefArch = "x64", string prefLocale = "pt-BR")
        {
            var list = (app?.Installers ?? new List<CatalogInstaller>()).Where(i => i.IsSupported).ToList();
            if (list.Count == 0) return null;
            int ArchRank(CatalogInstaller i) => string.Equals(i.Arch, prefArch, StringComparison.OrdinalIgnoreCase) ? 0 : i.Arch == "neutral" || string.IsNullOrEmpty(i.Arch) ? 1 : i.Arch == "x86" ? 2 : 3;
            int ScopeRank(CatalogInstaller i) => i.Scope == "machine" ? 0 : string.IsNullOrEmpty(i.Scope) ? 1 : 2;
            int LocRank(CatalogInstaller i) => string.Equals(i.Locale, prefLocale, StringComparison.OrdinalIgnoreCase) ? 0 : (i.Locale ?? "").StartsWith(prefLocale.Split('-')[0], StringComparison.OrdinalIgnoreCase) ? 1 : string.IsNullOrEmpty(i.Locale) ? 2 : string.Equals(i.Locale, "en-US", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
            int TypeRank(CatalogInstaller i) => i.IsMsi ? 0 : 1;
            return list.OrderBy(ArchRank).ThenBy(ScopeRank).ThenBy(TypeRank).ThenBy(LocRank).First();
        }

        /// <summary>Parametros silenciosos para um instalador do catalogo (padroes do winget quando nao informados).</summary>
        public static string SilentArgsFor(CatalogInstaller i)
        {
            if (i == null) return "";
            if (i.IsMsi) return (i.Custom ?? "").Trim();
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
            if (i.Type == "inno" && i.Scope == "machine" && b.IndexOf("/ALLUSERS", StringComparison.OrdinalIgnoreCase) < 0) b += " /ALLUSERS";
            return (b + " " + (i.Custom ?? "")).Trim();
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
            "wix" => "Windows Installer (WiX)",
            "msi" => "Windows Installer (MSI)",
            _ => null
        };

        /// <summary>Aplica as informacoes do catalogo a uma analise de instalador.</summary>
        public static void Apply(InstallerInfo info, CatalogApp app, CatalogInstaller inst, string match)
        {
            if (app == null) return;
            info.CatalogId = app.Id;
            info.CatalogMatch = match;
            info.AppName = Names.CleanAppName(app.Name);
            if (!string.IsNullOrEmpty(app.Publisher)) info.AppVendor = app.Publisher;
            if (match == "hash" || match == "productcode" || match == "download")
            {
                if (!string.IsNullOrEmpty(app.Version)) info.AppVersion = info.Type == "MSI" && !string.IsNullOrEmpty(info.AppVersion) ? info.AppVersion : app.Version;
            }
            info.Description = app.Description;
            info.Homepage = app.Homepage;
            string arpName = null, arpVersion = null;
            if (inst != null)
            {
                if (info.Type == "EXE") info.InstallArgs = SilentArgsFor(inst);
                else info.InstallArgs = (inst.Custom ?? "").Trim();
                var ua = UninstallArgsFor(inst);
                if (!string.IsNullOrEmpty(ua)) info.UninstallExeArgs = ua;
                var tech = TechnologyFor(inst);
                if (info.Type == "EXE") info.Technology = tech ?? (info.Technology == "Desconhecido" ? "Instalador do fabricante" : info.Technology);
                if (inst.Scope == "user") info.InstallBehavior = "User";
                if (!string.IsNullOrEmpty(inst.Arch) && inst.Arch != "neutral") info.AppArch = inst.Arch == "x86" ? "x86" : inst.Arch;
                var arp = inst.Arp?.FirstOrDefault(e => !string.IsNullOrEmpty(e.DisplayName));
                arpName = arp?.DisplayName;
                arpVersion = inst.Arp?.FirstOrDefault(e => !string.IsNullOrEmpty(e.DisplayVersion))?.DisplayVersion;
            }
            info.UninstallPattern = Names.NamePattern(arpName ?? (string.IsNullOrEmpty(info.MsiProductName) ? app.Name : info.MsiProductName));
            info.DetectionVersion = arpVersion ?? info.AppVersion;

            var x = app.Extra;
            if (x != null)
            {
                if (x.Processes != null && x.Processes.Count > 0) info.Processes = new List<string>(x.Processes);
                if (!string.IsNullOrEmpty(x.NamePattern)) info.UninstallPattern = x.NamePattern;
                if (!string.IsNullOrEmpty(x.UninstallArgs)) info.UninstallExeArgs = x.UninstallArgs;
                if (!string.IsNullOrEmpty(x.InstallArgs) && info.Type == "EXE") info.InstallArgs = x.InstallArgs;
                if (!string.IsNullOrEmpty(x.DetectionFile)) { info.DetectionMethod = "File"; info.DetectionFilePath = x.DetectionFile; }
                if (x.CheckVersion.HasValue) info.DetectionCheckVersion = x.CheckVersion.Value;
                if (x.RemovePrevious.HasValue) info.RemovePrevious = x.RemovePrevious.Value;
                if (!string.IsNullOrEmpty(x.Notes)) info.Notes.Add(x.Notes);
            }
        }
    }
}
