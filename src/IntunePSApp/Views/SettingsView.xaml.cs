using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Microsoft.Win32;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    public partial class SettingsView : UserControl, IRefreshable, ILocalized
    {
        private readonly List<SetupItem> _dependencies = SetupService.CreateItems();
        private bool _loading;
        private bool _working;

        public SettingsView()
        {
            InitializeComponent();
            DependencyList.ItemsSource = _dependencies;
            AboutAuthor.Text = AppInfo.Author;
            BuildSwatches();
            CompanyGrid.SizeChanged += (s, e) => UpdateCompanyLayout();
            IntuneConnection.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateIntuneStatus));
        }

        // ------------------------------------------------------------------ Microsoft Intune connection

        private CancellationTokenSource _intuneCts;

        /// <summary>Typing the registration by hand shows "Sign in" as soon as both values are valid.</summary>
        private void IntuneFields_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_loading) UpdateIntuneStatus(syncFields: false);
        }

        private void UpdateIntuneStatus() => UpdateIntuneStatus(syncFields: true);

        private void UpdateIntuneStatus(bool syncFields)
        {
            if (IntuneStatusTitle == null || TxtTenant == null || TxtClient == null) return;
            bool signedIn = IntuneConnection.IsSignedIn, configured = IntuneConnection.IsConfigured;
            IntuneStatusBox.Background = (Brush)FindResource(signedIn ? "SuccessSoft" : "BrandSoft");
            IntuneStatusIcon.Foreground = (Brush)FindResource(signedIn ? "Success" : "Brand");
            IntuneStatusIcon.Text = signedIn ? "" : "";
            IntuneStatusTitle.Text = signedIn ? T("Publish_ConnectedAs", IntuneConnection.Account)
                : configured ? T("Publish_NotSignedIn") : T("Publish_NotConnected");
            IntuneStatusText.Text = signedIn ? T("Settings_IntuneConnectedText", IntuneConnection.RegistrationName, IntuneConnection.ClientId, IntuneConnection.SessionTenant.Length > 0 ? IntuneConnection.SessionTenant : IntuneConnection.TenantId)
                : configured ? T("Settings_IntuneConfiguredText", IntuneConnection.ClientId, IntuneConnection.TenantId)
                : T("Publish_NotConnectedHint", IntuneConnection.RegistrationName);
            bool typed = IntunePublisher.IsTenant(TxtTenant.Text) && IntunePublisher.IsGuid(TxtClient.Text);
            BtnIntuneSignIn.Visibility = configured || typed ? Visibility.Visible : Visibility.Collapsed;
            BtnIntuneSignIn.Content = signedIn ? T("Publish_SwitchAccount") : T("Publish_SignIn");
            BtnIntuneTest.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            BtnIntuneSignOut.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            if (syncFields && !_loading)
            {
                _loading = true;
                try
                {
                    TxtTenant.Text = ConfigStore.Current.IntuneTenantId ?? "";
                    TxtClient.Text = ConfigStore.Current.IntuneClientId ?? "";
                }
                finally { _loading = false; }
            }
        }

        private void SetIntuneBusy(string text)
        {
            bool busy = !string.IsNullOrEmpty(text);
            IntuneBusyText.Text = text ?? "";
            IntuneBusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            BtnIntuneSetup.IsEnabled = BtnIntuneSignIn.IsEnabled = BtnIntuneTest.IsEnabled = BtnIntuneSignOut.IsEnabled = !busy;
        }

        private async void IntuneSetup_Click(object sender, RoutedEventArgs e)
        {
            _intuneCts = new CancellationTokenSource();
            SetIntuneBusy(T("Connect_StepAdminSignIn"));
            try { await IntuneConnectUi.SetupAsync(s => Dispatcher.Invoke(() => SetIntuneBusy(string.IsNullOrEmpty(s) ? T("Common_Working") : s)), _intuneCts.Token); }
            finally { SetIntuneBusy(null); UpdateIntuneStatus(); }
        }

        private async void IntuneSignIn_Click(object sender, RoutedEventArgs e)
        {
            // Typed by hand: saved first, so the sign-in uses them.
            string tenant = TxtTenant.Text.Trim(), client = TxtClient.Text.Trim();
            if (tenant.Length > 0 && client.Length > 0 && (!IntunePublisher.IsTenant(tenant) || !IntunePublisher.IsGuid(client))) { Ui.Warn(T(IntunePublisher.IsTenant(tenant) ? "Publish_InvalidClient" : "Publish_InvalidTenant")); return; }
            if (tenant.Length > 0 && client.Length > 0) { ConfigStore.Current.IntuneTenantId = tenant; ConfigStore.Current.IntuneClientId = client; ConfigStore.Save(); }
            _intuneCts = new CancellationTokenSource();
            SetIntuneBusy(T("Publish_SigningIn"));
            try { await IntuneConnectUi.SignInAsync(null, _intuneCts.Token); }
            finally { SetIntuneBusy(null); UpdateIntuneStatus(); }
        }

        private async void IntuneTest_Click(object sender, RoutedEventArgs e)
        {
            SetIntuneBusy(T("Settings_IntuneTesting"));
            try { Ui.Info(await IntuneConnection.TestAsync(CancellationToken.None)); }
            catch (Exception ex) { Ui.Error(T("Auth_Failed", ex.Message)); }
            finally { SetIntuneBusy(null); UpdateIntuneStatus(); }
        }

        private void IntuneSignOut_Click(object sender, RoutedEventArgs e)
        {
            IntuneConnection.SignOut();
            MainWindow.Current.Toast(T("Settings_IntuneSignedOut"), "info");
            UpdateIntuneStatus();
        }

        // ------------------------------------------------------------------ branding preview

        private void BuildSwatches()
        {
            SwatchPanel.Children.Clear();
            foreach (var (hex, key) in EditorView.AccentPalette)
            {
                var b = new Border
                {
                    Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 5, 0), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = Ui.Hex(hex), ToolTip = $"{T(key)} ({hex})", Tag = hex
                };
                b.MouseLeftButtonUp += (s, e) => TxtAccent.Text = (string)((Border)s).Tag;
                SwatchPanel.Children.Add(b);
            }
        }

        /// <summary>The preview sits beside the fields; on narrow windows it goes below them.</summary>
        private void UpdateCompanyLayout()
        {
            bool narrow = CompanyGrid.ActualWidth < 900;
            Grid.SetColumn(BrandPreviewPanel, narrow ? 0 : 1);
            Grid.SetRow(BrandPreviewPanel, narrow ? 1 : 0);
            BrandPreviewPanel.Margin = narrow ? new Thickness(0, 20, 0, 0) : new Thickness(24, 0, 0, 0);
            BrandPreviewPanel.HorizontalAlignment = HorizontalAlignment.Left;
        }

        private void Brand_Changed(object sender, RoutedEventArgs e) => UpdateBrandPreview();

        /// <summary>The welcome dialog with the branding being edited (company, logo, accent colour, style).</summary>
        private void UpdateBrandPreview()
        {
            if (BrandPreview == null || TxtCompany == null || TxtAccent == null || CmbStyle == null) return;
            string hex = (TxtAccent.Text ?? "").Trim();
            if (hex.Length == 6 && !hex.StartsWith("#")) hex = "#" + hex;
            Brush accent = null;
            try { if (System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9A-Fa-f]{6}$")) accent = Ui.Hex(hex); } catch { }
            accent ??= (Brush)FindResource("Brand");
            AccentSwatch.Background = accent;
            string company = string.IsNullOrWhiteSpace(TxtCompany.Text) ? AppInfo.PsadtName : TxtCompany.Text.Trim();
            string sample = T("Settings_PreviewApp");
            BrandPreview.Show(new DialogPreviewModel
            {
                Style = TagOf(CmbStyle) == "Classic" ? "Classic" : "Fluent",
                Accent = accent,
                Logo = (ImageSource)MainWindow.LoadBitmap(TxtLogo.Text.Trim().Trim('"')) ?? new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app-256.png")),
                Company = company,
                Title = sample,
                Subtitle = T("Prev_Subtitle", company),
                Body = T("Prev_CloseBody"),
                Apps = new List<string> { T("Settings_PreviewProcess") },
                Custom = TxtWelcome?.Text.Trim() ?? "",
                DeferText = ChkDefer?.IsChecked == true ? T("Prev_DeferText", int.TryParse(TxtDefer?.Text, out int d) ? d : 3) : null,
                MainButton = T("Prev_CloseAndInstall")
            });
        }

        public static void SelectTag(ComboBox c, string tag)
        {
            foreach (ComboBoxItem it in c.Items) if (string.Equals(it.Tag as string, tag, StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = it; return; }
            c.SelectedIndex = 0;
        }

        public static string TagOf(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        /// <summary>The selection box of a ComboBox keeps the text it had when the item was selected: select again after a language change.</summary>
        public static void RefreshSelection(params ComboBox[] combos)
        {
            foreach (var c in combos)
            {
                int i = c.SelectedIndex;
                if (i < 0) continue;
                c.SelectedIndex = -1;
                c.SelectedIndex = i;
            }
        }

        /// <summary>Scrolls the page to its last section (About).</summary>
        public void ScrollToEnd()
        {
            PageScroll.UpdateLayout();
            PageScroll.ScrollToEnd();
        }

        public void Refresh()
        {
            _loading = true;
            var c = ConfigStore.Current;
            SelectTag(CmbUiLanguage, c.UiLanguage);
            ChkUpdates.IsChecked = c.CheckForUpdates;
            TxtCompany.Text = c.CompanyName;
            TxtAuthor.Text = c.Author;
            TxtLogo.Text = c.LogoPath;
            TxtAccent.Text = c.AccentColor;
            SelectTag(CmbStyle, c.DialogStyle);
            SelectTag(CmbLanguage, c.Language);
            ChkImeLogs.IsChecked = c.LogsInIntuneFolder;
            SelectTag(CmbMode, c.DeployMode);
            TxtDefer.Text = c.DeferTimes.ToString();
            TxtCountdown.Text = c.CloseAppsCountdown.ToString();
            ChkDefer.IsChecked = c.AllowDefer;
            ChkBlock.IsChecked = c.BlockExecution;
            ChkDisk.IsChecked = c.CheckDiskSpace;
            ChkRemovePrev.IsChecked = c.RemovePreviousVersions;
            TxtWelcome.Text = c.WelcomeMessage;
            TxtCompletion.Text = c.CompletionMessage;
            SelectTag(CmbArch, c.PreferredArch);
            SelectTag(CmbLocale, c.PreferredLocale);
            TxtOutput.Text = c.OutputFolder;
            TxtData.Text = AppPaths.Data;
            TxtCatalogUrl.Text = c.CatalogUrl;
            TxtCatalogDays.Text = c.CatalogUpdateDays.ToString();
            TxtTenant.Text = c.IntuneTenantId;
            TxtClient.Text = c.IntuneClientId;
            _loading = false;
            UpdateInfo();
            UpdateIntuneStatus();
            UpdateBrandPreview();
        }

        public void OnLanguageChanged()
        {
            _loading = true;
            RefreshSelection(CmbUiLanguage, CmbStyle, CmbLanguage, CmbMode, CmbArch);
            _loading = false;
            foreach (var d in _dependencies) d.RefreshTexts();
            UpdateInfo();
            UpdateIntuneStatus();
            UpdateBrandPreview();
        }

        /// <summary>Scrolls to the Microsoft Intune card (from the indicator on Home).</summary>
        public void ShowIntune()
        {
            Dispatcher.BeginInvoke(new Action(() => IntuneCard.BringIntoView()), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>Updates the information blocks (database, versions, installation) without touching the form.</summary>
        public void RefreshInfo() => UpdateInfo();

        private void UpdateInfo()
        {
            var culture = Loc.Instance.Culture;
            var cat = CatalogService.Instance;
            CatalogInfo.Text = cat.IsLoaded
                ? T("Settings_CatalogInfo", cat.Apps.Count.ToString("N0", culture), cat.Generated?.ToLocalTime().ToString("g", culture) ?? "?")
                  + (string.IsNullOrEmpty(cat.Commit) ? "" : $" · winget-pkgs {cat.Commit[..Math.Min(8, cat.Commit.Length)]}")
                : T("Main_NotDownloaded");
            PortableInfo.Text = AppPaths.IsInstalledCopy ? T("Settings_DataInstalled") : AppPaths.IsPortable ? T("Settings_DataPortable") : T("Settings_DataReadOnly");
            AboutTitle.Text = $"{AppInfo.Name} {AppInfo.Version}";
            LogoThumb.Source = MainWindow.LoadBitmap(TxtLogo.Text);
            if (!_working) SetupService.Inspect(_dependencies);

            string newApp = UpdateService.NewAppVersion;
            UpdateInfoText.Text = newApp != null ? T("Settings_UpdateAvailable", newApp)
                : ConfigStore.Current.LastUpdateCheck.HasValue ? T("Settings_UpToDate", ConfigStore.Current.LastUpdateCheck.Value.ToString("g", culture)) : "";
            BtnUpdateNow.Visibility = newApp != null ? Visibility.Visible : Visibility.Collapsed;
            BtnUpdateNowText.Text = newApp != null ? T("Settings_UpdateTo", newApp) : "";

            // Screenshots never show the installation of the computer that renders them.
            bool installed = InstallService.IsInstalled && !App.HasArg("--snapshot");
            bool envData = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("J365_INTUNEPSAPP_DATA"));
            InstallInfo.Text = AppPaths.IsInstalledCopy ? T("Settings_InstallRunning", AppPaths.InstallDir)
                : installed ? T("Settings_InstallOther", InstallService.InstalledVersion ?? "?") : T("Settings_InstallPortable");
            BtnInstall.Visibility = !AppPaths.IsInstalledCopy && !envData ? Visibility.Visible : Visibility.Collapsed;
            BtnInstall.Content = installed ? T("Settings_InstallUpdate") : T("Settings_Install");
            BtnUninstall.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            ChkDesktopShortcut.IsChecked = installed && InstallService.HasDesktopShortcut;
        }

        // ------------------------------------------------------------------ application

        private void UiLanguage_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || !IsLoaded) return;
            string tag = TagOf(CmbUiLanguage);
            if (tag.Length > 0 && tag != ConfigStore.Current.UiLanguage) MainWindow.Current.SetLanguage(tag);
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            UpdateInfoText.Text = T("Main_Checking");
            await UpdateService.CheckAsync(true, CancellationToken.None);
            UpdateInfo();
            // A newer version: the update itself asks before downloading.
            if (UpdateService.NewAppVersion != null) await MainWindow.Current.UpdateAppAsync();
        }

        private async void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            await MainWindow.Current.UpdateAppAsync();
            UpdateInfo();
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            // The confirmation explains what happens; an open installed copy is handled by InstallForUser.
            string question = InstallService.IsInstalled
                ? T("Settings_InstallUpdateConfirm", InstallService.InstalledVersion ?? "?", AppInfo.Version, AppPaths.InstallDir)
                : T("Settings_InstallConfirm", AppPaths.InstallDir);
            if (!Ui.Confirm(question, InstallService.IsInstalled ? T("Settings_InstallUpdate") : T("Settings_Install"), T("Common_Cancel"))) return;
            MainWindow.Current.InstallForUser(ChkDesktopShortcut.IsChecked == true);
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (!Ui.Confirm(T("Install_ConfirmUninstall"), T("Install_Uninstall"), T("Common_Cancel"), danger: true)) return;
            bool purge = Ui.Confirm(T("Install_ConfirmPurge"), T("Install_PurgeYes"), T("Install_PurgeNo"));
            try
            {
                InstallService.Uninstall(purge);
                if (AppPaths.IsInstalledCopy)
                {
                    // This is the installed copy: it closes now so that its own folder can be removed.
                    Application.Current.Shutdown();
                    return;
                }
                MainWindow.Current.Toast(T("Install_Uninstalled"), "ok");
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
            UpdateInfo();
        }

        private void DesktopShortcut_Click(object sender, RoutedEventArgs e)
        {
            try { InstallService.SetDesktopShortcut(ChkDesktopShortcut.IsChecked == true); }
            catch (Exception ex) { Ui.Error(ex.Message); }
            UpdateInfo();
        }

        // ------------------------------------------------------------------ company and defaults

        private void Logo_Changed(object sender, TextChangedEventArgs e)
        {
            LogoThumb.Source = MainWindow.LoadBitmap(TxtLogo.Text);
            UpdateBrandPreview();
        }

        private void Logo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = T("Filter_Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtLogo.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string tenant = TxtTenant.Text.Trim(), client = TxtClient.Text.Trim();
            if (tenant.Length > 0 && !IntunePublisher.IsTenant(tenant)) { Ui.Warn(T("Publish_InvalidTenant")); return; }
            if (client.Length > 0 && !IntunePublisher.IsGuid(client)) { Ui.Warn(T("Publish_InvalidClient")); return; }
            string accent = TxtAccent.Text.Trim();
            if (accent.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(accent, "^#?[0-9A-Fa-f]{6}$")) { Ui.Warn(T("Val_AccentColor")); return; }

            var c = ConfigStore.Current;
            // What a new package gets now; the package open in the editor follows wherever it still had these defaults.
            var before = c.NewPackage();
            c.CheckForUpdates = ChkUpdates.IsChecked == true;
            c.CompanyName = TxtCompany.Text.Trim();
            c.Author = TxtAuthor.Text.Trim();
            string logo = TxtLogo.Text.Trim().Trim('"');
            if (!string.IsNullOrEmpty(logo))
            {
                if (!File.Exists(logo)) { Ui.Warn(T("Val_LogoMissing", logo)); return; }
                try
                {
                    // The logo is copied next to the settings so it keeps working if the original file is moved.
                    string dest = Path.Combine(AppPaths.Branding, "company-logo" + Path.GetExtension(logo).ToLowerInvariant());
                    if (!string.Equals(Path.GetFullPath(logo), dest, StringComparison.OrdinalIgnoreCase)) File.Copy(logo, dest, true);
                    logo = dest;
                }
                catch (Exception ex) { Ui.Error(ex.Message); return; }
            }
            c.LogoPath = logo;
            c.AccentColor = accent;
            c.DialogStyle = TagOf(CmbStyle);
            c.Language = TagOf(CmbLanguage);
            c.LogsInIntuneFolder = ChkImeLogs.IsChecked == true;
            c.DeployMode = TagOf(CmbMode);
            c.DeferTimes = int.TryParse(TxtDefer.Text, out var d) ? Math.Clamp(d, 1, 99) : 3;
            c.CloseAppsCountdown = int.TryParse(TxtCountdown.Text, out var cd) ? Math.Clamp(cd, 0, 3000) : 300;
            c.AllowDefer = ChkDefer.IsChecked == true;
            c.BlockExecution = ChkBlock.IsChecked == true;
            c.CheckDiskSpace = ChkDisk.IsChecked == true;
            c.RemovePreviousVersions = ChkRemovePrev.IsChecked == true;
            c.WelcomeMessage = TxtWelcome.Text.Trim();
            c.CompletionMessage = TxtCompletion.Text.Trim();
            c.PreferredArch = TagOf(CmbArch);
            c.PreferredLocale = TagOf(CmbLocale);
            c.OutputFolder = string.IsNullOrWhiteSpace(TxtOutput.Text) ? AppPaths.DefaultOutput : TxtOutput.Text.Trim().Trim('"');
            c.CatalogUrl = string.IsNullOrWhiteSpace(TxtCatalogUrl.Text) ? AppInfo.DefaultCatalogUrl : TxtCatalogUrl.Text.Trim();
            c.CatalogUpdateDays = int.TryParse(TxtCatalogDays.Text, out var days) ? Math.Clamp(days, 1, 90) : 1;
            c.IntuneTenantId = tenant;
            c.IntuneClientId = client;
            ConfigStore.Save();
            int applied = MainWindow.Current.Editor?.ApplyNewDefaults(before, c.NewPackage()) ?? 0;
            Refresh();
            MainWindow.Current.Toast(applied > 0 ? T("Settings_SavedApplied", applied) : T("Settings_Saved"), "ok");
        }

        // ------------------------------------------------------------------ folders

        private void Output_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = T("Settings_Output") };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtOutput.Text = dlg.FolderName;
        }

        private void OpenData_Click(object sender, RoutedEventArgs e) => Ui.OpenPath(AppPaths.Data);
        private void OpenLogs_Click(object sender, RoutedEventArgs e) => Ui.OpenPath(AppPaths.Logs);
        private void OpenTools_Click(object sender, RoutedEventArgs e) => Ui.OpenPath(AppPaths.Tools);

        // ------------------------------------------------------------------ database

        private void ApplyCatalogUrl()
        {
            ConfigStore.Current.CatalogUrl = string.IsNullOrWhiteSpace(TxtCatalogUrl.Text) ? AppInfo.DefaultCatalogUrl : TxtCatalogUrl.Text.Trim();
            ConfigStore.Save();
        }

        private async void CatalogUpdate_Click(object sender, RoutedEventArgs e)
        {
            ApplyCatalogUrl();
            await MainWindow.Current.UpdateCatalogAsync(userRequested: true);
            UpdateInfo();
        }

        private async void CatalogForce_Click(object sender, RoutedEventArgs e)
        {
            ApplyCatalogUrl();
            await MainWindow.Current.UpdateCatalogAsync(userRequested: true, force: true);
            UpdateInfo();
        }

        private async void CatalogImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = T("Filter_Catalog") + "|*.gz" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                await CatalogService.Instance.ImportAsync(dlg.FileName);
                MainWindow.Current.Toast(T("Main_CatalogUpdated", CatalogService.Instance.Apps.Count.ToString("N0", Loc.Instance.Culture)), "ok");
            }
            catch (Exception ex) { MainWindow.Current.Toast(T("Settings_CatalogInvalid", ex.Message), "error"); }
            UpdateInfo();
        }

        // ------------------------------------------------------------------ dependencies

        private async Task RunDependenciesAsync(bool refresh)
        {
            if (_working) return;
            _working = true;
            BtnRepair.IsEnabled = BtnRefreshTools.IsEnabled = false;
            var main = MainWindow.Current;
            main.SetStatus(T("Settings_DependenciesWorking"), true);
            try
            {
                bool ok = await SetupService.RunAsync(_dependencies, refresh, CancellationToken.None);
                main.Toast(ok ? T("Settings_DependenciesOk") : T("Connect_Failed"), ok ? "ok" : "warn");
                if (refresh) await UpdateService.CheckAsync(true, CancellationToken.None);
            }
            catch (Exception ex) { main.Toast(ex.Message, "error"); }
            finally
            {
                _working = false;
                BtnRepair.IsEnabled = BtnRefreshTools.IsEnabled = true;
                main.SetStatus(null);
                main.UpdateToolsStatus();
                main.UpdateCatalogStatus(refreshPage: false);
                UpdateInfo();
            }
        }

        private async void Repair_Click(object sender, RoutedEventArgs e) => await RunDependenciesAsync(refresh: false);
        private async void RefreshTools_Click(object sender, RoutedEventArgs e) => await RunDependenciesAsync(refresh: true);

        /// <summary>Downloads the newest PSAppDeployToolkit 4.x (used by the notice on the home page).</summary>
        public async Task UpdatePsadtAsync()
        {
            var main = MainWindow.Current;
            main.ShowBusy(T("Settings_PsadtUpdating"), T("Settings_PsadtConsulting"), false);
            try
            {
                var progress = new Progress<(long, long?)>(p => main.BusyProgressReport(p.Item1, p.Item2));
                await ToolsService.UpdatePsadtAsync(CancellationToken.None, progress);
                await UpdateService.CheckAsync(true, CancellationToken.None);
                main.Toast(T("Log_PsadtReady", ToolsService.PsadtVersion), "ok");
            }
            catch (Exception ex) { main.HideBusy(); Ui.Warn(ex.Message); }
            finally
            {
                main.HideBusy();
                main.UpdateToolsStatus();
                if (IsLoaded) UpdateInfo();
                main.Home.Refresh();
            }
        }

        // ------------------------------------------------------------------ links

        private void PublishGuide_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.PublishDocsUrl);
        private void Blog_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.BlogUrl);
        private void Repo_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.RepoUrl);
        private void LinkedIn_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.AuthorLinkedIn);
        private void Psadt_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.PsadtUrl);
    }
}
