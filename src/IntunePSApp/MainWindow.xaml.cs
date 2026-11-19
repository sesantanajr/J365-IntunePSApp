using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Views;

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

        private CancellationTokenSource _busyCts;
        private bool _catalogUpdating;

        public MainWindow()
        {
            Current = this;
            InitializeComponent();
            Home = new HomeView();
            Catalog = new CatalogView();
            Editor = new EditorView();
            Packages = new PackagesView();
            Settings = new SettingsView();
            VersionText.Text = $"Versão {AppInfo.Version} · Código aberto (MIT)";
            StateChanged += (s, e) => ApplyMaximizedFix();
            Loaded += OnLoaded;
            CatalogService.Instance.Changed += () => Dispatcher.Invoke(UpdateCatalogStatus);
            NavHome.IsChecked = true;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateToolsStatus();
            SetStatus("Carregando o banco de dados de aplicativos...");
            await CatalogService.Instance.LoadAsync();
            UpdateCatalogStatus();
            SetStatus("Pronto");

            string snap = App.Arg("--snapshot");
            if (!string.IsNullOrEmpty(snap)) { await SnapshotRunner.RunAsync(this, snap); return; }

            if (!App.HasArg("--no-update") && CatalogService.Instance.NeedsUpdate(ConfigStore.Current))
                _ = UpdateCatalogAsync(silent: CatalogService.Instance.IsLoaded);

            string open = App.Args.Length > 0 && File.Exists(App.Args[0]) ? App.Args[0] : null;
            if (open != null)
            {
                Navigate("editor");
                if (open.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) Editor.LoadProject(open);
                else await Editor.AnalyzeFileAsync(open);
            }
        }

        // ------------------------------------------------------------------ navegacao

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
            UserControl page = key switch { "catalog" => Catalog, "editor" => Editor, "packages" => Packages, "settings" => Settings, _ => Home };
            CaptionText.Text = key switch
            {
                "catalog" => "Catálogo de aplicativos",
                "editor" => "Novo pacote",
                "packages" => "Pacotes gerados",
                "settings" => "Configurações",
                _ => "Início"
            } + "  ·  J365 IntunePSApp";
            if (page is IRefreshable r) r.Refresh();
            PageHost.Content = page;
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        // ------------------------------------------------------------------ status, notificacoes e ocupado

        public void SetStatus(string text, bool busy = false)
        {
            StatusLeft.Text = text;
            StatusDot.Fill = busy ? (Brush)FindResource("Cyan") : (Brush)FindResource("Success");
        }

        public void UpdateToolsStatus()
        {
            string psadt = ToolsService.PsadtVersion is string v ? $"PSADT {v}" : "PSADT: baixado na 1ª geração";
            string iw = ToolsService.HasIntuneWinAppUtil ? "IntuneWinAppUtil ✓" : "IntuneWinAppUtil: baixado na 1ª geração";
            StatusRight.Text = $"{psadt}   ·   {iw}   ·   {(AppPaths.IsPortable ? "Modo portátil" : "Dados em %LocalAppData%")}";
        }

        public void UpdateCatalogStatus()
        {
            var c = CatalogService.Instance;
            if (c.IsLoaded)
            {
                CatalogCount.Text = $"{c.Apps.Count:N0} apps";
                CatalogDate.Text = c.Generated.HasValue ? $"Atualizado em {c.Generated.Value.ToLocalTime():dd/MM/yyyy HH:mm}" : "Atualizado";
            }
            else
            {
                CatalogCount.Text = "—";
                CatalogDate.Text = "Ainda não baixado. Clique em Atualizar.";
            }
            if (PageHost.Content is IRefreshable r && PageHost.Content != Editor) r.Refresh();
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
                Background = (Brush)new BrushConverter().ConvertFromString(bg),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(fg),
                BorderThickness = new Thickness(0, 0, 0, 0),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 8, 0, 0),
                Effect = (System.Windows.Media.Effects.Effect)FindResource("PopShadow")
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), Foreground = (Brush)new BrushConverter().ConvertFromString(fg), FontSize = 16, Margin = new Thickness(0, 1, 12, 0), VerticalAlignment = VerticalAlignment.Top });
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Width = 300, Foreground = (Brush)new BrushConverter().ConvertFromString(fg) });
            border.Child = sp;
            ToastHost.Children.Add(border);
            border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == "error" ? 9 : 5) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (s2, e2) => ToastHost.Children.Remove(border);
                border.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        }

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
            if (total.HasValue && total.Value > 0)
            {
                BusyProgress.IsIndeterminate = false;
                BusyProgress.Value = read * 100.0 / total.Value;
                BusyText.Text = $"{read / 1024d / 1024d:N1} MB de {total.Value / 1024d / 1024d:N1} MB";
            }
            else BusyText.Text = $"{read / 1024d / 1024d:N1} MB";
        }

        public void HideBusy() => BusyOverlay.Visibility = Visibility.Collapsed;

        private void BusyCancel_Click(object sender, RoutedEventArgs e) => _busyCts?.Cancel();

        // ------------------------------------------------------------------ banco de dados

        public async Task UpdateCatalogAsync(bool silent = false)
        {
            if (_catalogUpdating) return;
            _catalogUpdating = true;
            BtnCatalogUpdate.IsEnabled = false;
            CatalogDate.Text = "Baixando...";
            SetStatus("Atualizando o banco de dados de aplicativos...", true);
            try
            {
                var progress = new Progress<(long, long?)>(p =>
                {
                    if (p.Item2.HasValue && p.Item2 > 0) CatalogDate.Text = $"Baixando... {p.Item1 * 100 / p.Item2.Value}%";
                });
                await CatalogService.Instance.UpdateAsync(ConfigStore.Current.CatalogUrl, progress, CancellationToken.None);
                if (!silent) Toast($"Banco de dados atualizado: {CatalogService.Instance.Apps.Count:N0} aplicativos.", "ok");
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao atualizar o banco de dados: " + ex.Message);
                Toast("Não foi possível atualizar o banco de dados agora. " + (CatalogService.Instance.IsLoaded ? "A versão local continua disponível." : "Verifique a conexão ou importe o arquivo em Configurações."), "warn");
            }
            finally
            {
                _catalogUpdating = false;
                BtnCatalogUpdate.IsEnabled = true;
                UpdateCatalogStatus();
                SetStatus("Pronto");
            }
        }

        private async void CatalogUpdate_Click(object sender, RoutedEventArgs e) => await UpdateCatalogAsync();

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        private void Blog_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.BlogUrl);
        private void Repo_Click(object sender, RoutedEventArgs e) => OpenUrl(AppInfo.RepoUrl);

        // ------------------------------------------------------------------ janela

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
            if (Editor != null && Editor.IsBuilding)
            {
                var r = MessageBox.Show(this, "Um pacote está sendo gerado. Deseja realmente sair?", AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            }
            base.OnClosing(e);
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

    public interface IRefreshable
    {
        void Refresh();
    }
}
