using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using J365.IntunePSApp.Core;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    public enum MessageKind { Info, Warning, Error, Question }

    /// <summary>Message boxes in the application's own style and language (the Windows ones follow the OS language).</summary>
    public static class Ui
    {
        private static Window Owner
        {
            get
            {
                var w = Application.Current?.MainWindow;
                return w != null && w.IsLoaded && w.IsVisible && w.Left > -10000 ? w : null;
            }
        }

        public static void Info(string text) => Show(text, MessageKind.Info);
        public static void Warn(string text) => Show(text, MessageKind.Warning);
        public static void Error(string text) => Show(text, MessageKind.Error);

        /// <summary>Yes/No question. Returns true when the primary button was chosen.</summary>
        public static bool Confirm(string text, string yes = null, string no = null, bool danger = false)
            => Show(text, danger ? MessageKind.Warning : MessageKind.Question, yes ?? T("Common_Yes"), no ?? T("Common_No"));

        private static bool Show(string text, MessageKind kind, string primary = null, string secondary = null)
        {
            var d = new MessageDialog(text, kind, primary ?? T("Common_Ok"), secondary);
            SetOwner(d);
            return d.ShowDialog() == true;
        }

        public static void SetOwner(Window dialog)
        {
            var owner = Owner;
            if (owner != null && owner != dialog) dialog.Owner = owner;
            else { dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen; dialog.ShowInTaskbar = true; }
        }

        public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
        public static Brush Hex(string color) => (Brush)new BrushConverter().ConvertFromString(color);
        public static FontFamily Font(string key) => (FontFamily)Application.Current.FindResource(key);
        public static Style Style(string key) => (Style)Application.Current.FindResource(key);

        public static void CopyText(string text)
        {
            try { Clipboard.SetText(text ?? ""); } catch { }
        }

        public static void OpenPath(string path)
        {
            try { if (!string.IsNullOrEmpty(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        /// <summary>Opens Explorer with the file selected.</summary>
        public static void RevealFile(string file)
        {
            try { if (File.Exists(file)) Process.Start("explorer.exe", $"/select,\"{file}\""); } catch { }
        }
    }

    /// <summary>Shows only the file or folder name of a full path (the full path stays available for tooltips).</summary>
    public sealed class PathNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is string s ? Path.GetFileName(s.TrimEnd('\\')) : value;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Path of an image file -> image, read into memory at once: the file is not kept open, so the package folder it
    /// belongs to can still be renamed or deleted. A missing file gives no image.
    /// </summary>
    public sealed class PackageIconConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is string path ? MainWindow.LoadBitmap(path) : null;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Common look of the dialog windows (application theme and fonts).</summary>
    public class DialogWindow : Window
    {
        public DialogWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Title bar drawn in the application theme (light or dark), with the close button of the theme.
            Style = Ui.Style("DialogWindowStyle");
            CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.CloseWindowCommand, (s, e) => SystemCommands.CloseWindow(this)));
            FontFamily = Ui.Font("UiFont");
            FontSize = 13.5;
            UseLayoutRounding = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")); } catch { }
            // Dialogs designed for large screens never grow past the work area of a small one (notebooks at 125-150 % scaling).
            var area = SystemParameters.WorkArea;
            MaxWidth = Math.Max(360, area.Width - 32);
            MaxHeight = Math.Max(280, area.Height - 32);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ThemeManager.ApplyWindowFrame(this);
            if (MinWidth > MaxWidth) MinWidth = MaxWidth;
            if (MinHeight > MaxHeight) MinHeight = MaxHeight;
        }

        protected Style S(string key) => Ui.Style(key);

        protected Button MakeButton(string text, string style, RoutedEventHandler click, double left = 8)
        {
            var b = new Button { Content = text, Style = S(style), Margin = new Thickness(left, 0, 0, 0), MinWidth = 96 };
            b.Click += click;
            return b;
        }

        protected TextBlock Label(string text) => new TextBlock { Text = text, Style = S("Label") };
        protected TextBlock Hint(string text) => new TextBlock { Text = text, Style = S("Hint") };
    }

    public class MessageDialog : DialogWindow
    {
        public MessageDialog(string text, MessageKind kind, string primary, string secondary)
        {
            Title = AppInfo.Name;
            Width = 500;
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Min(680, MaxHeight);
            ResizeMode = ResizeMode.NoResize;

            var (glyph, color, soft) = kind switch
            {
                MessageKind.Warning => ("", "Warning", "WarningSoft"),
                MessageKind.Error => ("", "Danger", "DangerSoft"),
                MessageKind.Question => ("", "BrandText", "BrandSoft"),
                _ => ("", "BrandText", "BrandSoft")
            };

            var root = new DockPanel { Margin = new Thickness(24, 22, 24, 20) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            DockPanel.SetDock(bar, Dock.Bottom);
            if (!string.IsNullOrEmpty(secondary)) bar.Children.Add(MakeButton(secondary, "SecondaryButton", (s, e) => DialogResult = false));
            var ok = MakeButton(primary, "PrimaryButton", (s, e) => DialogResult = true);
            ok.IsDefault = true;
            bar.Children.Add(ok);
            root.Children.Add(bar);

            var icon = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(14), Background = Ui.Brush(soft), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 16, 0) };
            icon.Child = new TextBlock { Text = glyph, FontFamily = Ui.Font("IconFont"), FontSize = 20, Foreground = Ui.Brush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(icon, Dock.Left);
            root.Children.Add(icon);

            var body = new TextBox
            {
                Text = text ?? "", IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Foreground = Ui.Brush("Ink"), Padding = new Thickness(0), MinHeight = 44,
                VerticalContentAlignment = VerticalAlignment.Center, FocusVisualStyle = null, IsTabStop = false
            };
            root.Children.Add(body);
            Content = root;
            PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false; };
        }
    }

    public class TextViewerWindow : DialogWindow
    {
        public TextViewerWindow(string title, string text)
        {
            Title = title;
            Width = 1000; Height = 720;
            var dock = new DockPanel { Margin = new Thickness(18) };
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(bar, Dock.Bottom);
            bar.Children.Add(MakeButton(T("Dlg_CopyAll"), "SecondaryButton", (s, e) => Ui.CopyText(text)));
            bar.Children.Add(MakeButton(T("Common_Close"), "PrimaryButton", (s, e) => Close()));
            var tb = new TextBox
            {
                Text = text, IsReadOnly = true, FontFamily = Ui.Font("MonoFont"), FontSize = 12.5,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(10)
            };
            dock.Children.Add(bar);
            dock.Children.Add(tb);
            Content = dock;
        }
    }

    public class ProcessPickerWindow : DialogWindow
    {
        private readonly ListBox _list;
        public List<string> Selected { get; } = new List<string>();

        public ProcessPickerWindow()
        {
            Title = T("Proc_Title");
            Width = 600; Height = 640;
            var dock = new DockPanel { Margin = new Thickness(20) };
            var head = new StackPanel();
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock { Text = T("Proc_Heading"), Style = S("H2") });
            head.Children.Add(new TextBlock { Text = T("Proc_Hint"), Style = S("MutedText"), Margin = new Thickness(0, 4, 0, 12) });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(bar, Dock.Bottom);
            bar.Children.Add(MakeButton(T("Common_Cancel"), "SecondaryButton", (s, e) => { DialogResult = false; }));
            bar.Children.Add(MakeButton(T("Proc_AddSelected"), "PrimaryButton", (s, e) =>
            {
                Selected.AddRange(_list.SelectedItems.Cast<ListBoxItem>().Select(i => (string)i.Tag));
                DialogResult = true;
            }));
            _list = new ListBox { SelectionMode = SelectionMode.Multiple };
            var procs = Process.GetProcesses()
                .Select(p => { string t = ""; try { t = p.MainWindowTitle; } catch { } return (Name: p.ProcessName, Title: t); })
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Title: g.Select(x => x.Title).FirstOrDefault(t => !string.IsNullOrEmpty(t)) ?? ""))
                .OrderBy(p => string.IsNullOrEmpty(p.Title) ? 1 : 0).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var p in procs)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, Width = 200 });
                sp.Children.Add(new TextBlock { Text = p.Title, Foreground = Ui.Brush("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, Width = 300 });
                _list.Items.Add(new ListBoxItem { Content = sp, Tag = p.Name });
            }
            dock.Children.Add(head);
            dock.Children.Add(bar);
            dock.Children.Add(_list);
            Content = dock;
        }
    }

    /// <summary>Adds or edits a ready-made action (no scripting).</summary>
    public class ActionEditorWindow : DialogWindow
    {
        /// <summary>Fields whose value is the name of an extra file of the package.</summary>
        private static readonly HashSet<string> PackageFileFields = new HashSet<string> { "InstallDependency.Source", "CopyFile.Source", "CopyToUserProfiles.Source", "ImportRegFile.Source" };

        private readonly ListBox _types;
        private readonly StackPanel _form;
        private readonly ComboBox _phase;
        private readonly CheckBox _continue;
        private readonly Dictionary<string, FrameworkElement> _inputs = new Dictionary<string, FrameworkElement>();
        private readonly string _appName;
        private readonly List<string> _extraFiles;
        private ActionDef _def;

        public PackageAction Result { get; private set; }

        public ActionEditorWindow(PackageAction existing, string type, string appName, List<string> extraFileNames)
        {
            _appName = appName;
            _extraFiles = extraFileNames ?? new List<string>();
            Title = existing == null ? T("ActionDlg_TitleAdd") : T("ActionDlg_TitleEdit");
            Width = 960; Height = 660; MinWidth = 840; MinHeight = 540;

            var root = new Grid { Margin = new Thickness(20) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _types = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, ItemContainerStyle = S("AppListItem") };
            ScrollViewer.SetHorizontalScrollBarVisibility(_types, ScrollBarVisibility.Disabled);
            foreach (var d in ActionCatalog.All)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var ico = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = Ui.Brush("BrandSoft") };
                ico.Child = new TextBlock { Text = d.Glyph, FontFamily = Ui.Font("IconFont"), Foreground = Ui.Brush("Brand"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var txt = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                txt.Children.Add(new TextBlock { Text = d.Label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                txt.Children.Add(new TextBlock { Text = d.CategoryLabel, FontSize = 11.5, Foreground = Ui.Brush("Muted") });
                Grid.SetColumn(txt, 1);
                g.Children.Add(ico);
                g.Children.Add(txt);
                _types.Items.Add(new ListBoxItem { Content = g, Tag = d });
            }
            _types.SelectionChanged += (s, e) => { if (_types.SelectedItem is ListBoxItem li) BuildForm((ActionDef)li.Tag, null); };
            root.Children.Add(_types);

            var card = new Border { Style = S("Card") };
            Grid.SetColumn(card, 2);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var right = new StackPanel();
            _form = new StackPanel();
            right.Children.Add(_form);
            right.Children.Add(Label(T("ActionDlg_When")));
            _phase = new ComboBox();
            foreach (var p in ActionCatalog.Phases) _phase.Items.Add(new ComboBoxItem { Content = ActionCatalog.PhaseLabel(p), Tag = p });
            right.Children.Add(_phase);
            _continue = new CheckBox { Style = S("Switch"), Content = T("ActionDlg_Continue"), IsChecked = true, Margin = new Thickness(0, 16, 0, 0) };
            right.Children.Add(_continue);
            scroll.Content = right;
            card.Child = scroll;
            root.Children.Add(card);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 3);
            bar.Children.Add(MakeButton(T("Common_Cancel"), "SecondaryButton", (s, e) => DialogResult = false));
            bar.Children.Add(MakeButton(existing == null ? T("Common_Add") : T("Common_Save"), "PrimaryButton", (s, e) => Save()));
            root.Children.Add(bar);
            Content = root;

            string initial = existing?.Type ?? type ?? ActionCatalog.All[0].Type;
            foreach (ListBoxItem li in _types.Items)
                if (((ActionDef)li.Tag).Type == initial) { _types.SelectedItem = li; li.BringIntoView(); break; }
            if (existing != null) BuildForm(ActionCatalog.Get(existing.Type), existing);
        }

        private void BuildForm(ActionDef def, PackageAction values)
        {
            _def = def;
            _form.Children.Clear();
            _inputs.Clear();
            if (def == null) return;
            _form.Children.Add(new TextBlock { Text = def.Label, Style = S("H2") });
            _form.Children.Add(new TextBlock { Text = def.Description, Style = S("MutedText"), Margin = new Thickness(0, 4, 0, 4) });
            foreach (var f in def.Fields)
            {
                string val = values?.P(f.Key);
                if (string.IsNullOrEmpty(val)) val = DefaultFor(def, f);
                bool packageFile = PackageFileFields.Contains(def.Type + "." + f.Key);
                FrameworkElement input;
                switch (f.Kind)
                {
                    case "bool":
                        input = new CheckBox { Style = S("Switch"), Content = f.Label, IsChecked = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase), Margin = new Thickness(0, 16, 0, 0) };
                        _form.Children.Add(input);
                        break;
                    case "choice":
                        _form.Children.Add(Label(f.Label));
                        var cb = new ComboBox();
                        foreach (var o in f.Options) cb.Items.Add(new ComboBoxItem { Content = f.OptionLabel(o), Tag = o });
                        cb.SelectedItem = cb.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == val) ?? cb.Items[0];
                        input = cb;
                        _form.Children.Add(cb);
                        break;
                    default:
                        _form.Children.Add(Label(f.Label + (f.Required ? " *" : "")));
                        if (packageFile && _extraFiles.Count > 0)
                        {
                            // Pick one of the extra files, or type a sub-path / wildcard.
                            var pick = new ComboBox { IsEditable = true, Text = val ?? "" };
                            foreach (var name in _extraFiles) pick.Items.Add(name);
                            pick.Text = val ?? "";
                            input = pick;
                            _form.Children.Add(pick);
                            break;
                        }
                        var tb = new TextBox { Text = val ?? "" };
                        if (f.Kind == "multiline" || f.Kind == "code")
                        {
                            tb.AcceptsReturn = true;
                            tb.MinHeight = f.Kind == "code" ? 180 : 80;
                            tb.TextWrapping = f.Kind == "code" ? TextWrapping.NoWrap : TextWrapping.Wrap;
                            tb.VerticalContentAlignment = VerticalAlignment.Top;
                            tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                            tb.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                            if (f.Kind == "code") { tb.FontFamily = Ui.Font("MonoFont"); tb.AcceptsTab = true; }
                        }
                        input = tb;
                        _form.Children.Add(tb);
                        break;
                }
                if (!string.IsNullOrEmpty(f.Hint)) _form.Children.Add(Hint(f.Hint));
                if (packageFile && _extraFiles.Count == 0) _form.Children.Add(Hint(T("ActionDlg_NoExtraFiles")));
                _inputs[f.Key] = input;
            }
            string phase = values?.Phase ?? def.DefaultPhase;
            foreach (ComboBoxItem it in _phase.Items) if ((string)it.Tag == phase) _phase.SelectedItem = it;
            if (_phase.SelectedItem == null) _phase.SelectedIndex = 1;
            _continue.IsChecked = values?.ContinueOnError ?? def.Type != "InstallDependency";
        }

        private string DefaultFor(ActionDef def, ActionField f)
        {
            if (f.Key == "Name" && (def.Type == "RemoveDesktopShortcut" || def.Type == "RemoveStartMenuShortcut") && !string.IsNullOrEmpty(_appName)) return _appName + "*";
            if (f.Key == "Source" && PackageFileFields.Contains(def.Type + ".Source") && _extraFiles.Count == 1) return _extraFiles[0];
            return f.Default ?? "";
        }

        private void Save()
        {
            if (_def == null) return;
            var a = new PackageAction
            {
                Type = _def.Type,
                Phase = (_phase.SelectedItem as ComboBoxItem)?.Tag as string ?? _def.DefaultPhase,
                ContinueOnError = _continue.IsChecked == true
            };
            foreach (var kv in _inputs)
            {
                a.Params[kv.Key] = kv.Value switch
                {
                    CheckBox c => c.IsChecked == true ? "true" : "false",
                    ComboBox c when c.IsEditable => (c.Text ?? "").Trim(),
                    ComboBox c => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
                    TextBox t => t.Text,
                    _ => ""
                };
            }
            var errs = ActionCatalog.Validate(a);
            if (errs.Count > 0) { Ui.Warn(string.Join("\n", errs)); return; }
            Result = a;
            DialogResult = true;
        }
    }

    /// <summary>
    /// Publishing to Intune. Connected to the tenant (Settings > Microsoft Intune), the package is published inside the
    /// application with its progress, without a PowerShell window or a new sign-in. Not connected yet, the same dialog
    /// signs in or sets the connection up automatically. Running the script in its own window stays available.
    /// </summary>
    public class PublishDialog : DialogWindow
    {
        private readonly TextBox _group, _command;
        private readonly ComboBox _ifExists, _assign;
        private readonly StackPanel _groupPanel, _connection, _advanced, _matches;
        private readonly TextBlock _status, _existsNote;
        private readonly Button _publish, _toggleAdvanced;
        private readonly string _script, _displayName, _appName;
        private CancellationTokenSource _cts;
        private bool _busy;
        /// <summary>Intune refused the lookup of this dialog: the publication would stop at its first request too.</summary>
        private Exception _refusal;

        /// <summary>The PowerShell window of the publication, when "Run in a PowerShell window" was chosen.</summary>
        public Process Started { get; private set; }

        /// <summary>The options of a publication inside the application (the session is open).</summary>
        public PublishOptions InAppOptions { get; private set; }

        public PublishDialog(string publishScript, string appTitle, string displayName, string appName)
        {
            _script = publishScript;
            _displayName = string.IsNullOrWhiteSpace(displayName) ? appTitle : displayName;
            _appName = string.IsNullOrWhiteSpace(appName) ? _displayName : appName;
            Title = T("Publish_Title");
            Width = 720;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            var cfg = ConfigStore.Current;

            var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            root.Children.Add(new TextBlock { Text = T("Publish_Heading", appTitle), Style = S("H2"), TextWrapping = TextWrapping.Wrap });
            root.Children.Add(new TextBlock { Text = T("Publish_IntroInApp"), Style = S("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) });

            _connection = new StackPanel();
            root.Children.Add(_connection);
            _status = new TextBlock { Style = S("Hint"), Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
            root.Children.Add(_status);

            root.Children.Add(Label(T("Publish_IfExists")));
            _ifExists = new ComboBox();
            foreach (var (tag, key) in new[] { ("Ask", "Publish_ExistsAsk"), ("Update", "Publish_ExistsUpdate"), ("CreateNew", "Publish_ExistsNew"), ("Fail", "Publish_ExistsFail") })
                _ifExists.Items.Add(new ComboBoxItem { Tag = tag, Content = T(key) });
            _ifExists.SelectedIndex = 0;
            root.Children.Add(_ifExists);
            _existsNote = new TextBlock { Style = S("Hint"), Foreground = Ui.Brush("Warning"), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(_existsNote);
            _matches = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(_matches);

            root.Children.Add(Label(T("Publish_Assign")));
            _assign = new ComboBox();
            foreach (var (tag, key) in new[]
            {
                ("none|AllUsers", "Publish_AssignNone"), ("available|Group", "Publish_AssignAvailableGroup"), ("available|AllUsers", "Publish_AssignAvailableAll"),
                ("required|Group", "Publish_AssignRequiredGroup"), ("required|AllUsers", "Publish_AssignRequiredUsers"), ("required|AllDevices", "Publish_AssignRequiredDevices")
            })
                _assign.Items.Add(new ComboBoxItem { Tag = tag, Content = T(key) });
            _assign.SelectedIndex = 0;
            root.Children.Add(_assign);
            root.Children.Add(Hint(T("Publish_AssignHint")));
            _groupPanel = new StackPanel { Visibility = Visibility.Collapsed };
            _groupPanel.Children.Add(Label(T("Publish_Group")));
            _group = new TextBox { FontFamily = Ui.Font("MonoFont") };
            _groupPanel.Children.Add(_group);
            root.Children.Add(_groupPanel);

            // Advanced: the script in its own PowerShell window, or its command to copy (automation), with the connection of
            // this computer.
            _toggleAdvanced = MakeButton(T("Publish_ShowAdvanced"), "GhostButton", (s, e) => ToggleAdvanced(), 0);
            _toggleAdvanced.HorizontalAlignment = HorizontalAlignment.Left;
            _toggleAdvanced.Margin = new Thickness(-8, 14, 0, 0);
            root.Children.Add(_toggleAdvanced);
            _advanced = new StackPanel { Visibility = Visibility.Collapsed };
            _advanced.Children.Add(Label(T("Publish_Command")));
            _command = new TextBox { IsReadOnly = true, FontFamily = Ui.Font("MonoFont"), FontSize = 12, TextWrapping = TextWrapping.Wrap, MinHeight = 58, VerticalContentAlignment = VerticalAlignment.Top };
            _advanced.Children.Add(_command);
            var advancedBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            advancedBar.Children.Add(MakeButton(T("Publish_RunWindow"), "SecondaryButton", (s, e) => RunInWindow(), 0));
            advancedBar.Children.Add(MakeButton(T("Publish_CopyCommand"), "SecondaryButton", (s, e) => { if (Check() is PublishOptions o) Ui.CopyText(IntunePublisher.CommandLine(_script, o)); }));
            _advanced.Children.Add(advancedBar);
            root.Children.Add(_advanced);

            var bar = new DockPanel { Margin = new Thickness(0, 22, 0, 0), LastChildFill = false };
            var guide = MakeButton(T("Publish_Guide"), "GhostButton", (s, e) => Ui.OpenPath(AppInfo.PublishDocsUrl), 0);
            DockPanel.SetDock(guide, Dock.Left);
            bar.Children.Add(guide);
            var validate = MakeButton(T("Publish_Validate"), "GhostButton", async (s, e) => await ValidatePackageAsync((Button)s), 4);
            DockPanel.SetDock(validate, Dock.Left);
            bar.Children.Add(validate);
            _publish = MakeButton(T("Publish_PublishNow"), "PrimaryButton", async (s, e) => await PublishAsync());
            DockPanel.SetDock(_publish, Dock.Right);
            bar.Children.Add(_publish);
            var cancel = MakeButton(T("Common_Cancel"), "SecondaryButton", (s, e) => { _cts?.Cancel(); DialogResult = false; });
            DockPanel.SetDock(cancel, Dock.Right);
            bar.Children.Add(cancel);
            root.Children.Add(bar);
            // Small screens: the form scrolls instead of leaving the buttons out of reach.
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };

            _group.TextChanged += (s, e) => UpdateCommand();
            _ifExists.SelectionChanged += (s, e) => { _existsNote.Visibility = Visibility.Collapsed; _matches.Visibility = Visibility.Collapsed; UpdateCommand(); };
            _assign.SelectionChanged += (s, e) => UpdateCommand();
            Closed += (s, e) => _cts?.Cancel();
            UpdateCommand();
            RenderConnection();
        }

        private static string TagOf(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        private void ToggleAdvanced()
        {
            bool show = _advanced.Visibility != Visibility.Visible;
            _advanced.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            _toggleAdvanced.Content = show ? T("Publish_HideAdvanced") : T("Publish_ShowAdvanced");
        }

        /// <summary>The connection card: signed in, registration known but no session, or nothing configured.</summary>
        private void RenderConnection()
        {
            _connection.Children.Clear();
            bool signedIn = IntuneConnection.IsSignedIn;
            var box = new Border
            {
                Background = Ui.Brush(signedIn ? "SuccessSoft" : "BrandSoft"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 12, 14, 12),
                Margin = new Thickness(0, 14, 0, 0)
            };
            var dock = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Right);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var icon = new TextBlock { Text = signedIn ? "" : "", Style = S("Icon"), FontSize = 18, Foreground = Ui.Brush(signedIn ? "Success" : "Brand"), Margin = new Thickness(0, 2, 12, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(icon, Dock.Left);
            if (IntuneConnection.IsConfigured && IntuneConnection.NeedsSetup)
            {
                // The registration is gone or disabled, or the certificate cannot be used: the way out is a new connection.
                string problem = IntuneConnection.Problem;
                var broken = problem.Length == 0 ? AppCertificate.Describe(IntuneConnection.CertificateThumbprint) : null;
                text.Children.Add(new TextBlock { Text = problem.Length > 0 ? T("Settings_IntuneProblemTitle") : T("Publish_CertBroken"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock
                {
                    Text = problem.Length > 0 ? problem
                        : broken != null && broken.Expired ? T("Settings_IntuneCertExpired", broken.NotAfter.ToString("d", J365.IntunePSApp.Localization.Loc.Instance.Culture)) : T("Auth_ErrCertMissing"),
                    Style = S("MutedText"), TextWrapping = TextWrapping.Wrap
                });
                buttons.Children.Add(MakeButton(T("Settings_IntuneReconnect"), "PrimaryButton", async (s, e) => await ConnectAsync(true), 0));
            }
            else if (signedIn && IntuneConnection.UsesCertificate)
            {
                // The application publishes with the certificate of this computer: nothing to sign in.
                var cert = AppCertificate.Describe(IntuneConnection.CertificateThumbprint);
                text.Children.Add(new TextBlock { Text = T("Publish_ConnectedCert"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock { Text = T("Publish_ConnectedCertHint", IntuneConnection.TenantLabel, cert == null ? "?" : cert.NotAfter.ToString("d", J365.IntunePSApp.Localization.Loc.Instance.Culture)), Style = S("MutedText"), TextWrapping = TextWrapping.Wrap });
                buttons.Children.Add(MakeButton(T("Publish_ChangeConnection"), "SecondaryButton", async (s, e) => await ConnectAsync(true), 0));
            }
            else if (signedIn)
            {
                text.Children.Add(new TextBlock { Text = T("Publish_ConnectedAs", IntuneConnection.Account), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock { Text = T("Publish_ConnectedHint", IntuneConnection.SessionTenant.Length > 0 ? IntuneConnection.SessionTenant : IntuneConnection.TenantId), Style = S("MutedText"), TextWrapping = TextWrapping.Wrap });
                // A user connection depends on the role of the account (PIM): the certificate does not.
                text.Children.Add(new TextBlock { Text = T("Publish_DelegatedHint"), Style = S("Hint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                var useCert = MakeButton(T("Publish_UseCert"), "LinkButton", async (s, e) => await ConnectAsync(true), 0);
                useCert.HorizontalAlignment = HorizontalAlignment.Left;
                useCert.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(useCert);
                buttons.Children.Add(MakeButton(T("Publish_SwitchAccount"), "SecondaryButton", async (s, e) => await ConnectAsync(false), 0));
            }
            else if (IntuneConnection.IsConfigured)
            {
                text.Children.Add(new TextBlock { Text = T("Publish_NotSignedIn"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock { Text = T("Publish_NotSignedInHint"), Style = S("MutedText"), TextWrapping = TextWrapping.Wrap });
                buttons.Children.Add(MakeButton(T("Publish_SignIn"), "SecondaryButton", async (s, e) => await ConnectAsync(false), 0));
            }
            else
            {
                text.Children.Add(new TextBlock { Text = T("Publish_NotConnected"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock { Text = T("Publish_NotConnectedHint", IntuneConnection.RegistrationName), Style = S("MutedText"), TextWrapping = TextWrapping.Wrap });
                buttons.Children.Add(MakeButton(T("Publish_SetupAuto"), "PrimaryButton", async (s, e) => await ConnectAsync(true), 0));
            }
            dock.Children.Add(icon);
            dock.Children.Add(buttons);
            dock.Children.Add(text);
            box.Child = dock;
            _connection.Children.Add(box);
            // Connected, but Intune refuses: the tenant has no Intune (known for certain), or the lookup was refused. The
            // publication would stop at its first request: why, and what to do, before anything is sent.
            if (signedIn && !IntuneConnection.NeedsSetup && (IntuneSubscription.Blocks || _refusal != null)) _connection.Children.Add(RefusalBox());
            _publish.Content = signedIn && !IntuneConnection.NeedsSetup ? T("Publish_PublishNow") : T("Publish_SignInAndPublish");
        }

        private Border RefusalBox()
        {
            bool noPlan = IntuneSubscription.Blocks;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = noPlan ? T("Publish_NoPlanTitle") : T("Publish_RefusedTitle"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            string why = noPlan ? IntuneDiagnosis.Causes(null, IntuneConnection.UsesCertificate)[0] : IntuneConnectUi.RefusalText(_refusal);
            // What the answer rests on: the plan of Intune found in the tenant and its status.
            if (noPlan && IntuneSubscription.FoundLine is { Length: > 0 } found) why += Environment.NewLine + found;
            panel.Children.Add(new TextBlock { Text = why, Style = S("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (noPlan) actions.Children.Add(MakeButton(T(IntuneSubscription.FixLabelKey), "SecondaryButton", (s, e) => MainWindow.OpenUrl(IntuneSubscription.FixUrl), 0));
            actions.Children.Add(MakeButton(noPlan ? T("Settings_IntuneCheckPlanAgain") : T("Settings_IntuneCheckPlan"), "SecondaryButton", async (s, e) => await CheckPlanAsync(), noPlan ? 8 : 0));
            if (actions.Children.Count > 0) panel.Children.Add(actions);
            return new Border { Background = Ui.Brush("WarningSoft"), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 14, 12), Margin = new Thickness(0, 8, 0, 0), Child = panel };
        }

        /// <summary>"Check the subscription" from the dialog: an administrator signs in in the browser, the result is shown here.</summary>
        private async Task CheckPlanAsync()
        {
            if (_busy) return;
            _cts = new CancellationTokenSource();
            SetBusy(true, T("Settings_IntuneCheckingPlan"));
            try { await IntuneConnectUi.CheckPlanAsync(_cts.Token); }
            finally
            {
                SetBusy(false);
                if (IsLoaded) RenderConnection();
            }
        }

        private void SetBusy(bool busy, string status = null)
        {
            _busy = busy;
            _publish.IsEnabled = !busy;
            _connection.IsEnabled = !busy;
            _status.Text = status ?? "";
            _status.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task ConnectAsync(bool setup)
        {
            if (_busy) return;
            _cts = new CancellationTokenSource();
            // A refusal belongs to the connection that met it: another account, the certificate or a new connection is
            // asked again by the next lookup.
            _refusal = null;
            SetBusy(true, setup ? null : T("Publish_SigningIn"));
            try
            {
                // The connection window shows its own steps, over this dialog.
                if (setup) ConnectWindow.Run(this);
                else await IntuneConnectUi.SignInAsync(null, _cts.Token);
            }
            finally
            {
                SetBusy(false);
                if (IsLoaded)
                {
                    RenderConnection();
                    UpdateCommand();
                }
            }
        }

        private PublishOptions Options()
        {
            var assign = TagOf(_assign).Split('|');
            return new PublishOptions
            {
                // The connection of this computer: the command copied or run in a window uses it too.
                TenantId = IntuneConnection.TenantId,
                ClientId = IntuneConnection.ClientId,
                IfExists = TagOf(_ifExists),
                AssignIntent = assign[0],
                AssignTo = assign.Length > 1 ? assign[1] : "AllUsers",
                GroupId = _group.Text.Trim(),
                // The command copied or run in a window connects the same way as the application.
                CertificateThumbprint = IntuneConnection.UsesCertificate ? IntuneConnection.CertificateThumbprint : ""
            };
        }

        private void UpdateCommand()
        {
            var o = Options();
            _groupPanel.Visibility = o.AssignIntent != "none" && o.AssignTo == "Group" ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrWhiteSpace(o.TenantId)) o.TenantId = "<tenant>";
            if (string.IsNullOrWhiteSpace(o.ClientId)) o.ClientId = "<client-id>";
            if (string.IsNullOrWhiteSpace(o.GroupId)) o.GroupId = "<group-id>";
            _command.Text = IntunePublisher.CommandLine(_script, o);
        }

        /// <summary>The options when they are valid; otherwise a message and null.</summary>
        private PublishOptions Check()
        {
            var o = Options();
            string problem = IntunePublisher.Check(o);
            if (problem != null)
            {
                Ui.Warn(problem is "Publish_InvalidTenant" or "Publish_InvalidClient" ? T("Publish_SetupFirst") : T(problem));
                return null;
            }
            return o;
        }

        private async Task ValidatePackageAsync(Button button)
        {
            button.IsEnabled = false;
            try
            {
                if (await Task.Run(() => PowerShellPolicy.IsRestricted)) { Ui.Warn(T("Err_PowerShellRestricted")); return; }
                var result = await Task.Run(() => IntunePublisher.Validate(_script));
                new TextViewerWindow(T("Publish_ValidateTitle"), result.Output) { Owner = this, Width = 760, Height = 460 }.ShowDialog();
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
            finally { button.IsEnabled = true; }
        }

        /// <summary>Publishes inside the application: signs in when needed and settles what to do with an app of the same name.</summary>
        private async Task PublishAsync()
        {
            if (_busy) return;
            if (!IntuneConnection.IsSignedIn || IntuneConnection.NeedsSetup)
            {
                // Not connected, the registration gone, or a certificate that cannot be used: the connection window. A user
                // connection without a session signs in again.
                await ConnectAsync(IntuneConnection.NeedsSetup);
                if (!IntuneConnection.IsSignedIn || IntuneConnection.NeedsSetup) return;
            }
            var o = Check();
            if (o == null) return;
            _cts = new CancellationTokenSource();
            if (o.IfExists == "Ask" && _matches.Visibility == Visibility.Visible)
            {
                // Second click: the app chosen in the list (a previous version to update, or a new app).
                var chosen = _matches.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
                if (chosen == null) return;
                if (chosen == "new") o.IfExists = "CreateNew";
                else { o.IfExists = "Update"; o.AppId = chosen; }
            }
            else if (o.IfExists == "Ask")
            {
                SetBusy(true, T("Publish_LookingForApp"));
                try
                {
                    var found = await IntuneConnection.FindRelatedAppsAsync(_appName, _cts.Token);
                    SetBusy(false);
                    _refusal = null;
                    RenderConnection();
                    if (found.Count > 0)
                    {
                        // The user decides here, before anything is sent: update one of them (a new version keeps its
                        // assignments) or create another app.
                        _existsNote.Text = T("Publish_RelatedFound", _appName, found.Count);
                        _existsNote.Visibility = Visibility.Visible;
                        _matches.Children.Clear();
                        bool any = false;
                        foreach (var (id, name, version) in found.Take(8))
                        {
                            bool same = string.Equals(name, _displayName, StringComparison.OrdinalIgnoreCase);
                            _matches.Children.Add(new RadioButton
                            {
                                GroupName = "intuneApp", Tag = id, Margin = new Thickness(0, 4, 0, 0), IsChecked = !any,
                                Content = same ? T("Publish_UpdateSame", name) : T("Publish_UpdateOlder", name, string.IsNullOrEmpty(version) ? "?" : version)
                            });
                            any = true;
                        }
                        _matches.Children.Add(new RadioButton { GroupName = "intuneApp", Tag = "new", Margin = new Thickness(0, 4, 0, 0), Content = T("Publish_CreateNewApp", _displayName) });
                        _matches.Visibility = Visibility.Visible;
                        return;
                    }
                    o.IfExists = "Fail";   // nothing with this name: create (and stop if one appears meanwhile)
                }
                catch (OperationCanceledException) { SetBusy(false); return; }
                catch (Exception ex)
                {
                    SetBusy(false);
                    // A refusal of Intune would stop the publication as well: the dialog says why and what to do, and
                    // nothing is started. Any other failure of the lookup only means that the user says what to do with
                    // an app of the same name.
                    if (IntuneDiagnosis.IsRefusal(ex)) _refusal = ex;
                    else if (ex is AuthException) Ui.Error(T("Auth_Failed", ex.Message));
                    else Ui.Warn(T("Publish_LookupFailed", ex.Message));
                    RenderConnection();
                    return;
                }
            }
            bool broad = o.AssignIntent == "required" && o.AssignTo != "Group";
            if (broad && !Ui.Confirm(T("Publish_ConfirmBroad"), danger: true)) return;
            InAppOptions = o;
            DialogResult = true;
        }

        /// <summary>The publication in its own PowerShell window (it signs in by itself).</summary>
        private void RunInWindow()
        {
            var o = Check();
            if (o == null) return;
            bool broad = o.AssignIntent == "required" && o.AssignTo != "Group";
            if (broad && !Ui.Confirm(T("Publish_ConfirmBroad"), danger: true)) return;
            try
            {
                Started = IntunePublisher.Launch(_script, o);
                DialogResult = true;
            }
            catch (Exception ex) { Ui.Error(ex.Message); }
        }
    }
}
