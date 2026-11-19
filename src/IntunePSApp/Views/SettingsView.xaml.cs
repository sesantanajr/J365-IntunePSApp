using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;
using Microsoft.Win32;

namespace J365.IntunePSApp.Views
{
    public partial class SettingsView : UserControl, IRefreshable
    {
        public SettingsView() { InitializeComponent(); }

        private static void SelectTag(ComboBox c, string tag)
        {
            foreach (ComboBoxItem it in c.Items) if (string.Equals(it.Tag as string, tag, StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = it; return; }
            c.SelectedIndex = 0;
        }

        private static string TagOf(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        public void Refresh()
        {
            var c = ConfigStore.Current;
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
            PortableInfo.Text = AppPaths.IsPortable
                ? "Modo portátil: configurações, ferramentas, banco de dados e downloads ficam ao lado do .exe — leve a pasta para qualquer computador."
                : "Os dados ficam no perfil do usuário porque a pasta do .exe não permite gravação.";
            TxtCatalogUrl.Text = c.CatalogUrl;
            TxtCatalogDays.Text = c.CatalogUpdateDays.ToString();
            UpdateInfo();
        }

        private void UpdateInfo()
        {
            var cat = CatalogService.Instance;
            CatalogInfo.Text = cat.IsLoaded
                ? $"{cat.Apps.Count:N0} aplicativos · gerado em {cat.Generated?.ToLocalTime():dd/MM/yyyy HH:mm}" + (string.IsNullOrEmpty(cat.Commit) ? "" : $" · winget-pkgs {cat.Commit[..Math.Min(8, cat.Commit.Length)]}")
                : "Ainda não baixado.";
            PsadtInfo.Text = ToolsService.PsadtVersion is string v ? $"Versão {v} instalada em {ToolsService.LocalPsadt()}" : "Não baixado (é baixado automaticamente na primeira geração).";
            IwauInfo.Text = ToolsService.HasIntuneWinAppUtil ? $"Pronto · {ToolsService.IntuneWinAppUtilPath}" : "Não baixado (sem ele, o empacotador interno compatível é usado).";
            AboutTitle.Text = $"J365 IntunePSApp {AppInfo.Version}";
            LogoThumb.Source = MainWindow.LoadBitmap(TxtLogo.Text);
        }

        private void Logo_Changed(object sender, TextChangedEventArgs e) => LogoThumb.Source = MainWindow.LoadBitmap(TxtLogo.Text);

        private void Logo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtLogo.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var c = ConfigStore.Current;
            c.CompanyName = TxtCompany.Text.Trim();
            c.Author = TxtAuthor.Text.Trim();
            string logo = TxtLogo.Text.Trim();
            if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
            {
                string dest = Path.Combine(AppPaths.Branding, "logo-empresa" + Path.GetExtension(logo).ToLowerInvariant());
                if (!string.Equals(Path.GetFullPath(logo), dest, StringComparison.OrdinalIgnoreCase)) File.Copy(logo, dest, true);
                logo = dest;
            }
            c.LogoPath = logo;
            c.AccentColor = TxtAccent.Text.Trim();
            c.DialogStyle = TagOf(CmbStyle);
            c.Language = TagOf(CmbLanguage);
            c.LogsInIntuneFolder = ChkImeLogs.IsChecked == true;
            c.DeployMode = TagOf(CmbMode);
            c.DeferTimes = int.TryParse(TxtDefer.Text, out var d) ? d : 3;
            c.CloseAppsCountdown = int.TryParse(TxtCountdown.Text, out var cd) ? cd : 300;
            c.AllowDefer = ChkDefer.IsChecked == true;
            c.BlockExecution = ChkBlock.IsChecked == true;
            c.CheckDiskSpace = ChkDisk.IsChecked == true;
            c.RemovePreviousVersions = ChkRemovePrev.IsChecked == true;
            c.WelcomeMessage = TxtWelcome.Text.Trim();
            c.CompletionMessage = TxtCompletion.Text.Trim();
            c.PreferredArch = TagOf(CmbArch);
            c.PreferredLocale = TagOf(CmbLocale);
            c.OutputFolder = string.IsNullOrWhiteSpace(TxtOutput.Text) ? AppPaths.DefaultOutput : TxtOutput.Text.Trim();
            c.CatalogUrl = string.IsNullOrWhiteSpace(TxtCatalogUrl.Text) ? AppInfo.DefaultCatalogUrl : TxtCatalogUrl.Text.Trim();
            c.CatalogUpdateDays = int.TryParse(TxtCatalogDays.Text, out var days) ? Math.Clamp(days, 1, 90) : 3;
            ConfigStore.Save();
            Refresh();
            MainWindow.Current.Toast("Configurações salvas. Valem para os próximos pacotes.", "ok");
        }

        private void Output_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Pasta onde os pacotes serão criados" };
            if (dlg.ShowDialog(Window.GetWindow(this)) == true) TxtOutput.Text = dlg.FolderName;
        }

        private void OpenData_Click(object sender, RoutedEventArgs e) => Process.Start("explorer.exe", $"\"{AppPaths.Data}\"");

        private async void CatalogUpdate_Click(object sender, RoutedEventArgs e)
        {
            ConfigStore.Current.CatalogUrl = string.IsNullOrWhiteSpace(TxtCatalogUrl.Text) ? AppInfo.DefaultCatalogUrl : TxtCatalogUrl.Text.Trim();
            ConfigStore.Save();
            await MainWindow.Current.UpdateCatalogAsync();
            UpdateInfo();
        }

        private async void CatalogImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Banco de dados (catalog.json.gz)|*.gz" };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                await CatalogService.Instance.ImportAsync(dlg.FileName);
                MainWindow.Current.Toast($"Banco de dados importado: {CatalogService.Instance.Apps.Count:N0} aplicativos.", "ok");
            }
            catch (Exception ex) { MainWindow.Current.Toast("Arquivo inválido: " + ex.Message, "error"); }
            UpdateInfo();
        }

        private async void PsadtUpdate_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Current;
            main.ShowBusy("Atualizando o PSAppDeployToolkit", "Consultando o GitHub oficial...", false);
            try { await System.Threading.Tasks.Task.Run(() => ToolsService.UpdatePsadtAsync(CancellationToken.None)); main.Toast($"PSAppDeployToolkit {ToolsService.PsadtVersion} pronto.", "ok"); }
            catch (Exception ex) { main.Toast("Falha ao baixar o PSADT: " + ex.Message, "error"); }
            finally { main.HideBusy(); main.UpdateToolsStatus(); UpdateInfo(); }
        }

        private async void IwauUpdate_Click(object sender, RoutedEventArgs e)
        {
            var main = MainWindow.Current;
            main.ShowBusy("Baixando o IntuneWinAppUtil.exe", "GitHub oficial da Microsoft...", false);
            try
            {
                var r = await System.Threading.Tasks.Task.Run(() => ToolsService.EnsureIntuneWinAppUtilAsync(CancellationToken.None));
                main.Toast(r != null ? "IntuneWinAppUtil.exe pronto (assinatura Microsoft verificada)." : "Não foi possível baixar; o empacotador interno será usado.", r != null ? "ok" : "warn");
            }
            finally { main.HideBusy(); main.UpdateToolsStatus(); UpdateInfo(); }
        }

        private void Blog_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.BlogUrl);
        private void Repo_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.RepoUrl);
    }
}
