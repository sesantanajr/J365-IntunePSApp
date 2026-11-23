using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    /// <summary>
    /// Packages several applications of the database with the same settings (company, display mode, Intune toasts,
    /// deferral). Each one is downloaded, checked, analysed and built as the editor does; the list shows how far each got.
    /// </summary>
    public partial class BatchWindow : DialogWindow, ILocalized
    {
        private readonly ObservableCollection<BatchItem> _items;
        private CancellationTokenSource _cts;
        private bool _running, _closeWhenDone;

        /// <summary>Applications whose package was generated (the catalog takes them out of its selection).</summary>
        public IReadOnlyList<CatalogApp> Built => _items.Where(i => i.Succeeded).Select(i => i.App).ToList();

        public BatchWindow(IEnumerable<CatalogApp> apps)
        {
            InitializeComponent();
            var cfg = ConfigStore.Current;
            _items = new ObservableCollection<BatchItem>(BatchPackager.Plan(apps, cfg));
            Items.ItemsSource = _items;

            var o = BatchOptions.FromConfig(cfg);
            TxtCompany.Text = o.CompanyName;
            TxtOwner.Text = "";
            SelectTag(CmbMode, o.DeployMode);
            SelectTag(CmbNotify, o.IntuneNotifications);
            ChkDefer.IsChecked = o.AllowDefer;
            TxtDeferTimes.Text = o.DeferTimes.ToString();
            ChkDefer.Checked += (s, e) => UpdateHints();
            ChkDefer.Unchecked += (s, e) => UpdateHints();
            Closing += OnClosing;
            Loc.Instance.LanguageChanged += OnLanguageChanged;
            Closed += (s, e) => Loc.Instance.LanguageChanged -= OnLanguageChanged;
            OnLanguageChanged();
        }

        public void OnLanguageChanged()
        {
            SettingsView.RefreshSelection(CmbMode, CmbNotify);
            Title = T("Batch_Title");
            Heading.Text = T("Batch_Heading", _items.Count);
            foreach (var i in _items) i.Changed();
            UpdateHints();
            UpdateButtons();
        }

        private static void SelectTag(ComboBox c, string tag)
        {
            foreach (ComboBoxItem it in c.Items) if (string.Equals(it.Tag as string ?? "", tag ?? "", StringComparison.OrdinalIgnoreCase)) { c.SelectedItem = it; return; }
            c.SelectedIndex = 0;
        }

        private static string TagOf(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) UpdateHints(); }

        private void UpdateHints()
        {
            if (ModeHint == null) return;
            string mode = TagOf(CmbMode);
            bool silent = mode == "Silent";
            var probe = new PackageSettings { DeployMode = mode, IntuneNotifications = TagOf(CmbNotify) };
            string notify = PackageBuilder.IntuneNotifications(probe) switch
            {
                "hideAll" => T("Opt_NotifyHideAll"),
                "showReboot" => T("Opt_NotifyReboot"),
                _ => T("Opt_NotifyShowAll")
            };
            ModeHint.Text = (silent ? T("Ed_ModeHintSilent") : mode switch
            {
                "Interactive" => T("Ed_ModeHintInteractive"),
                "WhenNeeded" => T("Ed_ModeHintWhenNeeded"),
                _ => T("Ed_ModeHintAuto")
            }) + " " + T("Ed_NotifyEffective", notify);
            // A silent package has nothing to defer: the switch would only suggest a dialog.
            DeferPanel.IsEnabled = !silent && !_running;
            TxtDeferTimes.IsEnabled = ChkDefer.IsChecked == true;
            var cfg = ConfigStore.Current;
            DefaultsHint.Text = T("Batch_DefaultsHint", string.IsNullOrWhiteSpace(cfg.AccentColor) ? "—" : cfg.AccentColor, cfg.DialogStyle, cfg.OutputFolder);
            ListTitle.Text = T("Batch_ListTitle", _items.Count);
        }

        private void UpdateButtons()
        {
            int pending = _items.Count(i => !i.Succeeded && i.Installer != null);
            BtnStart.Content = _running ? T("Batch_Running") : _items.Any(i => i.IsFinished) && pending > 0 ? T("Batch_Retry", pending) : T("Batch_Start", pending);
            BtnStart.IsEnabled = !_running && pending > 0;
            BtnClose.Content = _running ? T("Common_Cancel") : T("Common_Close");
            SettingsCard.IsEnabled = !_running;
            BtnOpenFolder.Visibility = _items.Any(i => i.Succeeded) && !_running ? Visibility.Visible : Visibility.Collapsed;
            int ok = _items.Count(i => i.State == BatchState.Done), warn = _items.Count(i => i.State == BatchState.Warning);
            int skipped = _items.Count(i => i.State == BatchState.Skipped), failed = _items.Count(i => i.State is BatchState.Failed or BatchState.Cancelled);
            Summary.Text = !_items.Any(i => i.IsFinished) ? T("Batch_SummaryReady", _items.Count(i => i.Installer != null))
                : T("Batch_Summary", ok + warn, _items.Count, warn, skipped, failed);
        }

        private BatchOptions Options() => new BatchOptions
        {
            CompanyName = TxtCompany.Text.Trim(),
            Owner = TxtOwner.Text.Trim(),
            DeployMode = TagOf(CmbMode),
            IntuneNotifications = TagOf(CmbNotify),
            AllowDefer = ChkDefer.IsChecked == true,
            DeferTimes = int.TryParse(TxtDeferTimes.Text.Trim(), out int n) ? n : 3,
        };

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            var o = Options();
            if (o.AllowDefer && (o.DeferTimes < 1 || o.DeferTimes > 99)) { Ui.Warn(T("Val_DeferTimes")); return; }
            if (MainWindow.Current?.Editor?.IsBuilding == true) { Ui.Warn(T("Ed_BusyBuilding")); return; }
            if (ChkSaveDefaults.IsChecked == true)
            {
                var cfg = ConfigStore.Current;
                cfg.CompanyName = o.CompanyName;
                cfg.DeployMode = o.DeployMode;
                cfg.IntuneNotifications = o.IntuneNotifications;
                cfg.AllowDefer = o.AllowDefer;
                cfg.DeferTimes = Math.Clamp(o.DeferTimes, 1, 99);
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
            }
            int built = _items.Count(i => i.Succeeded);
            main?.Toast(T("Batch_Toast", built, _items.Count), built == _items.Count ? "ok" : "warn");
            if (_closeWhenDone) Close();
        }

        /// <summary>The bar of the taskbar and the summary follow the whole batch.</summary>
        private void Item_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BatchItem.Percent)) return;
            if (sender is BatchItem current && current.IsRunning) Items.ScrollIntoView(current);
            double done = _items.Sum(i => i.IsFinished ? 100 : i.State == BatchState.Building ? 50 + i.Percent / 2 : i.IsRunning ? i.Percent / 2 : 0);
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
            // The package being built is removed by the builder when cancelled; the window closes once it stopped.
            e.Cancel = true;
            if (Ui.Confirm(T("Batch_ConfirmCancel"), T("Batch_CancelBatch"), T("Common_No"), danger: true))
            {
                _closeWhenDone = true;
                _cts?.Cancel();
            }
        }

        private void OpenPackages_Click(object sender, RoutedEventArgs e)
        {
            var folder = _items.Where(i => i.Succeeded && !string.IsNullOrEmpty(i.PackageFolder)).Select(i => System.IO.Path.GetDirectoryName(i.PackageFolder)).FirstOrDefault();
            Ui.OpenPath(folder ?? ConfigStore.Current.OutputFolder);
        }
    }
}
