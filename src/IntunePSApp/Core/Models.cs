using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>Product identity. Forks: change these constants (see docs/FORKING.md).</summary>
    public static class AppInfo
    {
        public const string Name = "J365 IntunePSApp";
        public const string Author = "Sérgio Sant'Ana Junior";
        public const string AuthorLinkedIn = "https://www.linkedin.com/in/sergiosantanacloud/";
        public const string Brand = "Jornada 365";
        /// <summary>
        /// Who may sign the releases of this project (docs/SIGNING.md): the SignPath Foundation certificate (free signing
        /// for open-source projects), or a certificate in the name of the author or of Jornada 365 (Azure Artifact Signing,
        /// a Certum "Open Source Developer" certificate...). An update signed by any of them is accepted.
        /// </summary>
        public static readonly string[] TrustedPublishers = { "SignPath Foundation", Author, Brand };
        public const string BlogUrl = "https://jornada365.cloud";
        public const string GitHubRepo = "sesantanajr/J365-IntunePSApp";
        public const string PsadtName = "PSAppDeployToolkit";
        public const string PsadtUrl = "https://psappdeploytoolkit.com";

        public static string RepoUrl => "https://github.com/" + GitHubRepo;
        public static string ReleasesUrl => RepoUrl + "/releases/latest";
        public static string LatestReleaseApi => "https://api.github.com/repos/" + GitHubRepo + "/releases/latest";
        public static string DefaultCatalogUrl => RepoUrl + "/releases/download/catalog/catalog.json.gz";
        public static string PublishDocsUrl => RepoUrl + "/blob/main/docs/INTUNE-PUBLISH.md";

        public static string Version
        {
            get
            {
                var v = typeof(AppInfo).Assembly.GetName().Version;
                return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }
    }

    /// <summary>Every option of a package (persisted as project.json).</summary>
    public class PackageSettings
    {
        public const int CurrentSchema = 2;
        public int SchemaVersion { get; set; }

        // Installer
        public string InstallerPath { get; set; } = "";
        public bool IncludeInstallerFolder { get; set; }
        public string InstallerType { get; set; } = "";       // MSI | EXE
        public string CatalogId { get; set; } = "";
        public string CatalogVersion { get; set; } = "";     // winget version of the catalog entry the installer came from
        // Application
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
        public string PrivacyUrl { get; set; } = "";
        public string Developer { get; set; } = "";
        public string Owner { get; set; } = "";
        public string MinimumOs { get; set; } = "W10_1607";
        /// <summary>Icon shown in Intune and in the Company Portal. Empty: the icon of the installer, or a tile with the initials.</summary>
        public string AppIconPath { get; set; } = "";
        // User experience
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
        // Uninstall / previous versions
        public bool RemovePreviousVersions { get; set; } = true;
        public string UninstallPattern { get; set; } = "";
        public string UninstallExeArgs { get; set; } = "";
        // Actions and extra files
        public List<PackageAction> Actions { get; set; } = new List<PackageAction>();
        public List<string> ExtraFiles { get; set; } = new List<string>();
        // Branding (PSADT dialogs)
        public string CompanyName { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public string AccentColor { get; set; } = "";
        public string DialogStyle { get; set; } = "Fluent";
        public string Language { get; set; } = "Auto";
        public bool LogsInIntuneFolder { get; set; } = true;
        public string Author { get; set; } = "";
        // Detection
        public string DetectionMethod { get; set; } = "Registry"; // Registry | ProductCode | File
        public string DetectionNamePattern { get; set; } = "";
        public string DetectionVersion { get; set; } = "";
        public bool DetectionCheckVersion { get; set; } = true;
        public string DetectionFilePath { get; set; } = "";
        /// <summary>
        /// The package writes a registry mark when it finishes and the detection requires it: a copy of the application
        /// installed some other way (by hand, by another tool) is not "installed" for Intune, so the package runs there,
        /// removes that copy and installs this version.
        /// </summary>
        public bool DetectionRequireMark { get; set; } = true;
        /// <summary>A newer version already installed is kept (the package does not downgrade it; it only writes its mark).</summary>
        public bool KeepNewerVersion { get; set; } = true;
        // Output
        public string OutputRoot { get; set; } = "";

        public PackageSettings Clone()
        {
            var json = JsonSerializer.Serialize(this, Json.Options);
            return JsonSerializer.Deserialize<PackageSettings>(json, Json.Options);
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

    /// <summary>Application preferences and company defaults (portable: stored next to the executable).</summary>
    public class AppConfig
    {
        // Application
        public string UiLanguage { get; set; } = "auto";      // auto | en | pt-BR
        public string AppTheme { get; set; } = "System";      // System | Light | Dark
        public bool SetupCompleted { get; set; }
        public bool CheckForUpdates { get; set; } = true;
        public DateTime? LastUpdateCheck { get; set; }
        public string LatestAppVersion { get; set; } = "";
        public string LatestPsadtVersion { get; set; } = "";
        // Main window (size of the last session, in device-independent pixels)
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
        // Company branding
        public string CompanyName { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public string AccentColor { get; set; } = "#0A5CA8";
        public string DialogStyle { get; set; } = "Fluent";
        public string Language { get; set; } = "Auto";
        public bool LogsInIntuneFolder { get; set; } = true;
        public string Author { get; set; } = "";
        // Experience defaults
        public string DeployMode { get; set; } = "Auto";
        public bool AllowDefer { get; set; } = true;
        public int DeferTimes { get; set; } = 3;
        public int CloseAppsCountdown { get; set; } = 300;
        public bool BlockExecution { get; set; } = true;
        public bool CheckDiskSpace { get; set; } = true;
        public bool RemovePreviousVersions { get; set; } = true;
        public string WelcomeMessage { get; set; } = "";
        public string CompletionMessage { get; set; } = "";
        // Folders, catalog and Intune
        public string OutputFolder { get; set; } = "";
        public string CatalogUrl { get; set; } = "";
        public int CatalogUpdateDays { get; set; } = 1;
        public DateTime? LastCatalogCheck { get; set; }
        public string PreferredArch { get; set; } = "x64";
        public string PreferredLocale { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.Name is { Length: > 0 } culture ? culture : "en-US";
        public string IntuneTenantId { get; set; } = "";
        public string IntuneClientId { get; set; } = "";
        /// <summary>
        /// How this computer connects to Intune: "delegated" (a user signs in) or "certificate" (the app registration
        /// itself, app-only, with a certificate of this computer).
        /// </summary>
        public string IntuneAuthMode { get; set; } = "delegated";
        /// <summary>Thumbprint of the certificate of the certificate mode. Its key stays in the TPM or in the Windows key store.</summary>
        public string IntuneCertificateThumbprint { get; set; } = "";
        /// <summary>The tenant as people read it (the domain of the administrator who set the connection up).</summary>
        public string IntuneTenantName { get; set; } = "";
        /// <summary>
        /// What the tenant has of Microsoft Intune (IntuneSubscription): "active", "basic" (Basic Mobility and Security
        /// only), "inactive" (expired or suspended), "none"; empty when unknown.
        /// </summary>
        public string IntunePlan { get; set; } = "";
        /// <summary>The tenant <see cref="IntunePlan"/> belongs to, as the connection names it.</summary>
        public string IntunePlanTenant { get; set; } = "";
        public DateTime? IntunePlanUtc { get; set; }
        /// <summary>The plans of Intune found by that reading (what the answer rests on), or null.</summary>
        public List<IntunePlanFound> IntunePlans { get; set; }
        /// <summary>How long a new certificate of the connection is valid, in months (6, 12 or 24).</summary>
        public int CertificateMonths { get; set; } = AppCertificate.DefaultMonths;
        /// <summary>Opens the publication to Intune as soon as a package is generated.</summary>
        public bool PublishAfterBuild { get; set; }

        public PackageSettings NewPackage()
        {
            return new PackageSettings
            {
                SchemaVersion = PackageSettings.CurrentSchema,
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

    /// <summary>Result of analysing an installer file.</summary>
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
        /// <summary>Extra exit codes that mean success, declared by the database for this installer ("3010, 1641").</summary>
        public string SuccessCodes { get; set; } = "";
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
        public string CatalogVersion { get; set; }
        public string CatalogMatch { get; set; }   // hash | productcode | download | name
        public string Description { get; set; }
        public string Homepage { get; set; }
        public string PrivacyUrl { get; set; }
        public List<string> Notes { get; set; } = new List<string>();

        [JsonIgnore] public bool IsCatalogExact => CatalogMatch is "hash" or "productcode" or "download";
    }

    public class BuildResult
    {
        public string PackageFolder { get; set; }
        public string PsadtFolder { get; set; }
        public string IntuneFolder { get; set; }
        public string IntuneWinFile { get; set; }
        public string IntuneWinTool { get; set; }
        public long IntuneWinSize { get; set; }
        public string Sha256 { get; set; }
        public string DetectionScript { get; set; }
        public string AppIcon { get; set; }
        public string InstallCommand { get; set; }
        public string UninstallCommand { get; set; }
        public string PsadtVersion { get; set; }
        public string SummaryHtml { get; set; }
        public string SummaryTxt { get; set; }
        public string MetadataFile { get; set; }
        public string PublishScript { get; set; }
        public string ProjectFile { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class ValidationResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    // ------------------------------------------------------------------ Catalog (application database)

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
        [JsonPropertyName("dpt")] public string DescriptionPt { get; set; }
        [JsonPropertyName("hp")] public string Homepage { get; set; }
        [JsonPropertyName("pu")] public string PrivacyUrl { get; set; }
        [JsonPropertyName("lic")] public string License { get; set; }
        [JsonPropertyName("m")] public string Moniker { get; set; }
        [JsonPropertyName("tg")] public List<string> Tags { get; set; }
        [JsonPropertyName("dep")] public List<string> Dependencies { get; set; }
        [JsonPropertyName("i")] public List<CatalogInstaller> Installers { get; set; } = new List<CatalogInstaller>();
        [JsonPropertyName("x")] public CuratedEntry Extra { get; set; }

        [JsonIgnore] public string SearchText { get; set; }
        [JsonIgnore] public string NameLower { get; set; }
        /// <summary>Words of the name, lower case (search ranks a whole word above a piece of a longer one).</summary>
        [JsonIgnore] public HashSet<string> NameWords { get; set; }
        [JsonIgnore] public string LocalizedDescription => Loc.Instance.IsPortuguese && !string.IsNullOrEmpty(DescriptionPt) ? DescriptionPt : Description;
        [JsonIgnore] public string Initials => Names.Initials(Name);
        [JsonIgnore] public string Subtitle => string.IsNullOrEmpty(Publisher) ? Id : $"{Publisher}  ·  {Id}";
        [JsonIgnore] public string TypesLabel => string.Join("  ", (Installers ?? new List<CatalogInstaller>()).Select(i => i.TypeLabel).Distinct().Take(3));
        [JsonIgnore] public bool HasCuration => Extra != null;
        [JsonIgnore] public bool IsSupported => Installers != null && Installers.Any(i => i.IsSupported);
        [JsonIgnore] public string VersionLabel => string.IsNullOrEmpty(Version) ? "" : "v" + Version;
        [JsonIgnore] public string AvatarColor => Names.AvatarColor(Id);
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
        [JsonPropertyName("dep")] public List<string> Dependencies { get; set; }
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
            "portable" => T("Type_Portable"),
            _ => (Type ?? "?").ToUpperInvariant()
        };
        [JsonIgnore] public string ArchLabel => string.IsNullOrEmpty(Arch) ? "—" : Arch;
        [JsonIgnore] public string ScopeLabel => Scope switch { "machine" => T("Scope_Machine"), "user" => T("Scope_User"), _ => T("Scope_Both") };
        [JsonIgnore] public string LocaleLabel => string.IsNullOrEmpty(Locale) ? T("Locale_Neutral") : Locale;
        [JsonIgnore] public string SilentLabel
        {
            get
            {
                var s = CatalogService.SilentArgsFor(this);
                if (IsMsi) return string.IsNullOrEmpty(s) ? T("Silent_MsiAuto") : "/qn + " + s;
                return string.IsNullOrEmpty(s) ? T("Silent_Unknown") : s;
            }
        }
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

    /// <summary>Curated knowledge the winget manifests do not carry (catalog/curated.json).</summary>
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
        [JsonPropertyName("notesEn")] public string NotesEn { get; set; }

        [JsonIgnore]
        public string LocalizedNotes
        {
            get
            {
                if (Loc.Instance.IsPortuguese) return string.IsNullOrEmpty(Notes) ? NotesEn : Notes;
                return string.IsNullOrEmpty(NotesEn) ? Notes : NotesEn;
            }
        }
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
        /// <summary>Icon shown in "Installed apps" ("C:\Program Files\7-Zip\7zFM.exe",0).</summary>
        public string DisplayIcon { get; set; } = "";
        public string SilentLabel => Type == "MSI" || QuietUninstall ? T("Common_Yes") : T("Installed_NeedsParameter");
    }

    public class PackageHistoryItem
    {
        public string Folder { get; set; }
        public string ProjectFile { get; set; }
        public string AppName { get; set; }
        public string AppVersion { get; set; }
        public string Vendor { get; set; }
        public string CatalogId { get; set; }
        public DateTime Date { get; set; }
        public string IntuneWin { get; set; }
        public string SummaryHtml { get; set; }
        public string LatestVersion { get; set; }
        /// <summary>Intune\AppIcon.png of the package, when it has one.</summary>
        public string Icon { get; set; }
        public bool HasIcon => !string.IsNullOrEmpty(Icon);
        public bool NoIcon => !HasIcon;

        public string Title => $"{AppName} {AppVersion}".Trim();
        public string DateLabel => Date.ToString("g", Loc.Instance.Culture);
        public string Initials => Names.Initials(AppName);
        public bool HasUpdate => !string.IsNullOrEmpty(LatestVersion);
        public string UpdateLabel => HasUpdate ? T("Packages_UpdateTo", LatestVersion) : "";
    }

    /// <summary>A package dependency declared by the winget manifest of an application.</summary>
    public class DependencyInfo
    {
        public string Id { get; set; }
        public CatalogApp App { get; set; }
        public bool Included { get; set; }

        public string Title => App?.Name ?? Id;
        public string Subtitle => App == null ? T("Deps_NotInCatalog") : $"{App.Publisher}  ·  v{App.Version}  ·  {Id}";
        public bool CanInclude => App != null && App.IsSupported && !Included;
        public string StatusLabel => Included ? T("Deps_Included") : App == null || !App.IsSupported ? T("Deps_Manual") : "";
    }
}
