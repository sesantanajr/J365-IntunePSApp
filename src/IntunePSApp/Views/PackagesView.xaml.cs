using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp.Views
{
    public partial class PackagesView : UserControl, IRefreshable
    {
        public PackagesView() { InitializeComponent(); }

        public void Refresh()
        {
            string root = ConfigStore.Current.OutputFolder;
            var list = PackageBuilder.History(root);
            List.ItemsSource = list;
            Empty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            List.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Subtitle.Text = $"{list.Count} pacote(s) em {root}";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

        private void OpenOutput_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(ConfigStore.Current.OutputFolder);
            Process.Start("explorer.exe", $"\"{ConfigStore.Current.OutputFolder}\"");
        }

        private static PackageHistoryItem Item(object sender) => (sender as FrameworkElement)?.Tag as PackageHistoryItem;

        private void Reopen_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i != null) MainWindow.Current.Editor.LoadProject(i.ProjectFile);
        }

        private void Instructions_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i != null && File.Exists(i.SummaryHtml)) MainWindow.OpenUrl(i.SummaryHtml);
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i == null) return;
            if (i.IntuneWin != null) Process.Start("explorer.exe", $"/select,\"{i.IntuneWin}\"");
            else Process.Start("explorer.exe", $"\"{i.Folder}\"");
        }
    }
}
