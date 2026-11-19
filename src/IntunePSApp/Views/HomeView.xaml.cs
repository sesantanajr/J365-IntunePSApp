using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp.Views
{
    public partial class HomeView : UserControl, IRefreshable
    {
        public HomeView()
        {
            InitializeComponent();
            Steps.ItemsSource = new[]
            {
                Tuple.Create("", "1. Escolha o aplicativo", "Busque entre milhares de apps do banco de dados (atualizado diariamente a partir do winget) ou arraste o seu instalador .msi/.exe."),
                Tuple.Create("", "2. Personalize", "Mensagens, fechamento de apps, adiamento, logo da empresa, remoção de versões anteriores e ações como registro, atalhos e serviços."),
                Tuple.Create("", "3. Envie ao Intune", "Receba o .intunewin, os comandos, o script de detecção e um guia com todos os campos do Intune prontos para copiar.")
            };
        }

        public void Refresh()
        {
            var c = CatalogService.Instance;
            StatApps.Text = c.IsLoaded ? c.Apps.Count.ToString("N0") : "—";
            StatAppsSub.Text = c.IsLoaded && c.Generated.HasValue ? $"atualizado em {c.Generated.Value.ToLocalTime():dd/MM/yyyy}" : "baixe em Configurações ou na barra lateral";
            var hist = PackageBuilder.History(ConfigStore.Current.OutputFolder);
            StatPackages.Text = hist.Count.ToString();
            StatPsadt.Text = ToolsService.PsadtVersion ?? "—";
            StatPsadtSub.Text = ToolsService.PsadtVersion != null ? "template v4 oficial" : "baixado na primeira geração";
            StatIwau.Text = ToolsService.HasIntuneWinAppUtil ? "Pronto" : "—";
            StatIwauSub.Text = ToolsService.HasIntuneWinAppUtil ? "IntuneWinAppUtil.exe (Microsoft)" : "baixado na primeira geração";
            Recent.ItemsSource = hist.Take(5).ToList();
            RecentEmpty.Visibility = hist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Catalog_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("catalog");
        private void Package_Click(object sender, RoutedEventArgs e) { MainWindow.Current.Navigate("editor"); MainWindow.Current.Editor.BrowseInstaller(); }
        private void Packages_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("packages");
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string p) Process.Start("explorer.exe", $"\"{p}\"");
        }
    }
}
