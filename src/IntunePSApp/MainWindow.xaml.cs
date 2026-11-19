using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using J365.IntunePSApp.Views;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp
{
    public partial class MainWindow : Window
    {
        public static MainWindow Current { get; private set; }

        public HomeView Home { get; private set; }
        public CatalogView Catalog { get; private set; }
        public EditorView Editor { get; private set; }
        public PackagesView Packages { get; private set; }
        public SettingsView Settings { get; private set; }

        private readonly List<SetupItem> _setupItems = SetupService.CreateItems();
        private TaskCompletionSource<bool> _setupClosed;
        private bool? _setupResult;          // null while running
        private bool _setupRunning;
        private CancellationTokenSource _busyCts;
        private bool _catalogUpdating;
        private bool _syncingLanguage;
        private bool _statusBusy;
        private string _pageKey = "home";

        public MainWindow()
        {
            Current = this;
            InitializeComponent();
            Home = new HomeView();
            Catalog = new CatalogView();
            Editor = new EditorView();
            Packages = new PackagesView();
            Settings = new SettingsView();
            AuthorText.Text = AppInfo.Author;
            SetupList.ItemsSource = _setupItems;
            ApplyInitialPlacement();
            SizeChanged += (s, e) => ApplyResponsiveLayout();
            StateChanged += (s, e) => ApplyMaximizedFix();
            Loaded += OnLoaded;
            CatalogService.Instance.Changed += () => Dispatcher.Invoke(() => UpdateCatalogStatus());
            UpdateService.Changed += () => Dispatcher.Invoke(() => { if (PageHost.Content == Home) Home.Refresh(); });
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            SyncLanguageToggles();
            ApplyStaticTexts();
            NavHome.IsChecked = true;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateToolsStatus();
            SetStatus(T("Status_LoadingCatalog"), true);
            await CatalogService.Instance.LoadAsync();
            UpdateCatalogStatus();
            SetStatus(null);

            string snap = App.Arg("--snapshot");
            if (!string.IsNullOrEmpty(snap)) { await SnapshotRunner.RunAsync(this, snap); return; }

            if (App.HasArg("--installed")) Toast(T("Install_Done"), "ok");
            if (App.Arg("--updated") is string from) Toast(T("Update_Done", AppInfo.Name, AppInfo.Version, from), "ok");
            if (SetupService.NeedsSetup && !App.HasArg("--no-setup")) await ShowSetupAsync(refresh: false);
            else if (!App.HasArg("--no-update")) _ = BackgroundChecksAsync();

            string open = App.Args.FirstOrDefault(a => !a.StartsWith("-") && File.Exists(a));
            if (open != null)
            {
                Navigate("editor");
                if (open.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) Editor.LoadProject(open);
                else await Editor.AnalyzeFileAsync(open);
            }
        }

        /// <summary>Quiet housekeeping after start: database, optional tools and new versions. Never interrupts the user.</summary>
        private async Task BackgroundChecksAsync()
        {
            try
            {
                if (CatalogService.Instance.NeedsUpdateCheck(ConfigStore.Current)) await UpdateCatalogAsync(userRequested: false);
                if (!ToolsService.HasIntuneWinAppUtil)
                {
                    await ToolsService.EnsureIntuneWinAppUtilAsync(CancellationToken.None);
                    UpdateToolsStatus();
                }
                await UpdateService.CheckAsync(false, CancellationToken.None);
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
        }

        // ------------------------------------------------------------------ navigation

        public void Navigate(string key)
        {
            var target = key switch { "catalog" => NavCatalog, "editor" => NavEditor, "packages" => NavPackages, "settings" => NavSettings, _ => NavHome };
            if (target.IsChecked == true) ShowPage(key); else target.IsChecked = true;
        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string key) ShowPage(key);
        }

        private void ShowPage(string key)
        {
            if (Home == null) return;
            _pageKey = key;
            UserControl page = key switch { "catalog" => Catalog, "editor" => Editor, "packages" => Packages, "settings" => Settings, _ => Home };
            UpdateCaption();
            if (page is IRefreshable r) r.Refresh();
            PageHost.Content = page;
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        private void UpdateCaption()
        {
            string title = _pageKey switch
            {
                "catalog" => T("Nav_Catalog"),
                "editor" => T("Nav_Editor"),
                "packages" => T("Nav_Packages"),
                "settings" => T("Nav_Settings"),
                _ => T("Nav_Home")
            };
            CaptionText.Text = $"{title}  ·  {AppInfo.Name}";
        }

        // ------------------------------------------------------------------ language

        /// <summary>Changes the interface language now and remembers it ("auto" follows Windows).</summary>
        public void SetLanguage(string setting)
        {
            ConfigStore.Current.UiLanguage = setting;
            ConfigStore.Save();
            Loc.Instance.SetLanguage(Loc.Resolve(setting));
        }

        private void Lang_Checked(object sender, RoutedEventArgs e)
        {
            if (_syncingLanguage || !(sender is RadioButton rb) || !(rb.Tag is string code)) return;
            if (code != Loc.Instance.Language || ConfigStore.Current.UiLanguage != code) SetLanguage(code);
        }

        private void SyncLanguageToggles()
        {
            _syncingLanguage = true;
            bool pt = Loc.Instance.IsPortuguese;
            LangPt.IsChecked = pt; LangEn.IsChecked = !pt;
            SetupLangPt.IsChecked = pt; SetupLangEn.IsChecked = !pt;
            _syncingLanguage = false;
        }

        private void ApplyStaticTexts()
        {
            VersionText.Text = T("Main_Version", AppInfo.Version);
            if (!_statusBusy) StatusLeft.Text = T("Common_Ready");
        }

        private void OnLanguageChanged()
        {
            SyncLanguageToggles();
            ApplyStaticTexts();
            UpdateCaption();
            UpdateToolsStatus();
            UpdateCatalogStatus(refreshPage: false);
            foreach (var item in _setupItems) item.RefreshTexts();
            if (!_setupRunning) SetupService.Inspect(_setupItems);
            UpdateSetupTexts();
            foreach (var view in new ILocalized[] { Home, Catalog, Editor, Packages, Settings }) view?.OnLanguageChanged();
        }

        // ------------------------------------------------------------------ status, notifications and busy overlay

        /// <summary>Status bar text; null means "Ready".</summary>
        public void SetStatus(string text, bool busy = false)
        {
            _statusBusy = busy && text != null;
            StatusLeft.Text = text ?? T("Common_Ready");
            StatusDot.Fill = _statusBusy ? (Brush)FindResource("Cyan") : (Brush)FindResource("Success");
        }

        public void UpdateToolsStatus()
        {
            string psadt = ToolsService.PsadtVersion is string v ? $"PSAppDeployToolkit {v}" : T("Status_PsadtMissing");
            string iw = ToolsService.HasIntuneWinAppUtil ? "IntuneWinAppUtil ✓" : T("Status_IwauMissing");
            string mode = AppPaths.IsInstalledCopy ? T("Status_Installed") : AppPaths.IsPortable ? T("Status_Portable") : T("Status_UserData");
            StatusRight.Text = $"{psadt}   ·   {iw}   ·   {mode}";
        }

        public void UpdateCatalogStatus(bool refreshPage = true)
        {
            var c = CatalogService.Instance;
            if (c.IsLoaded)
            {
                CatalogCount.Text = T("Main_AppsCount", c.Apps.Count.ToString("N0", Loc.Instance.Culture));
                if (!_catalogUpdating)
                    CatalogDate.Text = c.Generated.HasValue ? T("Main_DataFrom", c.Generated.Value.ToLocalTime().ToString("g", Loc.Instance.Culture)) : "";
            }
            else
            {
                CatalogCount.Text = "—";
                if (!_catalogUpdating) CatalogDate.Text = T("Main_NotDownloaded");
            }
            if (refreshPage && PageHost.Content is IRefreshable r && PageHost.Content != Editor) r.Refresh();
        }

        public void Toast(string text, string kind = "info")
        {
            var (bg, fg, glyph) = kind switch
            {
                "ok" => ("#E8F7EE", "#166534", ""),
                "warn" => ("#FEF4E2", "#92400E", ""),
                "error" => ("#FDECEC", "#991B1B", ""),
                _ => ("#E8F1FB", "#0B3E73", "")
            };
            var border = new Border
            {
                Background = Ui.Hex(bg),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 8, 0, 0),
                Effect = (System.Windows.Media.Effects.Effect)FindResource("PopShadow")
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.Font("IconFont"), Foreground = Ui.Hex(fg), FontSize = 16, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top });
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Width = 300, Foreground = Ui.Hex(fg) });
            border.Child = sp;
            ToastHost.Children.Add(border);
            border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == "error" ? 9 : kind == "warn" ? 7 : 5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (s2, e2) => ToastHost.Children.Remove(border);
                border.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        }

        public void ClearToasts() => ToastHost.Children.Clear();

        public CancellationToken ShowBusy(string title, string text, bool cancellable = true)
        {
            _busyCts?.Dispose();
            _busyCts = new CancellationTokenSource();
            BusyTitle.Text = title;
            BusyText.Text = text;
            BusyProgress.IsIndeterminate = true;
            BusyCancel.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed;
            BusyOverlay.Visibility = Visibility.Visible;
            return _busyCts.Token;
        }

        public void BusyProgressReport(long read, long? total)
        {
            var culture = Loc.Instance.Culture;
            string done = (read / 1048576d).ToString("N1", culture);
            if (total.HasValue && total.Value > 0)
            {
                BusyProgress.IsIndeterminate = false;
                BusyProgress.Value = read * 100.0 / total.Value;
                BusyText.Text = T("Busy_Progress", done, (total.Value / 1048576d).ToString("N1", culture));
            }
            else BusyText.Text = done + " MB";
        }

        public void HideBusy() => BusyOverlay.Visibility = Visibility.Collapsed;

        private void BusyCancel_Click(object sender, RoutedEventArgs e) => _busyCts?.Cancel();

        // ------------------------------------------------------------------ first run / dependencies

        /// <summary>Shows the setup screen and downloads what is missing. Completes when the user closes the screen.</summary>
        public async Task ShowSetupAsync(bool refresh)
        {
            if (_setupRunning) return;
            _setupClosed = new TaskCompletionSource<bool>();
            SetupOverlay.Visibility = Visibility.Visible;
            await RunSetupAsync(refresh);
            await _setupClosed.Task;
        }

        private async Task RunSetupAsync(bool refresh)
        {
            _setupRunning = true;
            _setupResult = null;
            UpdateSetupTexts();
            bool ok = false;
            try { ok = await SetupService.RunAsync(_setupItems, refresh, CancellationToken.None); }
            catch (Exception ex) { Log.Warn(ex.Message); }
            _setupRunning = false;
            _setupResult = ok;
            UpdateToolsStatus();
            UpdateCatalogStatus();
            UpdateSetupTexts();
        }

        private void UpdateSetupTexts()
        {
            bool running = _setupResult == null;
            bool allOk = _setupItems.All(i => i.State == SetupState.Ok);
            SetupContinue.IsEnabled = !running;
            SetupContinue.Content = running ? T("Setup_Preparing") : _setupResult == true ? T("Setup_Start") : T("Setup_ContinueAnyway");
            SetupRetry.Visibility = !running && !allOk ? Visibility.Visible : Visibility.Collapsed;
            SetupInstall.Visibility = !running && AppPaths.IsPortable && !AppPaths.IsInstalledCopy && !InstallService.IsInstalled
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("J365_INTUNEPSAPP_DATA")) ? Visibility.Visible : Visibility.Collapsed;
            SetupNote.Text = running ? T("Setup_Working") : allOk ? T("Setup_Done") : _setupResult == true ? T("Setup_DoneWithWarnings") : T("Setup_Failed");
        }

        public bool IsSetupRunning => _setupRunning;

        private void SetupContinue_Click(object sender, RoutedEventArgs e) => CloseSetup();

        public void CloseSetup()
        {
            SetupOverlay.Visibility = Visibility.Collapsed;
            _setupClosed?.TrySetResult(true);
            if (PageHost.Content is IRefreshable r) r.Refresh();
            if (!App.HasArg("--no-update")) _ = UpdateService.CheckAsync(false, CancellationToken.None);
        }

        private async void SetupRetry_Click(object sender, RoutedEventArgs e)
        {
            if (!_setupRunning) await RunSetupAsync(refresh: false);
        }

        private void SetupInstall_Click(object sender, RoutedEventArgs e) => InstallForUser(desktopShortcut: false);

        /// <summary>
        /// Installs (or updates) the per-user copy and restarts from it. When the installed copy is open it is asked to
        /// close first (it may be building a package); if it stays open it keeps running the old version until closed.
        /// </summary>
        public void InstallForUser(bool desktopShortcut)
        {
            try
            {
                var running = AppUpdater.RunningCopies(InstallService.InstalledExe);
                if (running.Count > 0)
                {
                    if (!Ui.Confirm(T("Install_RunningPrompt", InstallService.InstalledVersion ?? "?"), T("Install_CloseAndUpdate"), T("Common_Cancel"))) return;
                    foreach (var p in running)
                    {
                        try { p.CloseMainWindow(); if (!p.WaitForExit(15000)) Log.Warn(T("Install_StillRunning")); }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                ConfigStore.Save();
                string exe = InstallService.Install(desktopShortcut);
                Process.Start(new ProcessStartInfo(exe, "--installed") { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        }

        // ------------------------------------------------------------------ application update

        private bool _updating;

        /// <summary>
        /// Updates the application with it open: finds the latest release, asks, downloads it with progress (it can be
        /// cancelled), checks it and restarts in the new version. Packages, settings, the database and tools are kept.
        /// </summary>
        public async Task UpdateAppAsync()
        {
            if (_updating) return;
            if (Editor != null && Editor.IsBuilding) { Ui.Warn(T("Update_WaitBuild")); return; }
            string target = AppPaths.ExePath;
            if (!AppUpdater.CanUpdateInPlace)
            {
                if (Ui.Confirm(T("Update_ReadOnly", Path.GetDirectoryName(target)), T("Notice_AppUpdateButton"), T("Common_Close"))) OpenUrl(AppInfo.ReleasesUrl);
                return;
            }
            _updating = true;
            try
            {
                var ct = ShowBusy(T("Update_Title"), T("Update_Checking"));
                var release = await AppUpdater.GetLatestAsync(ct);
                HideBusy();
                ConfigStore.Current.LatestAppVersion = release.Version;
                if (VersionUtil.Compare(release.Version, AppInfo.Version) <= 0) { Toast(T("Update_UpToDate", AppInfo.Version), "ok"); return; }
                string size = release.ZipSize > 0 ? (release.ZipSize / 1048576d).ToString("N0", Loc.Instance.Culture) + " MB" : "?";
                if (!Ui.Confirm(T("Update_Confirm", AppInfo.Name, AppInfo.Version, release.Version, size), T("Update_Now"), T("Common_Cancel"))) return;

                ct = ShowBusy(T("Update_DownloadingTitle", release.Version), T("Update_Starting"));
                SetStatus(T("Update_DownloadingTitle", release.Version), true);
                var progress = new Progress<(long, long?)>(p => BusyProgressReport(p.Item1, p.Item2));
                var (newExe, zip) = await AppUpdater.DownloadAsync(release, target, progress, ct);

                ShowBusy(T("Update_Installing", release.Version), T("Update_Restarting"), cancellable: false);
                string previous = AppInfo.Version;
                ConfigStore.Save();
                await Task.Run(() =>
                {
                    AppUpdater.Replace(newExe, target);
                    AppUpdater.RefreshCompanionFiles(zip, Path.GetDirectoryName(target));
                });
                if (AppPaths.IsInstalledCopy) InstallService.SetRegisteredVersion(release.Version);
                Log.Ok(T("Log_Updated", previous, release.Version));
                try { AppUpdater.Restart(target, previous); }
                catch (Exception ex)
                {
                    // The new version is in place; only the restart failed: the user opens it again.
                    HideBusy();
                    Ui.Info(T("Update_RestartManually", release.Version, ex.Message));
                }
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
                HideBusy();
                Toast(T("Update_Cancelled"), "info");
            }
            catch (Exception ex)
            {
                HideBusy();
                Log.Error(T("Log_UpdateFailed", ex.Message));
                if (Ui.Confirm(T("Update_Failed", ex.Message), T("Notice_AppUpdateButton"), T("Common_Close"))) OpenUrl(AppInfo.ReleasesUrl);
            }
            finally
            {
                _updating = false;
                SetStatus(null);
            }
        }

        // ------------------------------------------------------------------ application database

        public async Task UpdateCatalogAsync(bool userRequested, bool force = false)
        {
            if (_catalogUpdating) return;
            _catalogUpdating = true;
            BtnCatalogUpdate.IsEnabled = false;
            CatalogDate.Text = T("Main_Checking");
            SetStatus(T("Status_UpdatingCatalog"), true);
            try
            {
                var progress = new Progress<(long, long?)>(p =>
                {
                    if (p.Item2.HasValue && p.Item2 > 0) CatalogDate.Text = T("Main_Downloading", p.Item1 * 100 / p.Item2.Value);
                });
                bool updated = await CatalogService.Instance.UpdateAsync(ConfigStore.Current.CatalogUrl, progress, CancellationToken.None, force);
                if (userRequested)
                    Toast(updated ? T("Main_CatalogUpdated", CatalogService.Instance.Apps.Count.ToString("N0", Loc.Instance.Culture)) : T("Main_CatalogCurrent"), "ok");
            }
            catch (Exception ex)
            {
                Log.Warn(T("Log_CatalogUpdateFailed", ex.Message));
                if (userRequested || !CatalogService.Instance.IsLoaded)
                    Toast(CatalogService.Instance.IsLoaded ? T("Main_CatalogFailedLocal") : T("Main_CatalogFailedNone"), "warn");
            }
            finally
            {
                _catalogUpdating = false;
                BtnCatalogUpdate.IsEnabled = true;
                UpdateCatalogStatus();
                SetStatus(null);
            }
        }

        private async void CatalogUpdate_Click(object sender, RoutedEventArgs e) => await UpdateCatalogAsync(userRequested: true);

        public static void OpenUrl(string url) => Ui.OpenPath(url);

        private void Blog_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.BlogUrl);
        private void Repo_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.RepoUrl);
        private void Author_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.AuthorLinkedIn);
        private void Psadt_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.PsadtUrl);

        // ------------------------------------------------------------------ window

        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ApplyMaximizedFix()
        {
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            BtnMax.Content = WindowState == WindowState.Maximized ? "" : "";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (Editor != null && Editor.IsBuilding && !Ui.Confirm(T("Main_ConfirmExitBuilding"), danger: true)) { e.Cancel = true; return; }
            SavePlacement();
            base.OnClosing(e);
        }

        // ------------------------------------------------------------------ size and responsive layout

        /// <summary>Window width (DIPs) under which the sidebar shows icons only.</summary>
        public const double CompactSidebarBelow = 1180;
        /// <summary>Window height (DIPs) under which the database card of the sidebar gives its room to the navigation.</summary>
        public const double ShortSidebarBelow = 760;

        private bool? _compactSidebar;

        /// <summary>
        /// First size of the window: the one of the last session, never larger than the work area of the screen. Where the
        /// default size does not fit (small notebooks, 125-150 % scaling, remote sessions) the window starts maximized.
        /// </summary>
        private void ApplyInitialPlacement()
        {
            if (App.HasArg("--snapshot"))
            {
                // Capture mode: "--snapshot-size 1024x640" renders the pages at that window size (responsive checks).
                var m = System.Text.RegularExpressions.Regex.Match(App.Arg("--snapshot-size") ?? "", @"^(\d{3,4})x(\d{3,4})$");
                if (m.Success) { Width = Math.Max(MinWidth, int.Parse(m.Groups[1].Value)); Height = Math.Max(MinHeight, int.Parse(m.Groups[2].Value)); }
                return;
            }
            var area = SystemParameters.WorkArea;
            var c = ConfigStore.Current;
            MinWidth = Math.Min(MinWidth, area.Width);
            MinHeight = Math.Min(MinHeight, area.Height);
            if (c.WindowWidth >= MinWidth && c.WindowHeight >= MinHeight) { Width = c.WindowWidth; Height = c.WindowHeight; }
            bool fits = Width <= area.Width - 16 && Height <= area.Height - 16;
            if (!fits)
            {
                Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 16));
                Height = Math.Max(MinHeight, Math.Min(Height, area.Height - 16));
            }
            if (c.WindowMaximized || !fits) WindowState = WindowState.Maximized;
        }

        private void SavePlacement()
        {
            if (App.HasArg("--snapshot")) return;
            var c = ConfigStore.Current;
            c.WindowMaximized = WindowState == WindowState.Maximized;
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (!bounds.IsEmpty && bounds.Width >= MinWidth && bounds.Height >= MinHeight)
            {
                c.WindowWidth = Math.Round(bounds.Width);
                c.WindowHeight = Math.Round(bounds.Height);
            }
            ConfigStore.Save();
        }

        /// <summary>
        /// Narrow window: the sidebar keeps only its icons (64 px instead of 256). Short window: the database card is hidden.
        /// Everything hidden here is also available on the Home and Settings pages.
        /// </summary>
        private void ApplyResponsiveLayout()
        {
            if (ActualWidth <= 0) return;
            bool compact = ActualWidth < CompactSidebarBelow;
            if (_compactSidebar != compact)
            {
                _compactSidebar = compact;
                var hidden = compact ? Visibility.Collapsed : Visibility.Visible;
                SidebarColumn.Width = new GridLength(compact ? 64 : 256);
                SidebarPanel.Margin = compact ? new Thickness(10, 14, 10, 14) : new Thickness(16, 14, 16, 14);
                BrandPanel.Margin = compact ? new Thickness(0, 0, 0, 18) : new Thickness(4, 0, 0, 22);
                BrandIcon.Width = BrandIcon.Height = compact ? 44 : 46;
                BrandIcon.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 12, 0);
                BrandText.Visibility = MenuLabel.Visibility = SidebarFooter.Visibility = hidden;
                foreach (var item in NavPanel.Children.OfType<RadioButton>())
                {
                    if (!(item.Content is StackPanel sp) || sp.Children.Count != 2 || !(sp.Children[0] is TextBlock icon) || !(sp.Children[1] is TextBlock label)) continue;
                    icon.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 14, 0);
                    label.Visibility = hidden;
                    // The label becomes the tooltip (bound, so it follows the interface language).
                    if (compact) item.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = label });
                    else System.Windows.Data.BindingOperations.ClearBinding(item, ToolTipProperty);
                }
            }
            DatabaseCard.Visibility = ActualHeight < ShortSidebarBelow ? Visibility.Collapsed : Visibility.Visible;
        }

        public static BitmapImage LoadBitmap(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }
    }

    /// <summary>Pages that reload their data when shown.</summary>
    public interface IRefreshable
    {
        void Refresh();
    }

    /// <summary>Pages with texts built in code: called after the interface language changes.</summary>
    public interface ILocalized
    {
        void OnLanguageChanged();
    }
}
