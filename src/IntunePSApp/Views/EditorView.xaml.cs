using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// <summary>The package wizard: installer, application, experience, actions, uninstall, detection, branding and build.</summary>
    public partial class EditorView : UserControl, IRefreshable, ILocalized
    {
        private const int ReviewStep = 7;

        private PackageSettings _s;
        private bool _loading;
        private int _step;
        private readonly ObservableCollection<PackageAction> _actions = new ObservableCollection<PackageAction>();
        private readonly ObservableCollection<string> _extraFiles = new ObservableCollection<string>();
        private readonly ObservableCollection<LogEntry> _log = new ObservableCollection<LogEntry>();
        private BuildResult _last;
        private InstallerInfo _info;
        private string _installerIcon;
        private CancellationTokenSource _buildCts;

        public bool IsBuilding { get; private set; }
        public int StepCount => 8;

        public EditorView()
        {
            InitializeComponent();
            ActionsList.ItemsSource = _actions;
            ExtraFilesList.ItemsSource = _extraFiles;
            LogList.ItemsSource = _log;
            _actions.CollectionChanged += (s, e) => ActionsEmpty.Visibility = _actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _extraFiles.CollectionChanged += (s, e) => { if (!_loading) UpdateDependencies(); };
            foreach (var code in PackageBuilder.MinimumOsOptions) CmbMinOs.Items.Add(new ComboBoxItem { Content = PackageBuilder.MinimumOsLabel(code), Tag = code });
            BuildSwatches();
            BuildQuickActions();
            LoadSettings(ConfigStore.Current.NewPackage());
            GoStep(0);
            SizeChanged += (s, e) => UpdatePreviewVisibility();
        }

        public void Refresh()
        {
            UpdateHeader();
            UpdateDependencies();
            UpdateUpdateBanner();
        }

        public void OnLanguageChanged()
        {
            _loading = true;
            SettingsView.RefreshSelection(CmbArch, CmbBehavior, CmbMode, CmbStyle, CmbLanguage);
            _loading = false;
            BuildSwatches();
            BuildQuickActions();
            ActionsList.Items.Refresh();
            UpdateHints();
            UpdatePreview();
            UpdateHeader();
            UpdateDependencies();
            UpdateUpdateBanner();
            if (_info != null && AnalysisCard.Visibility == Visibility.Visible) ShowAnalysis(_info);
            if (_last != null && ResultCard.Visibility == Visibility.Visible) ShowResult(_last);
            if (_step == ReviewStep) BuildSummary();
            SetBuildButton(IsBuilding);
            UpdateExistingFlow();
            RefreshSandboxStatus();
            RefreshPublishStatus();
        }

        // ------------------------------------------------------------------ load / collect

        public void LoadSettings(PackageSettings s)
        {
            _loading = true;
            _s = s;
            try
            {
                ChkIncludeFolder.IsChecked = s.IncludeInstallerFolder;
                TxtName.Text = s.AppName;
                TxtVendor.Text = s.AppVendor;
                TxtVersion.Text = s.AppVersion;
                SelectTag(CmbArch, s.AppArch);
                TxtInstallArgs.Text = s.InstallArgs;
                SelectTag(CmbBehavior, s.InstallBehavior);
                TxtProcesses.Text = string.Join(", ", s.ProcessesToClose);
                TxtDescription.Text = s.Description;
                TxtHomepage.Text = s.Homepage;
                TxtAppIcon.Text = s.AppIconPath;
                SelectTag(CmbMinOs, s.MinimumOs);
                ChkWaitChildren.IsChecked = s.WaitForChildProcesses;
                TxtExtraCodes.Text = s.ExtraSuccessCodes;
                SelectTag(CmbMode, s.DeployMode);
                ChkDefer.IsChecked = s.AllowDefer;
                TxtDeferTimes.Text = s.DeferTimes.ToString();
                TxtCountdown.Text = s.CloseAppsCountdown.ToString();
                ChkBlock.IsChecked = s.BlockExecution;
                ChkDisk.IsChecked = s.CheckDiskSpace;
                TxtWelcome.Text = s.WelcomeMessage;
                TxtProgress.Text = s.ProgressMessage;
                TxtCompletion.Text = s.CompletionMessage;
                ChkRestart.IsChecked = s.RestartPrompt;
                TxtRestart.Text = s.RestartCountdown.ToString();
                _actions.Clear(); foreach (var a in s.Actions) _actions.Add(a);
                _extraFiles.Clear(); foreach (var f in s.ExtraFiles) _extraFiles.Add(f);
                ChkRemovePrev.IsChecked = s.RemovePreviousVersions;
                ChkRemovePrevQuick.IsChecked = s.RemovePreviousVersions;
                TxtPattern.Text = s.UninstallPattern;
                TxtUninstallArgs.Text = s.UninstallExeArgs;
                RbDetName.IsChecked = s.DetectionMethod == "Registry";
                RbDetCode.IsChecked = s.DetectionMethod == "ProductCode";
                RbDetFile.IsChecked = s.DetectionMethod == "File";
                TxtDetName.Text = s.DetectionNamePattern;
                TxtDetCode.Text = s.MsiProductCode;
                TxtDetFile.Text = s.DetectionFilePath;
                ChkDetVersion.IsChecked = s.DetectionCheckVersion;
                TxtDetVersion.Text = s.DetectionVersion;
                TxtCompany.Text = s.CompanyName;
                TxtLogo.Text = s.LogoPath;
                TxtAccent.Text = s.AccentColor;
                SelectTag(CmbStyle, s.DialogStyle);
                SelectTag(CmbLanguage, s.Language);
                ChkImeLogs.IsChecked = s.LogsInIntuneFolder;
                TxtAuthor.Text = s.Author;
            }
            finally { _loading = false; }
            ActionsEmpty.Visibility = _actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateHints();
            UpdateDetectionPanels();
            RefreshAppIcon();
            UpdateLogo();
            UpdatePreview();
            UpdateHeader();
            UpdateDependencies();
            UpdateUpdateBanner();
            UpdateExistingFlow();
        }

        // ------------------------------------------------------------------ application icon

        private string _appIconKind;

        /// <summary>
        /// Works out the icon the package will get (chosen file, icon of the installer or generated tile) and shows it
        /// in the header and on the Application step.
        /// </summary>
        private void RefreshAppIcon()
        {
            if (AppIconThumb == null) return;
            string installer = _s?.InstallerPath ?? "";
            string custom = TxtAppIcon.Text.Trim().Trim('"');
            _installerIcon = null;
            _appIconKind = null;
            if (installer.Length > 0 || custom.Length > 0)
            {
                try
                {
                    string file = Path.Combine(AppPaths.Temp, "editor-app-icon.png");
                    _appIconKind = ImageTools.SaveAppIcon(custom, installer, TxtName.Text.Trim(), _s?.CatalogId, file);
                    _installerIcon = file;
                }
                catch (Exception ex) { Log.Warn(ex.Message); }
            }
            AppIconThumb.Source = MainWindow.LoadBitmap(_installerIcon);
            AppIconHint.Text = _appIconKind switch
            {
                ImageTools.IconCustom => T("Ed_AppIconCustom"),
                ImageTools.IconInstaller => T("Ed_AppIconInstaller"),
                ImageTools.IconGenerated => T("Ed_AppIconGenerated"),
                _ => T("Ed_AppIconNone")
            };
        }

        private void AppIcon_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            RefreshAppIcon();
            UpdateLogo();
            UpdateHeader();
        }

        private void AppIcon_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = T("Filter_Icons") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe;*.dll" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtAppIcon.Text = dlg.FileName;
        }

        private void AppIconClear_Click(object sender, RoutedEventArgs e) => TxtAppIcon.Text = "";

        public PackageSettings Collect()
        {
            var s = _s?.Clone() ?? new PackageSettings();
            s.SchemaVersion = PackageSettings.CurrentSchema;
            s.IncludeInstallerFolder = ChkIncludeFolder.IsChecked == true;
            if (!string.IsNullOrEmpty(s.InstallerPath)) s.InstallerType = Path.GetExtension(s.InstallerPath).TrimStart('.').ToUpperInvariant();
            s.AppName = TxtName.Text.Trim();
            s.AppVendor = TxtVendor.Text.Trim();
            s.AppVersion = TxtVersion.Text.Trim();
            s.AppArch = TagOf(CmbArch);
            s.InstallArgs = TxtInstallArgs.Text.Trim();
            s.InstallBehavior = TagOf(CmbBehavior);
            s.ProcessesToClose = Names.ParseProcessList(TxtProcesses.Text);
            s.Description = TxtDescription.Text.Trim();
            s.Homepage = TxtHomepage.Text.Trim();
            s.AppIconPath = TxtAppIcon.Text.Trim().Trim('"');
            s.MinimumOs = TagOf(CmbMinOs);
            s.WaitForChildProcesses = ChkWaitChildren.IsChecked == true;
            s.ExtraSuccessCodes = TxtExtraCodes.Text.Trim();
            s.DeployMode = TagOf(CmbMode);
            s.AllowDefer = ChkDefer.IsChecked == true;
            s.DeferTimes = Int(TxtDeferTimes.Text, 3);
            s.CloseAppsCountdown = Int(TxtCountdown.Text, 300);
            s.BlockExecution = ChkBlock.IsChecked == true;
            s.CheckDiskSpace = ChkDisk.IsChecked == true;
            s.WelcomeMessage = TxtWelcome.Text.Trim();
            s.ProgressMessage = TxtProgress.Text.Trim();
            s.CompletionMessage = TxtCompletion.Text.Trim();
            s.RestartPrompt = ChkRestart.IsChecked == true;
            s.RestartCountdown = Int(TxtRestart.Text, 600);
            s.Actions = _actions.ToList();
            s.ExtraFiles = _extraFiles.ToList();
            s.RemovePreviousVersions = ChkRemovePrev.IsChecked == true;
            s.UninstallPattern = TxtPattern.Text.Trim();
            s.UninstallExeArgs = TxtUninstallArgs.Text.Trim();
            s.DetectionMethod = RbDetCode.IsChecked == true ? "ProductCode" : RbDetFile.IsChecked == true ? "File" : "Registry";
            s.DetectionNamePattern = TxtDetName.Text.Trim();
            s.MsiProductCode = TxtDetCode.Text.Trim();
            s.DetectionFilePath = TxtDetFile.Text.Trim();
            s.DetectionCheckVersion = ChkDetVersion.IsChecked == true;
            s.DetectionVersion = TxtDetVersion.Text.Trim();
            s.CompanyName = TxtCompany.Text.Trim();
            s.LogoPath = TxtLogo.Text.Trim().Trim('"');
            s.AccentColor = NormalizeHex(TxtAccent.Text);
            s.DialogStyle = TagOf(CmbStyle);
            s.Language = TagOf(CmbLanguage);
            s.LogsInIntuneFolder = ChkImeLogs.IsChecked == true;
            s.Author = TxtAuthor.Text.Trim();
            if (string.IsNullOrWhiteSpace(s.OutputRoot)) s.OutputRoot = ConfigStore.Current.OutputFolder;
            return s;
        }

        private static string NormalizeHex(string t)
        {
            t = (t ?? "").Trim();
            if (t.Length == 6 && !t.StartsWith("#")) t = "#" + t;
            return t;
        }

        private static int Int(string t, int def) => int.TryParse((t ?? "").Trim(), out int n) ? n : def;

        private static void SelectTag(ComboBox c, string tag)
        {
            foreach (ComboBoxItem it in c.Items) if (string.Equals(it.Tag as string ?? "", tag ?? "", StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = it; return; }
            c.SelectedIndex = 0;
        }

        private static string TagOf(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        // ------------------------------------------------------------------ installer

        public void BrowseInstaller()
        {
            var dlg = new OpenFileDialog { Title = T("Ed_SelectInstaller"), Filter = T("Filter_Installers") + "|*.msi;*.exe|" + T("Filter_All") + "|*.*" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) _ = AnalyzeFileAsync(dlg.FileName);
        }

        /// <summary>
        /// Analyses an installer and fills the wizard. When it is a new version of the application already in the
        /// editor, the customisations can be kept (always kept with <paramref name="keepCustomisations"/>).
        /// </summary>
        public async Task AnalyzeFileAsync(string path, CatalogApp app = null, CatalogInstaller inst = null, bool keepCustomisations = false)
        {
            var main = MainWindow.Current;
            main.SetStatus(T("Ed_Analyzing"), true);
            main.ShowBusy(T("Ed_AnalyzingTitle"), Path.GetFileName(path), false);
            try
            {
                var info = await Task.Run(() => InstallerAnalyzer.Analyze(path));
                if (app != null)
                {
                    // The row chosen in the catalog decides scope and architecture, even when the file was also recognised
                    // by its hash: manifests often list one file under several rows (x64 and ARM64, user and machine).
                    bool byHash = info.CatalogMatch == "hash";
                    if (!byHash || inst != null) CatalogService.Apply(info, app, inst, "download");
                    if (byHash) info.CatalogMatch = "hash";
                    else info.Notes.Insert(0, T("Analysis_CatalogApplied", app.Name, app.Id));
                }
                main.HideBusy();

                var cur = Collect();
                bool hadInstaller = !string.IsNullOrEmpty(cur.InstallerPath);
                bool keep = keepCustomisations;
                if (!keep && hadInstaller && IsSameApplication(cur, info) && !string.Equals(cur.InstallerPath, info.Path, StringComparison.OrdinalIgnoreCase))
                    keep = Ui.Confirm(T("Ed_SameAppPrompt", info.AppName, cur.AppVersion, info.AppVersion), T("Ed_SameAppKeep"), T("Ed_SameAppReset"));

                PackageSettings s;
                if (keep)
                {
                    s = cur;
                    InstallerAnalyzer.ApplyNewVersion(s, info);
                }
                else
                {
                    s = ConfigStore.Current.NewPackage();
                    // Branding edits always follow; actions and extra files only when nothing was packaged before.
                    s.CompanyName = cur.CompanyName; s.LogoPath = cur.LogoPath; s.AccentColor = cur.AccentColor; s.DialogStyle = cur.DialogStyle;
                    s.Language = cur.Language; s.LogsInIntuneFolder = cur.LogsInIntuneFolder; s.Author = cur.Author;
                    if (!hadInstaller) { s.Actions = cur.Actions; s.ExtraFiles = cur.ExtraFiles; }
                    InstallerAnalyzer.ApplyTo(s, info);
                }
                _info = info;
                _last = null;
                ResetSandboxStatus();
                LoadSettings(s);
                ShowAnalysis(info);
                ResultCard.Visibility = Visibility.Collapsed;
                LogCard.Visibility = Visibility.Collapsed;
                UpdateHeader();
                Log.Ok(T("Log_Analyzed", info.FileName, info.AppName, info.AppVersion));
                main.Toast(T("Ed_Identified", $"{info.AppName} {info.AppVersion}".Trim()), "ok");
            }
            catch (Exception ex)
            {
                main.HideBusy();
                Log.Error(T("Log_AnalysisFailed", ex.Message));
                Ui.Warn(T("Ed_AnalysisFailed", ex.Message));
            }
            finally
            {
                main.HideBusy();
                main.SetStatus(null);
            }
        }

        private static bool IsSameApplication(PackageSettings current, InstallerInfo info)
        {
            if (!string.IsNullOrEmpty(current.CatalogId) && info.IsCatalogExact) return string.Equals(current.CatalogId, info.CatalogId, StringComparison.OrdinalIgnoreCase);
            string a = Names.CleanAppName(current.AppName), b = Names.CleanAppName(info.AppName);
            return a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public Task LoadFromCatalogAsync(string path, CatalogApp app, CatalogInstaller inst) => AnalyzeFileAsync(path, app, inst);

        private void ShowAnalysis(InstallerInfo info)
        {
            AnalysisCard.Visibility = Visibility.Visible;
            AnalysisTitle.Text = T("Ed_AnalysisTitle", $"{info.AppName} {info.AppVersion}".Trim());
            AnalysisFile.Text = $"{info.FileName}  ·  {info.SizeMB.ToString("N1", Loc.Instance.Culture)} MB";
            DropText.Text = info.FileName;
            AnalysisChips.Children.Clear();
            void Chip(string text, string bg = "#E8F1FB", string fg = "#0A5CA8")
            {
                if (string.IsNullOrEmpty(text)) return;
                var b = new Border { Style = (Style)FindResource("Chip"), Background = Ui.Hex(bg) };
                b.Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Hex(fg) };
                AnalysisChips.Children.Add(b);
            }
            Chip(info.Type);
            Chip(InstallerAnalyzer.TechnologyLabel(info.Technology));
            Chip(string.IsNullOrEmpty(info.AppArch) ? T("Ed_ChipAnyArch") : info.AppArch);
            if (info.IsCatalogExact) Chip(T("Ed_ChipDatabase", info.CatalogId), "#E8F7EE", "#166534");
            else if (info.CatalogMatch == "name") Chip(T("Ed_ChipSimilar", info.CatalogId), "#FEF4E2", "#92400E");
            bool signed = !string.IsNullOrEmpty(info.Signer);
            Chip(signed ? T("Ed_ChipSigned", info.Signer) : T("Ed_ChipUnsigned"), signed ? "#EEF2F7" : "#FEF4E2", signed ? "#334155" : "#92400E");
            if (!string.IsNullOrEmpty(info.InstallArgs)) Chip(T("Ed_ChipSilent", info.InstallArgs), "#EEF2F7", "#334155");
            AnalysisNotes.ItemsSource = info.Notes.ToList();
        }

        private void Root_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DropRect.Stroke = e.Effects == DragDropEffects.Copy ? (Brush)FindResource("Brand") : Ui.Hex("#94A3B8");
            e.Handled = true;
        }

        private async void Root_Drop(object sender, DragEventArgs e)
        {
            DropRect.Stroke = Ui.Hex("#94A3B8");
            if (IsBuilding) return;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                string f = files[0];
                if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) LoadProject(f);
                else { GoStep(0); await AnalyzeFileAsync(f); }
            }
        }

        private void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => BrowseInstaller();
        private void Browse_Click(object sender, RoutedEventArgs e) => BrowseInstaller();
        private void GoCatalog_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("catalog");

        // ------------------------------------------------------------------ newer version in the database

        private void UpdateUpdateBanner()
        {
            if (UpdateBanner == null || _s == null) return;
            string newer = string.IsNullOrEmpty(_s.InstallerPath) ? null : PackageBuilder.NewerCatalogVersion(_s);
            UpdateBanner.Visibility = newer != null ? Visibility.Visible : Visibility.Collapsed;
            if (newer != null)
                UpdateBannerTitle.Text = T("Ed_UpdateBannerTitle", CatalogService.Instance.ById(_s.CatalogId)?.Name ?? _s.AppName, newer,
                    string.IsNullOrEmpty(_s.CatalogVersion) ? _s.AppVersion : _s.CatalogVersion);
        }

        private async void UpdateInstaller_Click(object sender, RoutedEventArgs e) => await UpdateFromCatalogAsync();

        /// <summary>Opens a project and replaces its installer with the newest version in the database, keeping every customisation.</summary>
        public async Task UpdateProjectAsync(string projectFile)
        {
            if (!LoadProject(projectFile, quiet: true)) return;
            await UpdateFromCatalogAsync();
        }

        private async Task UpdateFromCatalogAsync()
        {
            if (IsBuilding) return;
            var main = MainWindow.Current;
            var app = CatalogService.Instance.ById(_s?.CatalogId);
            if (app == null) { main.Toast(T("Ed_UpdateNoCatalog"), "warn"); return; }
            bool? preferMsi = _s.InstallerType == "MSI" ? true : _s.InstallerType == "EXE" ? false : (bool?)null;
            string arch = string.IsNullOrEmpty(_s.AppArch) ? ConfigStore.Current.PreferredArch : _s.AppArch;
            var inst = CatalogService.PickInstaller(app, arch, ConfigStore.Current.PreferredLocale, preferMsi);
            if (inst == null) { main.Toast(T("Catalog_PickSupported"), "warn"); return; }
            string file = await CatalogView.DownloadWithBusyAsync(app, inst);
            if (file == null) return;
            await AnalyzeFileAsync(file, app, inst, keepCustomisations: true);
            GoStep(ReviewStep);
        }

        // ------------------------------------------------------------------ steps

        public void GoStep(int i)
        {
            _step = Math.Clamp(i, 0, StepCount - 1);
            var rb = Stepper.Children.OfType<RadioButton>().ElementAt(_step);
            if (rb.IsChecked != true) { rb.IsChecked = true; return; }
            for (int k = 0; k < StepCount; k++) ((FrameworkElement)FindName("Step" + k)).Visibility = k == _step ? Visibility.Visible : Visibility.Collapsed;
            UpdatePreviewVisibility();
            BtnBack.IsEnabled = _step > 0;
            BtnNext.IsEnabled = _step < StepCount - 1;
            BodyScroll.ScrollToTop();
            if (_step == ReviewStep) BuildSummary();
            if (_step == 2 || _step == 6) UpdatePreview();
        }

        private void Step_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && int.TryParse(rb.Tag as string, out int i)) { _step = i; GoStep(i); }
        }

        /// <summary>Scrolls the current step to its end. Returns false when the whole step already fits in the window.</summary>
        public bool ScrollToEnd()
        {
            BodyScroll.UpdateLayout();
            if (BodyScroll.ScrollableHeight < 1) return false;
            BodyScroll.ScrollToEnd();
            return true;
        }

        private void Back_Click(object sender, RoutedEventArgs e) => GoStep(_step - 1);
        private void Next_Click(object sender, RoutedEventArgs e) => GoStep(_step + 1);

        /// <summary>Page width (DIPs) under which the dialog preview gives its room to the form.</summary>
        private const double PreviewNeedsWidth = 1000;

        /// <summary>The dialog preview sits beside the Experience and Visual steps, when the window is wide enough for both.</summary>
        private void UpdatePreviewVisibility()
        {
            if (PreviewPanel == null) return;
            bool wanted = _step == 2 || _step == 6;
            bool room = ActualWidth <= 0 || ActualWidth >= PreviewNeedsWidth;
            PreviewPanel.Visibility = wanted && room ? Visibility.Visible : Visibility.Collapsed;
            // Short windows: a lower drop area leaves room for the analysis of the installer.
            DropZone.Height = ActualHeight > 0 && ActualHeight < 620 ? 128 : 186;
        }

        // ------------------------------------------------------------------ versions already installed

        /// <summary>The switch exists on step 1 (where it is seen first) and on step 5 (with its details): both show the same setting.</summary>
        private void RemovePrev_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !(sender is CheckBox changed)) return;
            _loading = true;
            try
            {
                ChkRemovePrev.IsChecked = changed.IsChecked;
                ChkRemovePrevQuick.IsChecked = changed.IsChecked;
            }
            finally { _loading = false; }
            UpdateExistingFlow();
        }

        private void Pattern_Changed(object sender, TextChangedEventArgs e) { if (!_loading) UpdateExistingFlow(); }

        /// <summary>Spells out the order on the device: close the app, uninstall what is there, install the new version.</summary>
        private void UpdateExistingFlow()
        {
            if (ExistingFlow == null) return;
            if (ChkRemovePrev.IsChecked != true) { ExistingFlow.Text = T("Ed_ExistingFlowOff"); return; }
            string pattern = TxtPattern.Text.Trim();
            ExistingFlow.Text = T("Ed_ExistingFlowOn") + (pattern.Length > 0 ? " " + T("Ed_ExistingFlowName", pattern) : "");
        }

        private void UpdateHeader()
        {
            if (HeaderTitle == null) return;
            var name = $"{TxtName.Text} {TxtVersion.Text}".Trim();
            HeaderTitle.Text = string.IsNullOrEmpty(name) ? T("Nav_Editor") : name;
            var file = _s?.InstallerPath;
            HeaderSub.Text = string.IsNullOrEmpty(file)
                ? T("Ed_HeaderHint")
                : Path.GetFileName(file) + (string.IsNullOrEmpty(_s.CatalogId) ? "" : "  ·  " + T("Ed_HeaderCatalog", _s.CatalogId)) + (string.IsNullOrEmpty(TxtVendor.Text) ? "" : $"  ·  {TxtVendor.Text}");
            if (string.IsNullOrEmpty(file)) DropText.Text = T("Ed_DropHere");
            HeaderIcon.Source = MainWindow.LoadBitmap(_installerIcon);
            HeaderInitials.Visibility = HeaderIcon.Source == null ? Visibility.Visible : Visibility.Collapsed;
            HeaderAvatar.Background = HeaderIcon.Source == null ? (Brush)FindResource("Navy") : (Brush)FindResource("Subtle");
            FooterInfo.Text = string.IsNullOrEmpty(file) ? T("Ed_FooterStart") : T("Ed_FooterOutput", string.IsNullOrWhiteSpace(_s.OutputRoot) ? ConfigStore.Current.OutputFolder : _s.OutputRoot);
            SetBuildButton(IsBuilding);
        }

        private void SetBuildButton(bool building) => BtnGenerateText.Text = building ? T("Ed_Generating") : T("Ed_Generate");

        private void UpdateHints()
        {
            string ext = Path.GetExtension(_s?.InstallerPath ?? "").ToLowerInvariant();
            InstallArgsHint.Text = ext switch { ".msi" => T("Ed_ArgsHintMsi"), ".exe" => T("Ed_ArgsHintExe"), _ => T("Ed_ArgsHintNone") };
            ModeHint.Text = TagOf(CmbMode) switch { "Silent" => T("Ed_ModeHintSilent"), "Interactive" => T("Ed_ModeHintInteractive"), _ => T("Ed_ModeHintAuto") };
        }

        private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) { UpdateHints(); UpdatePreview(); } }

        private void Arch_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) UpdateDependencies(); }

        private void Preview_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            // The generated tile carries the initials of the name.
            if (sender == TxtName && _appIconKind == ImageTools.IconGenerated) RefreshAppIcon();
            UpdatePreview();
            if (sender == TxtName || sender == TxtVersion) UpdateHeader();
        }

        private void UpdatePreview()
        {
            if (PrevTitle == null) return;
            string name = $"{TxtName.Text} {TxtVersion.Text}".Trim();
            PrevTitle.Text = string.IsNullOrEmpty(name) ? T("Ed_AppName") : name;
            string company = string.IsNullOrWhiteSpace(TxtCompany.Text) ? AppInfo.PsadtName : TxtCompany.Text.Trim();
            PrevSubtitle.Text = T("Prev_Subtitle", company);
            var procs = Names.ParseProcessList(TxtProcesses.Text);
            if (procs.Count > 0)
            {
                PrevBody.Text = T("Prev_CloseBody");
                PrevAppsBox.Visibility = Visibility.Visible;
                string label = string.IsNullOrWhiteSpace(TxtName.Text) ? T("Prev_AppLabel") : TxtName.Text.Trim();
                PrevApps.Text = string.Join("\n", procs.Select(p => $"•  {label} ({p})"));
                PrevMainText.Text = T("Prev_CloseAndInstall");
            }
            else
            {
                PrevBody.Text = T("Prev_ReadyBody");
                PrevAppsBox.Visibility = Visibility.Collapsed;
                PrevMainText.Text = T("Prev_Install");
            }
            PrevCustom.Text = TxtWelcome.Text.Trim();
            PrevCustom.Visibility = string.IsNullOrWhiteSpace(TxtWelcome.Text) ? Visibility.Collapsed : Visibility.Visible;
            bool defer = ChkDefer.IsChecked == true;
            PrevDefer.Text = T("Prev_DeferText", Int(TxtDeferTimes.Text, 3));
            PrevDefer.Visibility = defer ? Visibility.Visible : Visibility.Collapsed;
            PrevDeferBtn.Visibility = defer ? Visibility.Visible : Visibility.Collapsed;
            var brush = AccentBrush();
            PrevMainBtn.Background = brush;
            PrevOkBtn.Background = brush;
            AccentSwatch.Background = brush;
            string done = TxtCompletion.Text.Trim();
            PrevDone.Text = done;
            var vis = string.IsNullOrEmpty(done) ? Visibility.Collapsed : Visibility.Visible;
            PrevDoneBox.Visibility = vis;
            PrevDoneLabel.Visibility = vis;
            PrevNote.Text = TagOf(CmbMode) switch
            {
                "Silent" => T("Prev_NoteSilent"),
                "Interactive" => T("Prev_NoteInteractive"),
                _ => procs.Count > 0 ? T("Prev_NoteAutoApps") : T("Prev_NoteAuto")
            };
        }

        private Brush AccentBrush()
        {
            string h = NormalizeHex(TxtAccent.Text);
            try { if (Regex.IsMatch(h, "^#[0-9A-Fa-f]{6}$")) return Ui.Hex(h); } catch { }
            return (Brush)FindResource("Brand");
        }

        private void UpdateLogo()
        {
            var img = MainWindow.LoadBitmap(TxtLogo.Text.Trim().Trim('"'));
            LogoThumb.Source = img;
            // No company logo: the dialogs show the application's own icon (a generated tile is not used there).
            if (img == null && _appIconKind != null && _appIconKind != ImageTools.IconGenerated) img = MainWindow.LoadBitmap(_installerIcon);
            if (img == null)
            {
                var tpl = ToolsService.LocalPsadt();
                if (tpl != null) img = MainWindow.LoadBitmap(Path.Combine(tpl, "Assets", "AppIcon.png"));
            }
            PrevLogo.Source = img ?? new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app-256.png"));
        }

        private void Logo_Changed(object sender, TextChangedEventArgs e) { if (!_loading) UpdateLogo(); }

        private void Logo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = T("Filter_Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtLogo.Text = dlg.FileName;
        }

        private void LogoClear_Click(object sender, RoutedEventArgs e) => TxtLogo.Text = "";

        private void BuildSwatches()
        {
            Swatches.Children.Clear();
            foreach (var (hex, key) in new[] { ("#0A5CA8", "Color_J365"), ("#0F6CBD", "Color_Microsoft"), ("#0B6E4F", "Color_Green"), ("#C50F1F", "Color_Red"), ("#CA5010", "Color_Orange"), ("#8764B8", "Color_Purple"), ("#038387", "Color_Teal"), ("#1F2937", "Color_Graphite") })
            {
                var b = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 7, 0), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = Ui.Hex(hex), ToolTip = $"{T(key)} ({hex})", Tag = hex
                };
                b.MouseLeftButtonUp += (s, e) => TxtAccent.Text = (string)((Border)s).Tag;
                Swatches.Children.Add(b);
            }
        }

        private void SaveCompany_Click(object sender, RoutedEventArgs e)
        {
            var s = Collect();
            var c = ConfigStore.Current;
            c.CompanyName = s.CompanyName;
            c.AccentColor = s.AccentColor;
            c.DialogStyle = s.DialogStyle;
            c.Language = s.Language;
            c.LogsInIntuneFolder = s.LogsInIntuneFolder;
            c.Author = s.Author;
            try
            {
                if (!string.IsNullOrWhiteSpace(s.LogoPath) && File.Exists(s.LogoPath))
                {
                    string dest = Path.Combine(AppPaths.Branding, "company-logo" + Path.GetExtension(s.LogoPath).ToLowerInvariant());
                    if (!string.Equals(Path.GetFullPath(s.LogoPath), dest, StringComparison.OrdinalIgnoreCase)) File.Copy(s.LogoPath, dest, true);
                    c.LogoPath = dest;
                    TxtLogo.Text = dest;
                }
                else c.LogoPath = "";
                ConfigStore.Save();
                MainWindow.Current.Toast(T("Ed_CompanySaved"), "ok");
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        }

        // ------------------------------------------------------------------ processes

        private void PickProcess_Click(object sender, RoutedEventArgs e)
        {
            var w = new ProcessPickerWindow { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true)
            {
                var all = Names.ParseProcessList(TxtProcesses.Text);
                foreach (var p in w.Selected) if (!all.Contains(p, StringComparer.OrdinalIgnoreCase)) all.Add(p);
                TxtProcesses.Text = string.Join(", ", all);
            }
        }

        // ------------------------------------------------------------------ dependencies

        private static bool IsUnder(string file, string folder)
            => Path.GetFullPath(file).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

        private void UpdateDependencies()
        {
            if (DependenciesCard == null) return;
            var deps = CatalogService.Instance.DependenciesOf(_s?.CatalogId, _loading ? _s?.AppArch : TagOf(CmbArch), _s?.InstallerType);
            foreach (var d in deps)
            {
                string folder = Path.Combine(AppPaths.Downloads, Names.Safe(d.Id));
                d.Included = _extraFiles.Any(f => IsUnder(f, folder));
            }
            DependencyList.ItemsSource = deps;
            DependenciesCard.Visibility = deps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void IncludeDependency_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is DependencyInfo d) await IncludeDependencyAsync(d);
        }

        /// <summary>Includes every dependency that can be downloaded from the database. Returns how many were added.</summary>
        public async Task<int> IncludeDependenciesAsync()
        {
            int added = 0;
            foreach (var d in (DependencyList.ItemsSource as IEnumerable<DependencyInfo> ?? new List<DependencyInfo>()).Where(x => x.CanInclude).ToList())
                if (await IncludeDependencyAsync(d)) added++;
            return added;
        }

        /// <summary>Downloads the dependency, ships it with the package and installs it before the application.</summary>
        private async Task<bool> IncludeDependencyAsync(DependencyInfo d)
        {
            if (d?.App == null || IsBuilding) return false;
            var main = MainWindow.Current;
            string arch = string.IsNullOrEmpty(TagOf(CmbArch)) ? ConfigStore.Current.PreferredArch : TagOf(CmbArch);
            var inst = CatalogService.PickInstaller(d.App, arch, ConfigStore.Current.PreferredLocale);
            if (inst == null) { main.Toast(T("Ed_DependencyNoInstaller", d.Title), "warn"); return false; }
            string file = await CatalogView.DownloadWithBusyAsync(d.App, inst);
            if (file == null) return false;

            if (!_extraFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) _extraFiles.Add(file);
            var codes = new List<string> { "0", "1638", "3010" };
            foreach (var c in inst.SuccessCodes ?? new List<long>()) if (!codes.Contains(c.ToString())) codes.Add(c.ToString());
            var action = new PackageAction { Type = "InstallDependency", Phase = "PreInstall", ContinueOnError = false };
            action.Params["Source"] = Path.GetFileName(file);
            action.Params["Arguments"] = CatalogService.SilentArgsFor(inst);
            action.Params["SuccessCodes"] = string.Join(", ", codes);
            _actions.Insert(0, action);
            UpdateDependencies();
            main.Toast(T("Ed_DependencyIncluded", d.Title), "ok");
            return true;
        }

        // ------------------------------------------------------------------ actions

        private void BuildQuickActions()
        {
            QuickActions.Children.Clear();
            foreach (var (type, key) in new[]
            {
                ("RemoveDesktopShortcut", "Quick_RemoveShortcut"),
                ("DisableScheduledTask", "Quick_DisableTask"),
                ("StopService", "Quick_StopService"),
                ("SetRegistry", "Quick_Registry"),
                ("CopyFile", "Quick_CopyFile"),
                ("InstallDependency", "Quick_Dependency"),
                ("RunPowerShell", "Quick_PowerShell")
            })
            {
                var b = new Button { Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6), Tag = type };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = ActionCatalog.Get(type)?.Glyph ?? "", FontFamily = Ui.Font("IconFont"), Foreground = Ui.Brush("Brand"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
                sp.Children.Add(new TextBlock { Text = T(key), FontSize = 12.5 });
                b.Content = sp;
                b.Click += (s, e) => OpenActionEditor(null, (string)((Button)s).Tag);
                QuickActions.Children.Add(b);
            }
        }

        private void OpenActionEditor(PackageAction existing, string type = null)
        {
            var names = _extraFiles.Select(f => Path.GetFileName(f.TrimEnd('\\'))).ToList();
            var w = new ActionEditorWindow(existing, type, TxtName.Text.Trim(), names) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() != true) return;
            if (existing != null)
            {
                int i = _actions.IndexOf(existing);
                if (i >= 0) _actions[i] = w.Result;
            }
            else _actions.Add(w.Result);
        }

        private void AddAction_Click(object sender, RoutedEventArgs e) => OpenActionEditor(null);
        private void ActionEdit_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is PackageAction a) OpenActionEditor(a); }
        private void ActionDelete_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is PackageAction a) _actions.Remove(a); }

        private void ActionUp_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is PackageAction a) { int i = _actions.IndexOf(a); if (i > 0) _actions.Move(i, i - 1); }
        }

        private void ActionDown_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is PackageAction a) { int i = _actions.IndexOf(a); if (i >= 0 && i < _actions.Count - 1) _actions.Move(i, i + 1); }
        }

        private void AddExtraFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true, Title = T("Ed_ExtraFiles") };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true)
                foreach (var f in dlg.FileNames) if (!_extraFiles.Contains(f, StringComparer.OrdinalIgnoreCase)) _extraFiles.Add(f);
        }

        private void AddExtraFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = T("Ed_ExtraFiles") };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true && !_extraFiles.Contains(dlg.FolderName, StringComparer.OrdinalIgnoreCase)) _extraFiles.Add(dlg.FolderName);
        }

        private void RemoveExtra_Click(object sender, RoutedEventArgs e)
        {
            if (ExtraFilesList.SelectedItem is string f) _extraFiles.Remove(f);
        }

        // ------------------------------------------------------------------ uninstall / detection

        private void ShowInstalled_Click(object sender, RoutedEventArgs e)
        {
            string pat = TxtPattern.Text.Trim();
            if (pat.Length == 0) { MainWindow.Current.Toast(T("Ed_PatternNeeded"), "warn"); return; }
            var list = InstallerAnalyzer.FindInstalled(pat);
            InstalledList.ItemsSource = list;
            InstalledList.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            InstalledInfo.Text = list.Count > 0 ? T("Ed_InstalledFound", list.Count, pat) : T("Ed_InstalledNone", pat);
        }

        private void Det_Changed(object sender, RoutedEventArgs e) => UpdateDetectionPanels();

        private void UpdateDetectionPanels()
        {
            if (PnlDetName == null) return;
            PnlDetName.Visibility = RbDetName.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PnlDetCode.Visibility = RbDetCode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PnlDetFile.Visibility = RbDetFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            TxtDetVersion.IsEnabled = ChkDetVersion.IsChecked == true;
        }

        private async void TestDetection_Click(object sender, RoutedEventArgs e)
        {
            var s = Collect();
            (bool Detected, string Output) r;
            try { r = await Task.Run(() => PackageBuilder.TestDetection(s)); }
            catch (Exception ex) { Ui.Error(ex.Message); return; }
            DetResult.Visibility = Visibility.Visible;
            DetResult.Background = r.Detected ? (Brush)FindResource("SuccessSoft") : (Brush)FindResource("WarningSoft");
            DetResultText.Foreground = r.Detected ? (Brush)FindResource("Success") : (Brush)FindResource("Warning");
            DetResultText.Text = r.Detected ? T("Ed_Detected", r.Output) : T("Ed_NotDetected");
        }

        private void ViewDetection_Click(object sender, RoutedEventArgs e)
            => new TextViewerWindow(T("Ed_DetectionTitle"), ScriptGenerator.DetectionScript(Collect())) { Owner = Window.GetWindow(this) }.ShowDialog();

        // ------------------------------------------------------------------ review and build

        private void BuildSummary()
        {
            var s = Collect();
            var cmds = ScriptGenerator.Commands(s);
            string mode = s.DeployMode == "Silent" ? T("Opt_ModeSilent") : s.DeployMode == "Interactive" ? T("Opt_ModeInteractive") : T("Opt_ModeAuto");
            SummaryList.ItemsSource = new List<Tuple<string, string>>
            {
                Tuple.Create(T("Rev_App"), $"{s.AppName} {s.AppVersion}  ·  {s.AppVendor}"),
                Tuple.Create(T("Rev_Installer"), string.IsNullOrEmpty(s.InstallerPath) ? "—" : $"{Path.GetFileName(s.InstallerPath)}  ({s.InstallerType}) {s.InstallArgs}"),
                Tuple.Create(T("Ed_InstallFor"), (s.InstallBehavior == "User" ? T("Rev_V_User") : T("Rev_V_System")) + "  ·  " + PackageBuilder.MinimumOsLabel(s.MinimumOs) + "+"),
                Tuple.Create(T("Rev_Experience"), mode + (s.ProcessesToClose.Count > 0 ? T("Rev_V_Closes", string.Join(", ", s.ProcessesToClose)) : "") + (s.AllowDefer ? T("Rev_V_Defer", s.DeferTimes) : "")),
                Tuple.Create(T("Rev_Previous"), s.RemovePreviousVersions ? T("Rev_V_Removes", s.UninstallPattern) : T("Rev_V_Keeps")),
                Tuple.Create(T("Rev_Actions"), s.Actions.Count == 0 ? T("Rev_V_None") : string.Join("; ", s.Actions.Select(a => a.TypeLabel))),
                Tuple.Create(T("Rev_Detection"), s.DetectionMethod switch { "File" => s.DetectionFilePath, "ProductCode" => s.MsiProductCode, _ => s.DetectionNamePattern } + (s.DetectionCheckVersion ? $"  ≥ {s.DetectionVersion}" : "")),
                Tuple.Create(T("Rev_Branding"), $"{(string.IsNullOrEmpty(s.CompanyName) ? T("Rev_V_NoCompany") : s.CompanyName)} · {s.DialogStyle} · {s.AccentColor}"),
                Tuple.Create(T("Sum_F_InstallCmd"), cmds.Install),
            };
            var v = PackageBuilder.Validate(s);
            var items = v.Errors.Select(x => Tuple.Create("#FDECEC", "✖  " + x, "#991B1B"))
                .Concat(v.Warnings.Select(x => Tuple.Create("#FEF4E2", "⚠  " + x, "#92400E"))).ToList();
            if (items.Count == 0) items.Add(Tuple.Create("#E8F7EE", "✓  " + T("Rev_Ready"), "#166534"));
            ValidationList.ItemsSource = items;
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (IsBuilding) return;
            var s = Collect();
            var v = PackageBuilder.Validate(s);
            if (!v.IsValid)
            {
                GoStep(ReviewStep);
                MainWindow.Current.Toast(T("Ed_FixFirst"), "warn");
                return;
            }
            if (v.Warnings.Count > 0 && !Ui.Confirm(T("Ed_WarningsPrompt", "• " + string.Join("\n• ", v.Warnings)), T("Ed_GenerateAnyway"), T("Common_Cancel"))) return;
            GoStep(ReviewStep);
            await BuildAsync(s);
        }

        public async Task<BuildResult> BuildAsync(PackageSettings s)
        {
            IsBuilding = true;
            BtnGenerate.IsEnabled = BtnGenerate2.IsEnabled = false;
            SetBuildButton(true);
            ResetSandboxStatus();
            ResultCard.Visibility = Visibility.Collapsed;
            LogCard.Visibility = Visibility.Visible;
            LogTitle.Text = T("Ed_GeneratingTitle");
            BuildProgress.Visibility = Visibility.Visible;
            BtnCancelBuild.Visibility = Visibility.Visible;
            _log.Clear();
            _buildCts = new CancellationTokenSource();
            var ct = _buildCts.Token;
            var main = MainWindow.Current;
            main.SetStatus(T("Ed_GeneratingStatus", $"{s.AppName} {s.AppVersion}".Trim()), true);
            void OnLog(LogEntry en) => Dispatcher.BeginInvoke(new Action(() => { _log.Add(en); BodyScroll.ScrollToEnd(); }));
            Log.Entry += OnLog;
            try
            {
                bool native = ChkNative.IsChecked == true;
                var r = await Task.Run(() => PackageBuilder.BuildAsync(s, native, ct));
                _last = r;
                _s = s;
                ShowResult(r);
                LogTitle.Text = T("Ed_Done");
                main.Toast(T("Ed_BuildOk", $"{s.AppName} {s.AppVersion}".Trim()), "ok");
                main.UpdateToolsStatus();
                UpdateUpdateBanner();
                _ = Dispatcher.BeginInvoke(new Action(() => BodyScroll.ScrollToTop()), System.Windows.Threading.DispatcherPriority.ContextIdle);
                return r;
            }
            catch (OperationCanceledException)
            {
                LogTitle.Text = T("Ed_Cancelled");
                main.Toast(T("Ed_Cancelled"), "warn");
                return null;
            }
            catch (Exception ex)
            {
                LogTitle.Text = T("Ed_Failed");
                Log.Error(ex.Message);
                Ui.Error(T("Ed_BuildFailed", ex.Message));
                return null;
            }
            finally
            {
                Log.Entry -= OnLog;
                IsBuilding = false;
                BuildProgress.Visibility = Visibility.Collapsed;
                BtnCancelBuild.Visibility = Visibility.Collapsed;
                BtnGenerate.IsEnabled = BtnGenerate2.IsEnabled = true;
                SetBuildButton(false);
                main.SetStatus(null);
            }
        }

        private void CancelBuild_Click(object sender, RoutedEventArgs e) => _buildCts?.Cancel();

        private void ShowResult(BuildResult r)
        {
            var parts = new List<string> { Path.GetFileName(r.IntuneWinFile), (r.IntuneWinSize / 1048576d).ToString("N1", Loc.Instance.Culture) + " MB" };
            if (!string.IsNullOrEmpty(r.IntuneWinTool)) parts.Add(r.IntuneWinTool);
            if (!string.IsNullOrEmpty(r.PsadtVersion)) parts.Add($"{AppInfo.PsadtName} {r.PsadtVersion}");
            ResultFile.Text = string.Join("  ·  ", parts);
            ResultHash.Text = string.IsNullOrEmpty(r.Sha256) ? "" : "SHA256 " + r.Sha256;
            ResultHash.Visibility = string.IsNullOrEmpty(r.Sha256) ? Visibility.Collapsed : Visibility.Visible;
            ResultInstall.Text = r.InstallCommand;
            ResultUninstall.Text = r.UninstallCommand;
            ResultCard.Visibility = Visibility.Visible;
        }

        private void PreviewScript_Click(object sender, RoutedEventArgs e)
        {
            var tpl = ToolsService.LocalPsadt();
            if (tpl == null) { MainWindow.Current.Toast(T("Ed_PsadtMissing"), "warn"); return; }
            try
            {
                var s = Collect();
                if (string.IsNullOrEmpty(s.InstallerType)) s.InstallerType = "EXE";
                var (text, _) = TextFiles.Read(Path.Combine(tpl, "Invoke-AppDeployToolkit.ps1"));
                string name = string.IsNullOrEmpty(s.InstallerPath) ? "setup.exe" : Path.GetFileName(s.InstallerPath);
                new TextViewerWindow(T("Ed_ScriptPreviewTitle"), ScriptGenerator.DeployScript(text, s, name)) { Owner = Window.GetWindow(this) }.ShowDialog();
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Ui.CopyText((sender as FrameworkElement)?.Tag as string == "install" ? ResultInstall.Text : ResultUninstall.Text);
            MainWindow.Current.Toast(T("Ed_CommandCopied"), "ok");
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_last == null) return;
            if (File.Exists(_last.IntuneWinFile)) Ui.RevealFile(_last.IntuneWinFile); else Ui.OpenPath(_last.PackageFolder);
        }

        private void OpenInstructions_Click(object sender, RoutedEventArgs e)
        {
            if (_last?.SummaryHtml != null && File.Exists(_last.SummaryHtml)) MainWindow.OpenUrl(_last.SummaryHtml);
            else MainWindow.Current.Toast(T("Packages_NoInstructions"), "warn");
        }

        private void TestInstall_Click(object sender, RoutedEventArgs e) => RunTest("Install");
        private void TestUninstall_Click(object sender, RoutedEventArgs e) => RunTest("Uninstall");

        private void RunTest(string type)
        {
            if (_last == null) return;
            if (!Ui.Confirm(type == "Install" ? T("Ed_TestConfirmInstall") : T("Ed_TestConfirmUninstall"), danger: true)) return;
            try
            {
                PackageBuilder.RunLocalTest(_last.PsadtFolder, type);
                MainWindow.Current.Toast(T("Ed_TestStarted"), "info");
            }
            catch { MainWindow.Current.Toast(T("Ed_TestCancelled"), "warn"); }
        }

        /// <summary>Installs the package inside Windows Sandbox (a disposable Windows), without touching this computer.</summary>
        private async void Sandbox_Click(object sender, RoutedEventArgs e)
        {
            if (_last == null) return;
            switch (SandboxService.State)
            {
                case SandboxState.NotSupported:
                    Ui.Info(T("Ed_SandboxUnsupported"));
                    return;
                case SandboxState.NotEnabled:
                    await OfferSandboxFeatureAsync();
                    return;
                case SandboxState.Running:
                    Ui.Info(T("Ed_SandboxRunning"));
                    return;
            }
            string folder = _last.PackageFolder;
            if (!SandboxService.CanTest(folder)) { MainWindow.Current.Toast(T("Ed_OldPackage"), "warn"); return; }
            try
            {
                SandboxService.Launch(SandboxService.Prepare(folder));
                MainWindow.Current.Toast(T("Ed_SandboxStarted"), "info");
                WatchSandbox(folder);
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        }

        /// <summary>The Windows feature is turned off: explains it and offers to turn it on (DISM, elevated).</summary>
        private async Task OfferSandboxFeatureAsync()
        {
            string text = T("Ed_SandboxNotEnabled");
            if (SandboxService.VirtualizationLooksDisabled) text += "\n\n" + T("Ed_SandboxVirtualization");
            if (!Ui.Confirm(text, T("Ed_SandboxEnable"), T("Common_Cancel"))) return;
            var main = MainWindow.Current;
            main.SetStatus(T("Ed_SandboxEnabling"), true);
            BtnSandbox.IsEnabled = false;
            try
            {
                int code = await SandboxService.EnableFeatureAsync();
                if (code == 0 || code == 3010) Ui.Info(T("Ed_SandboxEnabled"));
                else Ui.Warn(T("Ed_SandboxEnableFailed", code));
            }
            catch (System.ComponentModel.Win32Exception) { main.Toast(T("Ed_SandboxEnableCancelled"), "warn"); }
            catch (Exception ex) { Ui.Error(ex.Message); }
            finally
            {
                BtnSandbox.IsEnabled = true;
                main.SetStatus(null);
            }
        }

        // The sandbox writes Sandbox\Results\result.json through a writable mapped folder; this page follows it.
        private System.Windows.Threading.DispatcherTimer _sandboxTimer;
        private string _sandboxFolder;
        private SandboxResult _sandboxResult;
        private bool _sandboxClosed;

        private void WatchSandbox(string packageFolder)
        {
            StopSandboxWatch();
            _sandboxFolder = packageFolder;
            _sandboxResult = null;
            _sandboxClosed = false;
            RefreshSandboxStatus();
            var started = DateTime.UtcNow;
            int seen = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) =>
            {
                var result = SandboxService.ReadResult(packageFolder);
                if (result?.Steps != null && result.Steps.Count > seen)
                {
                    seen = result.Steps.Count;
                    _sandboxResult = result;
                    RefreshSandboxStatus();
                    var last = result.Last;
                    if (last != null) MainWindow.Current.Toast(SandboxSummary(last), last.Passed ? "ok" : "error");
                }
                // The sandbox takes a while to appear as a process; after that, a closed sandbox ends the watch.
                bool closed = (DateTime.UtcNow - started).TotalSeconds > 60 && !SandboxService.IsRunning;
                if (closed || (DateTime.UtcNow - started).TotalHours > 8)
                {
                    timer.Stop();
                    _sandboxClosed = true;
                    RefreshSandboxStatus();
                }
            };
            _sandboxTimer = timer;
            timer.Start();
        }

        private void StopSandboxWatch()
        {
            _sandboxTimer?.Stop();
            _sandboxTimer = null;
        }

        /// <summary>A reopened package shows the result of its last sandbox test, when one was recorded (Sandbox\Results\result.json).</summary>
        private void ShowLastSandboxResult(string packageFolder)
        {
            var result = SandboxService.ReadResult(packageFolder);
            if (result?.Last == null) return;
            _sandboxFolder = packageFolder;
            _sandboxResult = result;
            _sandboxClosed = true;
            RefreshSandboxStatus();
        }

        private void ResetSandboxStatus()
        {
            StopSandboxWatch();
            _sandboxFolder = null;
            _sandboxResult = null;
            RefreshSandboxStatus();
            ResetPublishStatus();
        }

        private static string SandboxSummary(SandboxStep step)
        {
            string name = step.Step == "uninstall" ? T("Ed_SandboxStepUninstall") : T("Ed_SandboxStepInstall");
            string detection = step.Detected ? T("Ed_SandboxDetected") : T("Ed_SandboxNotDetected");
            string code = step.ExitCode?.ToString() ?? "—";
            return step.Passed ? T("Ed_SandboxResultOk", name, code, detection, step.Seconds) : T("Ed_SandboxResultFail", name, code, detection);
        }

        private void RefreshSandboxStatus()
        {
            if (SandboxStatus == null) return;
            if (_sandboxFolder == null) { SandboxStatus.Visibility = Visibility.Collapsed; return; }
            var last = _sandboxResult?.Last;
            SandboxStatus.Visibility = Visibility.Visible;
            BtnSandboxLogs.Visibility = last != null ? Visibility.Visible : Visibility.Collapsed;
            string soft = last == null ? "BrandSoft" : last.Passed ? "SuccessSoft" : "DangerSoft";
            string strong = last == null ? "Brand" : last.Passed ? "Success" : "Danger";
            SandboxStatus.Background = (Brush)FindResource(soft);
            SandboxStatusIcon.Foreground = (Brush)FindResource(strong);
            SandboxStatusText.Text = last != null ? SandboxSummary(last) : _sandboxClosed ? T("Ed_SandboxClosed") : T("Ed_SandboxWaiting");
        }

        private void SandboxLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_sandboxFolder != null) Ui.OpenPath(SandboxService.ResultsDir(_sandboxFolder));
        }

        // Publish-ToIntune.ps1 writes Intune\publish-result.json on every exit path; this page shows it.
        private string _publishFolder;
        private PublishResult _publishResult;
        private bool _publishRunning;

        private async void Publish_Click(object sender, RoutedEventArgs e)
        {
            if (_last == null || _publishRunning) return;
            string intune = _last.IntuneFolder;
            if (string.IsNullOrEmpty(intune) || !File.Exists(Path.Combine(intune, PackageLayout.MetadataFile))) { MainWindow.Current.Toast(T("Ed_OldPackage"), "warn"); return; }
            string script;
            // Packages generated by an older version get the current script (no community module, result file).
            try { script = IntunePublisher.Prepare(intune); }
            catch (Exception ex) { Ui.Error(ex.Message); return; }

            var dialog = new PublishDialog(script, HeaderTitle.Text) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true || dialog.Started == null) return;
            MainWindow.Current.Toast(T("Ed_PublishStarted"), "info");
            _publishFolder = intune;
            _publishResult = null;
            _publishRunning = true;
            RefreshPublishStatus();

            var started = DateTime.UtcNow;
            using (var process = dialog.Started)
            {
                try { await process.WaitForExitAsync(); } catch { }
            }
            _publishRunning = false;
            if (_publishFolder != intune) return; // another package was opened while the window was running
            var result = IntunePublisher.ReadResult(intune);
            // A file older than this run belongs to a previous publication (the window was closed before it wrote anything).
            if (result != null && File.GetLastWriteTimeUtc(IntunePublisher.ResultFile(intune)) < started.AddSeconds(-5)) result = null;
            _publishResult = result;
            RefreshPublishStatus();
            if (result != null) MainWindow.Current.Toast(PublishSummary(result), result.IsPublished ? "ok" : result.Status == "cancelled" ? "warn" : "error");
        }

        /// <summary>A reopened package shows the outcome of its last publication, when one was recorded.</summary>
        private void ShowLastPublishResult(string intuneFolder)
        {
            if (string.IsNullOrEmpty(intuneFolder)) return;
            var result = IntunePublisher.ReadResult(intuneFolder);
            if (result == null || !result.IsPublished) return;
            _publishFolder = intuneFolder;
            _publishResult = result;
            RefreshPublishStatus();
        }

        private void ResetPublishStatus()
        {
            _publishFolder = null;
            _publishResult = null;
            RefreshPublishStatus();
        }

        private static string PublishSummary(PublishResult r)
        {
            if (r.IsPublished)
            {
                string text = r.Action == "updated" ? T("Ed_PublishUpdated", r.DisplayName, r.TenantId) : T("Ed_PublishCreated", r.DisplayName, r.TenantId);
                if (!string.IsNullOrWhiteSpace(r.Assignment)) text += " " + T("Ed_PublishAssigned", r.Assignment);
                return text;
            }
            return r.Status == "cancelled" ? T("Ed_PublishCancelled") : T("Ed_PublishFailed", r.Error);
        }

        private void RefreshPublishStatus()
        {
            if (PublishStatus == null) return;
            bool show = _publishFolder != null && (_publishRunning || _publishResult != null);
            PublishStatus.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            var r = _publishResult;
            bool ok = r != null && r.IsPublished;
            bool failed = r != null && !ok && r.Status != "cancelled";
            PublishStatus.Background = (Brush)FindResource(ok ? "SuccessSoft" : failed ? "DangerSoft" : "BrandSoft");
            PublishStatusIcon.Foreground = (Brush)FindResource(ok ? "Success" : failed ? "Danger" : "Brand");
            PublishStatusText.Text = r == null ? T("Ed_PublishRunning") : PublishSummary(r);
            BtnPublishOpen.Visibility = ok && !string.IsNullOrWhiteSpace(r.PortalUrl) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PublishOpen_Click(object sender, RoutedEventArgs e)
        {
            // Only the Intune admin center is opened from here, whatever the result file says.
            string url = _publishResult?.PortalUrl ?? "";
            if (url.StartsWith("https://intune.microsoft.com/", StringComparison.OrdinalIgnoreCase)) MainWindow.OpenUrl(url);
        }

        // ------------------------------------------------------------------ projects

        public void LoadProject(string path) => LoadProject(path, quiet: false);

        private bool LoadProject(string path, bool quiet)
        {
            if (IsBuilding) return false;
            var main = MainWindow.Current;
            try
            {
                var s = PackageBuilder.LoadProject(path);
                _info = null;
                _installerIcon = null;
                ResetSandboxStatus();
                LoadSettings(s);
                AnalysisCard.Visibility = Visibility.Collapsed;
                LogCard.Visibility = Visibility.Collapsed;
                InstalledList.Visibility = Visibility.Collapsed;
                DetResult.Visibility = Visibility.Collapsed;
                DropText.Text = string.IsNullOrEmpty(s.InstallerPath) ? T("Ed_DropHere") : Path.GetFileName(s.InstallerPath);
                // A project inside a package folder brings the previous result back (commands, tests, publishing).
                _last = PackageBuilder.TryLoadResult(path, s);
                if (_last != null) { ShowResult(_last); ShowLastSandboxResult(_last.PackageFolder); ShowLastPublishResult(_last.IntuneFolder); }
                else ResultCard.Visibility = Visibility.Collapsed;
                main.Navigate("editor");
                GoStep(_last != null ? ReviewStep : 1);
                if (!string.IsNullOrEmpty(s.InstallerPath) && !File.Exists(s.InstallerPath)) main.Toast(T("Ed_ProjectMissingInstaller"), "warn");
                else if (!quiet) main.Toast(T("Ed_ProjectLoaded", $"{s.AppName} {s.AppVersion}".Trim()), "ok");
                return true;
            }
            catch (Exception ex)
            {
                Ui.Warn(T("Ed_ProjectInvalid", ex.Message));
                return false;
            }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = T("Filter_Project") + "|*.json", InitialDirectory = Directory.Exists(ConfigStore.Current.OutputFolder) ? ConfigStore.Current.OutputFolder : null };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) LoadProject(dlg.FileName);
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            var s = Collect();
            var dlg = new SaveFileDialog { Filter = T("Filter_Project") + "|*.json", FileName = "project_" + Names.Safe($"{s.AppName}_{s.AppVersion}") + ".json" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try { Json.Save(dlg.FileName, s); MainWindow.Current.Toast(T("Ed_ProjectSaved"), "ok"); }
            catch (Exception ex) { Ui.Error(ex.Message); }
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            if (IsBuilding) return;
            _last = null;
            _info = null;
            _installerIcon = null;
            ResetSandboxStatus();
            AnalysisCard.Visibility = Visibility.Collapsed;
            ResultCard.Visibility = Visibility.Collapsed;
            LogCard.Visibility = Visibility.Collapsed;
            InstalledList.Visibility = Visibility.Collapsed;
            DetResult.Visibility = Visibility.Collapsed;
            LoadSettings(ConfigStore.Current.NewPackage());
            GoStep(0);
        }
    }
}
