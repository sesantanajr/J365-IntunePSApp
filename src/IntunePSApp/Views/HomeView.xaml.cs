using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    /// <summary>A banner on the home page with one action (new version, missing dependency, packages to update).</summary>
    public class HomeNotice
    {
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public string ButtonText { get; set; }
        public Action Action { get; set; }
    }

    public partial class HomeView : UserControl, IRefreshable, ILocalized
    {
        public HomeView()
        {
            InitializeComponent();
            BuildSteps();
        }

        private void BuildSteps()
        {
            Steps.ItemsSource = new[]
            {
                Tuple.Create("", T("Home_Step1Title"), T("Home_Step1Text")),
                Tuple.Create("", T("Home_Step2Title"), T("Home_Step2Text")),
                Tuple.Create("", T("Home_Step3Title"), T("Home_Step3Text"))
            };
        }

        public void OnLanguageChanged()
        {
            BuildSteps();
            Refresh();
        }

        public void Refresh()
        {
            var culture = Loc.Instance.Culture;
            var c = CatalogService.Instance;
            StatApps.Text = c.IsLoaded ? c.Apps.Count.ToString("N0", culture) : "—";
            StatAppsSub.Text = c.IsLoaded && c.Generated.HasValue ? T("Home_StatAppsSub", c.Generated.Value.ToLocalTime().ToString("d", culture)) : T("Home_StatAppsMissing");
            var hist = PackageBuilder.History(ConfigStore.Current.OutputFolder);
            StatPackages.Text = hist.Count.ToString("N0", culture);
            StatPackagesSub.Text = T("Home_StatPackagesSub");
            StatPsadt.Text = ToolsService.PsadtVersion ?? "—";
            StatPsadtSub.Text = ToolsService.PsadtVersion != null ? T("Home_StatPsadtSub") : T("Home_StatMissing");
            StatIwau.Text = ToolsService.HasIntuneWinAppUtil ? T("Common_Ready") : "—";
            StatIwauSub.Text = ToolsService.HasIntuneWinAppUtil ? "IntuneWinAppUtil.exe (Microsoft)" : T("Home_StatIwauMissing");
            Recent.ItemsSource = hist.Take(5).ToList();
            RecentEmpty.Visibility = hist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Notices.ItemsSource = BuildNotices(hist);
        }

        private static List<HomeNotice> BuildNotices(List<PackageHistoryItem> history)
        {
            var main = MainWindow.Current;
            var list = new List<HomeNotice>();

            string newApp = UpdateService.NewAppVersion;
            if (newApp != null)
                list.Add(new HomeNotice
                {
                    Glyph = "", Title = T("Notice_AppUpdateTitle", AppInfo.Name, newApp), Text = T("Notice_AppUpdateText", AppInfo.Version),
                    ButtonText = T("Notice_AppUpdateButton"), Action = () => MainWindow.OpenUrl(AppInfo.ReleasesUrl)
                });

            if (ToolsService.LocalPsadt() == null)
                list.Add(new HomeNotice
                {
                    Glyph = "", Title = T("Notice_PsadtMissingTitle"), Text = T("Notice_PsadtMissingText"),
                    ButtonText = T("Notice_DownloadNow"), Action = async () => await main.ShowSetupAsync(refresh: false)
                });
            else if (UpdateService.NewPsadtVersion is string newPsadt)
                list.Add(new HomeNotice
                {
                    Glyph = "", Title = T("Notice_PsadtUpdateTitle", newPsadt), Text = T("Notice_PsadtUpdateText", ToolsService.PsadtVersion),
                    ButtonText = T("Notice_UpdateNow"), Action = async () => await main.Settings.UpdatePsadtAsync()
                });

            if (!CatalogService.Instance.IsLoaded)
                list.Add(new HomeNotice
                {
                    Glyph = "", Title = T("Notice_CatalogMissingTitle"), Text = T("Notice_CatalogMissingText"),
                    ButtonText = T("Notice_DownloadNow"), Action = async () => await main.UpdateCatalogAsync(userRequested: true, force: true)
                });

            int outdated = history.Count(h => h.HasUpdate);
            if (outdated > 0)
                list.Add(new HomeNotice
                {
                    Glyph = "", Title = T("Notice_PackagesTitle", outdated), Text = T("Notice_PackagesText"),
                    ButtonText = T("Notice_PackagesButton"), Action = () => main.Navigate("packages")
                });
            return list;
        }

        private void Notice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is HomeNotice n) n.Action?.Invoke();
        }

        private void Catalog_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("catalog");
        private void Package_Click(object sender, RoutedEventArgs e) { MainWindow.Current.Navigate("editor"); MainWindow.Current.Editor.BrowseInstaller(); }
        private void Packages_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("packages");

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string p) Ui.OpenPath(p);
        }
    }
}
