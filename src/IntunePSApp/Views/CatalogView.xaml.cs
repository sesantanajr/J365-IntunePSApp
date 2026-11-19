using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp.Views
{
    public partial class CatalogView : UserControl, IRefreshable
    {
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private CatalogApp _current;
        private int _searchVersion;

        public CatalogView()
        {
            InitializeComponent();
            _debounce.Tick += async (s, e) => { _debounce.Stop(); await RunSearchAsync(); };
        }

        public void Refresh()
        {
            var c = CatalogService.Instance;
            bool loaded = c.IsLoaded;
            EmptyState.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
            Main.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
            Subtitle.Text = loaded
                ? $"{c.Apps.Count:N0} aplicativos · fonte: repositório público winget (Microsoft, licença MIT) · atualizado em {c.Generated?.ToLocalTime():dd/MM/yyyy HH:mm}"
                : "Banco de dados de aplicativos com parâmetros silenciosos, links oficiais e dados de detecção.";
            if (loaded && Results.Items.Count == 0) _ = RunSearchAsync();
        }

        public void FocusSearch(string text = null)
        {
            if (text != null) Search.Text = text;
            Search.Focus();
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed;
            _debounce.Stop();
            _debounce.Start();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) _ = RunSearchAsync();
        }

        private async Task RunSearchAsync()
        {
            if (!CatalogService.Instance.IsLoaded) return;
            int v = ++_searchVersion;
            string q = Search.Text;
            bool only = OnlySupported.IsChecked == true;
            var list = await Task.Run(() => CatalogService.Instance.Search(q, only, 400));
            if (v != _searchVersion) return;
            Results.ItemsSource = list;
            ResultInfo.Text = string.IsNullOrWhiteSpace(q)
                ? $"Aplicativos com curadoria ({list.Count}). Digite para buscar em todo o banco."
                : list.Count >= 400 ? "Mostrando os 400 melhores resultados. Refine a busca." : $"{list.Count} resultado(s)";
            if (list.Count > 0 && Results.SelectedIndex < 0) Results.SelectedIndex = 0;
        }

        private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails(Results.SelectedItem as CatalogApp);

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Results.SelectedItem is CatalogApp) Package_Click(sender, e);
        }

        public void Select(CatalogApp app)
        {
            Search.Text = app.Name;
            ShowDetails(app);
        }

        private void ShowDetails(CatalogApp a)
        {
            _current = a;
            NoSelection.Visibility = a == null ? Visibility.Visible : Visibility.Collapsed;
            Details.Visibility = a == null ? Visibility.Collapsed : Visibility.Visible;
            if (a == null) return;
            DAvatar.Background = (Brush)new BrushConverter().ConvertFromString(a.AvatarColor);
            DInitials.Text = a.Initials;
            DName.Text = a.Name;
            DPublisher.Text = a.Publisher ?? "";
            DVersion.Text = "Versão " + a.Version;
            DId.Text = a.Id;
            DLicense.Text = a.License ?? "";
            DLicenseChip.Visibility = string.IsNullOrEmpty(a.License) ? Visibility.Collapsed : Visibility.Visible;
            DCuratedChip.Visibility = a.HasCuration ? Visibility.Visible : Visibility.Collapsed;
            DDescription.Text = a.Description ?? "";
            DInstallers.ItemsSource = a.Installers;
            var best = CatalogService.PickInstaller(a, ConfigStore.Current.PreferredArch, ConfigStore.Current.PreferredLocale);
            DInstallers.SelectedItem = best ?? a.Installers.FirstOrDefault();
            if (DInstallers.SelectedItem != null) DInstallers.ScrollIntoView(DInstallers.SelectedItem);
            BtnSite.IsEnabled = !string.IsNullOrEmpty(a.Homepage);
            if (a.Extra != null)
            {
                var parts = new System.Collections.Generic.List<string>();
                if (a.Extra.Processes?.Count > 0) parts.Add("Aplicativos a fechar: " + string.Join(", ", a.Extra.Processes));
                if (!string.IsNullOrEmpty(a.Extra.NamePattern)) parts.Add("Nome em Aplicativos instalados: " + a.Extra.NamePattern);
                if (!string.IsNullOrEmpty(a.Extra.UninstallArgs)) parts.Add("Desinstalação silenciosa: " + a.Extra.UninstallArgs);
                if (!string.IsNullOrEmpty(a.Extra.Notes)) parts.Add(a.Extra.Notes);
                DCurationText.Text = string.Join("\n", parts);
                DCuration.Visibility = Visibility.Visible;
            }
            else DCuration.Visibility = Visibility.Collapsed;
        }

        private void DInstallers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var i = DInstallers.SelectedItem as CatalogInstaller;
            BtnPackage.IsEnabled = i != null && i.IsSupported;
            if (i == null) { DInstallerInfo.Text = ""; return; }
            DInstallerInfo.Text = i.IsSupported
                ? $"Selecionado: {i.TypeLabel} {i.ArchLabel} · {i.ScopeLabel}" + (string.IsNullOrEmpty(i.ProductCode) ? "" : $" · código do produto {i.ProductCode}")
                : $"Instaladores do tipo {i.TypeLabel} não são suportados por este assistente (use MSI ou EXE).";
        }

        private void Site_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_current?.Homepage)) MainWindow.OpenUrl(_current.Homepage);
        }

        private async void Download_Click(object sender, RoutedEventArgs e) => await MainWindow.Current.UpdateCatalogAsync();

        private async void Package_Click(object sender, RoutedEventArgs e)
        {
            var app = _current;
            var inst = DInstallers.SelectedItem as CatalogInstaller ?? CatalogService.PickInstaller(app);
            if (app == null || inst == null || !inst.IsSupported) { MainWindow.Current.Toast("Escolha um instalador MSI ou EXE da lista.", "warn"); return; }
            var main = MainWindow.Current;
            string dest = Path.Combine(AppPaths.Downloads, Names.Safe(app.Id), Names.Safe(app.Version ?? "latest"), FileNameFor(app, inst));
            var ct = main.ShowBusy($"Baixando {app.Name} {app.Version}", "Conectando ao site do fabricante...");
            try
            {
                bool cached = File.Exists(dest) && !string.IsNullOrEmpty(inst.Sha256) && Hashing.Sha256File(dest).Equals(inst.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!cached)
                {
                    Log.Step($"Baixando {inst.Url}");
                    var progress = new Progress<(long, long?)>(p => main.BusyProgressReport(p.Item1, p.Item2));
                    await Http.DownloadAsync(inst.Url, dest, progress, ct, inst.Sha256);
                    Log.Ok($"Download concluído e verificado: {Path.GetFileName(dest)}");
                }
                main.HideBusy();
                main.Navigate("editor");
                await main.Editor.LoadFromCatalogAsync(dest, app, inst);
            }
            catch (OperationCanceledException) { main.HideBusy(); main.Toast("Download cancelado.", "warn"); }
            catch (Exception ex)
            {
                main.HideBusy();
                Log.Error("Falha no download: " + ex.Message);
                MessageBox.Show(main, "Não foi possível baixar o instalador:\n\n" + ex.Message + "\n\nVocê pode baixar manualmente no site do fabricante e arrastar o arquivo na tela Novo pacote.", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string FileNameFor(CatalogApp app, CatalogInstaller inst)
        {
            string ext = inst.IsMsi ? ".msi" : ".exe";
            string name = "";
            try { name = Uri.UnescapeDataString(Path.GetFileName(new Uri(inst.Url).AbsolutePath)); } catch { }
            name = string.Concat((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (string.IsNullOrWhiteSpace(name) || name.Length < 5 || !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                name = $"{Names.Safe(app.Name)}_{Names.Safe(app.Version)}_{inst.Arch}{ext}";
            return name;
        }
    }
}
