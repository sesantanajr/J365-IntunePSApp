using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using J365.IntunePSApp.Core;
using Microsoft.Win32;

namespace J365.IntunePSApp.Views
{
    public partial class EditorView : UserControl, IRefreshable
    {
        private PackageSettings _s;
        private bool _loading;
        private int _step;
        private readonly ObservableCollection<PackageAction> _actions = new ObservableCollection<PackageAction>();
        private readonly ObservableCollection<string> _extraFiles = new ObservableCollection<string>();
        private readonly ObservableCollection<LogEntry> _log = new ObservableCollection<LogEntry>();
        private BuildResult _last;
        private string _installerIcon;

        public bool IsBuilding { get; private set; }
        public int StepCount => 8;

        public EditorView()
        {
            InitializeComponent();
            ActionsList.ItemsSource = _actions;
            ExtraFilesList.ItemsSource = _extraFiles;
            LogList.ItemsSource = _log;
            _actions.CollectionChanged += (s, e) => ActionsEmpty.Visibility = _actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BuildSwatches();
            BuildQuickActions();
            LoadSettings(ConfigStore.Current.NewPackage());
            GoStep(0);
        }

        public void Refresh() => UpdateHeader();

        // ------------------------------------------------------------------ carregar / coletar

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
            UpdateLogo();
            UpdatePreview();
            UpdateHeader();
        }

        public PackageSettings Collect()
        {
            var s = _s?.Clone() ?? new PackageSettings();
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
            s.LogoPath = TxtLogo.Text.Trim();
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

        // ------------------------------------------------------------------ instalador

        public void BrowseInstaller()
        {
            var dlg = new OpenFileDialog { Title = "Selecione o instalador", Filter = "Instaladores (*.msi;*.exe)|*.msi;*.exe|Todos os arquivos (*.*)|*.*" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) _ = AnalyzeFileAsync(dlg.FileName);
        }

        public async Task AnalyzeFileAsync(string path, CatalogApp app = null, CatalogInstaller inst = null)
        {
            var main = MainWindow.Current;
            main.SetStatus("Analisando o instalador...", true);
            main.ShowBusy("Analisando o instalador", Path.GetFileName(path), false);
            try
            {
                var info = await Task.Run(() => InstallerAnalyzer.Analyze(path));
                if (app != null && info.CatalogMatch != "hash")
                {
                    CatalogService.Apply(info, app, inst, "download");
                    info.Notes.Insert(0, $"Dados do catálogo aplicados: {app.Name} [{app.Id}].");
                }
                var s = ConfigStore.Current.NewPackage();
                var cur = Collect();
                s.Actions = cur.Actions; s.ExtraFiles = cur.ExtraFiles;
                s.CompanyName = cur.CompanyName; s.LogoPath = cur.LogoPath; s.AccentColor = cur.AccentColor; s.DialogStyle = cur.DialogStyle;
                s.Language = cur.Language; s.LogsInIntuneFolder = cur.LogsInIntuneFolder; s.Author = cur.Author;
                InstallerAnalyzer.ApplyTo(s, info);
                s.IncludeInstallerFolder = ChkIncludeFolder.IsChecked == true;
                _s = s;
                LoadSettings(s);
                ShowAnalysis(info);
                _installerIcon = null;
                if (info.Type == "EXE")
                {
                    string ico = Path.Combine(AppPaths.Temp, $"icon_{Guid.NewGuid():N}.png");
                    if (ImageTools.SaveExeIcon(path, ico)) _installerIcon = ico;
                }
                ResultCard.Visibility = Visibility.Collapsed;
                UpdateHeader();
                Log.Ok($"Instalador analisado: {info.FileName} → {info.AppName} {info.AppVersion}");
                main.Toast($"{info.AppName} {info.AppVersion} identificado. Revise as etapas e gere o pacote.", "ok");
            }
            catch (Exception ex)
            {
                Log.Error("Falha na análise: " + ex.Message);
                MessageBox.Show(main, "Não foi possível analisar o arquivo:\n\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                main.HideBusy();
                main.SetStatus("Pronto");
            }
        }

        public Task LoadFromCatalogAsync(string path, CatalogApp app, CatalogInstaller inst) => AnalyzeFileAsync(path, app, inst);

        private void ShowAnalysis(InstallerInfo info)
        {
            AnalysisCard.Visibility = Visibility.Visible;
            AnalysisTitle.Text = $"{info.AppName} {info.AppVersion} identificado";
            AnalysisFile.Text = $"{info.FileName}  ·  {info.SizeMB:N1} MB";
            DropText.Text = info.FileName;
            AnalysisChips.Children.Clear();
            void Chip(string text, string bg = "#E8F1FB", string fg = "#0A5CA8")
            {
                var b = new Border { Style = (Style)FindResource("Chip"), Background = (Brush)new BrushConverter().ConvertFromString(bg) };
                b.Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)new BrushConverter().ConvertFromString(fg) };
                AnalysisChips.Children.Add(b);
            }
            Chip(info.Type);
            Chip(info.Technology);
            Chip(string.IsNullOrEmpty(info.AppArch) ? "32/64 bits" : info.AppArch);
            if (info.CatalogMatch == "hash" || info.CatalogMatch == "productcode" || info.CatalogMatch == "download") Chip("✓ Banco de dados: " + info.CatalogId, "#E8F7EE", "#166534");
            else if (info.CatalogMatch == "nome") Chip("≈ Semelhante a " + info.CatalogId, "#FEF4E2", "#92400E");
            Chip(string.IsNullOrEmpty(info.Signer) ? "Sem assinatura digital" : "Assinado: " + info.Signer, string.IsNullOrEmpty(info.Signer) ? "#FEF4E2" : "#EEF2F7", string.IsNullOrEmpty(info.Signer) ? "#92400E" : "#334155");
            if (!string.IsNullOrEmpty(info.InstallArgs)) Chip("Silencioso: " + info.InstallArgs, "#EEF2F7", "#334155");
            AnalysisNotes.ItemsSource = info.Notes.ToList();
        }

        private void Root_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DropRect.Stroke = e.Effects == DragDropEffects.Copy ? (Brush)FindResource("Brand") : (Brush)new BrushConverter().ConvertFromString("#94A3B8");
            e.Handled = true;
        }

        private async void Root_Drop(object sender, DragEventArgs e)
        {
            DropRect.Stroke = (Brush)new BrushConverter().ConvertFromString("#94A3B8");
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

        // ------------------------------------------------------------------ etapas

        public void GoStep(int i)
        {
            _step = Math.Clamp(i, 0, StepCount - 1);
            var rb = Stepper.Children.OfType<RadioButton>().ElementAt(_step);
            if (rb.IsChecked != true) { rb.IsChecked = true; return; }
            for (int k = 0; k < StepCount; k++) ((FrameworkElement)FindName("Step" + k)).Visibility = k == _step ? Visibility.Visible : Visibility.Collapsed;
            PreviewPanel.Visibility = _step == 2 || _step == 6 ? Visibility.Visible : Visibility.Collapsed;
            BtnBack.IsEnabled = _step > 0;
            BtnNext.IsEnabled = _step < StepCount - 1;
            BodyScroll.ScrollToTop();
            if (_step == 7) BuildSummary();
            if (_step == 2 || _step == 6) UpdatePreview();
        }

        private void Step_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && int.TryParse(rb.Tag as string, out int i)) { _step = i; GoStep(i); }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => GoStep(_step - 1);
        private void Next_Click(object sender, RoutedEventArgs e) => GoStep(_step + 1);

        private void UpdateHeader()
        {
            var name = $"{TxtName.Text} {TxtVersion.Text}".Trim();
            HeaderTitle.Text = string.IsNullOrEmpty(name) ? "Novo pacote" : name;
            var file = _s?.InstallerPath;
            HeaderSub.Text = string.IsNullOrEmpty(file)
                ? "Arraste um instalador ou escolha um aplicativo no catálogo."
                : $"{Path.GetFileName(file)}" + (string.IsNullOrEmpty(_s.CatalogId) ? "" : $"  ·  catálogo: {_s.CatalogId}") + (string.IsNullOrEmpty(TxtVendor.Text) ? "" : $"  ·  {TxtVendor.Text}");
            HeaderIcon.Source = MainWindow.LoadBitmap(_installerIcon);
            HeaderInitials.Visibility = HeaderIcon.Source == null ? Visibility.Visible : Visibility.Collapsed;
            HeaderAvatar.Background = HeaderIcon.Source == null ? (Brush)FindResource("Navy") : (Brush)FindResource("Subtle");
            FooterInfo.Text = string.IsNullOrEmpty(file) ? "Etapa 1: selecione o instalador para começar." : $"Saída: {(string.IsNullOrWhiteSpace(_s.OutputRoot) ? ConfigStore.Current.OutputFolder : _s.OutputRoot)}";
        }

        private void UpdateHints()
        {
            string ext = Path.GetExtension(_s?.InstallerPath ?? "").ToLowerInvariant();
            InstallArgsHint.Text = ext switch
            {
                ".msi" => "MSI: a instalação silenciosa já é automática (/qn REBOOT=ReallySuppress). Informe apenas propriedades extras (ex.: ALLUSERS=1).",
                ".exe" => "EXE: parâmetros para instalar sem perguntas. Comuns: /S (NSIS), /VERYSILENT /NORESTART (Inno Setup), /quiet /norestart (WiX).",
                _ => "Selecione o instalador para preencher automaticamente."
            };
            ModeHint.Text = TagOf(CmbMode) switch
            {
                "Silent" => "Nenhuma janela é exibida. Aplicativos abertos da lista são fechados sem aviso.",
                "Interactive" => "As janelas sempre aparecem para o usuário conectado (útil para apps Disponíveis no Portal da Empresa).",
                _ => "As janelas só aparecem se houver alguém conectado e, quando há aplicativos para fechar, apenas se algum estiver aberto. No Autopilot a instalação é silenciosa."
            };
            RbDetCode.IsEnabled = true;
        }

        private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) { UpdateHints(); UpdatePreview(); } }

        private void Preview_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            UpdatePreview();
            if (sender == TxtName || sender == TxtVersion) UpdateHeader();
        }

        private void UpdatePreview()
        {
            if (PrevTitle == null) return;
            string name = $"{TxtName.Text} {TxtVersion.Text}".Trim();
            PrevTitle.Text = string.IsNullOrEmpty(name) ? "Nome do aplicativo" : name;
            string company = string.IsNullOrWhiteSpace(TxtCompany.Text) ? "PSAppDeployToolkit" : TxtCompany.Text.Trim();
            PrevSubtitle.Text = $"{company} - Instalação do Aplicativo";
            var procs = Names.ParseProcessList(TxtProcesses.Text);
            if (procs.Count > 0)
            {
                PrevBody.Text = "Os seguintes aplicativos precisam ser fechados antes de continuar. Salve seu trabalho e clique no botão para fechá-los e instalar.";
                PrevAppsBox.Visibility = Visibility.Visible;
                string label = string.IsNullOrWhiteSpace(TxtName.Text) ? "Aplicativo" : TxtName.Text.Trim();
                PrevApps.Text = string.Join("\n", procs.Select(p => $"•  {label} ({p})"));
                PrevMainText.Text = "Fechar aplicativos e instalar";
            }
            else
            {
                PrevBody.Text = "A instalação do aplicativo está pronta para começar.";
                PrevAppsBox.Visibility = Visibility.Collapsed;
                PrevMainText.Text = "Instalar";
            }
            PrevCustom.Text = TxtWelcome.Text.Trim();
            PrevCustom.Visibility = string.IsNullOrWhiteSpace(TxtWelcome.Text) ? Visibility.Collapsed : Visibility.Visible;
            bool defer = ChkDefer.IsChecked == true;
            PrevDefer.Text = $"Você pode adiar a instalação mais {Int(TxtDeferTimes.Text, 3)} vez(es).";
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
                "Silent" => "Modo Silencioso: esta janela NÃO será exibida.",
                "Interactive" => "Modo Interativo: a janela aparece sempre que houver um usuário conectado.",
                _ => procs.Count > 0 ? "Modo Automático: a janela só aparece se algum dos aplicativos listados estiver aberto." : "Modo Automático: a janela aparece quando há um usuário conectado."
            };
        }

        private Brush AccentBrush()
        {
            string h = NormalizeHex(TxtAccent.Text);
            try { if (System.Text.RegularExpressions.Regex.IsMatch(h, "^#[0-9A-Fa-f]{6}$")) return (Brush)new BrushConverter().ConvertFromString(h); } catch { }
            return (Brush)FindResource("Brand");
        }

        private void UpdateLogo()
        {
            var img = MainWindow.LoadBitmap(TxtLogo.Text.Trim());
            LogoThumb.Source = img;
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
            var dlg = new OpenFileDialog { Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtLogo.Text = dlg.FileName;
        }

        private void LogoClear_Click(object sender, RoutedEventArgs e) => TxtLogo.Text = "";

        private void BuildSwatches()
        {
            foreach (var (hex, name) in new[] { ("#0A5CA8", "Azul Jornada 365"), ("#0F6CBD", "Azul Microsoft"), ("#0B6E4F", "Verde"), ("#C50F1F", "Vermelho"), ("#CA5010", "Laranja"), ("#8764B8", "Roxo"), ("#038387", "Petróleo"), ("#1F2937", "Grafite") })
            {
                var b = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 7, 0), Cursor = System.Windows.Input.Cursors.Hand,
                    Background = (Brush)new BrushConverter().ConvertFromString(hex), ToolTip = $"{name} ({hex})", Tag = hex
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
            if (!string.IsNullOrWhiteSpace(s.LogoPath) && File.Exists(s.LogoPath))
            {
                string dest = Path.Combine(AppPaths.Branding, "logo-empresa" + Path.GetExtension(s.LogoPath).ToLowerInvariant());
                if (!string.Equals(Path.GetFullPath(s.LogoPath), dest, StringComparison.OrdinalIgnoreCase)) File.Copy(s.LogoPath, dest, true);
                c.LogoPath = dest;
                TxtLogo.Text = dest;
            }
            else c.LogoPath = "";
            ConfigStore.Save();
            MainWindow.Current.Toast("Identidade visual salva como padrão da empresa.", "ok");
        }

        // ------------------------------------------------------------------ processos

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

        // ------------------------------------------------------------------ acoes

        private void BuildQuickActions()
        {
            foreach (var (type, label) in new[]
            {
                ("RemoveDesktopShortcut", "Remover atalho da área de trabalho"),
                ("DisableScheduledTask", "Desativar tarefa de atualização"),
                ("StopService", "Desativar serviço de atualização"),
                ("SetRegistry", "Definir valor no registro"),
                ("CopyFile", "Copiar arquivo de licença/config."),
                ("StopProcess", "Encerrar processos"),
                ("RunPowerShell", "Script PowerShell")
            })
            {
                var b = new Button { Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 6, 12, 6), Tag = type };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = ActionCatalog.Get(type)?.Glyph ?? "\uE710", FontFamily = (FontFamily)FindResource("IconFont"), Foreground = (Brush)FindResource("Brand"), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
                sp.Children.Add(new TextBlock { Text = label, FontSize = 12.5 });
                b.Content = sp;
                b.Click += (s, e) => OpenActionEditor(null, (string)((Button)s).Tag);
                QuickActions.Children.Add(b);
            }
        }

        private void OpenActionEditor(PackageAction existing, string type = null)
        {
            var w = new ActionEditorWindow(existing, type, TxtName.Text.Trim(), _extraFiles.Select(Path.GetFileName).ToList()) { Owner = Window.GetWindow(this) };
            if (w.ShowDialog() == true)
            {
                if (existing != null)
                {
                    int i = _actions.IndexOf(existing);
                    _actions[i] = w.Result;
                }
                else _actions.Add(w.Result);
            }
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
            var dlg = new OpenFileDialog { Multiselect = true, Title = "Arquivos extras do pacote" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) foreach (var f in dlg.FileNames) if (!_extraFiles.Contains(f)) _extraFiles.Add(f);
        }

        private void AddExtraFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Pasta extra do pacote" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true && !_extraFiles.Contains(dlg.FolderName)) _extraFiles.Add(dlg.FolderName);
        }

        private void RemoveExtra_Click(object sender, RoutedEventArgs e)
        {
            if (ExtraFilesList.SelectedItem is string f) _extraFiles.Remove(f);
        }

        // ------------------------------------------------------------------ desinstalacao / deteccao

        private void ShowInstalled_Click(object sender, RoutedEventArgs e)
        {
            string pat = TxtPattern.Text.Trim();
            if (pat.Length == 0) { MainWindow.Current.Toast("Informe o nome do programa (ex.: 7-Zip*).", "warn"); return; }
            var list = InstallerAnalyzer.FindInstalled(pat);
            InstalledList.ItemsSource = list;
            InstalledList.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            InstalledInfo.Text = list.Count > 0
                ? $"{list.Count} programa(s) neste computador correspondem a '{pat}' e seriam removidos."
                : $"Nenhum programa neste computador corresponde a '{pat}' (normal se o aplicativo não estiver instalado aqui).";
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
            var r = await Task.Run(() => PackageBuilder.TestDetection(s));
            DetResult.Visibility = Visibility.Visible;
            DetResult.Background = r.Detected ? (Brush)FindResource("SuccessSoft") : (Brush)FindResource("WarningSoft");
            DetResultText.Foreground = r.Detected ? (Brush)FindResource("Success") : (Brush)FindResource("Warning");
            DetResultText.Text = r.Detected ? "✓ Detectado neste computador. " + r.Output : "Não detectado neste computador — normal se o aplicativo não estiver instalado aqui (ou se a versão instalada for menor).";
        }

        private void ViewDetection_Click(object sender, RoutedEventArgs e)
            => new TextViewerWindow("Script de detecção (Intune)", ScriptGenerator.DetectionScript(Collect())) { Owner = Window.GetWindow(this) }.ShowDialog();

        // ------------------------------------------------------------------ gerar

        private void BuildSummary()
        {
            var s = Collect();
            var cmds = ScriptGenerator.Commands(s);
            SummaryList.ItemsSource = new List<Tuple<string, string>>
            {
                Tuple.Create("Aplicativo", $"{s.AppName} {s.AppVersion}  ·  {s.AppVendor}"),
                Tuple.Create("Instalador", string.IsNullOrEmpty(s.InstallerPath) ? "—" : $"{Path.GetFileName(s.InstallerPath)}  ({s.InstallerType}) {s.InstallArgs}"),
                Tuple.Create("Instalar para", s.InstallBehavior == "User" ? "Usuário" : "Todo o computador (Sistema)"),
                Tuple.Create("Experiência", $"{(s.DeployMode == "Silent" ? "Silencioso" : s.DeployMode == "Interactive" ? "Interativo" : "Automático")}" + (s.ProcessesToClose.Count > 0 ? $" · fecha {string.Join(", ", s.ProcessesToClose)}" : "") + (s.AllowDefer ? $" · adiar até {s.DeferTimes}x" : "")),
                Tuple.Create("Versões anteriores", s.RemovePreviousVersions ? $"Remove '{s.UninstallPattern}' antes de instalar" : "Mantém"),
                Tuple.Create("Ações", s.Actions.Count == 0 ? "Nenhuma" : string.Join("; ", s.Actions.Select(a => a.TypeLabel))),
                Tuple.Create("Detecção", s.DetectionMethod switch { "File" => s.DetectionFilePath, "ProductCode" => s.MsiProductCode, _ => s.DetectionNamePattern } + (s.DetectionCheckVersion ? $"  ≥ {s.DetectionVersion}" : "")),
                Tuple.Create("Visual", $"{(string.IsNullOrEmpty(s.CompanyName) ? "(sem nome de empresa)" : s.CompanyName)} · {s.DialogStyle} · {s.AccentColor}"),
                Tuple.Create("Comando de instalação", cmds.Install),
            };
            var v = PackageBuilder.Validate(s);
            var items = v.Errors.Select(x => Tuple.Create("#FDECEC", "✖  " + x, "#991B1B"))
                .Concat(v.Warnings.Select(x => Tuple.Create("#FEF4E2", "⚠  " + x, "#92400E"))).ToList();
            if (items.Count == 0) items.Add(Tuple.Create("#E8F7EE", "✓  Tudo pronto para gerar o pacote.", "#166534"));
            ValidationList.ItemsSource = items;
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (IsBuilding) return;
            var s = Collect();
            var v = PackageBuilder.Validate(s);
            if (!v.IsValid)
            {
                GoStep(7);
                MainWindow.Current.Toast("Corrija os itens destacados antes de gerar.", "warn");
                return;
            }
            if (v.Warnings.Count > 0)
            {
                var r = MessageBox.Show(Window.GetWindow(this), "Atenção:\n\n• " + string.Join("\n• ", v.Warnings) + "\n\nGerar mesmo assim?", AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }
            GoStep(7);
            await BuildAsync(s);
        }

        public async Task<BuildResult> BuildAsync(PackageSettings s)
        {
            IsBuilding = true;
            BtnGenerate.IsEnabled = BtnGenerate2.IsEnabled = false;
            BtnGenerateText.Text = "Gerando...";
            ResultCard.Visibility = Visibility.Collapsed;
            LogCard.Visibility = Visibility.Visible;
            LogTitle.Text = "Gerando o pacote...";
            BuildProgress.Visibility = Visibility.Visible;
            _log.Clear();
            var main = MainWindow.Current;
            main.SetStatus($"Gerando {s.AppName} {s.AppVersion}...", true);
            void OnLog(LogEntry en) => Dispatcher.BeginInvoke(new Action(() => { _log.Add(en); BodyScroll.ScrollToEnd(); }));
            Log.Entry += OnLog;
            try
            {
                bool native = ChkNative.IsChecked == true;
                var r = await Task.Run(() => PackageBuilder.BuildAsync(s, native, CancellationToken.None));
                _last = r;
                _s = s;
                Json.Save(r.ProjectFile, s);
                ResultFile.Text = $"{Path.GetFileName(r.IntuneWinFile)}  ·  {r.IntuneWinSize / 1024d / 1024d:N1} MB  ·  {r.IntuneWinTool}  ·  PSADT {r.PsadtVersion}";
                ResultInstall.Text = r.InstallCommand;
                ResultUninstall.Text = r.UninstallCommand;
                ResultCard.Visibility = Visibility.Visible;
                LogTitle.Text = "Concluído";
                main.Toast($"Pacote {s.AppName} {s.AppVersion} gerado com sucesso.", "ok");
                main.UpdateToolsStatus();
                _ = Dispatcher.BeginInvoke(new Action(() => BodyScroll.ScrollToTop()), System.Windows.Threading.DispatcherPriority.ContextIdle);
                return r;
            }
            catch (Exception ex)
            {
                LogTitle.Text = "Falhou";
                Log.Error(ex.Message);
                MessageBox.Show(main, "Não foi possível gerar o pacote:\n\n" + ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            finally
            {
                Log.Entry -= OnLog;
                IsBuilding = false;
                BuildProgress.Visibility = Visibility.Collapsed;
                BtnGenerate.IsEnabled = BtnGenerate2.IsEnabled = true;
                BtnGenerateText.Text = "Gerar pacote";
                main.SetStatus("Pronto");
            }
        }

        private void PreviewScript_Click(object sender, RoutedEventArgs e)
        {
            var tpl = ToolsService.LocalPsadt();
            if (tpl == null) { MainWindow.Current.Toast("O PSAppDeployToolkit ainda não foi baixado. Ele é baixado na primeira geração (ou em Configurações).", "warn"); return; }
            var s = Collect();
            if (string.IsNullOrEmpty(s.InstallerType)) s.InstallerType = "EXE";
            var (text, _) = TextFiles.Read(Path.Combine(tpl, "Invoke-AppDeployToolkit.ps1"));
            string name = string.IsNullOrEmpty(s.InstallerPath) ? "instalador.exe" : Path.GetFileName(s.InstallerPath);
            new TextViewerWindow("Invoke-AppDeployToolkit.ps1 (prévia)", ScriptGenerator.DeployScript(text, s, name)) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            string t = (sender as FrameworkElement)?.Tag as string == "install" ? ResultInstall.Text : ResultUninstall.Text;
            try { Clipboard.SetText(t); MainWindow.Current.Toast("Comando copiado.", "ok"); } catch { }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_last != null) Process.Start("explorer.exe", $"/select,\"{_last.IntuneWinFile}\"");
        }

        private void OpenInstructions_Click(object sender, RoutedEventArgs e)
        {
            if (_last != null) MainWindow.OpenUrl(_last.SummaryHtml);
        }

        private void TestInstall_Click(object sender, RoutedEventArgs e) => RunTest("Install");
        private void TestUninstall_Click(object sender, RoutedEventArgs e) => RunTest("Uninstall");

        private void RunTest(string type)
        {
            if (_last == null) return;
            string verb = type == "Install" ? "instalar" : "desinstalar";
            var r = MessageBox.Show(Window.GetWindow(this), $"Isto vai {verb} o aplicativo NESTE computador, como Administrador, exibindo as janelas do PSADT.\n\nContinuar?", AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            try { PackageBuilder.RunLocalTest(_last.PsadtFolder, type); MainWindow.Current.Toast($"Teste ({verb}) iniciado. Logs em C:\\Windows\\Logs\\Software ou na pasta de logs do Intune.", "info"); }
            catch { MainWindow.Current.Toast("Teste cancelado (permissão de administrador negada).", "warn"); }
        }

        // ------------------------------------------------------------------ projeto

        public void LoadProject(string path)
        {
            try
            {
                var s = Json.Load<PackageSettings>(path);
                _s = s;
                LoadSettings(s);
                AnalysisCard.Visibility = Visibility.Collapsed;
                DropText.Text = string.IsNullOrEmpty(s.InstallerPath) ? "Arraste o instalador (.msi ou .exe) aqui ou clique para procurar" : Path.GetFileName(s.InstallerPath);
                ResultCard.Visibility = Visibility.Collapsed;
                MainWindow.Current.Navigate("editor");
                GoStep(1);
                if (!string.IsNullOrEmpty(s.InstallerPath) && !File.Exists(s.InstallerPath))
                    MainWindow.Current.Toast("O instalador do projeto não foi encontrado. Selecione-o novamente na etapa 1 (as configurações são mantidas).", "warn");
                else MainWindow.Current.Toast($"Projeto carregado: {s.AppName} {s.AppVersion}", "ok");
            }
            catch (Exception ex) { MessageBox.Show("Projeto inválido: " + ex.Message, AppInfo.Name); }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Projeto IntunePSApp (*.json)|*.json", InitialDirectory = Directory.Exists(ConfigStore.Current.OutputFolder) ? ConfigStore.Current.OutputFolder : null };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) LoadProject(dlg.FileName);
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            var s = Collect();
            var dlg = new SaveFileDialog { Filter = "Projeto IntunePSApp (*.json)|*.json", FileName = "projeto_" + Names.Safe($"{s.AppName}_{s.AppVersion}") + ".json" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) { Json.Save(dlg.FileName, s); MainWindow.Current.Toast("Projeto salvo.", "ok"); }
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            _last = null;
            _installerIcon = null;
            AnalysisCard.Visibility = Visibility.Collapsed;
            ResultCard.Visibility = Visibility.Collapsed;
            LogCard.Visibility = Visibility.Collapsed;
            InstalledList.Visibility = Visibility.Collapsed;
            DetResult.Visibility = Visibility.Collapsed;
            DropText.Text = "Arraste o instalador (.msi ou .exe) aqui ou clique para procurar";
            LoadSettings(ConfigStore.Current.NewPackage());
            GoStep(0);
        }
    }
}
