using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    /// <summary>
    /// Packages several applications of the database with the same settings (company, silent or with notification)
    /// and, when asked, publishes each package to Intune with the same assignment: a deploy of several
    /// applications at once. Each one is downloaded, checked, analysed and built as the editor does; the list shows how
    /// far each got, and a skipped one opens in the editor.
    /// </summary>
    public partial class BatchWindow : DialogWindow, ILocalized
    {
        private readonly ObservableCollection<BatchItem> _items;
        private CancellationTokenSource _cts;
        private bool _running, _closeWhenDone;
        /// <summary>
        /// Between the click on Start and the batch itself: the sign-in in the browser (up to 5 minutes) and the
        /// confirmation. A second click then would start a second batch over the same items.
        /// </summary>
        private bool _preparing, _closed;
        private CancellationTokenSource _signInCts;
        /// <summary>Every application comes from the Microsoft Store: nothing to build, the window publishes them.</summary>
        private readonly bool _storeOnly;

        /// <summary>Applications whose package was generated (and published, when asked): the catalog takes them out of its selection.</summary>
        public IReadOnlyList<CatalogApp> Built => _items.Where(i => i.Succeeded).Select(i => i.App).ToList();

        /// <summary>A skipped or failed application the user chose to finish in the editor (the window closed for it).</summary>
        public BatchItem OpenInEditor { get; private set; }

        public BatchWindow(IEnumerable<CatalogApp> apps)
        {
            InitializeComponent();
            var cfg = ConfigStore.Current;
            _items = new ObservableCollection<BatchItem>(BatchPackager.Plan(apps, cfg));
            Items.ItemsSource = _items;
            _storeOnly = _items.Count > 0 && _items.All(i => i.App?.IsStore == true);

            var o = BatchOptions.FromConfig(cfg);
            TxtCompany.Text = o.CompanyName;
            TxtOwner.Text = "";
            SelectTag(CmbMode, o.DeployMode);
            SelectTag(CmbExisting, Core.ExistingVersions.Of(cfg));
            SelectTag(CmbIfExists, o.IfExists);
            SelectTag(CmbAssign, "none|AllUsers");
            if (_storeOnly)
            {
                ChkPublish.IsChecked = true;
                ChkPublish.IsEnabled = false;
            }

            Closing += OnClosing;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            IntuneConnection.Changed += OnConnectionChanged;
            Closed += (s, e) =>
            {
                _closed = true;
                _signInCts?.Cancel();
                Loc.Instance.LanguageChanged -= OnLanguageChanged;
                IntuneConnection.Changed -= OnConnectionChanged;
            };
            OnLanguageChanged();
        }

        private void OnConnectionChanged() => Dispatcher.BeginInvoke(new Action(UpdatePublish));

        public void OnLanguageChanged()
        {
            SettingsView.RefreshSelection(CmbExisting, CmbMode, CmbIfExists, CmbAssign);
            Title = T("Batch_Title");
            Heading.Text = _storeOnly ? Loc.Count("Batch_HeadingStore", _items.Count) : Loc.Count("Batch_Heading", _items.Count);
            foreach (var i in _items) i.Changed();
            UpdateHints();
            UpdatePublish();
            UpdateButtons();
        }

        private static void SelectTag(ComboBox c, string tag)
        {
            foreach (ComboBoxItem it in c.Items) if (string.Equals(it.Tag as string ?? "", tag ?? "", StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = it; return; }
            c.SelectedIndex = 0;
        }

        private static string TagOf(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private void Existing_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) UpdateExistingHint(); }

        /// <summary>What every package does with the copies already installed, and the applications the database protects.</summary>
        private void UpdateExistingHint()
        {
            if (ExistingHint == null) return;
            string choice = TagOf(CmbExisting);
            ExistingHint.Text = choice switch
            {
                Core.ExistingVersions.RemoveAll => T("Batch_ExistingHintRemoveAll"),
                Core.ExistingVersions.KeepNewer => T("Batch_ExistingHintKeepNewer"),
                _ => T("Ed_ExistingFlowOff")
            } + (ConfigStore.Current.InstallIfRemovalFails ? " " + T("Batch_ExistingFailOver") : choice != Core.ExistingVersions.InstallOver ? " " + T("Batch_ExistingFailStops") : "");
        }

        private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) UpdateHints(); }

        private void Publish_Changed(object sender, RoutedEventArgs e) { UpdatePublish(); UpdateButtons(); }

        private void Assign_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) UpdatePublish(); }

        private void UpdateHints()
        {
            if (ModeHint == null) return;
            var probe = new PackageSettings { DeployMode = TagOf(CmbMode) };
            ModeHint.Text = T(ScriptGenerator.IsSilent(probe) ? "Ed_ModeHintSilent" : "Ed_ModeHintNotify")
                + " " + T("Ed_NotifyEffective", EditorView.NotifyLabel(PackageBuilder.IntuneNotifications(probe)));
            var cfg = ConfigStore.Current;
            DefaultsHint.Text = T("Batch_DefaultsHint", string.IsNullOrWhiteSpace(cfg.AccentColor) ? "—" : cfg.AccentColor, cfg.DialogStyle, cfg.OutputFolder);
            ListTitle.Text = T("Batch_ListTitle", _items.Count);
            UpdateExistingHint();
        }

        /// <summary>The publication: the connection it will use, or what to do first; the group only for a group assignment.</summary>
        private void UpdatePublish()
        {
            if (PublishPanel == null) return;
            bool publish = ChkPublish.IsChecked == true;
            PublishPanel.Visibility = publish ? Visibility.Visible : Visibility.Collapsed;
            GroupPanel.Visibility = publish && TagOf(CmbAssign).EndsWith("|Group", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
            bool ready = IntuneConnection.IsSignedIn && !IntuneConnection.NeedsSetup;
            PublishConnection.Text = !ready ? T("Batch_PublishNotConnected")
                : IntuneSubscription.Blocks ? T("Ed_PublishCalloutNoPlan", IntuneConnection.Account, IntuneConnection.TenantLabel)
                : T("Batch_PublishConnection", IntuneConnection.Account, IntuneConnection.TenantLabel);
            // Applications of the Store are only published: without the publication they would be skipped.
            int store = _items.Count(i => i.App?.IsStore == true);
            if (!publish && store > 0) PublishConnection.Text = T("Batch_StoreNeedsPublishHint", store) + " " + PublishConnection.Text;
            PublishConnection.SetResourceReference(TextBlock.ForegroundProperty, ready && !IntuneSubscription.Blocks && (publish || store == 0) ? "Muted" : "Warning");
        }

        private void UpdateButtons()
        {
            int pending = _items.Count(i => !i.Succeeded && i.Installer != null);
            bool publish = ChkPublish.IsChecked == true;
            BtnStart.Content = _running ? T("Batch_Running")
                : _items.Any(i => i.IsFinished) && pending > 0 ? Loc.Count("Batch_Retry", pending)
                : _storeOnly ? Loc.Count("Batch_StartStore", pending)
                : publish ? Loc.Count("Batch_StartPublish", pending) : Loc.Count("Batch_Start", pending);
            BtnStart.IsEnabled = !_running && !_preparing && pending > 0;
            BtnClose.Content = _running ? T("Common_Cancel") : T("Common_Close");
            SettingsCard.IsEnabled = !_running;
            BtnOpenFolder.Visibility = _items.Any(i => i.Built) && !_running ? Visibility.Visible : Visibility.Collapsed;
            BtnReport.Visibility = _items.Any(i => i.IsFinished) && !_running ? Visibility.Visible : Visibility.Collapsed;
            if (!_items.Any(i => i.IsFinished))
            {
                Summary.Text = Loc.Count("Batch_SummaryReady", _items.Count(i => i.Installer != null));
                return;
            }
            int built = _items.Count(i => i.Built), warn = _items.Count(i => i.State == BatchState.Warning);
            int skipped = _items.Count(i => i.State == BatchState.Skipped), failed = _items.Count(i => i.State is BatchState.Failed or BatchState.Cancelled or BatchState.PublishFailed);
            int published = _items.Count(i => i.State == BatchState.Published);
            Summary.Text = T("Batch_Summary", built, _items.Count, warn, skipped, failed) + (published > 0 ? "  ·  " + T("Batch_SummaryPublished", published) : "");
        }

        private BatchOptions Options()
        {
            var assign = TagOf(CmbAssign).Split('|');
            return new BatchOptions
            {
                CompanyName = TxtCompany.Text.Trim(),
                Owner = TxtOwner.Text.Trim(),
                DeployMode = DeployModes.Normalize(TagOf(CmbMode)),
                ExistingVersions = Core.ExistingVersions.Normalize(TagOf(CmbExisting)),
                Publish = ChkPublish.IsChecked == true,
                IfExists = TagOf(CmbIfExists),
                AssignIntent = assign[0],
                AssignTo = assign.Length > 1 ? assign[1] : "AllUsers",
                GroupId = TxtGroup.Text.Trim(),
            };
        }

        /// <summary>
        /// Before a batch that publishes: the connection (set up, signed in), a tenant that has Intune, a PowerShell that
        /// may run the script, a valid group, and an explicit yes with the tenant and the assignment spelled out.
        /// </summary>
        private async Task<bool> ReadyToPublishAsync(BatchOptions o)
        {
            if (IntuneConnection.NeedsSetup && !ConnectWindow.Run(this)) return false;
            if (!IntuneConnection.IsSignedIn)
            {
                // Closing the window stops the wait for the browser.
                _signInCts = new CancellationTokenSource();
                try { await IntuneConnectUi.SignInAsync(null, _signInCts.Token); }
                catch (OperationCanceledException) { return false; }
                catch (Exception ex) { if (!_closed) Ui.Error(T("Auth_Failed", ex.Message)); return false; }
                finally { _signInCts.Dispose(); _signInCts = null; }
                if (_closed) return false;
            }
            UpdatePublish();
            if (!IntuneConnection.IsSignedIn || IntuneConnection.NeedsSetup) { Ui.Warn(T("Batch_PublishNotConnected")); return false; }
            if (IntuneSubscription.Blocks) { Ui.Warn(T("Ed_PublishCalloutNoPlan", IntuneConnection.Account, IntuneConnection.TenantLabel)); return false; }
            if (await Task.Run(() => PowerShellPolicy.IsRestricted)) { Ui.Warn(T("Err_PowerShellRestricted")); return false; }
            if (IntunePublisher.Check(o.PublishOptions(IntuneConnection.UsesCertificate)) is string problem) { Ui.Warn(T(problem)); return false; }
            int count = _items.Count(i => !i.Succeeded && i.Installer != null);
            string assignment = (CmbAssign.SelectedItem as ComboBoxItem)?.Content as string ?? "";
            string ifExists = (CmbIfExists.SelectedItem as ComboBoxItem)?.Content as string ?? "";
            return Ui.Confirm(T("Batch_ConfirmPublish", count, IntuneConnection.TenantLabel, assignment, ifExists), T("Batch_PublishNow"), T("Common_Cancel"),
                danger: o.AssignIntent == "required");
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_running || _preparing) return;
            var o = Options();

            if (MainWindow.Current?.Editor?.IsBuilding == true) { Ui.Warn(T("Ed_BusyBuilding")); return; }
            if (o.Publish)
            {
                _preparing = true;
                UpdateButtons();
                bool ready;
                try { ready = await ReadyToPublishAsync(o); }
                finally
                {
                    _preparing = false;
                    if (!_closed) UpdateButtons();
                }
                // Closed while signing in or confirming: nothing starts without the window.
                if (!ready || _closed) return;
            }
            if (ChkSaveDefaults.IsChecked == true)
            {
                var cfg = ConfigStore.Current;
                cfg.CompanyName = o.CompanyName;
                cfg.DeployMode = o.DeployMode;
                Core.ExistingVersions.Set(cfg, o.ExistingVersions);
                ConfigStore.Save();
            }
            _running = true;
            _cts = new CancellationTokenSource();
            UpdateButtons();
            UpdateHints();
            var main = MainWindow.Current;
            main?.SetStatus(T("Batch_Status", _items.Count), true);
            foreach (var i in _items) i.PropertyChanged += Item_PropertyChanged;
            try
            {
                await BatchPackager.RunAsync(_items, o, _cts.Token);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Ui.Error(ex.Message);
            }
            finally
            {
                foreach (var i in _items) i.PropertyChanged -= Item_PropertyChanged;
                _running = false;
                _cts.Dispose();
                _cts = null;
                main?.SetStatus(null);
                main?.SetTaskbarProgress(null);
                UpdateButtons();
                UpdateHints();
                UpdatePublish();
            }
            int done = _items.Count(i => i.Succeeded);
            main?.Toast(T("Batch_Toast", done, _items.Count), done == _items.Count ? "ok" : "warn");
            if (_closeWhenDone) Close();
        }

        /// <summary>The bar of the taskbar and the summary follow the whole batch.</summary>
        private void Item_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BatchItem.Percent)) return;
            if (sender is BatchItem current && current.IsRunning) Items.ScrollIntoView(current);
            double done = _items.Sum(i => i.IsFinished ? 100 : i.State is BatchState.Building or BatchState.Publishing ? 50 + i.Percent / 2 : i.IsRunning ? i.Percent / 2 : 0);
            MainWindow.Current?.SetTaskbarProgress(_items.Count == 0 ? 0 : done / (_items.Count * 100d));
            UpdateButtons();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_running) { _cts?.Cancel(); return; }
            Close();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_running) return;
            // The package being built is removed by the builder when cancelled, a publication stops at a safe point; the
            // window closes once everything stopped.
            e.Cancel = true;
            if (Ui.Confirm(T("Batch_ConfirmCancel"), T("Batch_CancelBatch"), T("Common_No"), danger: true))
            {
                _closeWhenDone = true;
                _cts?.Cancel();
            }
        }

        private void OpenInEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_running || (sender as FrameworkElement)?.Tag is not BatchItem item || !item.CanOpenInEditor) return;
            OpenInEditor = item;
            Close();
        }

        private void CopyReport_Click(object sender, RoutedEventArgs e)
        {
            Ui.CopyText(BatchPackager.Report(_items));
            MainWindow.Current?.Toast(T("Batch_ReportCopied"), "ok");
        }

        private void OpenPackages_Click(object sender, RoutedEventArgs e)
        {
            var folder = _items.Where(i => i.Built && !string.IsNullOrEmpty(i.PackageFolder)).Select(i => System.IO.Path.GetDirectoryName(i.PackageFolder)).FirstOrDefault();
            Ui.OpenPath(folder ?? ConfigStore.Current.OutputFolder);
        }

        /// <summary>Capture mode: sample items in every state, nothing is downloaded or built.</summary>
        internal void ShowSampleStates()
        {
            var states = new[] { BatchState.Published, BatchState.Done, BatchState.Warning, BatchState.Publishing, BatchState.Building, BatchState.Skipped, BatchState.Waiting };
            for (int k = 0; k < _items.Count; k++)
            {
                var state = states[k % states.Length];
                _items[k].Update(state, state switch
                {
                    BatchState.Published => T("Ed_PublishCreated", _items[k].App?.Name, "contoso.onmicrosoft.com"),
                    BatchState.Done => T("Batch_Built", (_items[k].App?.Name ?? "app") + ".intunewin"),
                    BatchState.Warning => T("Batch_GuessedName", (_items[k].App?.Name ?? "App") + "*"),
                    BatchState.Publishing => T("Publish_StepUploading", "48.2 MB"),
                    BatchState.Building => T("Log_CreatingIntuneWin"),
                    BatchState.Skipped => T("Batch_NoSilentArgs"),
                    _ => ""
                }, state is BatchState.Publishing ? 62 : state is BatchState.Building ? 35 : 0);
                // A sample that got past its build has a package, as a real one would: the summary counts it as generated.
                if (state is BatchState.Published or BatchState.Done or BatchState.Warning or BatchState.Publishing)
                {
                    var app = _items[k].App;
                    _items[k].Result = new BuildResult { PackageFolder = System.IO.Path.Combine(ConfigStore.Current.OutputFolder ?? "", Names.Safe($"{app?.Name}_{app?.Version}")) };
                }
            }
            ChkPublish.IsChecked = true;
            UpdateButtons();
        }
    }
}
