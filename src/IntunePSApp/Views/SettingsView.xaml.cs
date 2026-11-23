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
            ThemeManager.Changed += SyncThemeCards;
            SecGeneral.IsChecked = true;
        }

        // ------------------------------------------------------------------ sections and theme

        private string _section = "general";

        private void Section_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string key) ShowSectionPanel(key);
        }

        private void ShowSectionPanel(string key)
        {
            if (PanelGeneral == null) return;
            _section = key;
            PanelGeneral.Visibility = key == "general" ? Visibility.Visible : Visibility.Collapsed;
            PanelAppearance.Visibility = key == "appearance" ? Visibility.Visible : Visibility.Collapsed;
            PanelCompany.Visibility = key == "company" ? Visibility.Visible : Visibility.Collapsed;
            PanelDefaults.Visibility = key == "defaults" ? Visibility.Visible : Visibility.Collapsed;
            PanelIntune.Visibility = key == "intune" ? Visibility.Visible : Visibility.Collapsed;
            PanelTools.Visibility = key == "tools" ? Visibility.Visible : Visibility.Collapsed;
            PanelAbout.Visibility = key == "about" ? Visibility.Visible : Visibility.Collapsed;
            PageScroll.ScrollToTop();
            if (key == "company") UpdateBrandPreview();
        }

        /// <summary>Opens a section: general, appearance, company, defaults, intune, tools or about.</summary>
        public void ShowSection(string key)
        {
            var target = key switch
            {
                "appearance" => SecAppearance, "company" => SecCompany, "defaults" => SecDefaults, "intune" => SecIntune,
                "tools" => SecTools, "about" => SecAbout, _ => SecGeneral
            };
            if (target.IsChecked == true) ShowSectionPanel(key); else target.IsChecked = true;
        }

        private bool _syncingTheme;

        private void SyncThemeCards()
        {
            if (ThemeSystem == null) return;
            _syncingTheme = true;
            string setting = ThemeManager.Setting;
            ThemeSystem.IsChecked = setting == ThemeManager.SystemTheme;
            ThemeLight.IsChecked = setting == ThemeManager.LightTheme;
            ThemeDark.IsChecked = setting == ThemeManager.DarkTheme;
            _syncingTheme = false;
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            // Applied at once (no need to save): the cards are the live switch.
            if (_syncingTheme || !(sender is RadioButton rb) || !(rb.Tag is string theme)) return;
            if (theme != ThemeManager.Setting) ThemeManager.Set(theme);
        }

        // ------------------------------------------------------------------ Microsoft Intune connection

        private CancellationTokenSource _intuneCts;

        private static string Glyph(int code) => ((char)code).ToString();

        /// <summary>
        /// The connection card, one state at a time: not connected (connect), a registration without a session (sign in),
        /// connected with an account or with the certificate (test, copy the code, change), something wrong with the
        /// registration or the certificate (connect again). Disconnect is there whenever anything is saved, so a connection
        /// can always be cleared and made again; while a sign-in waits for the browser, only Cancel.
        /// </summary>
        private void UpdateIntuneStatus()
        {
            if (IntuneStatusTitle == null) return;
            bool configured = IntuneConnection.IsConfigured, certificate = IntuneConnection.UsesCertificate, signedIn = IntuneConnection.IsSignedIn;
            bool busy = _intuneCts != null;
            string problem = IntuneConnection.Problem;
            var cert = certificate ? AppCertificate.Describe(IntuneConnection.CertificateThumbprint) : null;
            string Date(DateTime d) => d.ToString("d", J365.IntunePSApp.Localization.Loc.Instance.Culture);
            bool broken = configured && problem.Length > 0;
            bool certBroken = configured && certificate && !signedIn;   // expired, removed, its key gone
            bool renew = signedIn && cert != null && cert.ExpiresSoon;
            // Connected, but Intune refuses: the tenant has no Intune (known for certain), or the last request was refused.
            bool noPlan = signedIn && !broken && IntuneSubscription.Blocks;
            bool refused = signedIn && !broken && !noPlan && IntuneConnection.Refused;
            bool ok = signedIn && !broken && !renew && !noPlan && !refused;

            string tone = ok ? "Success" : broken || certBroken ? "Danger" : configured ? "Warning" : "Brand";
            IntuneStatusBox.SetResourceReference(Border.BackgroundProperty, tone + "Soft");
            IntuneStatusIcon.SetResourceReference(TextBlock.ForegroundProperty, tone == "Brand" ? "BrandText" : tone);
            IntuneStatusIcon.Text = ok ? Glyph(0xE73E) : broken || certBroken || noPlan || refused ? Glyph(0xE7BA) : configured ? Glyph(0xE72E) : Glyph(0xE753);
            if (broken)
            {
                IntuneStatusTitle.Text = T("Settings_IntuneProblemTitle");
                IntuneStatusText.Text = problem + Environment.NewLine + T("Settings_IntuneProblemHint");
            }
            else if (certBroken)
            {
                IntuneStatusTitle.Text = T("Publish_CertBroken");
                IntuneStatusText.Text = cert != null && cert.Expired ? T("Settings_IntuneCertExpired", Date(cert.NotAfter)) : T("Auth_ErrCertMissing");
            }
            else if (noPlan)
            {
                // The connection itself works: an administrator activates Intune in the tenant, nothing changes here.
                IntuneStatusTitle.Text = T("Settings_IntuneNoPlanTitle");
                string found = IntuneSubscription.FoundLine;
                IntuneStatusText.Text = IntuneDiagnosis.Causes(null, certificate)[0] + (found.Length > 0 ? Environment.NewLine + found : "")
                    + Environment.NewLine + T("Settings_IntuneNoPlanHint", IntuneConnection.Account);
            }
            else if (refused)
            {
                IntuneStatusTitle.Text = T("Settings_IntuneRefusedTitle");
                IntuneStatusText.Text = IntuneDiagnosis.Explain(IntuneConnection.LastToken, certificate);
            }
            else if (signedIn && cert != null)
            {
                IntuneStatusTitle.Text = T("Publish_ConnectedCert");
                IntuneStatusText.Text = T("Settings_IntuneCertText", IntuneConnection.RegistrationName, IntuneConnection.ClientId, cert.Thumbprint, Date(cert.NotAfter),
                        T(cert.Hardware ? "Settings_IntuneKeyTpm" : "Settings_IntuneKeySoftware"))
                    + (renew ? Environment.NewLine + T("Settings_IntuneCertExpiring", Date(cert.NotAfter)) : "");
            }
            else if (signedIn)
            {
                IntuneStatusTitle.Text = T("Publish_ConnectedAs", IntuneConnection.Account);
                IntuneStatusText.Text = T("Settings_IntuneConnectedText", IntuneConnection.RegistrationName, IntuneConnection.ClientId, IntuneConnection.TenantLabel);
            }
            else if (configured)
            {
                IntuneStatusTitle.Text = T("Publish_NotSignedIn");
                IntuneStatusText.Text = T("Settings_IntuneConfiguredText", IntuneConnection.ClientId, IntuneConnection.TenantLabel);
            }
            else
            {
                IntuneStatusTitle.Text = T("Publish_NotConnected");
                IntuneStatusText.Text = T("Publish_NotConnectedHint", IntuneConnection.RegistrationName);
            }
            IntuneDetails.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
            IntuneTenantValue.Text = IntuneConnection.TenantLabel;
            IntuneClientValue.Text = IntuneConnection.ClientId;
            IntunePermissionValue.Text = T(certificate ? "Settings_PermApp" : "Settings_PermDelegated", IntuneConnection.IntunePermission);

            // The main action of the state; the other buttons only where they make sense.
            string primary = !configured ? "connect" : broken || certBroken ? "reconnect" : renew ? "renew" : !signedIn ? "signin" : "";
            BtnIntunePrimary.Tag = primary;
            BtnIntunePrimary.Visibility = primary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnIntunePrimaryText.Text = primary switch
            {
                "connect" => T("Settings_IntuneConnect"),
                "reconnect" => T("Settings_IntuneReconnect"),
                "renew" => T("Settings_IntuneRenewCert"),
                _ => T("Publish_SignIn")
            };
            // The subscriptions of the tenant: checked again when known to be missing, checked whenever Intune refused (an
            // earlier answer of Intune does not prove that a trial is still running).
            BtnIntunePlan.Visibility = noPlan || refused ? Visibility.Visible : Visibility.Collapsed;
            BtnIntunePlanText.Text = noPlan ? T("Settings_IntuneCheckPlanAgain") : T("Settings_IntuneCheckPlan");
            BtnIntunePurchase.Visibility = noPlan ? Visibility.Visible : Visibility.Collapsed;
            BtnIntunePurchaseText.Text = T(IntuneSubscription.FixLabelKey);
            BtnIntuneTest.Visibility = signedIn && !broken ? Visibility.Visible : Visibility.Collapsed;
            BtnIntuneSwitchAccount.Visibility = signedIn && !certificate && !broken ? Visibility.Visible : Visibility.Collapsed;
            BtnIntuneCopyCode.Visibility = configured && !broken ? Visibility.Visible : Visibility.Collapsed;
            BtnIntuneChange.Visibility = configured && primary != "reconnect" ? Visibility.Visible : Visibility.Collapsed;
            BtnIntuneDisconnect.Visibility = configured ? Visibility.Visible : Visibility.Collapsed;
            foreach (var b in new Button[] { BtnIntunePrimary, BtnIntunePlan, BtnIntunePurchase, BtnIntuneTest, BtnIntuneSwitchAccount, BtnIntuneCopyCode, BtnIntuneChange, BtnIntuneDisconnect }) b.IsEnabled = !busy;
            BtnIntuneCancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Runs one action of the card (sign-in, test) with Cancel in place of the other buttons.</summary>
        private async Task RunIntuneAsync(string status, Func<CancellationToken, Task> action)
        {
            if (_intuneCts != null) return;
            _intuneCts = new CancellationTokenSource();
            IntuneBusyText.Text = status;
            IntuneBusyText.Visibility = Visibility.Visible;
            UpdateIntuneStatus();
            try { await action(_intuneCts.Token); }
            catch (OperationCanceledException) { }
            finally
            {
                _intuneCts.Dispose();
                _intuneCts = null;
                IntuneBusyText.Visibility = Visibility.Collapsed;
                UpdateIntuneStatus();
            }
        }

        private void IntuneCancel_Click(object sender, RoutedEventArgs e) => _intuneCts?.Cancel();

        private void OpenConnectWindow(string start = null)
        {
            // The connection window: with the certificate (recommended), an account, a code or Azure Cloud Shell, every step shown.
            if (ConnectWindow.Run(Window.GetWindow(this), start)) MainWindow.Current?.Toast(T("Auth_SignedIn", IntuneConnection.Account), "ok");
            UpdateIntuneStatus();
        }

        private async void IntunePrimary_Click(object sender, RoutedEventArgs e)
        {
            switch (BtnIntunePrimary.Tag as string)
            {
                case "signin":
                    await RunIntuneAsync(T("Publish_SigningIn"), ct => IntuneConnectUi.SignInAsync(null, ct));
                    break;
                case "renew":
                    OpenConnectWindow("certificate");
                    break;
                default:
                    OpenConnectWindow();
                    break;
            }
        }

        private void IntuneChange_Click(object sender, RoutedEventArgs e) => OpenConnectWindow();

        private async void IntuneSwitchAccount_Click(object sender, RoutedEventArgs e)
            => await RunIntuneAsync(T("Publish_SigningIn"), ct => IntuneConnectUi.SignInAsync(null, ct));

        private void IntuneCopyCode_Click(object sender, RoutedEventArgs e)
        {
            Ui.CopyText(IntuneConnection.ConnectionCode);
            MainWindow.Current?.Toast(T("Settings_IntuneCodeCopied"), "ok");
        }

        private async void IntuneTest_Click(object sender, RoutedEventArgs e)
        {
            await RunIntuneAsync(T("Settings_IntuneTesting"), async ct =>
            {
                try { Ui.Info(await IntuneConnection.TestAsync(ct)); }
                catch (OperationCanceledException) { throw; }
                // Intune refused (the sign-in worked): why, and what identifies the request for support. The card says it too.
                catch (Exception ex) when (IntuneDiagnosis.IsRefusal(ex)) { Ui.Warn(IntuneConnectUi.RefusalText(ex)); }
                catch (Exception ex) { Ui.Error(T("Auth_Failed", ex.Message)); }
            });
        }

        private async void IntunePlan_Click(object sender, RoutedEventArgs e)
            => await RunIntuneAsync(T("Settings_IntuneCheckingPlan"), ct => IntuneConnectUi.CheckPlanAsync(ct));

        private void IntunePurchase_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(IntuneSubscription.FixUrl);

        private void IntuneDisconnect_Click(object sender, RoutedEventArgs e)
        {
            // Everything saved here goes (the session, the certificate and its key, the registration), so a new connection
            // starts clean; the registration in Entra ID is not changed.
            string question = T("Settings_IntuneDisconnectConfirm") + (IntuneConnection.UsesCertificate ? " " + T("Settings_IntuneDisconnectConfirmCert") : "");
            if (!Ui.Confirm(question, T("Settings_IntuneDisconnect"), T("Common_Cancel"), danger: true)) return;
            IntuneConnection.Disconnect();
            MainWindow.Current?.Toast(T("Settings_IntuneDisconnected"), "info");
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
            var logo = (ImageSource)MainWindow.LoadBitmap(TxtLogo.Text.Trim().Trim('"')) ?? new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app-256.png"));
            // A sample application open on the computer: the dialog the packages show with this branding and these defaults.
            var input = new WelcomePreviewInput
            {
                Mode = "Auto",
                AppName = T("Settings_PreviewApp"),
                Company = company,
                Processes = new List<string> { "contoso" },
                AllowDefer = ChkDefer?.IsChecked == true,
                DeferTimes = int.TryParse(TxtDefer?.Text, out int d) ? d : 3,
                CountdownSeconds = int.TryParse(TxtCountdown?.Text, out int c) ? c : 300,
                CustomMessage = TxtWelcome?.Text.Trim() ?? ""
            };
            var info = WelcomePreview.Build(input, WelcomeMoment.AppOpen, EditorView.DialogStrings(TagOf(CmbLanguage)));
            BrandPreview.Show(info, new DialogPreviewLook { Style = TagOf(CmbStyle) == "Classic" ? "Classic" : "Fluent", Accent = accent, Logo = logo, Company = company });
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
            SelectTag(CmbNotify, c.IntuneNotifications);
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
            _loading = false;
            UpdateInfo();
            UpdateIntuneStatus();
            UpdateBrandPreview();
            SyncThemeCards();
        }

        public void OnLanguageChanged()
        {
            _loading = true;
            RefreshSelection(CmbUiLanguage, CmbStyle, CmbLanguage, CmbMode, CmbNotify, CmbArch);
            _loading = false;
            foreach (var d in _dependencies) d.RefreshTexts();
            UpdateInfo();
            UpdateIntuneStatus();
            UpdateBrandPreview();
        }

        /// <summary>Opens the Microsoft Intune section (from Home and the sidebar).</summary>
        public void ShowIntune() => ShowSection("intune");

        /// <summary>Updates the information blocks (database, versions, installation) without touching the form.</summary>
        public void RefreshInfo() => UpdateInfo();

        private (bool Signed, string Name)? _signature;

        private void UpdateInfo()
        {
            // Read once: whether this executable carries a valid signature, and whose (Settings > About).
            _signature ??= Signature.IsValid(AppPaths.ExePath) ? (true, Signature.Signer(AppPaths.ExePath).Name ?? "?") : (false, "");
            AboutSignatureIcon.Text = _signature.Value.Signed ? "" : "";
            AboutSignature.Text = _signature.Value.Signed ? T("About_SignedBy", _signature.Value.Name) : T("About_Unsigned");
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

            // A check that could not read the latest version says so (it is not "up to date"), with the page of the releases.
            string newApp = UpdateService.NewAppVersion, checkError = UpdateService.LastError;
            UpdateInfoText.Text = newApp != null ? T("Settings_UpdateAvailable", newApp)
                : checkError != null ? T("Settings_UpdateCheckFailed", checkError)
                : ConfigStore.Current.LastUpdateCheck.HasValue ? T("Settings_UpToDate", AppInfo.Version, ConfigStore.Current.LastUpdateCheck.Value.ToString("g", culture)) : "";
            UpdateInfoText.SetResourceReference(TextBlock.ForegroundProperty, checkError != null && newApp == null ? "Warning" : "Muted");
            BtnReleases.Visibility = checkError != null && newApp == null ? Visibility.Visible : Visibility.Collapsed;
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

        private void OpenReleases_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.ReleasesUrl);

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
            // The installed copy closes afterwards: a running build and unsaved changes are settled first.
            if (AppPaths.IsInstalledCopy && !MainWindow.Current.ConfirmClosing()) return;
            bool purge = Ui.Confirm(T("Install_ConfirmPurge"), T("Install_PurgeYes"), T("Install_PurgeNo"));
            try
            {
                InstallService.Uninstall(purge);
                if (AppPaths.IsInstalledCopy)
                {
                    // This is the installed copy: it closes now so that its own folder can be removed.
                    MainWindow.Current.CloseApplication();
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
            c.IntuneNotifications = TagOf(CmbNotify);
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
            // The connection to Intune is not part of these settings: the connection window and Disconnect change it.
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
