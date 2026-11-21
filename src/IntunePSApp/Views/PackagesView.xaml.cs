using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;
using Microsoft.VisualBasic.FileIO;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    public partial class PackagesView : UserControl, IRefreshable, ILocalized
    {
        private List<PackageHistoryItem> _all = new List<PackageHistoryItem>();

        private CancellationTokenSource _refreshCts;
        public PackagesView()
        {
            InitializeComponent();
            Unloaded += (s, e) => _refreshCts?.Cancel();
        }

        public async void Refresh()
        {
            _refreshCts?.Cancel();
            var source = new CancellationTokenSource();
            _refreshCts = source;
            string root = ConfigStore.Current.OutputFolder;
            Subtitle.Text = T("Packages_Loading");
            RefreshError.Visibility = Visibility.Collapsed;
            try
            {
                var items = await Task.Run(() => PackageBuilder.History(root, source.Token), source.Token);
                if (source.IsCancellationRequested || _refreshCts != source) return;
                _all = items;
                ApplyFilter();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (_refreshCts != source || source.IsCancellationRequested) return;
                RefreshErrorText.Text = T("Packages_LoadFailed", ex.Message);
                RefreshError.Visibility = Visibility.Visible;
                Subtitle.Text = root;
            }
            finally { if (_refreshCts == source) _refreshCts = null; source.Dispose(); }
        }

        public void OnLanguageChanged() => Refresh();

        private void ApplyFilter()
        {
            string q = (Filter.Text ?? "").Trim();
            IEnumerable<PackageHistoryItem> items = _all;
            if (q.Length > 0)
                items = items.Where(i => (i.Title ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || (i.Vendor ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            if (OnlyOutdated.IsChecked == true) items = items.Where(i => i.HasUpdate);
            items = Sort.SelectedIndex == 1 ? items.OrderBy(i => i.AppName, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(i => i.Date)
                : Sort.SelectedIndex == 2 ? items.OrderByDescending(i => i.HasUpdate).ThenBy(i => i.AppName, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderByDescending(i => i.Date);
            var list = items.ToList();
            List.ItemsSource = list;
            bool none = _all.Count == 0;
            Empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            List.Visibility = list.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            NoResults.Visibility = !none && list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            FilterBar.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            int outdated = _all.Count(i => i.HasUpdate);
            Subtitle.Text = T("Packages_Subtitle", _all.Count, ConfigStore.Current.OutputFolder) + (outdated > 0 ? T("Packages_SubtitleOutdated", outdated) : "");
        }

        private void Filter_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterHint.Visibility = string.IsNullOrEmpty(Filter.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (IsLoaded) ApplyFilter();
        }

        private void OnlyOutdated_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) ApplyFilter();
        }

        private void Sort_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
        private void ClearFilter_Click(object sender, RoutedEventArgs e) { Filter.Clear(); OnlyOutdated.IsChecked = false; ApplyFilter(); }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
        private void Catalog_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("catalog");

        private void OpenOutput_Click(object sender, RoutedEventArgs e)
        {
            try { Directory.CreateDirectory(ConfigStore.Current.OutputFolder); } catch { }
            Ui.OpenPath(ConfigStore.Current.OutputFolder);
        }

        private static PackageHistoryItem Item(object sender) => (sender as FrameworkElement)?.Tag as PackageHistoryItem;

        private void Reopen_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i != null) MainWindow.Current.Editor.LoadProject(i.ProjectFile);
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i != null) await MainWindow.Current.Editor.UpdateProjectAsync(i.ProjectFile);
        }

        private void Instructions_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i == null) return;
            if (File.Exists(i.SummaryHtml)) MainWindow.OpenUrl(i.SummaryHtml);
            else MainWindow.Current.Toast(T("Packages_NoInstructions"), "warn");
        }

        private void Folder_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i == null) return;
            if (i.IntuneWin != null && File.Exists(i.IntuneWin)) Ui.RevealFile(i.IntuneWin);
            else Ui.OpenPath(i.Folder);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var i = Item(sender);
            if (i == null || !Directory.Exists(i.Folder)) return;
            if (!Ui.Confirm(T("Packages_ConfirmDelete", i.Title, i.Folder), T("Packages_Delete"), T("Common_Cancel"), danger: true)) return;
            try
            {
                // Recycle Bin, not a permanent delete: the package can be restored.
                FileSystem.DeleteDirectory(i.Folder, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                MainWindow.Current.Toast(T("Packages_Deleted", i.Title), "ok");
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
            Refresh();
        }
    }
}
