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
        private string _builtSnapshot;
        private string _projectPath;
        private bool _requiresRebuild;
        private readonly System.Windows.Threading.DispatcherTimer _stateTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
        private InstallerInfo _info;
        private string _installerIcon;
        private CancellationTokenSource _buildCts;

        public bool IsBuilding { get; private set; }
        public int StepCount => 8;

        /// <summary>The Intune folder of the package generated last (capture mode), or null.</summary>
        internal string LastIntuneFolder => _last?.IntuneFolder;
        internal bool CurrentBuildMatchesSettings => IsCurrentBuild;

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
            // "Publish to Intune when done" is remembered; the publish card follows the connection.
            ChkPublishAfter.IsChecked = ConfigStore.Current.PublishAfterBuild;
            ChkPublishAfter.Checked += (s, e) => { ConfigStore.Current.PublishAfterBuild = true; ConfigStore.Save(); };
            ChkPublishAfter.Unchecked += (s, e) => { ConfigStore.Current.PublishAfterBuild = false; ConfigStore.Save(); };
            IntuneConnection.Changed += () => Dispatcher.BeginInvoke(new Action(() => { UpdatePublishCallout(); RefreshPublishStatus(); }));
            LoadSettings(ConfigStore.Current.NewPackage());
            GoStep(0);
            SizeChanged += (s, e) => UpdatePreviewVisibility();
            _stateTimer.Tick += (s, e) => { if (IsVisible) UpdateWorkspaceState(); };
            Loaded += (s, e) => { _stateTimer.Start(); UpdateWorkspaceState(); };
            Unloaded += (s, e) => _stateTimer.Stop();
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
            SettingsView.RefreshSelection(CmbArch, CmbBehavior, CmbStyle, CmbLanguage, CmbExisting);
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
            // Texts built in code on the Uninstall and Detection steps follow the language too.
            UpdateDetectionPanels();
            UpdateUninstallByCode();
        }

        // ------------------------------------------------------------------ load / collect

        public void LoadSettings(PackageSettings s)
        {
            if (IsBuilding) return;
            _loading = true;
            _s = s;
            _projectPath = null;
            _requiresRebuild = false;
            // Another package: the preview starts again at its first moment.
            _momentChosen = false;
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
                TxtPrivacy.Text = s.PrivacyUrl;
                TxtDeveloper.Text = s.Developer;
                TxtOwner.Text = s.Owner;
                TxtAppIcon.Text = s.AppIconPath;
                SelectTag(CmbMinOs, s.MinimumOs);
                ChkWaitChildren.IsChecked = s.WaitForChildProcesses;
                TxtExtraCodes.Text = s.ExtraSuccessCodes;
                SelectMode(s.DeployMode);
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
                SelectTag(CmbExisting, ExistingVersions.Of(s));
                ChkInstallOver.IsChecked = s.InstallIfRemovalFails;
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
                ChkDetMark.IsChecked = s.DetectionRequireMark;
                _followedPattern = s.UninstallPattern ?? "";
                _followedVersion = s.AppVersion ?? "";
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
            MarkClean();
        }

        // ------------------------------------------------------------------ unsaved changes

        private string _cleanSnapshot;

        /// <summary>The package as it is on screen, in a form that can be compared.</summary>
        private string Snapshot()
        {
            try
            {
                var s = Collect();
                // The output folder comes from Settings: changing it there is not an edit of this package.
                s.OutputRoot = null;
                return System.Text.Json.JsonSerializer.Serialize(s, Json.Options) + "\n" + TxtCountdown.Text + "|" + TxtRestart.Text;
            }
            catch { return null; }
        }

        /// <summary>Remembers the package as saved, generated or loaded: what changes after this is "unsaved".</summary>
        private void MarkClean() { _cleanSnapshot = Snapshot(); UpdateWorkspaceState(); }

        private bool IsCurrentBuild => !NumericInputErrors().Any() && _last != null && _builtSnapshot != null && _builtSnapshot == PackageRevision.Fingerprint(Collect());

        private void UpdateWorkspaceState()
        {
            if (_s == null || WorkspaceState == null || _loading) return;
            bool stale = _requiresRebuild || (_last != null && !IsCurrentBuild);
            WorkspaceState.Text = IsBuilding ? T("Ed_StateBuilding") : stale ? T("Ed_StateStale")
                : HasUnsavedChanges ? T("Ed_StateModified") : _projectPath == null ? T("Ed_StateDraft") : T("Ed_StateSaved");
            WorkspaceState.SetResourceReference(TextBlock.ForegroundProperty, stale || HasUnsavedChanges ? "Warning" : "Muted");
            WorkspacePath.Text = _projectPath == null ? T("Ed_StateDraftHint") : Path.GetFileName(_projectPath);
            WorkspacePath.ToolTip = _projectPath;
            StaleNotice.Visibility = stale ? Visibility.Visible : Visibility.Collapsed;
            ResultTitle.Text = stale ? T("Ed_PreviousResult") : T("Ed_ResultTitle");
            ResultCard.SetResourceReference(Border.BorderBrushProperty, stale ? "WarningLine" : "SuccessLine");
            ResultIndicator.SetResourceReference(Border.BackgroundProperty, stale ? "WarningSoft" : "SuccessSoft");
            ResultIndicatorIcon.Text = stale ? "\uE7BA" : "\uE73E";
            ResultIndicatorIcon.SetResourceReference(TextBlock.ForegroundProperty, stale ? "Warning" : "Success");
            PublishCallout.IsEnabled = TestActions.IsEnabled = !stale && !IsBuilding;
        }

        private bool RequireCurrentBuild()
        {
            if (_last == null || IsBuilding) return false;
            if (IsCurrentBuild) return true;
            GoStep(ReviewStep);
            UpdateWorkspaceState();
            MainWindow.Current.Toast(T("Ed_StateStaleHint"), "warn");
            return false;
        }

        private void SetEditingEnabled(bool enabled)
        {
            ProjectActions.IsEnabled = enabled;
            for (int i = 0; i < ReviewStep; i++) ((UIElement)FindName("Step" + i)).IsEnabled = enabled;
        }

        /// <summary>True when the open package has changes that were neither saved nor generated.</summary>
        public bool HasUnsavedChanges
        {
            get
            {
                if (_cleanSnapshot == null) return false;
                string now = Snapshot();
                return now != null && now != _cleanSnapshot;
            }
        }

        /// <summary>Asks before the open package is replaced while it has unsaved changes. True when it may be replaced.</summary>
        public bool ConfirmDiscard()
        {
            if (IsBuilding) { MainWindow.Current.Toast(T("Ed_BusyBuilding"), "warn"); return false; }
            if (!HasUnsavedChanges) return true;
            return Ui.Confirm(T("Ed_ConfirmDiscard", $"{TxtName.Text} {TxtVersion.Text}".Trim()), T("Ed_Discard"), T("Common_Cancel"), danger: true);
        }

        // ------------------------------------------------------------------ application icon

        private string _appIconKind;
        private int _iconRequest;

        /// <summary>
        /// Works out the icon the package will get (chosen file, icon of the installer, icon of the copy installed here
        /// or generated tile) and shows it in the header and on the Application step. Reading the program out of a large
        /// .msi takes a few seconds, so it runs in the background; only the latest request is shown.
        /// </summary>
        private async void RefreshAppIcon()
        {
            if (AppIconThumb == null) return;
            string installer = _s?.InstallerPath ?? "";
            string custom = TxtAppIcon.Text.Trim().Trim('"');
            string name = TxtName.Text.Trim(), colorKey = _s?.CatalogId, pattern = TxtPattern.Text.Trim();
            int request = ++_iconRequest;
            string kind = null, file = null;
            if (installer.Length > 0 || custom.Length > 0)
            {
                AppIconHint.Text = T("Ed_AppIconLooking");
                file = Path.Combine(AppPaths.Temp, $"editor-app-icon-{Guid.NewGuid():N}.png");
                try { kind = await Task.Run(() => ImageTools.SaveAppIcon(custom, installer, name, colorKey, file, pattern)); }
                catch (Exception ex) { Log.Warn(ex.Message); file = null; }
            }
            if (request != _iconRequest) { TryDelete(file); return; }   // a newer request replaced this one
            string previous = _installerIcon;
            _installerIcon = kind != null ? file : null;
            _appIconKind = kind;
            if (previous != null && previous != _installerIcon) TryDelete(previous);
            AppIconThumb.Source = MainWindow.LoadBitmap(_installerIcon);
            AppIconHint.Text = _appIconKind switch
            {
                ImageTools.IconCustom => T("Ed_AppIconCustom"),
                ImageTools.IconInstaller => T("Ed_AppIconInstaller"),
                ImageTools.IconInstalled => T("Ed_AppIconInstalled"),
                ImageTools.IconGenerated => T("Ed_AppIconGenerated"),
                _ => T("Ed_AppIconNone")
            };
            UpdateLogo();
            UpdateHeader();
        }

        private static void TryDelete(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            try { File.Delete(file); } catch { }
        }

        private void AppIcon_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            RefreshAppIcon();
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
            s.PrivacyUrl = TxtPrivacy.Text.Trim();
            s.Developer = TxtDeveloper.Text.Trim();
            s.Owner = TxtOwner.Text.Trim();
            s.AppIconPath = TxtAppIcon.Text.Trim().Trim('"');
            s.MinimumOs = TagOf(CmbMinOs);
            s.WaitForChildProcesses = ChkWaitChildren.IsChecked == true;
            s.ExtraSuccessCodes = TxtExtraCodes.Text.Trim();
            s.DeployMode = SelectedMode;
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
            // Installing over keeps "keep a newer version" as loaded: the project does not change by being shown.
            ExistingVersions.Set(s, TagOf(CmbExisting));
            s.InstallIfRemovalFails = ChkInstallOver.IsChecked == true;
            s.UninstallPattern = TxtPattern.Text.Trim();
            s.UninstallExeArgs = TxtUninstallArgs.Text.Trim();
            s.DetectionMethod = RbDetCode.IsChecked == true ? "ProductCode" : RbDetFile.IsChecked == true ? "File" : "Registry";
            s.DetectionNamePattern = TxtDetName.Text.Trim();
            s.MsiProductCode = TxtDetCode.Text.Trim();
            s.DetectionFilePath = TxtDetFile.Text.Trim();
            s.DetectionCheckVersion = ChkDetVersion.IsChecked == true;
            s.DetectionVersion = TxtDetVersion.Text.Trim();
            s.DetectionRequireMark = ChkDetMark.IsChecked == true;
            s.CompanyName = TxtCompany.Text.Trim();
            s.LogoPath = TxtLogo.Text.Trim().Trim('"');
            s.AccentColor = NormalizeHex(TxtAccent.Text);
            s.DialogStyle = TagOf(CmbStyle);
            s.Language = TagOf(CmbLanguage);
            s.LogsInIntuneFolder = ChkImeLogs.IsChecked == true;
            s.Author = TxtAuthor.Text.Trim();
            // Projects keep the folder they were built in: the folder of Settings wins (it may have changed, or the
            // project came from another computer).
            s.OutputRoot = ConfigStore.Current.OutputFolder;
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
            if (IsBuilding) { MainWindow.Current.Toast(T("Ed_BusyBuilding"), "warn"); return; }
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
                bool keep = keepCustomisations, asked = false;
                if (!keep && hadInstaller && IsSameApplication(cur, info) && !string.Equals(cur.InstallerPath, info.Path, StringComparison.OrdinalIgnoreCase))
                {
                    keep = Ui.Confirm(T("Ed_SameAppPrompt", info.AppName, cur.AppVersion, info.AppVersion), T("Ed_SameAppKeep"), T("Ed_SameAppReset"));
                    asked = true;
                }
                // A new package replaces the one on screen: its unsaved changes are not lost without a question.
                if (!keep && !asked && hadInstaller && !ConfirmDiscard()) return;

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
            // Colors are theme keys, so the chips follow a change of theme.
            void Chip(string text, string bg = "BrandSoft", string fg = "BrandText")
            {
                if (string.IsNullOrEmpty(text)) return;
                var b = new Border { Style = (Style)FindResource("Chip") };
                b.SetResourceReference(Border.BackgroundProperty, bg);
                var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold };
                label.SetResourceReference(TextBlock.ForegroundProperty, fg);
                b.Child = label;
                AnalysisChips.Children.Add(b);
            }
            Chip(info.Type);
            Chip(InstallerAnalyzer.TechnologyLabel(info.Technology));
            Chip(string.IsNullOrEmpty(info.AppArch) ? T("Ed_ChipAnyArch") : info.AppArch);
            if (info.IsCatalogExact) Chip(T("Ed_ChipDatabase", info.CatalogId), "SuccessSoft", "SuccessText");
            else if (info.CatalogMatch == "name") Chip(T("Ed_ChipSimilar", info.CatalogId), "WarningSoft", "WarningText");
            bool signed = !string.IsNullOrEmpty(info.Signer);
            Chip(signed ? T("Ed_ChipSigned", info.Signer) : T("Ed_ChipUnsigned"), signed ? "Subtle2" : "WarningSoft", signed ? "Ink2" : "WarningText");
            if (!string.IsNullOrEmpty(info.InstallArgs)) Chip(T("Ed_ChipSilent", info.InstallArgs), "Subtle2", "Ink2");
            AnalysisNotes.ItemsSource = info.Notes.ToList();
        }

        private void Root_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DropRect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, e.Effects == DragDropEffects.Copy ? "Brand" : "Faint");
            e.Handled = true;
        }

        private async void Root_Drop(object sender, DragEventArgs e)
        {
            DropRect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Faint");
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
            // Opening the project replaces the package on screen: its unsaved changes are not lost without a question.
            if (!ConfirmDiscard()) return;
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
            if (_step == 4) UpdateUninstallByCode();
            if (_step == 5) UpdateDetectionPanels();
        }

        /// <summary>A setup program that installs an MSI of a known product code: the uninstall step says how it is removed.</summary>
        private void UpdateUninstallByCode()
        {
            if (UninstallByCode == null || _s == null) return;
            string code = ScriptGenerator.ExeMsiCode(Collect());
            UninstallByCode.Visibility = code != null ? Visibility.Visible : Visibility.Collapsed;
            UninstallByCodeText.Text = code == null ? "" : T("Ed_UninstallByCode", code);
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

        /// <summary>The switch is on step 5 (with its details); step 1 only says what it does and links there.</summary>
        private void Existing_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            UpdateExistingFlow();
        }

        private void GoUninstall_Click(object sender, RoutedEventArgs e) => GoStep(4);

        // The name and the version of the detection are the ones of the Uninstall and Application steps until they are
        // changed on the Detection step: typing them once is enough (both were asked twice before 2.6).
        private string _followedPattern = "", _followedVersion = "";

        private void Pattern_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            string now = TxtPattern.Text.Trim();
            if (TxtDetName.Text.Trim().Length == 0 || string.Equals(TxtDetName.Text.Trim(), _followedPattern, StringComparison.Ordinal)) TxtDetName.Text = now;
            _followedPattern = now;
            UpdateExistingFlow();
        }

        private void FollowVersion()
        {
            string now = TxtVersion.Text.Trim();
            if (TxtDetVersion.Text.Trim().Length == 0 || string.Equals(TxtDetVersion.Text.Trim(), _followedVersion, StringComparison.Ordinal)) TxtDetVersion.Text = now;
            _followedVersion = now;
        }

        /// <summary>Spells out the order on the device: close the app, uninstall what is there, install the new version.</summary>
        private void UpdateExistingFlow()
        {
            if (ExistingFlow == null) return;
            string pattern = TxtPattern.Text.Trim();
            var others = CatalogService.Instance.OthersMatching(pattern, _s?.CatalogId);
            PatternOthers.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PatternOthersText.Text = others.Count > 0 ? T("Ed_PatternOthers", pattern, string.Join(", ", others)) : "";
            // What the choice does, on step 5 and in the summary of step 1. Installing over has nothing to fail.
            string choice = TagOf(CmbExisting);
            ChkInstallOver.IsEnabled = choice != ExistingVersions.InstallOver;
            ExistingHint.Text = choice switch
            {
                ExistingVersions.RemoveAll => T("Ed_ExistingHintRemoveAll"),
                ExistingVersions.KeepNewer => T("Ed_ExistingHintKeepNewer"),
                _ => T("Ed_ExistingFlowOff")
            };
            if (choice == ExistingVersions.InstallOver) { ExistingFlow.Text = T("Ed_ExistingFlowOff"); return; }
            ExistingFlow.Text = T(choice == ExistingVersions.RemoveAll ? "Ed_ExistingFlowOn" : "Ed_ExistingFlowKeepNewer")
                + (pattern.Length > 0 ? " " + T("Ed_ExistingFlowName", pattern) : "");
        }

        private void UpdateHeader()
        {
            if (HeaderTitle == null) return;
            var name = TxtName.Text.Trim();
            HeaderVersion.Text = TxtVersion.Text.Trim();
            VersionBadge.Visibility = string.IsNullOrWhiteSpace(TxtVersion.Text) ? Visibility.Collapsed : Visibility.Visible;
            HeaderTitle.Text = string.IsNullOrEmpty(name) ? T("Nav_Editor") : name;
            var file = _s?.InstallerPath;
            HeaderSub.Text = string.IsNullOrEmpty(file)
                ? T("Ed_HeaderHint")
                : Path.GetFileName(file) + (string.IsNullOrEmpty(_s.CatalogId) ? "" : "  ·  " + T("Ed_HeaderCatalog", _s.CatalogId)) + (string.IsNullOrEmpty(TxtVendor.Text) ? "" : $"  ·  {TxtVendor.Text}");
            if (string.IsNullOrEmpty(file)) DropText.Text = T("Ed_DropHere");
            HeaderIcon.Source = MainWindow.LoadBitmap(_installerIcon);
            HeaderInitials.Visibility = HeaderIcon.Source == null ? Visibility.Visible : Visibility.Collapsed;
            HeaderAvatar.SetResourceReference(Border.BackgroundProperty, HeaderIcon.Source == null ? "Avatar" : "Subtle");
            FooterInfo.Text = string.IsNullOrEmpty(file) ? T("Ed_FooterStart") : T("Ed_FooterOutput", string.IsNullOrWhiteSpace(_s.OutputRoot) ? ConfigStore.Current.OutputFolder : _s.OutputRoot);
            SetBuildButton(IsBuilding);
        }

        private void SetBuildButton(bool building) => BtnGenerateText.Text = building ? T("Ed_Generating") : T("Ed_Generate");

        private void UpdateHints()
        {
            string ext = Path.GetExtension(_s?.InstallerPath ?? "").ToLowerInvariant();
            InstallArgsHint.Text = ext switch { ".msi" => T("Ed_ArgsHintMsi"), ".exe" => T("Ed_ArgsHintExe"), _ => T("Ed_ArgsHintNone") };
            bool silent = SelectedMode == DeployModes.Silent;
            // A silent package shows no dialog: the countdown, the messages and the restart prompt do not apply.
            CountdownRow.IsEnabled = MessagesCard.IsEnabled = RestartCard.IsEnabled = !silent;
            CountdownRow.ToolTip = MessagesCard.ToolTip = RestartCard.ToolTip = silent ? T("Ed_SilentDisabled") : null;
            // Blocking shows a dialog to whoever starts a blocked application (PSAppDeployToolkit does it in every mode).
            ChkBlock.IsEnabled = !silent;
            ChkBlock.ToolTip = silent ? T("Ed_SilentNoBlock") : null;
        }

        /// <summary>The toasts of Intune that go with a mode, as shown in the review and in the batch window.</summary>
        public static string NotifyLabel(string value) => value == "hideAll" ? T("Opt_NotifyHideAll") : T("Opt_NotifyShowAll");

        /// <summary>The way chosen on the Experience step: Silent or Notify.</summary>
        private string SelectedMode => RbModeSilent.IsChecked == true ? DeployModes.Silent : DeployModes.Notify;

        private void SelectMode(string mode)
        {
            bool silent = DeployModes.Normalize(mode) == DeployModes.Silent;
            RbModeSilent.IsChecked = silent;
            RbModeNotify.IsChecked = !silent;
        }

        private void Mode_Changed(object sender, RoutedEventArgs e) { if (!_loading && IsInitialized) { UpdateHints(); UpdatePreview(); } }

        private void Style_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) UpdatePreview(); }

        private void Arch_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) UpdateDependencies(); }

        private void Preview_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            // The generated tile carries the initials of the name.
            if (sender == TxtName && _appIconKind == ImageTools.IconGenerated) RefreshAppIcon();
            if (sender == TxtVersion) FollowVersion();
            UpdatePreview();
            if (sender == TxtName || sender == TxtVersion) UpdateHeader();
        }

        private ImageSource _previewLogo;

        /// <summary>The moment of a required installation the preview shows.</summary>
        private WelcomeMoment _moment = WelcomeMoment.AppOpen;
        /// <summary>The user picked the moment: it stays while the settings still offer it. Otherwise the first one shows.</summary>
        private bool _momentChosen;
        private bool _syncingMoment;

        private static readonly Dictionary<string, PsadtStrings> DialogStringCache = new();

        /// <summary>
        /// The texts of the dialogs from the string table of the toolkit of this computer, in the language of the dialogs
        /// ("Auto": the language of this interface, as an example); null without a toolkit (the preview then uses its own).
        /// </summary>
        public static PsadtStrings DialogStrings(string language)
        {
            string culture = string.IsNullOrEmpty(language) || language == "Auto" ? Loc.Instance.Culture.Name : language;
            string tpl = ToolsService.LocalPsadt();
            if (tpl == null) return null;
            string key = tpl + "|" + culture;
            lock (DialogStringCache)
            {
                if (!DialogStringCache.TryGetValue(key, out var table))
                {
                    // A table that cannot be read leaves the texts of this application in the preview (and is not read again).
                    try { table = PsadtStrings.Load(Path.Combine(tpl, "Strings"), culture); }
                    catch (Exception ex) { Log.Warn(ex.Message); table = null; }
                    DialogStringCache[key] = table;
                }
                return table;
            }
        }

        private void PrevMoment_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || _syncingMoment || (sender as FrameworkElement)?.Tag is not string tag) return;
            if (Enum.TryParse(tag, out WelcomeMoment moment)) { _moment = moment; _momentChosen = true; }
            UpdatePreview();
        }

        /// <summary>Capture mode: the preview of the dialog is on screen.</summary>
        public bool PreviewVisible => PreviewPanel.Visibility == Visibility.Visible;

        /// <summary>Capture mode: the moments the preview offers for this package.</summary>
        public IReadOnlyList<WelcomeMoment> PreviewMoments => WelcomePreview.Moments(PreviewInput());

        /// <summary>Capture mode: shows the dialog at one moment (nothing of the package changes).</summary>
        public void ShowPreviewMoment(WelcomeMoment moment)
        {
            _moment = moment;
            _momentChosen = true;
            UpdatePreview();
        }

        /// <summary>Capture mode: the bar of a build halfway through (nothing is built).</summary>
        public void ShowProgressSample(bool show)
        {
            if (!show)
            {
                LogCard.Visibility = Visibility.Collapsed;
                BuildProgressPanel.Visibility = Visibility.Collapsed;
                BtnCancelBuild.Visibility = Visibility.Collapsed;
                _log.Clear();
                return;
            }
            GoStep(ReviewStep);
            ResultCard.Visibility = Visibility.Collapsed;
            LogCard.Visibility = Visibility.Visible;
            LogTitle.Text = T("Ed_GeneratingTitle");
            BuildProgressPanel.Visibility = Visibility.Visible;
            BtnCancelBuild.Visibility = Visibility.Visible;
            _log.Clear();
            var t = DateTime.Now.AddSeconds(-38);
            foreach (var (level, text, seconds) in new[]
            {
                (LogLevel.Step, T("Log_BuildStart", TxtName.Text.Trim(), TxtVersion.Text.Trim()), 0), (LogLevel.Ok, AppInfo.PsadtName + " 4.1.8", 1),
                (LogLevel.Step, T("Log_CopyingTemplate"), 1), (LogLevel.Step, T("Log_CopyingInstaller"), 2), (LogLevel.Ok, T("Log_ScriptOk", 0), 9),
                (LogLevel.Step, T("Log_ApplyingBranding"), 9), (LogLevel.Step, T("Log_GeneratingDetection"), 10), (LogLevel.Step, T("Log_CreatingIntuneWin"), 12)
            })
                _log.Add(new LogEntry { Time = t.AddSeconds(seconds), Level = level, Text = text });
            ShowBuildProgress(new BuildProgressInfo { Percent = 64, Step = T("Log_CreatingIntuneWin").TrimEnd('.', ' '), Elapsed = TimeSpan.FromSeconds(38), Remaining = TimeSpan.FromSeconds(21) });
            LogCard.BringIntoView();
        }

        private WelcomePreviewInput PreviewInput() => new()
        {
            Mode = SelectedMode,
            AppName = string.IsNullOrWhiteSpace(TxtName.Text) ? T("Ed_AppName") : TxtName.Text.Trim(),
            AppVersion = TxtVersion.Text.Trim(),
            Company = string.IsNullOrWhiteSpace(TxtCompany.Text) ? AppInfo.PsadtName : TxtCompany.Text.Trim(),
            Processes = Names.ParseProcessList(TxtProcesses.Text),
            CountdownSeconds = Int(TxtCountdown.Text, 300),
            CustomMessage = TxtWelcome.Text
        };

        private void UpdatePreview()
        {
            if (PrevDialog == null || TxtName == null) return;
            var brush = AccentBrush();
            bool classic = TagOf(CmbStyle) == "Classic";
            string mode = SelectedMode;
            string language = TagOf(CmbLanguage);
            var input = PreviewInput();
            string company = input.Company;
            // The moments worth showing for these settings: the one the user chose stays while it still exists, otherwise
            // the first (the list of applications may be filled after the preview was first drawn).
            var moments = WelcomePreview.Moments(input);
            if (!_momentChosen || !moments.Contains(_moment)) _moment = moments[0];
            _syncingMoment = true;
            try
            {
                foreach (var (button, moment) in new[] { (PrevMomentOpen, WelcomeMoment.AppOpen), (PrevMomentClosed, WelcomeMoment.AppClosed) })
                {
                    button.Visibility = moments.Contains(moment) ? Visibility.Visible : Visibility.Collapsed;
                    button.IsChecked = moment == _moment;
                }
            }
            finally { _syncingMoment = false; }
            PrevMoments.Visibility = moments.Count > 1 && mode != "Silent" ? Visibility.Visible : Visibility.Collapsed;

            var info = WelcomePreview.Build(input, _moment, DialogStrings(language));
            // The list shows the icon of the running program: the icon of the application when it is known.
            var appIcon = _appIconKind != null && _appIconKind != ImageTools.IconGenerated ? MainWindow.LoadBitmap(_installerIcon) : null;
            PrevDialog.Show(info, new DialogPreviewLook { Style = classic ? "Classic" : "Fluent", Accent = brush, Logo = _previewLogo, AppIcon = appIcon, Company = company });
            // The completion prompt follows the same style: accent button in Fluent, a plain Windows button in Classic.
            PrevOkBtn.Background = classic ? Ui.Hex("#E1E1E1") : brush;
            ((TextBlock)PrevOkBtn.Child).Foreground = classic ? Ui.Hex("#1F1F1F") : Brushes.White;
            PrevOkBtn.CornerRadius = new CornerRadius(classic ? 0 : 5);
            PrevDoneBox.CornerRadius = new CornerRadius(classic ? 0 : 10);
            PrevDoneBox.Background = classic ? Ui.Hex("#F0F0F0") : Brushes.White;
            AccentSwatch.Background = brush;
            string done = TxtCompletion.Text.Trim();
            PrevDone.Text = done;
            var vis = string.IsNullOrEmpty(done) ? Visibility.Collapsed : Visibility.Visible;
            PrevDoneBox.Visibility = vis;
            PrevDoneLabel.Visibility = vis;
            // What happens at this moment (a dialog that does not appear says why in its own card), in a required
            // installation, and in which language.
            var note = new List<string>();
            if (info.Shown) note.Add(info.Note);
            if (mode != "Silent") note.Add(T("Prev_NoteRequired"));
            if (string.IsNullOrEmpty(language) || language == "Auto") note.Add(T("Prev_NoteLanguage", Loc.Instance.Culture.NativeName));
            PrevNote.Text = string.Join(Environment.NewLine + Environment.NewLine, note);
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
            _previewLogo = img ?? new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app-256.png"));
            UpdatePreview();
        }

        private void Logo_Changed(object sender, TextChangedEventArgs e) { if (!_loading) UpdateLogo(); }

        private void Logo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = T("Filter_Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtLogo.Text = dlg.FileName;
        }

        private void LogoClear_Click(object sender, RoutedEventArgs e) => TxtLogo.Text = "";

        /// <summary>Accent colours offered next to the colour field (editor and Settings).</summary>
        public static readonly (string Hex, string Key)[] AccentPalette =
        {
            ("#0A5CA8", "Color_J365"), ("#0F6CBD", "Color_Microsoft"), ("#0B6E4F", "Color_Green"), ("#C50F1F", "Color_Red"),
            ("#CA5010", "Color_Orange"), ("#8764B8", "Color_Purple"), ("#038387", "Color_Teal"), ("#1F2937", "Color_Graphite")
        };

        /// <summary>
        /// New company defaults were saved in Settings: every field of the open package that still had the previous default
        /// takes the new one (what the user changed in this package is kept). Returns how many fields changed.
        /// </summary>
        public int ApplyNewDefaults(PackageSettings before, PackageSettings after)
        {
            if (IsBuilding) return 0;
            var cur = Collect();
            // New defaults are not edits of the package: a package without unsaved changes stays without them.
            bool wasClean = !HasUnsavedChanges;
            int n = 0;
            void Text(Func<PackageSettings, string> get, Action<string> set)
            {
                string b = get(before) ?? "", a = get(after) ?? "", c = get(cur) ?? "";
                if (a != b && c == b) { set(a); n++; }
            }
            void Flag(Func<PackageSettings, bool> get, Action<bool> set)
            {
                if (get(after) != get(before) && get(cur) == get(before)) { set(get(after)); n++; }
            }
            void Number(Func<PackageSettings, int> get, Action<int> set)
            {
                if (get(after) != get(before) && get(cur) == get(before)) { set(get(after)); n++; }
            }
            _loading = true;
            try
            {
                Text(s => s.CompanyName, v => TxtCompany.Text = v);
                Text(s => s.LogoPath, v => TxtLogo.Text = v);
                Text(s => NormalizeHex(s.AccentColor), v => TxtAccent.Text = v);
                Text(s => s.DialogStyle, v => SelectTag(CmbStyle, v));
                Text(s => s.Language, v => SelectTag(CmbLanguage, v));
                Flag(s => s.LogsInIntuneFolder, v => ChkImeLogs.IsChecked = v);
                Text(s => s.Author, v => TxtAuthor.Text = v);
                Text(s => DeployModes.Normalize(s.DeployMode), SelectMode);
                Number(s => s.CloseAppsCountdown, v => TxtCountdown.Text = v.ToString());
                Flag(s => s.BlockExecution, v => ChkBlock.IsChecked = v);
                Flag(s => s.CheckDiskSpace, v => ChkDisk.IsChecked = v);
                Text(s => ExistingVersions.Of(s), v => SelectTag(CmbExisting, v));
                Flag(s => s.InstallIfRemovalFails, v => ChkInstallOver.IsChecked = v);
                Text(s => s.WelcomeMessage, v => TxtWelcome.Text = v);
                Text(s => s.CompletionMessage, v => TxtCompletion.Text = v);
            }
            finally { _loading = false; }
            if (n > 0)
            {
                UpdateHints();
                UpdateLogo();
                UpdatePreview();
                UpdateHeader();
                if (wasClean) MarkClean();
            }
            return n;
        }

        private void BuildSwatches()
        {
            Swatches.Children.Clear();
            foreach (var (hex, key) in AccentPalette)
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
            // Checked first: a refused save leaves the company defaults as they were.
            if (!string.IsNullOrWhiteSpace(s.AccentColor) && !Regex.IsMatch(s.AccentColor.Trim(), "^#?[0-9A-Fa-f]{6}$")) { Ui.Warn(T("Val_AccentColor")); return; }
            if (!string.IsNullOrWhiteSpace(s.LogoPath) && !File.Exists(s.LogoPath)) { Ui.Warn(T("Val_LogoMissing", s.LogoPath)); return; }
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
                // A theme key, so the icons follow a change of theme.
                var icon = new TextBlock { Text = ActionCatalog.Get(type)?.Glyph ?? "", FontFamily = Ui.Font("IconFont"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
                icon.SetResourceReference(TextBlock.ForegroundProperty, "Brand");
                sp.Children.Add(icon);
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
            if (PnlDetName == null || DetMarkHint == null) return;
            PnlDetName.Visibility = RbDetName.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PnlDetCode.Visibility = RbDetCode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PnlDetFile.Visibility = RbDetFile.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            TxtDetVersion.IsEnabled = ChkDetVersion.IsChecked == true;
            var who = new PackageSettings { AppName = TxtName.Text.Trim(), InstallBehavior = TagOf(CmbBehavior), CatalogId = _s?.CatalogId ?? "" };
            DetMarkHint.Text = ChkDetMark.IsChecked == true ? T("Ed_DetMarkHint", ScriptGenerator.MarkDisplayPath(who)) : T("Ed_DetMarkOffHint");
        }

        private async void TestDetection_Click(object sender, RoutedEventArgs e)
        {
            var s = Collect();
            (bool Detected, bool NoMark, string Output) r;
            // PowerShell takes a few seconds: the button waits for it, so repeated clicks do not start runs that overwrite
            // each other's result.
            var button = sender as Button;
            if (button != null) button.IsEnabled = false;
            DetResult.Visibility = Visibility.Visible;
            DetResult.SetResourceReference(Border.BackgroundProperty, "InfoSoft");
            DetResultText.SetResourceReference(TextBlock.ForegroundProperty, "InfoText");
            DetResultText.Text = T("Ed_DetTesting");
            try { r = await Task.Run(() => PackageBuilder.TestDetection(s)); }
            catch (Exception ex) { DetResult.Visibility = Visibility.Collapsed; Ui.Error(ex.Message); return; }
            finally { if (button != null) button.IsEnabled = true; }
            DetResult.Visibility = Visibility.Visible;
            DetResult.SetResourceReference(Border.BackgroundProperty, r.Detected ? "SuccessSoft" : "WarningSoft");
            DetResultText.SetResourceReference(TextBlock.ForegroundProperty, r.Detected ? "SuccessText" : "WarningText");
            DetResultText.Text = r.Detected ? T("Ed_Detected", Regex.Replace(r.Output, @"^Detected:\s*", ""))
                : r.NoMark ? T("Ed_DetectedNoMark", ScriptGenerator.MarkDisplayPath(s))
                : T("Ed_NotDetected");
        }

        private void ViewDetection_Click(object sender, RoutedEventArgs e)
            => new TextViewerWindow(T("Ed_DetectionTitle"), ScriptGenerator.DetectionScript(Collect())) { Owner = Window.GetWindow(this) }.ShowDialog();

        // ------------------------------------------------------------------ review and build

        private IEnumerable<string> NumericInputErrors()
        {

            if (!int.TryParse(TxtCountdown.Text, out _)) yield return T("Val_CloseCountdown");
            if (ChkRestart.IsChecked == true && !int.TryParse(TxtRestart.Text, out _)) yield return T("Val_RestartCountdown");
        }

        private void BuildSummary()
        {
            var s = Collect();
            var cmds = ScriptGenerator.Commands(s);
            string mode = ScriptGenerator.IsSilent(s) ? T("Opt_ModeSilent") : T("Opt_ModeNotify");
            SummaryList.ItemsSource = new List<Tuple<string, string>>
            {
                Tuple.Create(T("Sum_F_Name"), s.AppName),
                Tuple.Create(T("Ed_Version"), s.AppVersion),
                Tuple.Create(T("Ed_Developer"), string.IsNullOrWhiteSpace(s.Developer) ? s.AppVendor : s.Developer),
                Tuple.Create(T("Ed_Owner"), string.IsNullOrWhiteSpace(s.Owner) ? s.CompanyName : s.Owner),
                Tuple.Create(T("Ed_PrivacyUrl"), string.IsNullOrWhiteSpace(s.PrivacyUrl) ? "—" : s.PrivacyUrl),
                Tuple.Create(T("Rev_Installer"), string.IsNullOrEmpty(s.InstallerPath) ? "—" : $"{Path.GetFileName(s.InstallerPath)}  ({s.InstallerType}) {s.InstallArgs}"),
                Tuple.Create(T("Ed_InstallFor"), (s.InstallBehavior == "User" ? T("Rev_V_User") : T("Rev_V_System")) + "  ·  " + PackageBuilder.MinimumOsLabel(s.MinimumOs) + "+"),
                Tuple.Create(T("Rev_Experience"), mode + (s.ProcessesToClose.Count > 0 ? T("Rev_V_Closes", string.Join(", ", s.ProcessesToClose)) : "")),
                Tuple.Create(T("Ed_IntuneNotify"), NotifyLabel(PackageBuilder.IntuneNotifications(s)) + (PackageBuilder.RestartBehavior(s) == "suppress" ? T("Rev_V_NoRestart") : "")),
                Tuple.Create(T("Rev_Previous"), !s.RemovePreviousVersions ? T("Rev_V_Keeps")
                    : T(s.KeepNewerVersion ? "Rev_V_Removes" : "Rev_V_RemovesAll", s.UninstallPattern) + T(s.InstallIfRemovalFails ? "Rev_V_RemovesOrOver" : "Rev_V_RemovesOrStops")),
                Tuple.Create(T("Rev_Actions"), s.Actions.Count == 0 ? T("Rev_V_None") : string.Join("; ", s.Actions.Select(a => a.TypeLabel))),
                Tuple.Create(T("Rev_Detection"), s.DetectionMethod switch { "File" => s.DetectionFilePath, "ProductCode" => s.MsiProductCode, _ => s.DetectionNamePattern } + (s.DetectionCheckVersion ? $"  ≥ {s.DetectionVersion}" : "") + (s.DetectionRequireMark ? T("Rev_V_Mark") : "")),
                Tuple.Create(T("Rev_Branding"), $"{(string.IsNullOrEmpty(s.CompanyName) ? T("Rev_V_NoCompany") : s.CompanyName)} · {s.DialogStyle} · {s.AccentColor}"),
                Tuple.Create(T("Sum_F_InstallCmd"), cmds.Install),
            };
            var v = PackageBuilder.Validate(s);
            v.Errors.AddRange(NumericInputErrors());
            // Item1 is the kind (error, warn, ok): the template picks the theme colors for it.
            var items = v.Errors.Select(x => Tuple.Create("error", "✖  " + x, ""))
                .Concat(v.Warnings.Select(x => Tuple.Create("warn", "⚠  " + x, ""))).ToList();
            if (items.Count == 0) items.Add(Tuple.Create("ok", "✓  " + T("Rev_Ready"), ""));
            ValidationList.ItemsSource = items;
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (IsBuilding) return;
            var s = Collect();
            var v = PackageBuilder.Validate(s);
            v.Errors.AddRange(NumericInputErrors());
            if (!v.IsValid)
            {
                GoStep(ReviewStep);
                MainWindow.Current.Toast(T("Ed_FixFirst"), "warn");
                return;
            }
            if (v.Warnings.Count > 0 && !Ui.Confirm(T("Ed_WarningsPrompt", "• " + string.Join("\n• ", v.Warnings)), T("Ed_GenerateAnyway"), T("Common_Cancel"))) return;
            GoStep(ReviewStep);
            var built = await BuildAsync(s);
            // "Publish to Intune when done": the publication opens with the new package.
            if (built != null && ConfigStore.Current.PublishAfterBuild && !App.HasArg("--snapshot")) Publish_Click(BtnPublish, new RoutedEventArgs());
        }

        public async Task<BuildResult> BuildAsync(PackageSettings s)
        {
            if (IsBuilding) return null;
            s = s.Clone();
            IsBuilding = true;
            SetEditingEnabled(false);
            UpdateWorkspaceState();
            BtnGenerate.IsEnabled = BtnGenerate2.IsEnabled = false;
            SetBuildButton(true);
            ResetSandboxStatus();
            ResultCard.Visibility = Visibility.Collapsed;
            LogCard.Visibility = Visibility.Visible;
            LogTitle.Text = T("Ed_GeneratingTitle");
            BuildProgressPanel.Visibility = Visibility.Visible;
            ShowBuildProgress(new BuildProgressInfo { Step = T("Log_CheckingPsadt") });
            BtnCancelBuild.Visibility = Visibility.Visible;
            _log.Clear();
            _buildCts = new CancellationTokenSource();
            var ct = _buildCts.Token;
            var main = MainWindow.Current;
            string title = $"{s.AppName} {s.AppVersion}".Trim();
            main.SetStatus(T("Ed_GeneratingStatus", title), true);
            main.SetTaskbarProgress(0);
            void OnLog(LogEntry en) => Dispatcher.BeginInvoke(new Action(() => { _log.Add(en); BodyScroll.ScrollToEnd(); }));
            Log.Entry += OnLog;
            // Created here, so its reports arrive on this thread.
            var progress = new Progress<BuildProgressInfo>(p =>
            {
                if (!IsBuilding) return;
                ShowBuildProgress(p);
                main.SetStatus(T("Ed_GeneratingStatusPct", title, Math.Floor(p.Percent).ToString("0", Loc.Instance.Culture)), true);
                main.SetTaskbarProgress(p.Percent / 100);
            });
            try
            {
                bool native = ChkNative.IsChecked == true;
                var r = await Task.Run(() => PackageBuilder.BuildAsync(s, native, ct, progress));
                _last = r;
                _s = s;
                _requiresRebuild = false;
                _projectPath = r.ProjectFile;
                _builtSnapshot = PackageRevision.Fingerprint(s);
                MarkClean();
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
                // The bar, the status and the taskbar go before the message (it waits for the user).
                EndBuildProgress(main);
                Ui.Error(T("Ed_BuildFailed", ex.Message));
                return null;
            }
            finally
            {
                Log.Entry -= OnLog;
                IsBuilding = false;
                SetEditingEnabled(true);
                UpdateWorkspaceState();
                _buildCts?.Dispose();
                _buildCts = null;
                EndBuildProgress(main);
                BtnGenerate.IsEnabled = BtnGenerate2.IsEnabled = true;
                SetBuildButton(false);
            }
        }

        private void EndBuildProgress(MainWindow main)
        {
            BuildProgressPanel.Visibility = Visibility.Collapsed;
            BtnCancelBuild.Visibility = Visibility.Collapsed;
            main.SetStatus(null);
            main.SetTaskbarProgress(null);
        }

        /// <summary>The bar of the build: percentage, current step, time spent and an estimate of the time left.</summary>
        private void ShowBuildProgress(BuildProgressInfo p)
        {
            BuildBar.Value = p.Percent;
            BuildPercent.Text = Math.Floor(p.Percent).ToString("0", Loc.Instance.Culture) + "%";
            BuildStep.Text = p.Step;
            BuildEta.Text = p.Elapsed.TotalSeconds < 1 ? ""
                : p.Remaining is TimeSpan left ? T("Ed_BuildEta", Core.BuildProgress.Duration(p.Elapsed), Core.BuildProgress.Duration(left))
                : T("Ed_BuildElapsed", Core.BuildProgress.Duration(p.Elapsed));
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
            UpdatePublishCallout();
            UpdateWorkspaceState();
        }

        /// <summary>The publish card of the result: the connection it will use, or how to connect.</summary>
        private void UpdatePublishCallout()
        {
            if (PublishCalloutText == null) return;
            bool ready = IntuneConnection.IsSignedIn && !IntuneConnection.NeedsSetup;
            // A tenant known to have no Intune refuses every publication: said before the upload, not after it.
            PublishCalloutText.Text = !ready ? T("Ed_PublishCalloutNotConnected")
                : IntuneSubscription.Blocks ? T("Ed_PublishCalloutNoPlan", IntuneConnection.Account, IntuneConnection.TenantLabel)
                : T("Ed_PublishCalloutConnected", IntuneConnection.Account, IntuneConnection.TenantLabel);
            BtnPublishText.Text = ready ? T("Ed_PublishNow") : T("Ed_ConnectAndPublish");
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
            if (!RequireCurrentBuild()) return;
            if (!Ui.Confirm(type == "Install" ? T("Ed_TestConfirmInstall") : T("Ed_TestConfirmUninstall"), danger: true)) return;
            try
            {
                PackageBuilder.RunLocalTest(_last.PsadtFolder, type);
                MainWindow.Current.Toast(T("Ed_TestStarted"), "info");
            }
            catch (InvalidOperationException ex) { Ui.Warn(ex.Message); }
            catch { MainWindow.Current.Toast(T("Ed_TestCancelled"), "warn"); }
        }

        /// <summary>Installs the package inside Windows Sandbox (a disposable Windows), without touching this computer.</summary>
        private async void Sandbox_Click(object sender, RoutedEventArgs e)
        {
            if (!RequireCurrentBuild()) return;
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
            SandboxStatus.SetResourceReference(Border.BackgroundProperty, soft);
            SandboxStatusIcon.SetResourceReference(TextBlock.ForegroundProperty, strong);
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
            // A publication still running (in its PowerShell window, or here) says so instead of ignoring the click.
            if (_publishRunning) { MainWindow.Current.Toast(T("Ed_PublishRunning"), "warn"); return; }
            if (!RequireCurrentBuild()) return;
            // Nothing set up yet, the registration gone, or a certificate that cannot be used: the connection comes first
            // (the certificate is the recommended way). A user connection without a session signs in again in the dialog.
            if (IntuneConnection.NeedsSetup && !ConnectWindow.Run(Window.GetWindow(this))) return;
            string intune = _last.IntuneFolder;
            if (string.IsNullOrEmpty(intune) || !File.Exists(Path.Combine(intune, PackageLayout.MetadataFile))) { MainWindow.Current.Toast(T("Ed_OldPackage"), "warn"); return; }
            string script;
            // Packages generated by an older version get the current script (no community module, result file).
            try { script = IntunePublisher.Prepare(intune); }
            catch (Exception ex) { Ui.Error(ex.Message); return; }

            var dialog = new PublishDialog(script, HeaderTitle.Text, IntuneDisplayName(intune), Collect().AppName, IntuneMetadata(intune, "appVersion")) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true) return;
            if (dialog.InAppOptions != null) { await PublishInAppAsync(script, intune, dialog.InAppOptions); return; }
            if (dialog.Started == null) return;
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
            NoteIntuneAnswer(result);
            RefreshPublishStatus();
            if (result != null) MainWindow.Current.Toast(Brief(PublishSummary(result)), result.IsPublished ? "ok" : result.Status == "cancelled" ? "warn" : "error");
        }

        /// <summary>A publication that went through, or that Intune refused, says how Intune answers this connection.</summary>
        private static void NoteIntuneAnswer(PublishResult r) => PublishTexts.NoteAnswer(r);

        private static bool IsRefused(PublishResult r) => PublishTexts.IsRefused(r);

        /// <summary>A toast says the outcome; the card below the package keeps the whole text.</summary>
        private static string Brief(string text) => text.Length > 220 ? text.Substring(0, 217).TrimEnd() + "…" : text;

        /// <summary>Name of the app in Intune, as the publishing script will send it (Intune\intune-app.json).</summary>
        private static string IntuneDisplayName(string intuneFolder) => IntuneMetadata(intuneFolder, "displayName");

        /// <summary>A text field of Intune\intune-app.json (displayName, appVersion...), or null.</summary>
        private static string IntuneMetadata(string intuneFolder, string field)
        {
            try { return System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(intuneFolder, PackageLayout.MetadataFile)))?[field]?.GetValue<string>(); }
            catch { return null; }
        }

        /// <summary>
        /// Publishes with the session open in the application: the script runs without a window and its steps and upload
        /// progress are shown here. Cancelling stops it; an app half created is removed by the script itself.
        /// </summary>
        private async Task PublishInAppAsync(string script, string intune, PublishOptions o)
        {
            var main = MainWindow.Current;
            TokenSet session;
            try { session = await IntuneConnection.RenewAsync(CancellationToken.None); }
            catch (Exception ex) { Ui.Error(T("Auth_Failed", ex.Message)); return; }
            // The certificate connects by itself in the script; a user session hands over its refresh token.
            bool certificate = IntuneConnection.UsesCertificate;
            if (session == null || (!certificate && string.IsNullOrEmpty(session.RefreshToken))) { Ui.Warn(T("Auth_ErrNotSignedIn")); return; }
            if (certificate) o.CertificateThumbprint = IntuneConnection.CertificateThumbprint;
            if (await Task.Run(() => PowerShellPolicy.IsRestricted)) { Ui.Warn(T("Err_PowerShellRestricted")); return; }

            _publishFolder = intune;
            _publishResult = null;
            _publishRunning = true;
            RefreshPublishStatus();
            string title = T("Publish_InAppTitle", HeaderTitle.Text);
            var ct = main.ShowBusy(title, T("Publish_StepSignIn"));
            main.SetStatus(title, true);
            var started = DateTime.UtcNow;
            try
            {
                using var p = IntunePublisher.LaunchInApp(script, o, certificate ? null : session.RefreshToken, line => Dispatcher.BeginInvoke(new Action(() => ShowPublishLine(line))));
                // Cancel asks the script to stop at its next safe point (it then removes an app it created, and an
                // existing app is never left half updated). Only a script that does not stop within 90 s is killed.
                string cancelFile = IntunePublisher.CancelFile(script);
                using (ct.Register(() =>
                {
                    try { File.WriteAllText(cancelFile, "cancel"); } catch { }
                    _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => { try { if (!p.HasExited) p.Kill(true); } catch { } });
                    Dispatcher.BeginInvoke(new Action(() => ShowPublishLine("> " + T("Publish_Cancelling"))));
                }))
                    await p.WaitForExitAsync();
                try { if (File.Exists(cancelFile)) File.Delete(cancelFile); } catch { }
            }
            catch (Exception ex)
            {
                main.HideBusy();
                main.SetStatus(null);
                _publishRunning = false;
                RefreshPublishStatus();
                Ui.Error(ex.Message);
                return;
            }
            main.HideBusy();
            main.SetStatus(null);
            _publishRunning = false;
            if (_publishFolder != intune) return;
            var result = IntunePublisher.ReadResult(intune);
            if (result != null && File.GetLastWriteTimeUtc(IntunePublisher.ResultFile(intune)) < started.AddSeconds(-5)) result = null;
            _publishResult = result;
            NoteIntuneAnswer(result);
            RefreshPublishStatus();
            if (result != null) MainWindow.Current.Toast(Brief(PublishSummary(result)), result.IsPublished ? "ok" : result.Status == "cancelled" ? "warn" : "error");
            else if (ct.IsCancellationRequested) MainWindow.Current.Toast(T("Publish_InAppCancelled"), "warn");
        }

        private void ShowPublishLine(string line)
        {
            Log.Info(line);
            var main = MainWindow.Current;
            var pct = Regex.Match(line, @"^\s*(\d{1,3})\s*%\s*$");
            if (pct.Success) { main.BusyPercent(int.Parse(pct.Groups[1].Value)); return; }
            string text = line.Trim();
            if (text.StartsWith("OK", StringComparison.Ordinal)) return;   // the step text stays until the next step
            if (text.StartsWith("> ", StringComparison.Ordinal))
            {
                string step = text.Substring(2);
                main.BusyStatus(PublishStepText(step), indeterminate: !step.StartsWith("Uploading", StringComparison.Ordinal));
                return;
            }
            main.BusyStatus(text, indeterminate: false);
        }

        /// <summary>The steps of the script (written in English) in the language of the interface.</summary>
        private static string PublishStepText(string step) => PublishTexts.StepText(step);

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

        private static string PublishSummary(PublishResult r) => PublishTexts.Summary(r);

        private void RefreshPublishStatus()
        {
            if (PublishStatus == null) return;
            bool show = _publishFolder != null && (_publishRunning || _publishResult != null);
            PublishStatus.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            var r = _publishResult;
            bool ok = r != null && r.IsPublished;
            bool failed = r != null && !ok && r.Status != "cancelled";
            PublishStatus.SetResourceReference(Border.BackgroundProperty, ok ? "SuccessSoft" : failed ? "DangerSoft" : "BrandSoft");
            PublishStatusIcon.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Success" : failed ? "Danger" : "Brand");
            string text = r == null ? T("Ed_PublishRunning") : PublishSummary(r);
            // Intune refused the caller: why (certain when the subscriptions of the tenant are known), what Intune said when
            // it says more, and what identifies the request for support.
            bool refused = IsRefused(r);
            if (refused) text += Environment.NewLine + Environment.NewLine + IntuneConnectUi.RefusalText(r.Error);
            PublishStatusText.Text = text;
            // The button of the card: the app in Intune, or what to do about a refusal (activate Intune, or check whether
            // the tenant has it: an earlier answer of Intune does not prove that a trial is still running).
            string action = ok && !string.IsNullOrWhiteSpace(r.PortalUrl) ? "open"
                : refused && IntuneSubscription.Blocks ? "purchase"
                : refused ? "plan" : "";
            BtnPublishOpen.Tag = action;
            BtnPublishOpen.Content = action switch { "purchase" => T(IntuneSubscription.FixLabelKey), "plan" => T("Settings_IntuneCheckPlan"), _ => T("Ed_PublishOpen") };
            BtnPublishOpen.Visibility = action.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            // A published app: how its installation goes on the devices, read from Intune.
            BtnPublishStatus.Visibility = ok && IntunePublisher.IsGuid(r.AppId) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PublishStatus_Click(object sender, RoutedEventArgs e)
        {
            if (_publishResult?.IsPublished == true) AppStatusWindow.Open(Window.GetWindow(this), _publishResult, HeaderTitle.Text);
        }

        private async void PublishOpen_Click(object sender, RoutedEventArgs e)
        {
            switch (BtnPublishOpen.Tag as string)
            {
                case "purchase":
                    MainWindow.OpenUrl(IntuneSubscription.FixUrl);
                    return;
                case "plan":
                    // An administrator signs in in the browser; the card then says for certain why Intune refused.
                    var main = MainWindow.Current;
                    var ct = main.ShowBusy(T("Settings_IntuneCheckPlan"), T("Settings_IntuneCheckingPlan"));
                    try { await IntuneConnectUi.CheckPlanAsync(ct); }
                    finally
                    {
                        main.HideBusy();
                        RefreshPublishStatus();
                    }
                    return;
            }
            // Only the Intune admin center is opened from here, whatever the result file says.
            string url = _publishResult?.PortalUrl ?? "";
            if (url.StartsWith("https://intune.microsoft.com/", StringComparison.OrdinalIgnoreCase)) MainWindow.OpenUrl(url);
        }

        // ------------------------------------------------------------------ projects

        public void LoadProject(string path) => LoadProject(path, quiet: false);

        private bool LoadProject(string path, bool quiet, bool confirmed = false)
        {
            if (IsBuilding) { MainWindow.Current.Toast(T("Ed_BusyBuilding"), "warn"); return false; }
            // Replacing a package with unsaved changes asks first (New, Open project, a .json dropped, Reopen in Packages).
            if (!quiet && !confirmed && !ConfirmDiscard()) return false;
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
                _projectPath = path;
                _requiresRebuild = !PackageRevision.Matches(Path.GetDirectoryName(path), s);
                _last = PackageBuilder.TryLoadResult(path, s);
                _builtSnapshot = _last == null ? null : PackageRevision.Fingerprint(Collect());
                UpdateWorkspaceState();
                if (_last != null) { ShowResult(_last); ShowLastSandboxResult(_last.PackageFolder); ShowLastPublishResult(_last.IntuneFolder); }
                else ResultCard.Visibility = Visibility.Collapsed;
                main.Navigate("editor");
                GoStep(_last != null || _requiresRebuild ? ReviewStep : 1);
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

        public void ProjectShortcut(System.Windows.Input.Key key)
        {
            if (IsBuilding || !IsEnabled) return;
            if (key == System.Windows.Input.Key.S) SaveProject((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0);
            else if (key == System.Windows.Input.Key.O) OpenProject_Click(this, new RoutedEventArgs());
            else if (key == System.Windows.Input.Key.N) New_Click(this, new RoutedEventArgs());
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = T("Filter_Project") + "|*.json", InitialDirectory = Directory.Exists(ConfigStore.Current.OutputFolder) ? ConfigStore.Current.OutputFolder : null };
            if (!ConfirmDiscard()) return;
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) LoadProject(dlg.FileName, quiet: false, confirmed: true);
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject(false);
        private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveProject(true);

        private void SaveProject(bool saveAs)
        {
            if (IsBuilding) return;
            var errors = NumericInputErrors().ToList();
            if (errors.Count > 0) { Ui.Warn(string.Join("\n", errors)); return; }
            var s = Collect();
            string path = _projectPath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                var dlg = new SaveFileDialog { Filter = T("Filter_Project") + "|*.json", FileName = "project_" + Names.Safe($"{s.AppName}_{s.AppVersion}") + ".json" };
                if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
                path = dlg.FileName;
            }
            try { Json.Save(path, s); _projectPath = path; MarkClean(); MainWindow.Current.Toast(T("Ed_ProjectSaved"), "ok"); }
            catch (Exception ex) { Ui.Error(ex.Message); }
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            if (IsBuilding) return;
            if (!ConfirmDiscard()) return;
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
