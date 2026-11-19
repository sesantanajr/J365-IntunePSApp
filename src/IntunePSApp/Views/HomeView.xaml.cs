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
        /// <summary>Optional second, quieter button (for example "What's new").</summary>
        public string LinkText { get; set; }
        public Action LinkAction { get; set; }
        public Visibility LinkVisibility => string.IsNullOrEmpty(LinkText) ? Visibility.Collapsed : Visibility.Visible;
    }

    public partial class HomeView : UserControl, IRefreshable, ILocalized
    {
        public HomeView()
        {
            InitializeComponent();
            BuildSteps();
            SizeChanged += (s, e) => ApplyResponsiveLayout();
        }

        /// <summary>Page width (DIPs) under which the page switches to its narrow layout.</summary>
        private const double NarrowBelow = 900;
        private bool? _narrow;

        /// <summary>Narrow window: indicators in 2 columns, the two information cards stacked and no artwork in the banner.</summary>
        private void ApplyResponsiveLayout()
        {
            if (ActualWidth <= 0) return;
            bool narrow = ActualWidth < NarrowBelow;
            if (_narrow == narrow) return;
            _narrow = narrow;
            Stats.Columns = narrow ? 2 : 4;
            HeroArt.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            Info.ColumnDefinitions[1].Width = new GridLength(narrow ? 0 : 16);
            Info.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(RecentCard, narrow ? 0 : 2);
            Grid.SetRow(RecentCard, narrow ? 1 : 0);
            RecentCard.Margin = narrow ? new Thickness(0, 16, 0, 0) : new Thickness(0);
        }

        private void Blog_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(AppInfo.BlogUrl);

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
            UpdateIntuneCard();
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
                    ButtonText = T("Update_Now"), Action = async () => await main.UpdateAppAsync(),
                    LinkText = T("Notice_WhatsNew"), LinkAction = () => MainWindow.OpenUrl(AppInfo.ReleasesUrl)
                });

            // This (portable) copy is newer than the installed one: the Start menu still opens the old version.
            // (Not in screenshots: it depends on the computer that renders them.)
            if (!App.HasArg("--snapshot") && !AppPaths.IsInstalledCopy && InstallService.IsInstalled && InstallService.InstalledVersion is string installed
                && VersionUtil.Compare(installed, AppInfo.Version) < 0)
                list.Add(new HomeNotice
                {
                    Glyph = "", Title = T("Notice_InstalledOlderTitle", installed, AppInfo.Version), Text = T("Notice_InstalledOlderText"),
                    ButtonText = T("Notice_InstalledOlderButton"), Action = () => main.InstallForUser(InstallService.HasDesktopShortcut)
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

        /// <summary>Connected, configured without a session, or not connected yet.</summary>
        private void UpdateIntuneCard()
        {
            bool signedIn = IntuneConnection.IsSignedIn;
            StatIntune.Text = signedIn ? T("Home_StatIntuneOn") : IntuneConnection.IsConfigured ? T("Home_StatIntuneReady") : T("Home_StatIntuneOff");
            StatIntune.SetResourceReference(TextBlock.ForegroundProperty, signedIn ? "Success" : "Ink");
            IntuneDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, signedIn ? "Success" : IntuneConnection.IsConfigured ? "Warning" : "Faint");
            StatIntuneSub.Text = signedIn ? IntuneConnection.Account : IntuneConnection.IsConfigured ? T("Home_StatIntuneSignIn") : T("Home_StatIntuneSetup");
        }

        private void Intune_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MainWindow.Current.Navigate("settings");
            MainWindow.Current.Settings.ShowIntune();
        }

        private void Notice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is HomeNotice n) n.Action?.Invoke();
        }

        private void NoticeLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is HomeNotice n) n.LinkAction?.Invoke();
        }

        private void Catalog_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("catalog");
        private void StatApps_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => MainWindow.Current.Navigate("catalog");
        private void StatPackages_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => MainWindow.Current.Navigate("packages");
        private void StatPsadt_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MainWindow.Current.Navigate("settings");
            MainWindow.Current.Settings.ShowSection("tools");
        }
        private void Package_Click(object sender, RoutedEventArgs e) { MainWindow.Current.Navigate("editor"); MainWindow.Current.Editor.BrowseInstaller(); }
        private void Packages_Click(object sender, RoutedEventArgs e) => MainWindow.Current.Navigate("packages");

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string p) Ui.OpenPath(p);
        }
    }
}
