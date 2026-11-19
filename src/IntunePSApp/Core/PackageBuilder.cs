using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Names of the files and folders of a generated package. Version 1.x used Portuguese names;
    /// the Find* helpers keep those packages readable.
    /// </summary>
    public static class PackageLayout
    {
        public const string PackageDir = "Package";
        public const string IntuneDir = "Intune";
        public const string SandboxDir = "Sandbox";
        public const string ProjectFile = "project.json";
        public const string BuildLog = "build.log";
        public const string MetadataFile = "intune-app.json";
        public const string PublishScript = "Publish-ToIntune.ps1";
        public const string SandboxScript = "Sandbox-Test.ps1";
        public const string InstructionsHtml = "Intune-Instructions.html";
        public const string InstructionsTxt = "Intune-Instructions.txt";
        public const string AppIcon = "AppIcon.png";
        public const string DetectionPrefix = "Detection_";

        private const string LegacyPackageDir = "Pacote_PSADT";
        private const string LegacyProjectFile = "projeto.json";
        private const string LegacyInstructionsHtml = "INSTRUCOES_INTUNE.html";
        private const string LegacyAppIcon = "Icone_App.png";
        private const string LegacyDetectionPrefix = "Deteccao_";

        private static string FirstFile(string dir, params string[] names)
            => names.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);

        public static string FindProject(string folder) => FirstFile(folder, ProjectFile, LegacyProjectFile);
        public static string FindInstructions(string folder) => FirstFile(Path.Combine(folder, IntuneDir), InstructionsHtml, LegacyInstructionsHtml);
        public static string FindIcon(string folder) => FirstFile(Path.Combine(folder, IntuneDir), AppIcon, LegacyAppIcon);

        public static string FindPackageDir(string folder)
            => new[] { PackageDir, LegacyPackageDir }.Select(n => Path.Combine(folder, n)).FirstOrDefault(Directory.Exists);

        public static string FindIntuneWin(string folder)
        {
            string dir = Path.Combine(folder, IntuneDir);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.intunewin").FirstOrDefault() : null;
        }

        public static string FindDetection(string folder)
        {
            string dir = Path.Combine(folder, IntuneDir);
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir, "*.ps1").FirstOrDefault(f =>
                Path.GetFileName(f).StartsWith(DetectionPrefix, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(f).StartsWith(LegacyDetectionPrefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Machine-readable description of the Intune app (intune-app.json), consumed by Publish-ToIntune.ps1 and Sandbox-Test.ps1.</summary>
    public class IntuneAppMetadata
    {
        public int Schema { get; set; } = 1;
        public string Generator { get; set; }
        public string GeneratedUtc { get; set; }
        public string PsadtVersion { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Publisher { get; set; }
        public string AppVersion { get; set; }
        public string InformationUrl { get; set; }
        public string Notes { get; set; }
        public string IntuneWinFile { get; set; }
        public string IntuneWinSha256 { get; set; }
        public string DetectionScript { get; set; }
        public string Icon { get; set; }
        public string InstallCommandLine { get; set; }
        public string UninstallCommandLine { get; set; }
        public string InstallExperience { get; set; }
        public string RestartBehavior { get; set; } = "basedOnReturnCode";
        public int MaximumInstallationTimeInMinutes { get; set; } = 60;
        public bool AllowAvailableUninstall { get; set; } = true;
        public string Architecture { get; set; }
        public string MinimumSupportedWindowsRelease { get; set; }
    }

    /// <summary>Orchestrates a build: PSADT template + installer + actions + branding + detection + .intunewin + instructions.</summary>
    public static class PackageBuilder
    {
        public static readonly string[] MinimumOsOptions = { "W10_1607", "W10_1809", "W10_1909", "W10_2004", "W10_21H2", "W10_22H2", "W11_21H2", "W11_22H2" };

        private static readonly JsonSerializerOptions MetadataJson = new JsonSerializerOptions(Json.Options) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>"W10_22H2" -> "Windows 10 22H2"</summary>
        public static string MinimumOsLabel(string code)
        {
            var m = Regex.Match(code ?? "", @"^W(\d+)_(\w+)$");
            return m.Success ? $"Windows {m.Groups[1].Value} {m.Groups[2].Value}" : code ?? "";
        }

        /// <summary>Architecture value understood by the Intune requirement rule (x86 installers also run on x64).</summary>
        public static string IntuneArchitecture(PackageSettings s) => s.AppArch switch { "x64" => "x64", "arm64" => "arm64", _ => "x64x86" };

        // ------------------------------------------------------------------ projects

        public static PackageSettings LoadProject(string file)
        {
            var s = JsonSerializer.Deserialize<PackageSettings>(File.ReadAllText(file, Encoding.UTF8), Json.Options)
                    ?? throw new InvalidDataException(T("Err_ProjectInvalid"));
            Migrate(s);
            return s;
        }

        /// <summary>Brings a project saved by an older version up to the current schema (idempotent).</summary>
        public static void Migrate(PackageSettings s)
        {
            s.Actions ??= new List<PackageAction>();
            s.ExtraFiles ??= new List<string>();
            s.ProcessesToClose ??= new List<string>();
            foreach (var a in s.Actions) ActionCatalog.Normalize(a);
            if (string.IsNullOrEmpty(s.MinimumOs)) s.MinimumOs = MinimumOsOptions[0];
            if (string.IsNullOrEmpty(s.InstallerType) && !string.IsNullOrEmpty(s.InstallerPath))
                s.InstallerType = Path.GetExtension(s.InstallerPath).TrimStart('.').ToUpperInvariant();
            s.SchemaVersion = PackageSettings.CurrentSchema;
        }

        // ------------------------------------------------------------------ validation

        public static ValidationResult Validate(PackageSettings s)
        {
            var v = new ValidationResult();
            bool hasInstaller = !string.IsNullOrWhiteSpace(s.InstallerPath) && File.Exists(s.InstallerPath);
            if (!hasInstaller) v.Errors.Add(T("Val_Installer"));
            else if (!Regex.IsMatch(Path.GetExtension(s.InstallerPath), @"^\.(msi|exe)$", RegexOptions.IgnoreCase)) v.Errors.Add(T("Val_InstallerType"));
            else if (s.IncludeInstallerFolder && IsSharedFolder(Path.GetDirectoryName(Path.GetFullPath(s.InstallerPath))))
                v.Errors.Add(T("Val_InstallerFolderUnsafe", Path.GetDirectoryName(Path.GetFullPath(s.InstallerPath))));
            if (string.IsNullOrWhiteSpace(s.AppName)) v.Errors.Add(T("Val_AppName"));
            if (string.IsNullOrWhiteSpace(s.AppVersion)) v.Errors.Add(T("Val_AppVersion"));

            string pat = s.UninstallPattern ?? "";
            if (string.IsNullOrWhiteSpace(pat)) v.Errors.Add(T("Val_UninstallPattern"));
            else if (Regex.Replace(pat, @"[\*\?\s]", "").Length < 3) v.Errors.Add(T("Val_PatternTooGeneric", pat));

            if (s.InstallerType == "EXE" && string.IsNullOrWhiteSpace(s.InstallArgs)) v.Warnings.Add(T("Val_Warn_NoSilentArgs"));
            if (s.InstallerType == "EXE" && string.IsNullOrWhiteSpace(s.UninstallExeArgs)) v.Warnings.Add(T("Val_Warn_NoUninstallArgs"));
            foreach (var p in s.ProcessesToClose) if (!Regex.IsMatch(p, @"^[\w\.\-\+ ]+$")) v.Errors.Add(T("Val_ProcessName", p));
            if (s.AllowDefer && (s.DeferTimes < 1 || s.DeferTimes > 99)) v.Errors.Add(T("Val_DeferTimes"));
            if (s.CloseAppsCountdown < 0 || s.CloseAppsCountdown > 3000) v.Errors.Add(T("Val_CloseCountdown"));
            if (s.RestartPrompt && (s.RestartCountdown < 60 || s.RestartCountdown > 86400)) v.Errors.Add(T("Val_RestartCountdown"));
            if (!string.IsNullOrWhiteSpace(s.LogoPath) && !File.Exists(s.LogoPath)) v.Errors.Add(T("Val_LogoMissing", s.LogoPath));
            if (!string.IsNullOrWhiteSpace(s.AccentColor) && !Regex.IsMatch(s.AccentColor.Trim(), "^#?[0-9A-Fa-f]{6}$")) v.Errors.Add(T("Val_AccentColor"));
            if (!string.IsNullOrWhiteSpace(s.ExtraSuccessCodes) && !Regex.IsMatch(s.ExtraSuccessCodes, @"^[\d,;\s\-]+$")) v.Errors.Add(T("Val_SuccessCodes"));

            var extraNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in s.ExtraFiles)
            {
                if (!File.Exists(f) && !Directory.Exists(f)) { v.Errors.Add(T("Val_ExtraMissing", f)); continue; }
                string name = Path.GetFileName(f.TrimEnd('\\'));
                if (!extraNames.Add(name)) v.Errors.Add(T("Val_ExtraDuplicate", name));
            }
            foreach (var a in s.Actions)
            {
                v.Errors.AddRange(ActionCatalog.Validate(a));
                foreach (var reference in PackageFileReferences(a))
                {
                    string first = reference.Replace('/', '\\').TrimStart('.', '\\').Split('\\')[0];
                    if (first.Length == 0 || first.IndexOfAny(new[] { '*', '?' }) >= 0) continue;
                    if (!extraNames.Contains(first)) v.Errors.Add(T("Val_ActionFileMissing", a.TypeLabel, first));
                }
            }

            switch (s.DetectionMethod)
            {
                case "Registry":
                    if (Regex.Replace(s.DetectionNamePattern ?? "", @"[\*\?\s]", "").Length < 2) v.Errors.Add(T("Val_DetName"));
                    break;
                case "ProductCode":
                    if (string.IsNullOrWhiteSpace(s.MsiProductCode)) v.Errors.Add(T("Val_DetCode"));
                    break;
                case "File":
                    if (string.IsNullOrWhiteSpace(s.DetectionFilePath)) v.Errors.Add(T("Val_DetFile"));
                    else if (s.InstallBehavior != "User" && Regex.IsMatch(s.DetectionFilePath, "%(LOCALAPPDATA|APPDATA|USERPROFILE|TEMP|TMP|HOMEPATH)%", RegexOptions.IgnoreCase))
                        v.Warnings.Add(T("Val_Warn_DetUserPath"));
                    break;
                default: v.Errors.Add(T("Val_DetUnknown")); break;
            }
            if (s.DetectionCheckVersion && !Regex.IsMatch(s.DetectionVersion ?? "", @"\d")) v.Errors.Add(T("Val_DetVersion"));
            return v;
        }

        /// <summary>Names of package files (SupportFiles) an action depends on.</summary>
        private static IEnumerable<string> PackageFileReferences(PackageAction a)
        {
            switch (a.Type)
            {
                case "InstallDependency":
                case "CopyFile":
                case "CopyToUserProfiles":
                case "ImportRegFile":
                    if (!string.IsNullOrWhiteSpace(a.P("Source"))) yield return a.P("Source").Trim();
                    break;
                case "RunProgram":
                    if (a.P("Source") == "Package" && !string.IsNullOrWhiteSpace(a.P("FilePath"))) yield return a.P("FilePath").Trim();
                    break;
            }
        }

        /// <summary>
        /// True for folders that hold unrelated files (Downloads, Desktop, a drive root...). "Include every file of the
        /// installer folder" must never be used there: the whole folder would end up inside the package.
        /// </summary>
        public static bool IsSharedFolder(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return true;
            string d = Path.GetFullPath(dir).TrimEnd('\\');
            if (d.Length == 2 && d[1] == ':') return true; // drive root
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var shared = new[]
            {
                profile,
                Path.Combine(profile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.GetTempPath(),
                AppPaths.Downloads,
                AppPaths.Data,
            };
            return shared.Any(x => !string.IsNullOrEmpty(x) && string.Equals(Path.GetFullPath(x).TrimEnd('\\'), d, StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ build

        public static async Task<BuildResult> BuildAsync(PackageSettings input, bool forceNative, CancellationToken ct)
        {
            var s = input.Clone();
            Migrate(s);
            if (string.IsNullOrWhiteSpace(s.DetectionNamePattern)) s.DetectionNamePattern = s.UninstallPattern;
            var check = Validate(s);
            foreach (var w in check.Warnings) Log.Warn(w);
            if (!check.IsValid) throw new InvalidOperationException(T("Err_FixBeforeBuild") + "\n• " + string.Join("\n• ", check.Errors));

            var sw = Stopwatch.StartNew();
            Log.Step(T("Log_BuildStart", s.AppName, s.AppVersion));
            Log.Step(T("Log_CheckingPsadt"));
            string template = await ToolsService.EnsurePsadtAsync(ct);
            string psadtVersion = Path.GetFileName(template);
            Log.Ok($"{AppInfo.PsadtName} {psadtVersion}");

            string outRoot = string.IsNullOrWhiteSpace(s.OutputRoot) ? ConfigStore.Current.OutputFolder : s.OutputRoot;
            Directory.CreateDirectory(outRoot);
            string name = Names.Safe($"{s.AppName}_{s.AppVersion}");
            string pkgRoot = Path.Combine(outRoot, name);
            if (Directory.Exists(pkgRoot)) pkgRoot = Path.Combine(outRoot, $"{name}_{DateTime.Now:yyyyMMdd-HHmmss}");
            string srcDir = Path.Combine(pkgRoot, PackageLayout.PackageDir);
            string intuneDir = Path.Combine(pkgRoot, PackageLayout.IntuneDir);
            Directory.CreateDirectory(srcDir);
            Directory.CreateDirectory(intuneDir);
            Log.PackageLogFile = Path.Combine(pkgRoot, PackageLayout.BuildLog);
            try
            {
                Log.Info(T("Log_PackageFolder", pkgRoot));

                Log.Step(T("Log_CopyingTemplate"));
                await Task.Run(() => CopyDirectory(template, srcDir), ct);
                foreach (var ph in new[] { @"Files\Add Setup Files Here.txt", @"SupportFiles\Add Supporting Files Here.txt" })
                {
                    var p = Path.Combine(srcDir, ph);
                    if (File.Exists(p)) File.Delete(p);
                }
                string filesDir = Path.Combine(srcDir, "Files");
                string supportDir = Path.Combine(srcDir, "SupportFiles");
                Directory.CreateDirectory(filesDir);
                Directory.CreateDirectory(supportDir);

                Log.Step(T("Log_CopyingInstaller"));
                var inst = new FileInfo(s.InstallerPath);
                if (s.IncludeInstallerFolder) await Task.Run(() => CopyInstallerFolder(inst, filesDir, outRoot, ct), ct);
                else File.Copy(inst.FullName, Path.Combine(filesDir, inst.Name), true);

                if (s.ExtraFiles.Count > 0)
                {
                    Log.Step(T("Log_CopyingExtras", s.ExtraFiles.Count));
                    await Task.Run(() =>
                    {
                        foreach (var f in s.ExtraFiles)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (File.Exists(f)) File.Copy(f, Path.Combine(supportDir, Path.GetFileName(f)), true);
                            else if (Directory.Exists(f)) CopyDirectory(f, Path.Combine(supportDir, Path.GetFileName(f.TrimEnd('\\'))));
                        }
                    }, ct);
                }

                Log.Step(T("Log_GeneratingScript"));
                var (tpl, _) = TextFiles.Read(Path.Combine(template, "Invoke-AppDeployToolkit.ps1"));
                string scriptPath = Path.Combine(srcDir, "Invoke-AppDeployToolkit.ps1");
                TextFiles.Write(scriptPath, ScriptGenerator.DeployScript(tpl, s, inst.Name), true);
                var errors = await Task.Run(() => PowerShellValidator.Validate(scriptPath), ct);
                if (errors.Length > 0) throw new InvalidDataException(T("Err_ScriptSyntax") + "\n" + string.Join("\n", errors.Take(5)));
                Log.Ok(T("Log_ScriptOk", s.Actions.Count));

                Log.Step(T("Log_ApplyingBranding"));
                ScriptGenerator.UpdateConfig(Path.Combine(srcDir, "Config", "config.psd1"), s);
                int n = ScriptGenerator.UpdateStrings(Path.Combine(srcDir, "Strings"), s);
                if (n > 0) Log.Info(T("Log_WelcomeApplied", n));
                string assets = Path.Combine(srcDir, "Assets");
                Directory.CreateDirectory(assets);
                string logoPng = null;
                if (!string.IsNullOrWhiteSpace(s.LogoPath))
                {
                    logoPng = Path.Combine(assets, "AppIcon.png");
                    ImageTools.SaveLogoPng(s.LogoPath, logoPng, 256);
                    Log.Ok(T("Log_LogoApplied"));
                }
                if (!string.IsNullOrWhiteSpace(s.LogoPath) || !string.IsNullOrWhiteSpace(s.CompanyName))
                    ImageTools.SaveBanner(logoPng ?? Path.Combine(assets, "AppIcon.png"), s.CompanyName, s.AccentColor, Path.Combine(assets, "Banner.Classic.png"));

                Log.Step(T("Log_GeneratingDetection"));
                string detPath = Path.Combine(intuneDir, PackageLayout.DetectionPrefix + name + ".ps1");
                File.WriteAllText(detPath, ScriptGenerator.DetectionScript(s), Encoding.ASCII);
                var detErr = await Task.Run(() => PowerShellValidator.Validate(detPath), ct);
                if (detErr.Length > 0) throw new InvalidDataException(T("Err_DetectionSyntax", detErr[0]));
                Log.Ok(T("Log_DetectionOk", Path.GetFileName(detPath)));

                string icon = null;
                string iconPath = Path.Combine(intuneDir, PackageLayout.AppIcon);
                if (s.InstallerType == "EXE" && ImageTools.SaveExeIcon(inst.FullName, iconPath)) icon = iconPath;
                if (icon == null && logoPng != null) { File.Copy(logoPng, iconPath, true); icon = iconPath; }

                Log.Step(T("Log_CreatingIntuneWin"));
                string iwPath = Path.Combine(intuneDir, name + ".intunewin");
                string tool = await IntuneWinBuilder.BuildAsync(srcDir, iwPath, forceNative, ct);
                var val = await Task.Run(() => IntuneWinBuilder.Validate(iwPath), ct);
                if (!val.Valid) throw new InvalidDataException(T("Err_IntuneWinInvalid"));
                long size = new FileInfo(iwPath).Length;
                string sha = await Task.Run(() => Hashing.Sha256File(iwPath), ct);
                Log.Ok(T("Log_IntuneWinOk", (size / 1024d / 1024d).ToString("N1", Culture()), val.FileCount, tool));

                var cmds = ScriptGenerator.Commands(s);
                var result = new BuildResult
                {
                    PackageFolder = pkgRoot,
                    PsadtFolder = srcDir,
                    IntuneFolder = intuneDir,
                    IntuneWinFile = iwPath,
                    IntuneWinTool = tool,
                    IntuneWinSize = size,
                    Sha256 = sha,
                    DetectionScript = detPath,
                    AppIcon = icon,
                    InstallCommand = cmds.Install,
                    UninstallCommand = cmds.Uninstall,
                    PsadtVersion = psadtVersion,
                    ProjectFile = Path.Combine(pkgRoot, PackageLayout.ProjectFile),
                    Warnings = check.Warnings
                };
                WriteCompanions(s, result);
                var (html, txt) = SummaryWriter.Write(s, result, intuneDir);
                result.SummaryHtml = html;
                result.SummaryTxt = txt;
                Json.Save(result.ProjectFile, s);
                Log.Ok(T("Log_BuildDone", sw.Elapsed.TotalSeconds.ToString("N0", Culture())));
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex is OperationCanceledException ? T("Log_BuildCancelled") : T("Log_BuildFailed"));
                Log.PackageLogFile = null;
                // The folder was created by this run and is incomplete: remove it (the application log keeps every entry).
                try { Directory.Delete(pkgRoot, true); } catch { }
                throw;
            }
            finally { Log.PackageLogFile = null; }
        }

        private static CultureInfo Culture() => Localization.Loc.Instance.Culture;

        /// <summary>intune-app.json, Publish-ToIntune.ps1 and the Windows Sandbox test script.</summary>
        private static void WriteCompanions(PackageSettings s, BuildResult r)
        {
            string desc = !string.IsNullOrWhiteSpace(s.Description) ? s.Description : $"{s.AppName} {s.AppVersion}".Trim();
            var meta = new IntuneAppMetadata
            {
                Generator = $"{AppInfo.Name} {AppInfo.Version}",
                GeneratedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                PsadtVersion = r.PsadtVersion,
                DisplayName = $"{s.AppName} {s.AppVersion}".Trim(),
                Description = desc,
                Publisher = string.IsNullOrWhiteSpace(s.AppVendor) ? s.AppName : s.AppVendor,
                AppVersion = s.AppVersion,
                InformationUrl = s.Homepage ?? "",
                Notes = $"Packaged with {AppInfo.Name} {AppInfo.Version} ({AppInfo.PsadtName} {r.PsadtVersion})",
                IntuneWinFile = Path.GetFileName(r.IntuneWinFile),
                IntuneWinSha256 = r.Sha256,
                DetectionScript = Path.GetFileName(r.DetectionScript),
                Icon = r.AppIcon != null ? Path.GetFileName(r.AppIcon) : "",
                InstallCommandLine = r.InstallCommand,
                UninstallCommandLine = r.UninstallCommand,
                InstallExperience = s.InstallBehavior == "User" ? "user" : "system",
                Architecture = IntuneArchitecture(s),
                MinimumSupportedWindowsRelease = string.IsNullOrEmpty(s.MinimumOs) ? MinimumOsOptions[0] : s.MinimumOs,
            };
            r.MetadataFile = Path.Combine(r.IntuneFolder, PackageLayout.MetadataFile);
            File.WriteAllText(r.MetadataFile, JsonSerializer.Serialize(meta, MetadataJson), TextFiles.Utf8NoBom);

            r.PublishScript = Path.Combine(r.IntuneFolder, PackageLayout.PublishScript);
            File.WriteAllText(r.PublishScript, CompanionScript(PackageLayout.PublishScript), Encoding.ASCII);

            string sandboxDir = Path.Combine(r.PackageFolder, PackageLayout.SandboxDir);
            Directory.CreateDirectory(sandboxDir);
            File.WriteAllText(Path.Combine(sandboxDir, PackageLayout.SandboxScript), CompanionScript(PackageLayout.SandboxScript), Encoding.ASCII);
        }

        /// <summary>Embedded companion script with the product name and repository of this build (forks get their own links).</summary>
        public static string CompanionScript(string resourceName)
            => EmbeddedText(resourceName).Replace("https://github.com/sesantanajr/J365-IntunePSApp", AppInfo.RepoUrl).Replace("J365 IntunePSApp", AppInfo.Name);

        public static string EmbeddedText(string resourceName)
        {
            using var st = typeof(PackageBuilder).Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Missing embedded resource: " + resourceName);
            using var reader = new StreamReader(st, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void CopyInstallerFolder(FileInfo inst, string filesDir, string outRoot, CancellationToken ct)
        {
            string outFull = Path.GetFullPath(outRoot).TrimEnd('\\') + "\\";
            var files = Directory.GetFiles(inst.DirectoryName, "*", SearchOption.AllDirectories)
                .Where(f => !f.StartsWith(outFull, StringComparison.OrdinalIgnoreCase)).ToList();
            long total = files.Sum(f => new FileInfo(f).Length);
            if (files.Count > 2000 || total > 2L * 1024 * 1024 * 1024)
                Log.Warn(T("Log_InstallerFolderLarge", files.Count, (total / 1024d / 1024d / 1024d).ToString("N1", Culture())));
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                string dest = Path.Combine(filesDir, Path.GetRelativePath(inst.DirectoryName, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(f, dest, true);
            }
        }

        private static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(src, d)));
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(f, Path.Combine(dest, Path.GetRelativePath(src, f)), true);
        }

        // ------------------------------------------------------------------ history

        public static List<PackageHistoryItem> History(string outputRoot)
        {
            var list = new List<PackageHistoryItem>();
            if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot)) return list;
            foreach (var dir in Directory.GetDirectories(outputRoot))
            {
                string proj = PackageLayout.FindProject(dir);
                if (proj == null) continue;
                try
                {
                    var s = LoadProject(proj);
                    list.Add(new PackageHistoryItem
                    {
                        Folder = dir,
                        ProjectFile = proj,
                        AppName = s.AppName,
                        AppVersion = s.AppVersion,
                        Vendor = s.AppVendor,
                        CatalogId = s.CatalogId,
                        Date = File.GetLastWriteTime(proj),
                        IntuneWin = PackageLayout.FindIntuneWin(dir),
                        SummaryHtml = PackageLayout.FindInstructions(dir),
                        LatestVersion = NewerCatalogVersion(s)
                    });
                }
                catch { }
            }
            return list.OrderByDescending(i => i.Date).ToList();
        }

        /// <summary>Version available in the application database when it is newer than the packaged one; otherwise null.</summary>
        public static string NewerCatalogVersion(PackageSettings s)
        {
            var app = CatalogService.Instance.ById(s.CatalogId);
            if (app == null || string.IsNullOrEmpty(app.Version)) return null;
            string packaged = !string.IsNullOrEmpty(s.CatalogVersion) ? s.CatalogVersion : s.AppVersion;
            if (string.IsNullOrEmpty(packaged)) return null;
            return VersionUtil.Compare(app.Version, packaged) > 0 ? app.Version : null;
        }

        /// <summary>Rebuilds the result of a previous build from its folder (used when a project is reopened).</summary>
        public static BuildResult TryLoadResult(string projectFile, PackageSettings s)
        {
            try
            {
                string dir = Path.GetDirectoryName(projectFile);
                string iw = PackageLayout.FindIntuneWin(dir);
                string pkg = PackageLayout.FindPackageDir(dir);
                if (iw == null || pkg == null) return null;
                var cmds = ScriptGenerator.Commands(s);
                string intune = Path.Combine(dir, PackageLayout.IntuneDir);
                string meta = Path.Combine(intune, PackageLayout.MetadataFile);
                string publish = Path.Combine(intune, PackageLayout.PublishScript);
                string txt = Path.Combine(intune, PackageLayout.InstructionsTxt);
                return new BuildResult
                {
                    PackageFolder = dir,
                    PsadtFolder = pkg,
                    IntuneFolder = intune,
                    IntuneWinFile = iw,
                    IntuneWinSize = new FileInfo(iw).Length,
                    DetectionScript = PackageLayout.FindDetection(dir),
                    AppIcon = PackageLayout.FindIcon(dir),
                    InstallCommand = cmds.Install,
                    UninstallCommand = cmds.Uninstall,
                    SummaryHtml = PackageLayout.FindInstructions(dir),
                    SummaryTxt = File.Exists(txt) ? txt : null,
                    MetadataFile = File.Exists(meta) ? meta : null,
                    PublishScript = File.Exists(publish) ? publish : null,
                    ProjectFile = projectFile
                };
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ local tests

        public static (bool Detected, string Output) TestDetection(PackageSettings s)
        {
            string tmp = Path.Combine(AppPaths.Temp, $"detection_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(tmp, ScriptGenerator.DetectionScript(s), Encoding.ASCII);
            try
            {
                // Our own freshly generated script: the execution policy is bypassed for this single process only.
                var psi = ToolsService.PowerShellStartInfo($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tmp}\"");
                using var p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd().Trim();
                if (!p.WaitForExit(60000)) { try { p.Kill(true); } catch { } return (false, output); }
                return (p.ExitCode == 0 && output.Length > 0, output);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        /// <summary>Runs the package on this computer, elevated and with the dialogs visible.</summary>
        public static void RunLocalTest(string psadtFolder, string deploymentType)
        {
            var psi = new ProcessStartInfo(Path.Combine(psadtFolder, "Invoke-AppDeployToolkit.exe"), $"-DeploymentType {deploymentType} -DeployMode Interactive")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = psadtFolder
            };
            Process.Start(psi);
        }
    }
}
