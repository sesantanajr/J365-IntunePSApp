using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        }

        public void OnLanguageChanged()
        {
            _loading = true;
            RefreshSelection(CmbUiLanguage, CmbStyle, CmbLanguage, CmbMode, CmbArch);
            _loading = false;
            foreach (var d in _dependencies) d.RefreshTexts();
            UpdateInfo();
        }

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

            bool installed = InstallService.IsInstalled;
            bool envData = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("J365_INTUNEPSAPP_DATA"));
            InstallInfo.Text = AppPaths.IsInstalledCopy ? T("Settings_InstallRunning", AppPaths.InstallDir)
                : installed ? T("Settings_InstallOther", InstallService.InstalledVersion ?? "?") : T("Settings_InstallPortable");
            BtnInstall.Visibility = !AppPaths.IsInstalledCopy && !envData ? Visibility.Visible : Visibility.Collapsed;
            BtnInstall.Content = installed ? T("Settings_InstallUpdate") : T("Settings_Install");
            BtnUninstall.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
            ChkDesktopShortcut.IsChecked = InstallService.HasDesktopShortcut;
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
            string v = UpdateService.NewAppVersion;
            if (v != null && Ui.Confirm(T("Settings_UpdatePrompt", AppInfo.Name, v, AppInfo.Version), T("Notice_AppUpdateButton"), T("Common_Close")))
                MainWindow.OpenUrl(AppInfo.ReleasesUrl);
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            if (!Ui.Confirm(T("Settings_InstallConfirm", AppPaths.InstallDir), T("Settings_Install"), T("Common_Cancel"))) return;
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

        private void Logo_Changed(object sender, TextChangedEventArgs e) => LogoThumb.Source = MainWindow.LoadBitmap(TxtLogo.Text);

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
            Refresh();
            MainWindow.Current.Toast(T("Settings_Saved"), "ok");
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
                main.Toast(ok ? T("Settings_DependenciesOk") : T("Setup_Failed"), ok ? "ok" : "warn");
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
