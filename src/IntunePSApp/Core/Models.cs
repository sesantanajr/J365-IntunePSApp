using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace J365.IntunePSApp.Core
{
    public static class AppInfo
    {
        public const string Name = "J365 IntunePSApp";
        public const string GitHubRepo = "sesantanajr/J365-IntunePSApp";
        public const string BlogUrl = "https://jornada365.cloud";
        public static string RepoUrl => "https://github.com/" + GitHubRepo;
        public static string DefaultCatalogUrl => RepoUrl + "/releases/download/catalog/catalog.json.gz";
        public static string Version
        {
            get
            {
                var v = typeof(AppInfo).Assembly.GetName().Version;
                return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }
    }

    /// <summary>Todas as opcoes de um pacote (salvas em projeto.json).</summary>
    public class PackageSettings
    {
        // Instalador
        public string InstallerPath { get; set; } = "";
        public bool IncludeInstallerFolder { get; set; }
        public string InstallerType { get; set; } = "";       // MSI | EXE
        public string CatalogId { get; set; } = "";
        // Aplicativo
        public string AppName { get; set; } = "";
        public string AppVendor { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public string AppArch { get; set; } = "";             // "" | x64 | x86 | arm64
        public string InstallArgs { get; set; } = "";
        public string MsiProductCode { get; set; } = "";
        public string InstallBehavior { get; set; } = "System"; // System | User
        public bool WaitForChildProcesses { get; set; }
        public string ExtraSuccessCodes { get; set; } = "";
        public string Description { get; set; } = "";
        public string Homepage { get; set; } = "";
        // Experiencia
        public string DeployMode { get; set; } = "Auto";      // Auto | Silent | Interactive
        public List<string> ProcessesToClose { get; set; } = new List<string>();
        public int CloseAppsCountdown { get; set; } = 300;
        public bool AllowDefer { get; set; } = true;
        public int DeferTimes { get; set; } = 3;
        public bool BlockExecution { get; set; } = true;
        public bool CheckDiskSpace { get; set; } = true;
        public string WelcomeMessage { get; set; } = "";
        public string ProgressMessage { get; set; } = "";
        public string CompletionMessage { get; set; } = "";
        public bool RestartPrompt { get; set; }
        public int RestartCountdown { get; set; } = 600;
        // Desinstalacao / versoes anteriores
        public bool RemovePreviousVersions { get; set; } = true;
        public string UninstallPattern { get; set; } = "";
        public string UninstallExeArgs { get; set; } = "";
        // Acoes e arquivos extras
        public List<PackageAction> Actions { get; set; } = new List<PackageAction>();
        public List<string> ExtraFiles { get; set; } = new List<string>();
        // Visual
        public string CompanyName { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public string AccentColor { get; set; } = "";
        public string DialogStyle { get; set; } = "Fluent";
        public string Language { get; set; } = "Auto";
        public bool LogsInIntuneFolder { get; set; } = true;
        public string Author { get; set; } = "";
        // Deteccao
        public string DetectionMethod { get; set; } = "Registry"; // Registry | ProductCode | File
        public string DetectionNamePattern { get; set; } = "";
        public string DetectionVersion { get; set; } = "";
        public bool DetectionCheckVersion { get; set; } = true;
        public string DetectionFilePath { get; set; } = "";
        // Saida
        public string OutputRoot { get; set; } = "";

        public PackageSettings Clone()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this, Json.Options);
            return System.Text.Json.JsonSerializer.Deserialize<PackageSettings>(json, Json.Options);
        }
    }

    public class PackageAction
    {
        public string Type { get; set; } = "";
        public string Phase { get; set; } = "PostInstall";   // PreInstall | PostInstall | PreUninstall | PostUninstall
        public bool ContinueOnError { get; set; } = true;
        public Dictionary<string, string> Params { get; set; } = new Dictionary<string, string>();

        public string P(string key) => Params != null && Params.TryGetValue(key, out var v) ? v ?? "" : "";

        [JsonIgnore] public string TypeLabel => ActionCatalog.Get(Type)?.Label ?? Type;
        [JsonIgnore] public string PhaseLabel => ActionCatalog.PhaseLabel(Phase);
        [JsonIgnore] public string Summary => ActionCatalog.Summarize(this);
        [JsonIgnore] public string Glyph => ActionCatalog.Get(Type)?.Glyph ?? "";
    }

    /// <summary>Configuracao do aplicativo e padroes da empresa (portatil).</summary>
    public class AppConfig
    {
        public string CompanyName { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public string AccentColor { get; set; } = "#0A5CA8";
        public string DialogStyle { get; set; } = "Fluent";
        public string Language { get; set; } = "Auto";
        public bool LogsInIntuneFolder { get; set; } = true;
        public string Author { get; set; } = "";
        public string DeployMode { get; set; } = "Auto";
        public bool AllowDefer { get; set; } = true;
        public int DeferTimes { get; set; } = 3;
        public int CloseAppsCountdown { get; set; } = 300;
        public bool BlockExecution { get; set; } = true;
        public bool CheckDiskSpace { get; set; } = true;
        public bool RemovePreviousVersions { get; set; } = true;
        public string WelcomeMessage { get; set; } = "";
        public string CompletionMessage { get; set; } = "";
        public string OutputFolder { get; set; } = "";
        public string CatalogUrl { get; set; } = "";
        public int CatalogUpdateDays { get; set; } = 3;
        public DateTime? LastCatalogCheck { get; set; }
        public string PreferredArch { get; set; } = "x64";
        public string PreferredLocale { get; set; } = "pt-BR";

        public PackageSettings NewPackage()
        {
            return new PackageSettings
            {
                CompanyName = CompanyName,
                LogoPath = LogoPath,
                AccentColor = AccentColor,
                DialogStyle = DialogStyle,
                Language = Language,
                LogsInIntuneFolder = LogsInIntuneFolder,
                Author = Author,
                DeployMode = DeployMode,
                AllowDefer = AllowDefer,
                DeferTimes = DeferTimes,
                CloseAppsCountdown = CloseAppsCountdown,
                BlockExecution = BlockExecution,
                CheckDiskSpace = CheckDiskSpace,
                RemovePreviousVersions = RemovePreviousVersions,
                WelcomeMessage = WelcomeMessage,
                CompletionMessage = CompletionMessage,
                OutputRoot = OutputFolder,
            };
        }
    }

    /// <summary>Resultado da analise de um instalador.</summary>
    public class InstallerInfo
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public double SizeMB { get; set; }
        public string Type { get; set; }          // MSI | EXE
        public string Technology { get; set; }
        public string AppName { get; set; }
        public string AppVendor { get; set; }
        public string AppVersion { get; set; }
        public string AppArch { get; set; }
        public string ProductCode { get; set; }
        public string UpgradeCode { get; set; }
        public string MsiProductName { get; set; }
        public string InstallArgs { get; set; } = "";
        public string UninstallExeArgs { get; set; } = "";
        public string UninstallPattern { get; set; } = "";
        public List<string> Processes { get; set; } = new List<string>();
        public string DetectionMethod { get; set; } = "Registry";
        public string DetectionFilePath { get; set; } = "";
        public string DetectionVersion { get; set; } = "";
        public bool DetectionCheckVersion { get; set; } = true;
        public bool? RemovePrevious { get; set; }
        public string InstallBehavior { get; set; } = "System";
        public string Signer { get; set; }
        public string Sha256 { get; set; }
        public string CatalogId { get; set; }
        public string CatalogMatch { get; set; }   // "hash" | "productcode" | "nome"
        public string Description { get; set; }
        public string Homepage { get; set; }
        public List<string> Notes { get; set; } = new List<string>();
    }

    public class BuildResult
    {
        public string PackageFolder { get; set; }
        public string PsadtFolder { get; set; }
        public string IntuneFolder { get; set; }
        public string IntuneWinFile { get; set; }
        public string IntuneWinTool { get; set; }
        public string DetectionScript { get; set; }
        public string AppIcon { get; set; }
        public string InstallCommand { get; set; }
        public string UninstallCommand { get; set; }
        public string PsadtVersion { get; set; }
        public string SummaryHtml { get; set; }
        public string SummaryTxt { get; set; }
        public string ProjectFile { get; set; }
        public long IntuneWinSize { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class ValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    // ------------------------------------------------------------------ Catalogo (banco de dados)

    public class CatalogFile
    {
        [JsonPropertyName("schema")] public int Schema { get; set; }
        [JsonPropertyName("generated")] public DateTime Generated { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; }
        [JsonPropertyName("commit")] public string Commit { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("apps")] public List<CatalogApp> Apps { get; set; } = new List<CatalogApp>();
    }

    public class CatalogApp
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("n")] public string Name { get; set; }
        [JsonPropertyName("p")] public string Publisher { get; set; }
        [JsonPropertyName("v")] public string Version { get; set; }
        [JsonPropertyName("d")] public string Description { get; set; }
        [JsonPropertyName("hp")] public string Homepage { get; set; }
        [JsonPropertyName("lic")] public string License { get; set; }
        [JsonPropertyName("m")] public string Moniker { get; set; }
        [JsonPropertyName("tg")] public List<string> Tags { get; set; }
        [JsonPropertyName("i")] public List<CatalogInstaller> Installers { get; set; } = new List<CatalogInstaller>();
        [JsonPropertyName("x")] public CuratedEntry Extra { get; set; }

        [JsonIgnore] public string SearchText { get; set; }
        [JsonIgnore] public string NameLower { get; set; }
        [JsonIgnore] public string Initials
        {
            get
            {
                var parts = (Name ?? "?").Split(new[] { ' ', '-', '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "?";
                if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
                return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
            }
        }
        [JsonIgnore] public string Subtitle => string.IsNullOrEmpty(Publisher) ? Id : $"{Publisher}  ·  {Id}";
        [JsonIgnore] public string TypesLabel => string.Join("  ", (Installers ?? new List<CatalogInstaller>()).Select(i => i.TypeLabel).Distinct().Take(3));
        [JsonIgnore] public bool HasCuration => Extra != null;
        [JsonIgnore] public bool IsSupported => Installers != null && Installers.Any(i => i.IsSupported);
        [JsonIgnore] public string AvatarColor
        {
            get
            {
                string[] palette = { "#0A5CA8", "#0E7490", "#6D28D9", "#B45309", "#BE185D", "#15803D", "#1E3A8A", "#9333EA" };
                int h = 0;
                foreach (char c in Id ?? "") h = (h * 31 + c) & 0x7FFFFFFF;
                return palette[h % palette.Length];
            }
        }
        [JsonIgnore] public string VersionLabel => string.IsNullOrEmpty(Version) ? "" : "v" + Version;
    }

    public class CatalogInstaller
    {
        [JsonPropertyName("a")] public string Arch { get; set; }
        [JsonPropertyName("t")] public string Type { get; set; }
        [JsonPropertyName("nt")] public string NestedType { get; set; }
        [JsonPropertyName("s")] public string Scope { get; set; }
        [JsonPropertyName("loc")] public string Locale { get; set; }
        [JsonPropertyName("u")] public string Url { get; set; }
        [JsonPropertyName("h")] public string Sha256 { get; set; }
        [JsonPropertyName("pc")] public string ProductCode { get; set; }
        [JsonPropertyName("sw")] public string Silent { get; set; }
        [JsonPropertyName("swp")] public string SilentWithProgress { get; set; }
        [JsonPropertyName("cu")] public string Custom { get; set; }
        [JsonPropertyName("el")] public string Elevation { get; set; }
        [JsonPropertyName("sc")] public List<long> SuccessCodes { get; set; }
        [JsonPropertyName("arp")] public List<ArpEntry> Arp { get; set; }

        [JsonIgnore] public bool IsMsi => Type == "msi" || Type == "wix";
        [JsonIgnore] public bool IsSupported => Type is "exe" or "msi" or "wix" or "inno" or "nullsoft" or "burn";
        [JsonIgnore] public string TypeLabel => Type switch
        {
            "msi" => "MSI",
            "wix" => "MSI (WiX)",
            "inno" => "Inno Setup",
            "nullsoft" => "NSIS",
            "burn" => "WiX Burn",
            "exe" => "EXE",
            "msix" => "MSIX",
            "appx" => "APPX",
            "zip" => "ZIP",
            "portable" => "Portátil",
            _ => (Type ?? "?").ToUpperInvariant()
        };
        [JsonIgnore] public string ArchLabel => string.IsNullOrEmpty(Arch) ? "—" : Arch;
        [JsonIgnore] public string ScopeLabel => Scope switch { "machine" => "Computador", "user" => "Usuário", _ => "Ambos" };
        [JsonIgnore] public string LocaleLabel => string.IsNullOrEmpty(Locale) ? "Neutro" : Locale;
        [JsonIgnore] public string SilentLabel
        {
            get
            {
                var s = CatalogService.SilentArgsFor(this);
                if (IsMsi) return string.IsNullOrEmpty(s) ? "msiexec /qn (automático)" : "/qn + " + s;
                return string.IsNullOrEmpty(s) ? "(sem parâmetro conhecido)" : s;
            }
        }
        [JsonIgnore] public string SupportLabel => IsSupported ? "" : "Não suportado";
    }

    public class ArpEntry
    {
        [JsonPropertyName("dn")] public string DisplayName { get; set; }
        [JsonPropertyName("dv")] public string DisplayVersion { get; set; }
        [JsonPropertyName("pub")] public string Publisher { get; set; }
        [JsonPropertyName("pc")] public string ProductCode { get; set; }
        [JsonPropertyName("uc")] public string UpgradeCode { get; set; }
    }

    public class CuratedFile
    {
        [JsonPropertyName("apps")] public Dictionary<string, CuratedEntry> Apps { get; set; }
    }

    public class CuratedEntry
    {
        [JsonPropertyName("processes")] public List<string> Processes { get; set; }
        [JsonPropertyName("namePattern")] public string NamePattern { get; set; }
        [JsonPropertyName("uninstallArgs")] public string UninstallArgs { get; set; }
        [JsonPropertyName("installArgs")] public string InstallArgs { get; set; }
        [JsonPropertyName("detectionFile")] public string DetectionFile { get; set; }
        [JsonPropertyName("checkVersion")] public bool? CheckVersion { get; set; }
        [JsonPropertyName("removePrevious")] public bool? RemovePrevious { get; set; }
        [JsonPropertyName("notes")] public string Notes { get; set; }
    }

    public class InstalledProgram
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Publisher { get; set; }
        public string Type { get; set; }
        public bool QuietUninstall { get; set; }
        public string Scope { get; set; }
        public string KeyName { get; set; }
        public string SilentLabel => Type == "MSI" || QuietUninstall ? "Sim" : "Precisa de parâmetro";
    }

    public class PackageHistoryItem
    {
        public string Folder { get; set; }
        public string ProjectFile { get; set; }
        public string AppName { get; set; }
        public string AppVersion { get; set; }
        public string Vendor { get; set; }
        public DateTime Date { get; set; }
        public string IntuneWin { get; set; }
        public string SummaryHtml { get; set; }
        public string Title => $"{AppName} {AppVersion}".Trim();
        public string DateLabel => Date.ToString("dd/MM/yyyy HH:mm");
        public string Initials { get { var c = new string((AppName ?? "").Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant(); return c.Length == 0 ? "?" : c; } }
    }
}
