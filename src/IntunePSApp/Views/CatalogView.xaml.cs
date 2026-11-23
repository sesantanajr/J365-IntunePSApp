using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    public partial class CatalogView : UserControl, IRefreshable, ILocalized
    {
        private const int MaxResults = 400;
        private readonly DispatcherTimer _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        private CatalogApp _current;
        private int _searchVersion;
        private int _lastCount;
        private string _lastQuery = "";
        /// <summary>Applications ticked for batch packaging, in the order they were chosen.</summary>
        private readonly List<CatalogApp> _queue = new List<CatalogApp>();
        /// <summary>The list on screen: the database first, then the Microsoft Store as its answer arrives.</summary>
        private ObservableCollection<CatalogApp> _results = new ObservableCollection<CatalogApp>();
        private CancellationTokenSource _storeCts;
        /// <summary>Matches hidden by "Only supported", applications found in the Store, and the state of that search.</summary>
        private int _hidden, _storeCount, _detailsRequest;
        private string _storeState;

        public CatalogView()
        {
            InitializeComponent();
            _debounce.Tick += async (s, e) => { _debounce.Stop(); await RunSearchAsync(); };
            CatalogApp.QueuedChanged += OnQueuedChanged;
        }

        // ------------------------------------------------------------------ batch

        private void OnQueuedChanged(CatalogApp app)
        {
            if (app.IsQueued && !_queue.Contains(app)) _queue.Add(app);
            else if (!app.IsQueued) _queue.Remove(app);
            UpdateBatchBar();
        }

        private void UpdateBatchBar()
        {
            BatchBar.Visibility = _queue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BatchCount.Text = T("Catalog_BatchCount", _queue.Count);
            BatchNames.Text = string.Join(", ", _queue.Select(a => a.Name));
            BatchNames.ToolTip = string.Join("\n", _queue.Select(a => $"{a.Name} {a.Version}"));
            UpdateQueueButton();
        }

        private void UpdateQueueButton()
        {
            bool queued = _current?.IsQueued == true;
            BtnQueue.IsEnabled = _current?.CanDeploy == true;
            BtnQueueText.Text = queued ? T("Catalog_BatchRemove") : T("Catalog_BatchAdd");
            BtnQueueIcon.Text = queued ? "\uE738" : "\uE710";
        }

        /// <summary>
        /// A new copy of the database replaces every application object: the ticks move to the new objects of the same ids
        /// (an application that left the database leaves the selection).
        /// </summary>
        private void SyncQueue()
        {
            var c = CatalogService.Instance;
            if (_queue.Count == 0 || !c.IsLoaded || _queue.All(a => a.IsStore || ReferenceEquals(c.ById(a.Id), a))) return;
            var old = _queue.ToList();
            _queue.Clear();
            foreach (var a in old)
            {
                // Applications of the Store do not come from the database: they stay as they are.
                var app = a.IsStore ? a : c.ById(a.Id);
                if (app == null) continue;
                if (app.IsQueued) _queue.Add(app);
                else app.IsQueued = true;   // adds it through OnQueuedChanged
            }
            UpdateBatchBar();
        }

        private void Queue_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || !_current.CanDeploy) return;
            _current.IsQueued = !_current.IsQueued;
        }

        private void BatchClear_Click(object sender, RoutedEventArgs e)
        {
            foreach (var a in _queue.ToList()) a.IsQueued = false;
        }

        private async void Batch_Click(object sender, RoutedEventArgs e)
        {
            if (_queue.Count == 0) return;
            var main = MainWindow.Current;
            if (main.Editor?.IsBuilding == true) { main.Toast(T("Ed_BusyBuilding"), "warn"); return; }
            var w = new BatchWindow(_queue.ToList());
            Ui.SetOwner(w);
            w.ShowDialog();
            // What was generated leaves the selection; what was skipped or failed stays ticked, to be opened in the editor.
            foreach (var a in w.Built) a.IsQueued = false;
            main.Packages?.Refresh();
            main.Home?.Refresh();
            // A skipped application finished by hand: the editor opens with the installer the batch downloaded.
            if (w.OpenInEditor is BatchItem open)
            {
                main.Navigate("editor");
                await main.Editor.LoadFromCatalogAsync(open.InstallerFile, open.App, open.Installer);
            }
        }

        public void Refresh()
        {
            var c = CatalogService.Instance;
            bool loaded = c.IsLoaded;
            EmptyState.Visibility = loaded ? Visibility.Collapsed : Visibility.Visible;
            Main.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
            var culture = Loc.Instance.Culture;
            Subtitle.Text = loaded
                ? T("Catalog_Subtitle", c.Apps.Count.ToString("N0", culture), c.Generated?.ToLocalTime().ToString("g", culture) ?? "?")
                : T("Catalog_SubtitleEmpty");
            if (loaded && Results.Items.Count == 0) _ = RunSearchAsync();
            SyncQueue();
        }

        public void OnLanguageChanged()
        {
            Refresh();
            UpdateBatchBar();
            UpdateResultInfo();
            // Rebind so the localized labels of the rows (type, scope, silent switches) are read again.
            var selected = DInstallers.SelectedItem;
            ShowDetails(_current);
            if (selected != null) DInstallers.SelectedItem = selected;
        }

        public void FocusSearch(string text = null)
        {
            if (text != null) Search.Text = text;
            Search.Focus();
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed;
            ++_searchVersion; // Invalidate in-flight results immediately, before the debounce expires.
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
            var (list, hidden) = await Task.Run(() =>
            {
                var found = CatalogService.Instance.Search(q, only, MaxResults);
                // What "Only supported" hides (MSIX, ZIP, portable) is said: an application that seems missing can be found.
                int all = only && !string.IsNullOrWhiteSpace(q) ? CatalogService.Instance.Search(q, false, MaxResults).Count : found.Count;
                return (found, Math.Max(0, all - found.Count));
            });
            if (v != _searchVersion) return;
            _results = new ObservableCollection<CatalogApp>(list);
            Results.ItemsSource = _results;
            _lastCount = list.Count;
            _hidden = hidden;
            _lastQuery = q ?? "";
            _storeCount = 0;
            _storeState = null;
            UpdateResultInfo();
            if (list.Count > 0 && Results.SelectedIndex < 0) Results.SelectedIndex = 0;
            await SearchStoreAsync(q, v);
        }

        /// <summary>
        /// The Microsoft Store, searched live after the database: its applications (the msstore source of winget, such as
        /// Microsoft PC Manager, 9PM860492SZD) are not in the database. A product id is looked up by id, any other text by name.
        /// </summary>
        private async Task SearchStoreAsync(string q, int v)
        {
            _storeCts?.Cancel();
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return;
            var cts = _storeCts = new CancellationTokenSource();
            _storeState = "searching";
            UpdateResultInfo();
            try
            {
                var found = await StoreCatalog.SearchAsync(q, 25, cts.Token);
                if (v != _searchVersion) return;
                foreach (var app in found)
                    if (!_results.Any(a => string.Equals(a.Id, app.Id, StringComparison.OrdinalIgnoreCase))) _results.Add(app);
                _storeCount = found.Count;
                _storeState = "ok";
                if (Results.SelectedIndex < 0 && _results.Count > 0) Results.SelectedIndex = 0;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (v != _searchVersion) return;
                _storeState = "unavailable";
                Log.Warn(ex.Message);
            }
            UpdateResultInfo();
        }

        private void UpdateResultInfo()
        {
            string text = string.IsNullOrWhiteSpace(_lastQuery) ? T("Catalog_ResultsCurated", _lastCount)
                : _lastCount >= MaxResults ? T("Catalog_ResultsMax", MaxResults)
                : T("Catalog_Results", _lastCount);
            if (_hidden > 0) text += "  ·  " + T("Catalog_HiddenUnsupported", _hidden);
            if (_storeState == "searching") text += "  ·  " + T("Catalog_StoreSearching");
            else if (_storeState == "unavailable") text += "  ·  " + T("Catalog_StoreUnavailable");
            else if (_storeState == "ok") text += "  ·  " + T("Catalog_StoreResults", _storeCount);
            ResultInfo.Text = text;
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
            UpdateQueueButton();
            if (a == null) return;
            _detailsRequest++;
            DAvatar.Background = Ui.Hex(a.AvatarColor);
            DInitials.Text = a.Initials;
            DInitials.Visibility = Visibility.Visible;
            DLogo.Source = null;
            DLogo.Visibility = Visibility.Collapsed;
            DName.Text = a.Name;
            DPublisher.Text = a.Publisher ?? "";
            DVersion.Text = a.IsStore ? T("Catalog_StoreVersion") : T("Catalog_Version", a.Version);
            DStoreChip.Visibility = a.IsStore ? Visibility.Visible : Visibility.Collapsed;
            DStore.Visibility = a.IsStore ? Visibility.Visible : Visibility.Collapsed;
            // An application of the database that cannot be packaged (MSIX, ZIP, portable) may be in the Store.
            BtnSearchStore.Visibility = !a.IsStore && !a.IsSupported ? Visibility.Visible : Visibility.Collapsed;
            DNote.Text = a.IsStore ? T("Catalog_StoreNote") : T("Catalog_DownloadNote");
            DId.Text = a.Id;
            DLicense.Text = a.License ?? "";
            DLicenseChip.Visibility = string.IsNullOrEmpty(a.License) ? Visibility.Collapsed : Visibility.Visible;
            DCuratedChip.Visibility = a.HasCuration ? Visibility.Visible : Visibility.Collapsed;
            DDescription.Text = a.LocalizedDescription ?? "";
            DInstallers.ItemsSource = null;
            DInstallers.ItemsSource = a.Installers;
            var best = CatalogService.PickInstaller(a, ConfigStore.Current.PreferredArch, ConfigStore.Current.PreferredLocale);
            DInstallers.SelectedItem = best ?? a.Installers.FirstOrDefault();
            if (DInstallers.SelectedItem != null) DInstallers.ScrollIntoView(DInstallers.SelectedItem);
            BtnSite.IsEnabled = !string.IsNullOrEmpty(a.Homepage);

            if (a.Extra != null)
            {
                var parts = new List<string>();
                if (a.Extra.Processes?.Count > 0) parts.Add(T("Catalog_CurationProcesses", string.Join(", ", a.Extra.Processes)));
                if (!string.IsNullOrEmpty(a.Extra.NamePattern)) parts.Add(T("Catalog_CurationName", a.Extra.NamePattern));
                if (!string.IsNullOrEmpty(a.Extra.UninstallArgs)) parts.Add(T("Catalog_CurationUninstall", a.Extra.UninstallArgs));
                if (!string.IsNullOrEmpty(a.Extra.LocalizedNotes)) parts.Add(a.Extra.LocalizedNotes);
                DCurationText.Text = string.Join("\n", parts);
                DCuration.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else DCuration.Visibility = Visibility.Collapsed;

            ShowDependencies();
            if (a.IsStore) _ = LoadStoreDetailsAsync(a, _detailsRequest);
        }

        /// <summary>
        /// What the Store says about an application of the Store, read when it is shown: the description in the language of
        /// the interface, the architectures, how Intune will install it and the official logo.
        /// </summary>
        private async Task LoadStoreDetailsAsync(CatalogApp a, int request)
        {
            DStoreText.Text = T("Catalog_StoreLoading");
            try
            {
                var s = await StoreCatalog.GetAsync(a.Id, ConfigStore.Current.PreferredLocale, CancellationToken.None);
                if (request != _detailsRequest || !ReferenceEquals(_current, a)) return;
                // The application of the list learns what the Store said (the batch and the details use it).
                a.Description = s.Description;
                a.DescriptionPt = null;
                if (!string.IsNullOrEmpty(s.Publisher)) a.Publisher = s.Publisher;
                a.PrivacyUrl = s.PrivacyUrl;
                string scope = s.RunAsAccount == "user" ? "user" : "machine";
                a.Installers = (s.Architectures.Count > 0 ? s.Architectures : new List<string> { "" })
                    .Select(arch => new CatalogInstaller { Type = StoreCatalog.InstallerType, Arch = arch, Scope = scope }).ToList();
                DPublisher.Text = a.Publisher;
                DDescription.Text = s.Description;
                DInstallers.ItemsSource = a.Installers;
                DInstallers.SelectedIndex = 0;
                DStoreText.Text = T("Catalog_StoreInfo", s.RunAsAccount == "user" ? T("Catalog_StoreRunAsUser") : T("Catalog_StoreRunAsSystem"));
                var (logo, _) = await StoreCatalog.LogoAsync(s, CancellationToken.None);
                if (logo == null || request != _detailsRequest || !ReferenceEquals(_current, a)) return;
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 128;
                image.StreamSource = new MemoryStream(logo);
                image.EndInit();
                image.Freeze();
                DLogo.Source = image;
                DLogo.Visibility = Visibility.Visible;
                DInitials.Visibility = Visibility.Collapsed;
                DAvatar.SetResourceReference(Border.BackgroundProperty, "Subtle");
            }
            catch (Exception ex)
            {
                if (request == _detailsRequest) DStoreText.Text = T("Catalog_StoreDetailsFailed", ex.Message);
            }
        }

        /// <summary>An application of the database that cannot be packaged: the same name in the Microsoft Store.</summary>
        private void SearchStore_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;
            Search.Text = _current.Name;
            Search.Focus();
        }

        /// <summary>An application of the Store goes to Intune as "Microsoft Store app (new)": the publication of the batch window.</summary>
        private void PublishFromStore(CatalogApp app)
        {
            var main = MainWindow.Current;
            if (main.Editor?.IsBuilding == true) { main.Toast(T("Ed_BusyBuilding"), "warn"); return; }
            var w = new BatchWindow(new[] { app });
            Ui.SetOwner(w);
            w.ShowDialog();
            if (w.Built.Contains(app) && app.IsQueued) app.IsQueued = false;
        }

        private void ShowDependencies()
        {
            var deps = CatalogService.Instance.DependenciesOf(_current, DInstallers.SelectedItem as CatalogInstaller);
            DDependencyList.ItemsSource = deps;
            DDependencies.Visibility = deps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DInstallers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var i = DInstallers.SelectedItem as CatalogInstaller;
            bool store = _current?.IsStore == true;
            BtnPackage.IsEnabled = store || (i != null && i.IsSupported);
            BtnPackageText.Text = store ? T("Catalog_PublishStore") : T("Catalog_DownloadAndPackage");
            BtnPackageIcon.Text = store ? "\uE898" : "\uE896";
            ShowDependencies();
            if (store) { DInstallerInfo.Text = T("Catalog_StoreSelected"); return; }
            if (i == null) { DInstallerInfo.Text = ""; return; }
            DInstallerInfo.Text = i.IsSupported
                ? T("Catalog_Selected", i.TypeLabel, i.ArchLabel, i.ScopeLabel) + (string.IsNullOrEmpty(i.ProductCode) ? "" : T("Catalog_SelectedCode", i.ProductCode))
                : T("Catalog_Unsupported", i.TypeLabel);
        }

        private void Site_Click(object sender, RoutedEventArgs e)
        {
            // Only web pages: the database comes from a public source, and a "homepage" with another scheme (file:,
            // a protocol handler) would start a program.
            if (Uri.TryCreate(_current?.Homepage, UriKind.Absolute, out var page) && (page.Scheme == Uri.UriSchemeHttps || page.Scheme == Uri.UriSchemeHttp))
                MainWindow.OpenUrl(page.AbsoluteUri);
        }

        private async void Download_Click(object sender, RoutedEventArgs e) => await MainWindow.Current.UpdateCatalogAsync(userRequested: true, force: true);

        private async void Package_Click(object sender, RoutedEventArgs e)
        {
            var app = _current;
            if (app?.IsStore == true) { PublishFromStore(app); return; }
            var inst = DInstallers.SelectedItem as CatalogInstaller ?? CatalogService.PickInstaller(app);
            var main = MainWindow.Current;
            if (app == null || inst == null || !inst.IsSupported) { main.Toast(T("Catalog_PickSupported"), "warn"); return; }
            string dest = await DownloadWithBusyAsync(app, inst);
            if (dest == null) return;
            main.Navigate("editor");
            await main.Editor.LoadFromCatalogAsync(dest, app, inst);
        }

        /// <summary>Downloads an installer of the database showing the busy overlay. Returns null when cancelled or failed (already reported).</summary>
        public static async Task<string> DownloadWithBusyAsync(CatalogApp app, CatalogInstaller inst)
        {
            var main = MainWindow.Current;
            var ct = main.ShowBusy(T("Catalog_Downloading", app.Name, app.Version), T("Catalog_Connecting"));
            try
            {
                var progress = new Progress<(long, long?)>(p => main.BusyProgressReport(p.Item1, p.Item2));
                return await CatalogService.DownloadInstallerAsync(app, inst, progress, ct);
            }
            catch (OperationCanceledException)
            {
                main.Toast(T("Catalog_DownloadCancelled"), "warn");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(T("Log_DownloadFailed", ex.Message));
                main.HideBusy();
                Ui.Warn(T("Catalog_DownloadFailed", ex.Message));
                return null;
            }
            finally { main.HideBusy(); }
        }
    }
}
