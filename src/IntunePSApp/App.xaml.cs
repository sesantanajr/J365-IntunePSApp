using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using J365.IntunePSApp.Views;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp
{
    public partial class App : Application
    {
        public static string[] Args { get; private set; } = Array.Empty<string>();

        public static string Arg(string name)
        {
            int i = Array.FindIndex(Args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
        }

        public static bool HasArg(string name) => Args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Command-line switches that run without opening the main window.</summary>
        private static readonly string[] CliSwitches = { "--help", "-h", "/?", "--version", "--build", "--setup", "--update-catalog", "--sandbox", "--install", "--uninstall", "--update", "--batch" };

        protected override void OnStartup(StartupEventArgs e)
        {
            Args = e.Args ?? Array.Empty<string>();
            Loc.Instance.SetLanguage(Loc.Resolve(Arg("--lang") ?? ConfigStore.Current.UiLanguage));
            ThemeManager.Initialize(Arg("--theme"));

            DispatcherUnhandledException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Error(T("Log_Unhandled", ex.ExceptionObject));
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Error(T("Log_TaskError", ex.Exception.GetBaseException().Message)); ex.SetObserved(); };

            base.OnStartup(e);
            AppPaths.CleanTemp();
            Log.Prune();
            // After an update: the previous executable (….old) goes, and "Installed apps" shows the running version.
            _ = AppUpdater.CleanupAsync();
            InstallService.RefreshRegistration();

            if (CliSwitches.Any(HasArg))
            {
                // Keep the application alive while dialogs of --install/--uninstall are open.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int code = RunCli();
                Shutdown(code);
                return;
            }

            Log.Info(T("Log_Started", AppInfo.Name, AppInfo.Version, AppPaths.Data));
            var w = new MainWindow();
            MainWindow = w;
            if (HasArg("--snapshot"))
            {
                w.WindowStartupLocation = WindowStartupLocation.Manual;
                w.Left = -32000; w.Top = 0;
                w.ShowActivated = false;
                w.ShowInTaskbar = false;
            }
            w.Show();
        }

        private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(T("Log_UiError", e.Exception));
            e.Handled = true;
            try { Ui.Warn(T("Err_Unexpected", e.Exception.GetBaseException().Message, AppPaths.Logs)); }
            catch { }
        }

        // ------------------------------------------------------------------ command line

        private int RunCli()
        {
            NativeMethods.AttachConsole(-1);
            try
            {
                if (HasArg("--help") || HasArg("-h") || HasArg("/?")) { Console.WriteLine(); Console.WriteLine(T("Cli_Help", AppInfo.Name, AppInfo.Version)); return 0; }
                if (HasArg("--version")) { Console.WriteLine(AppInfo.Version); return 0; }
                if (HasArg("--install")) return CliInstall();
                if (HasArg("--uninstall")) return CliUninstall();
                if (HasArg("--sandbox")) return CliSandbox();
                // Network/build work runs off the UI thread: awaiting on the dispatcher while blocking it would deadlock.
                return Task.Run(RunCliWork).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine(T("Cli_Error", ex.Message));
                return 1;
            }
        }

        private static int CliInstall()
        {
            bool quiet = HasArg("--quiet");
            try
            {
                string exe = InstallService.Install(HasArg("--desktop-shortcut"));
                Console.WriteLine(T("Log_Installed", AppPaths.InstallDir));
                if (!quiet) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(T("Cli_Error", ex.Message));
                if (!quiet) Ui.Error(ex.Message);
                return 1;
            }
        }

        private static int CliUninstall()
        {
            bool quiet = HasArg("--quiet");
            bool purge = HasArg("--purge");
            if (!quiet)
            {
                if (!Ui.Confirm(T("Install_ConfirmUninstall"), T("Install_Uninstall"), danger: true)) return 1602;
                if (!purge) purge = Ui.Confirm(T("Install_ConfirmPurge"), T("Install_PurgeYes"), T("Install_PurgeNo"));
            }
            InstallService.Uninstall(purge);
            Console.WriteLine(T("Install_Uninstalled"));
            // The installed copy must exit right away so its own folder can be removed: no dialog in that case.
            if (!quiet && !AppPaths.IsInstalledCopy) Ui.Info(T("Install_Uninstalled"));
            return 0;
        }

        /// <summary>
        /// --sandbox &lt;package folder|project.json&gt;: opens Windows Sandbox with the test of a generated package.
        /// Exit codes: 0 started, 2 wrong usage, 3 Windows Sandbox not available, 4 a sandbox is already open.
        /// </summary>
        private static int CliSandbox()
        {
            string target = Arg("--sandbox");
            if (!string.IsNullOrWhiteSpace(target) && File.Exists(target)) target = Path.GetDirectoryName(Path.GetFullPath(target));
            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target) || !SandboxService.CanTest(Path.GetFullPath(target)))
            {
                Console.WriteLine(T("Cli_SandboxUsage"));
                return 2;
            }
            target = Path.GetFullPath(target).TrimEnd('\\');
            switch (SandboxService.State)
            {
                case SandboxState.NotSupported: Console.WriteLine(T("Cli_SandboxUnsupported")); return 3;
                case SandboxState.NotEnabled: Console.WriteLine(T("Cli_SandboxNotEnabled", SandboxService.FeatureName)); return 3;
                case SandboxState.Running: Console.WriteLine(T("Cli_SandboxRunning")); return 4;
            }
            SandboxService.Launch(SandboxService.Prepare(target));
            Console.WriteLine(T("Cli_SandboxStarted", SandboxService.ResultFile(target)));
            return 0;
        }

        /// <summary>
        /// --update [--check]: updates this executable to the latest release (the next start runs the new version).
        /// Exit codes: 0 updated or already current, 1 failed, 10 (--check) a newer version is available.
        /// </summary>
        private static int CliUpdate()
        {
            var r = AppUpdater.GetLatestAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (VersionUtil.Compare(r.Version, AppInfo.Version) <= 0) { Console.WriteLine(T("Update_UpToDate", AppInfo.Version)); return 0; }
            if (HasArg("--check")) { Console.WriteLine(T("Cli_UpdateAvailable", r.Version, AppInfo.Version)); return 10; }
            if (!AppUpdater.CanUpdateInPlace) { Console.WriteLine(T("Update_ReadOnly", Path.GetDirectoryName(AppPaths.ExePath))); return 1; }
            int lastTenth = -1;
            var progress = new Progress<(long, long?)>(p =>
            {
                if (p.Item2 is not long total || total <= 0) return;
                int tenth = (int)(p.Item1 * 10 / total);
                if (tenth != lastTenth) { lastTenth = tenth; Console.WriteLine($"  {tenth * 10}%"); }
            });
            Console.WriteLine(T("Update_Downloading", r.Version));
            var (newExe, zip) = AppUpdater.DownloadAsync(r, AppPaths.ExePath, progress, CancellationToken.None).GetAwaiter().GetResult();
            AppUpdater.Replace(newExe, AppPaths.ExePath);
            AppUpdater.RefreshCompanionFiles(zip, Path.GetDirectoryName(AppPaths.ExePath));
            if (AppPaths.IsInstalledCopy) InstallService.SetRegisteredVersion(r.Version);
            Console.WriteLine(T("Cli_Updated", AppInfo.Version, r.Version));
            return 0;
        }

        /// <summary>
        /// --batch id1,id2,... [--mode Auto|WhenNeeded|Silent|Interactive] [--notifications showAll|showReboot|hideAll]
        /// [--company "Name"] [--out folder] [--native]: packages several applications of the database with the same settings.
        /// Exit code 0 when every package was generated, 1 otherwise (each line says why).
        /// </summary>
        private static int CliBatch()
        {
            var ids = (Arg("--batch") ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ids.Length == 0 || !CatalogService.Instance.IsLoaded) { Console.WriteLine(T("Cli_BatchUsage")); return 2; }
            var apps = new List<CatalogApp>();
            foreach (var id in ids)
            {
                // An id of the database, or a product id of the Microsoft Store (9PM860492SZD), published as a Store app.
                var app = CatalogService.Instance.ById(id);
                if (app == null && StoreCatalog.IsStoreId(id))
                {
                    try { app = StoreCatalog.FindAsync(id, ConfigStore.Current.PreferredLocale, CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (Exception ex) { Console.WriteLine(ex.Message); return 2; }
                }
                if (app == null) { Console.WriteLine(T("Cli_BatchUnknown", id)); return 2; }
                apps.Add(app);
            }
            var o = BatchOptions.FromConfig(ConfigStore.Current);
            if (Arg("--company") is string company) o.CompanyName = company;
            if (Arg("--mode") is string mode) o.DeployMode = mode;
            if (Arg("--notifications") is string notifications) o.IntuneNotifications = notifications;
            if (Arg("--out") is string output) o.OutputRoot = output;
            o.NativePackager = HasArg("--native");
            // --publish: each package goes to Intune with the connection of this computer (the certificate connects without
            // anyone; a user connection needs a session opened in the application before).
            o.Publish = HasArg("--publish");
            if (Arg("--if-exists") is string ifExists) o.IfExists = ifExists;
            if (Arg("--assign") is string assign) o.AssignIntent = assign;
            if (Arg("--assign-to") is string assignTo) o.AssignTo = assignTo;
            if (Arg("--group") is string group) o.GroupId = group;
            if (o.Publish)
            {
                if (IntuneConnection.NeedsSetup) { Console.WriteLine(T("Batch_PublishNotConnected")); return 2; }
                if (IntuneSubscription.Blocks) { Console.WriteLine(T("Ed_PublishCalloutNoPlan", IntuneConnection.Account, IntuneConnection.TenantLabel)); return 2; }
                if (IntunePublisher.Check(o.PublishOptions(IntuneConnection.UsesCertificate)) is string problem) { Console.WriteLine(T(problem)); return 2; }
            }
            var items = BatchPackager.Plan(apps, ConfigStore.Current);
            BatchPackager.RunAsync(items, o, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine();
            foreach (var i in items) Console.WriteLine($"  {i.StateLabel,-18} {i.Title}  {i.Message}  {i.PackageFolder}".TrimEnd());
            return items.All(i => i.Succeeded) ? 0 : 1;
        }

        private static int RunCliWork()
        {
            Log.Entry += en => Console.WriteLine($"[{en.TimeLabel}] {en.Text}");
            try
            {
                CatalogService.Instance.LoadAsync().GetAwaiter().GetResult();

                if (HasArg("--setup"))
                {
                    var items = SetupService.CreateItems();
                    bool ok = SetupService.RunAsync(items, refresh: false, CancellationToken.None).GetAwaiter().GetResult();
                    foreach (var i in items) Console.WriteLine($"  {i.StateLabel,-10} {i.Title}  {i.Detail}");
                    return ok ? 0 : 1;
                }
                if (HasArg("--update")) return CliUpdate();
                if (HasArg("--update-catalog"))
                {
                    CatalogService.Instance.UpdateAsync(ConfigStore.Current.CatalogUrl, null, CancellationToken.None, force: HasArg("--force")).GetAwaiter().GetResult();
                    Console.WriteLine(T("Log_CatalogUpdated", CatalogService.Instance.Apps.Count));
                    return 0;
                }

                if (HasArg("--batch")) return CliBatch();

                string input = Arg("--build");
                if (string.IsNullOrWhiteSpace(input) || !File.Exists(input)) { Console.WriteLine(T("Cli_BuildUsage")); return 2; }
                PackageSettings s;
                if (input.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) s = PackageBuilder.LoadProject(input);
                else
                {
                    s = ConfigStore.Current.NewPackage();
                    var info = InstallerAnalyzer.Analyze(input);
                    InstallerAnalyzer.ApplyTo(s, info);
                    foreach (var n in info.Notes) Console.WriteLine("  - " + n);
                }
                var outDir = Arg("--out");
                if (!string.IsNullOrWhiteSpace(outDir)) s.OutputRoot = outDir;
                var r = PackageBuilder.BuildAsync(s, HasArg("--native"), CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine();
                Console.WriteLine(T("Cli_Result", r.PackageFolder, r.IntuneWinFile, r.DetectionScript, r.InstallCommand, r.UninstallCommand));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(T("Cli_Error", ex.Message));
                return 1;
            }
        }
    }
}
