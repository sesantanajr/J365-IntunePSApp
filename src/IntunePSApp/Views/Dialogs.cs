using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp.Views
{
    /// <summary>Base visual das janelas de dialogo (tema e fontes do aplicativo).</summary>
    public class DialogWindow : Window
    {
        public DialogWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = (Brush)Application.Current.FindResource("PageBg");
            FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
            FontSize = 13.5;
            UseLayoutRounding = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResize;
            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")); } catch { }
        }

        protected Style S(string key) => (Style)Application.Current.FindResource(key);

        protected Button MakeButton(string text, string style, RoutedEventHandler click, double left = 8)
        {
            var b = new Button { Content = text, Style = S(style), Margin = new Thickness(left, 0, 0, 0), MinWidth = 96 };
            b.Click += click;
            return b;
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
            bar.Children.Add(MakeButton("Copiar tudo", "SecondaryButton", (s, e) => { try { Clipboard.SetText(text); } catch { } }));
            bar.Children.Add(MakeButton("Fechar", "PrimaryButton", (s, e) => Close()));
            var tb = new TextBox
            {
                Text = text, IsReadOnly = true, FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"), FontSize = 12.5,
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
            Title = "Escolher aplicativos em execução";
            Width = 600; Height = 640;
            var dock = new DockPanel { Margin = new Thickness(20) };
            var head = new StackPanel();
            DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(new TextBlock { Text = "Aplicativos em execução", Style = (Style)Application.Current.FindResource("H2") });
            head.Children.Add(new TextBlock { Text = "Abra o aplicativo que será instalado, depois clique nos itens para marcar e confirme.", Style = (Style)Application.Current.FindResource("MutedText"), Margin = new Thickness(0, 4, 0, 12) });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(bar, Dock.Bottom);
            bar.Children.Add(MakeButton("Cancelar", "SecondaryButton", (s, e) => { DialogResult = false; }));
            bar.Children.Add(MakeButton("Adicionar selecionados", "PrimaryButton", (s, e) =>
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
                sp.Children.Add(new TextBlock { Text = p.Title, Foreground = (Brush)Application.Current.FindResource("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, Width = 300 });
                _list.Items.Add(new ListBoxItem { Content = sp, Tag = p.Name });
            }
            dock.Children.Add(head);
            dock.Children.Add(bar);
            dock.Children.Add(_list);
            Content = dock;
        }
    }

    /// <summary>Adicionar/editar uma acao sem script.</summary>
    public class ActionEditorWindow : DialogWindow
    {
        private readonly ListBox _types;
        private readonly StackPanel _form;
        private readonly ComboBox _phase;
        private readonly CheckBox _continue;
        private readonly Dictionary<string, FrameworkElement> _inputs = new Dictionary<string, FrameworkElement>();
        private readonly string _appName;
        private readonly List<string> _extraFiles;
        private ActionDef _def;
        private readonly PackageAction _existing;

        public PackageAction Result { get; private set; }

        public ActionEditorWindow(PackageAction existing, string type, string appName, List<string> extraFiles)
        {
            _existing = existing;
            _appName = appName;
            _extraFiles = extraFiles ?? new List<string>();
            Title = existing == null ? "Adicionar ação" : "Editar ação";
            Width = 940; Height = 640; MinWidth = 820; MinHeight = 520;

            var root = new Grid { Margin = new Thickness(20) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
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
                var ico = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Background = (Brush)Application.Current.FindResource("BrandSoft") };
                ico.Child = new TextBlock { Text = d.Glyph, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), Foreground = (Brush)Application.Current.FindResource("Brand"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var txt = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                txt.Children.Add(new TextBlock { Text = d.Label, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                txt.Children.Add(new TextBlock { Text = d.Category, FontSize = 11.5, Foreground = (Brush)Application.Current.FindResource("Muted") });
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
            right.Children.Add(new TextBlock { Text = "Quando executar", Style = S("Label") });
            _phase = new ComboBox();
            foreach (var p in ActionCatalog.Phases) _phase.Items.Add(new ComboBoxItem { Content = ActionCatalog.PhaseLabel(p), Tag = p });
            right.Children.Add(_phase);
            _continue = new CheckBox { Style = S("Switch"), Content = "Continuar a instalação se esta ação falhar (recomendado)", IsChecked = true, Margin = new Thickness(0, 16, 0, 0) };
            right.Children.Add(_continue);
            scroll.Content = right;
            card.Child = scroll;
            root.Children.Add(card);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 3);
            bar.Children.Add(MakeButton("Cancelar", "SecondaryButton", (s, e) => DialogResult = false));
            bar.Children.Add(MakeButton(existing == null ? "Adicionar" : "Salvar", "PrimaryButton", (s, e) => Save()));
            root.Children.Add(bar);
            Content = root;

            string initial = existing?.Type ?? type ?? ActionCatalog.All[0].Type;
            foreach (ListBoxItem li in _types.Items)
                if (((ActionDef)li.Tag).Type == initial) { _types.SelectedItem = li; break; }
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
                FrameworkElement input;
                switch (f.Kind)
                {
                    case "bool":
                        input = new CheckBox { Style = S("Switch"), Content = f.Label, IsChecked = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase), Margin = new Thickness(0, 16, 0, 0) };
                        _form.Children.Add(input);
                        break;
                    case "choice":
                        _form.Children.Add(new TextBlock { Text = f.Label, Style = S("Label") });
                        var cb = new ComboBox();
                        foreach (var o in f.Options) cb.Items.Add(o);
                        cb.SelectedItem = f.Options.Contains(val) ? val : f.Options.FirstOrDefault();
                        input = cb;
                        _form.Children.Add(cb);
                        break;
                    default:
                        _form.Children.Add(new TextBlock { Text = f.Label + (f.Required ? " *" : ""), Style = S("Label") });
                        var tb = new TextBox { Text = val ?? "" };
                        if (f.Kind == "multiline" || f.Kind == "code")
                        {
                            tb.AcceptsReturn = true;
                            tb.MinHeight = f.Kind == "code" ? 180 : 80;
                            tb.TextWrapping = f.Kind == "code" ? TextWrapping.NoWrap : TextWrapping.Wrap;
                            tb.VerticalContentAlignment = VerticalAlignment.Top;
                            tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                            tb.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                            if (f.Kind == "code") { tb.FontFamily = (FontFamily)Application.Current.FindResource("MonoFont"); tb.AcceptsTab = true; }
                        }
                        input = tb;
                        _form.Children.Add(tb);
                        break;
                }
                if (!string.IsNullOrEmpty(f.Hint)) _form.Children.Add(new TextBlock { Text = f.Hint, Style = S("Hint") });
                if (f.Key == "Source" && def.Type.StartsWith("Copy") && _extraFiles.Count > 0)
                    _form.Children.Add(new TextBlock { Text = "Arquivos extras do pacote: " + string.Join(", ", _extraFiles), Style = S("Hint") });
                _inputs[f.Key] = input;
            }
            string phase = values?.Phase ?? def.DefaultPhase;
            foreach (ComboBoxItem it in _phase.Items) if ((string)it.Tag == phase) _phase.SelectedItem = it;
            if (_phase.SelectedItem == null) _phase.SelectedIndex = 1;
            _continue.IsChecked = values?.ContinueOnError ?? true;
        }

        private string DefaultFor(ActionDef def, ActionField f)
        {
            if (def.Type == "RemoveDesktopShortcut" && f.Key == "Name" && !string.IsNullOrEmpty(_appName)) return _appName + "*";
            if (def.Type == "CopyFile" && f.Key == "Source" && _extraFiles.Count == 1) return _extraFiles[0];
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
                    ComboBox c => c.SelectedItem as string ?? "",
                    TextBox t => t.Text,
                    _ => ""
                };
            }
            var errs = ActionCatalog.Validate(a);
            if (errs.Count > 0) { MessageBox.Show(this, string.Join("\n", errs), Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            Result = a;
            DialogResult = true;
        }
    }
}
