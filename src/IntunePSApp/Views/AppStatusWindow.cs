using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    /// <summary>
    /// "Status in Intune": how a published app is doing (installed, failed, pending...), its assignments and supersedence,
    /// and the devices with a problem first, read with the connection of this computer. Nothing is changed in the tenant.
    /// </summary>
    public class AppStatusWindow : DialogWindow
    {
        /// <summary>Devices listed in the window (the copied report has every device read).</summary>
        private const int ShownDevices = 300;

        private readonly string _appId, _title, _tenant;
        private readonly StackPanel _body;
        private readonly TextBlock _subtitle;
        private readonly Button _refresh, _copy;
        private CancellationTokenSource _cts;
        private AppDeployment _status;

        public AppStatusWindow(string appId, string title, string tenant = null, AppDeployment sample = null)
        {
            _appId = appId;
            _title = title;
            _tenant = tenant;
            Title = T("AppStatus_Title");
            Width = 920;
            Height = 700;
            MinWidth = 640;
            MinHeight = 420;

            var dock = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = T("AppStatus_Heading", title), Style = S("H2"), TextWrapping = TextWrapping.Wrap });
            _subtitle = new TextBlock { Style = S("MutedText"), Margin = new Thickness(0, 4, 0, 0) };
            header.Children.Add(_subtitle);
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            var bar = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
            _copy = MakeButton(T("AppStatus_Copy"), "GhostButton", (s, e) => CopyReport(), 0);
            _copy.IsEnabled = false;
            DockPanel.SetDock(_copy, Dock.Left);
            bar.Children.Add(_copy);
            var open = MakeButton(T("Ed_PublishOpen"), "GhostButton", (s, e) => MainWindow.OpenUrl(IntuneAppStatus.PortalUrl(_appId)), 4);
            DockPanel.SetDock(open, Dock.Left);
            bar.Children.Add(open);
            _refresh = MakeButton(T("AppStatus_Refresh"), "PrimaryButton", async (s, e) => await LoadAsync());
            DockPanel.SetDock(_refresh, Dock.Right);
            bar.Children.Add(_refresh);
            var close = Cancel(MakeButton(T("Common_Close"), "SecondaryButton", (s, e) => Close()));
            DockPanel.SetDock(close, Dock.Right);
            bar.Children.Add(close);
            DockPanel.SetDock(bar, Dock.Bottom);
            dock.Children.Add(bar);

            _body = new StackPanel();
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(0, 0, 6, 0) });
            Content = dock;
            Closed += (s, e) => _cts?.Cancel();

            if (sample != null)
            {
                _status = sample;
                Render();
            }
            else Loaded += async (s, e) => await LoadAsync();
        }

        /// <summary>The status of a published package (the connection window first when nothing is set up).</summary>
        public static void Open(Window owner, PublishResult result, string title)
        {
            if (result == null || !IntunePublisher.IsGuid(result.AppId)) return;
            if (IntuneConnection.NeedsSetup && !ConnectWindow.Run(owner)) return;
            new AppStatusWindow(result.AppId, title, result.TenantId) { Owner = owner }.ShowDialog();
        }

        private async Task LoadAsync()
        {
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            _refresh.IsEnabled = false;
            try
            {
                if (IntuneConnection.NeedsSetup)
                {
                    ShowMessage(T("AppStatus_NotConnected"), "WarningSoft", T("Settings_IntuneReconnect"), () => { if (ConnectWindow.Run(this)) _ = LoadAsync(); });
                    return;
                }
                if (!IntuneConnection.IsSignedIn)
                {
                    ShowMessage(T("AppStatus_SignInFirst"), "BrandSoft", T("Publish_SignIn"), async () =>
                    {
                        if (await IntuneConnectUi.SignInAsync(null, cts.Token) && IsLoaded) await LoadAsync();
                    });
                    return;
                }
                ShowMessage(T("AppStatus_Loading"), "BrandSoft", null, null);
                var status = await IntuneAppStatus.GetAsync(_appId, cts.Token);
                if (cts.IsCancellationRequested || !IsLoaded) return;
                _status = status;
                Render();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsLoaded || cts.IsCancellationRequested) return;
                string text = IntuneDiagnosis.IsRefusal(ex) ? IntuneConnectUi.RefusalText(ex) : ex is AuthException ? T("Auth_Failed", ex.Message) : ex.Message;
                ShowMessage(text, "DangerSoft", null, null);
            }
            finally
            {
                if (IsLoaded) _refresh.IsEnabled = true;
            }
        }

        /// <summary>One message instead of the status (reading, not connected, an error), with an optional action.</summary>
        private void ShowMessage(string text, string background, string action, Action click)
        {
            _body.Children.Clear();
            _copy.IsEnabled = false;
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("Ink2") });
            if (action != null)
            {
                var button = MakeButton(action, "SecondaryButton", (s, e) => click(), 0);
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 10, 0, 0);
                panel.Children.Add(button);
            }
            var box = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 16, 12), Child = panel };
            box.SetResourceReference(Border.BackgroundProperty, background);
            _body.Children.Add(box);
        }

        private void Render()
        {
            var d = _status;
            _body.Children.Clear();
            _copy.IsEnabled = true;
            var culture = Loc.Instance.Culture;
            string version = string.IsNullOrEmpty(d.Version) ? "" : T("AppStatus_Version", d.Version) + "  ·  ";
            string tenant = IntuneConnection.TenantLabel;
            _subtitle.Text = version + (string.IsNullOrWhiteSpace(tenant) ? T("AppStatus_ReadTime", d.Read.ToString("t", culture)) : T("AppStatus_ReadAt", d.Read.ToString("t", culture), tenant));

            if (!string.IsNullOrWhiteSpace(_tenant) && !SameTenant(_tenant))
                _body.Children.Add(Note(T("AppStatus_OtherTenant", _tenant, IntuneConnection.TenantLabel), "WarningSoft"));

            var tiles = new UniformGrid { Columns = 5 };
            tiles.Children.Add(Tile(InstallOutcome.Installed, "SuccessSoft", "Success"));
            tiles.Children.Add(Tile(InstallOutcome.Failed, "DangerSoft", "Danger"));
            tiles.Children.Add(Tile(InstallOutcome.Pending, "InfoSoft", "Info"));
            tiles.Children.Add(Tile(InstallOutcome.NotInstalled, "Subtle", "Ink2"));
            tiles.Children.Add(Tile(InstallOutcome.NotApplicable, "Subtle", "Muted"));
            _body.Children.Add(tiles);

            if (!d.ReportRead) _body.Children.Add(Note(d.ReportProblem.Length > 0 ? d.ReportProblem : T("AppStatus_ReportFailed", "?"), "WarningSoft"));
            else if (d.Devices.Count == 0)
                _body.Children.Add(Note(d.AssignmentsRead && d.Assignments.Count == 0 ? T("AppStatus_NoAssignment") : T("AppStatus_NoDevices"), "InfoSoft"));
            else if (d.Partial) _body.Children.Add(new TextBlock { Text = T("AppStatus_Partial", d.Devices.Count, d.Total), Style = S("Hint") });

            _body.Children.Add(Label(T("AppStatus_Assignments")));
            if (!d.AssignmentsRead) _body.Children.Add(Hint(T("AppStatus_NotRead")));
            else if (d.Assignments.Count == 0) _body.Children.Add(Hint(T("AppStatus_NoAssignmentShort")));
            foreach (string a in d.Assignments) _body.Children.Add(Bullet(a));

            if (d.Relations.Count > 0)
            {
                _body.Children.Add(Label(T("AppStatus_Supersedence")));
                foreach (var r in d.Relations)
                {
                    string other = PublishTexts.AppLabel(r.Name, r.Version);
                    string type = r.Type == "replace" ? T("AppStatus_TypeReplace") : T("AppStatus_TypeUpdate");
                    _body.Children.Add(Bullet(r.Supersedes ? T("AppStatus_Supersedes", other, type) : T("AppStatus_SupersededBy", other, type)));
                }
            }

            if (d.Devices.Count > 0)
            {
                _body.Children.Add(Label(T("AppStatus_Devices")));
                _body.Children.Add(DeviceTable(Ordered(d.Devices).Take(ShownDevices).ToList()));
                if (d.Devices.Count > ShownDevices) _body.Children.Add(Hint(T("AppStatus_MoreDevices", d.Devices.Count - ShownDevices)));
            }
        }

        /// <summary>The devices that need attention first: failed, pending, not installed; then the most recent.</summary>
        private static IEnumerable<DeviceInstallStatus> Ordered(IEnumerable<DeviceInstallStatus> devices)
        {
            int Rank(InstallOutcome o) => o switch
            {
                InstallOutcome.Failed => 0, InstallOutcome.Pending => 1, InstallOutcome.NotInstalled => 2,
                InstallOutcome.Unknown => 3, InstallOutcome.Installed => 4, _ => 5
            };
            return devices.OrderBy(x => Rank(x.Outcome)).ThenByDescending(x => x.Changed ?? DateTime.MinValue).ThenBy(x => x.Device, StringComparer.CurrentCultureIgnoreCase);
        }

        private Border Tile(InstallOutcome outcome, string soft, string strong)
        {
            var panel = new StackPanel();
            var number = new TextBlock { Text = _status.ReportRead ? _status.Count(outcome).ToString("N0", Loc.Instance.Culture) : "-", FontSize = 26, FontWeight = FontWeights.SemiBold };
            number.SetResourceReference(TextBlock.ForegroundProperty, strong);
            panel.Children.Add(number);
            panel.Children.Add(new TextBlock { Text = OutcomeLabel(outcome), Style = S("MutedText"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 8), Child = panel };
            tile.SetResourceReference(Border.BackgroundProperty, soft);
            return tile;
        }

        public static string OutcomeLabel(InstallOutcome outcome) => outcome switch
        {
            InstallOutcome.Installed => T("AppStatus_Installed"),
            InstallOutcome.Failed => T("AppStatus_Failed"),
            InstallOutcome.Pending => T("AppStatus_Pending"),
            InstallOutcome.NotInstalled => T("AppStatus_NotInstalled"),
            InstallOutcome.NotApplicable => T("AppStatus_NotApplicable"),
            _ => T("AppStatus_Unknown")
        };

        private static string OutcomeBrush(InstallOutcome outcome) => outcome switch
        {
            InstallOutcome.Installed => "Success",
            InstallOutcome.Failed => "Danger",
            InstallOutcome.Pending => "Info",
            _ => "Muted"
        };

        private Border Note(string text, string background)
        {
            var box = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("Ink2") } };
            box.SetResourceReference(Border.BackgroundProperty, background);
            return box;
        }

        private TextBlock Bullet(string text) => new TextBlock { Text = "•  " + text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("Ink2"), Margin = new Thickness(0, 2, 0, 2) };

        /// <summary>Device, user, state (with its detail), error (with what it usually means) and the last change.</summary>
        private FrameworkElement DeviceTable(List<DeviceInstallStatus> devices)
        {
            var grid = new Grid();
            // The last change gets room for a date and a time with AM/PM on one line.
            foreach (double width in new[] { 1.5, 1.9, 1.3, 2.5, 1.6 }) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
            void Cell(UIElement element, int row, int column)
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                grid.Children.Add(element);
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] headers = { T("AppStatus_ColDevice"), T("AppStatus_ColUser"), T("AppStatus_ColState"), T("AppStatus_ColError"), T("AppStatus_ColChanged") };
            for (int c = 0; c < headers.Length; c++)
                Cell(new TextBlock { Text = headers[c], Style = S("Overline"), Margin = new Thickness(0, 0, 10, 6) }, 0, c);
            int row = 1;
            var culture = Loc.Instance.Culture;
            foreach (var x in devices)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
                line.SetResourceReference(Border.BorderBrushProperty, "Line");
                Grid.SetColumnSpan(line, 5);
                Cell(line, row, 0);

                Cell(Text(x.Device, semibold: true), row, 0);
                Cell(Text(x.User), row, 1);
                var state = new StackPanel { Margin = new Thickness(0, 6, 10, 6) };
                var label = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Shape.FillProperty, OutcomeBrush(x.Outcome));
                label.Children.Add(dot);
                label.Children.Add(new TextBlock { Text = x.Outcome == InstallOutcome.Unknown && x.State.Length > 0 ? x.State : OutcomeLabel(x.Outcome), Foreground = Ui.Brush("Ink"), TextWrapping = TextWrapping.Wrap });
                state.Children.Add(label);
                if (x.Detail.Length > 0) state.Children.Add(new TextBlock { Text = x.Detail, Style = S("Hint"), Margin = new Thickness(14, 2, 0, 0) });
                Cell(state, row, 2);
                var error = new StackPanel { Margin = new Thickness(0, 6, 10, 6) };
                if (x.ErrorCode.Length > 0)
                {
                    error.Children.Add(new TextBlock { Text = x.ErrorCode, FontFamily = Ui.Font("MonoFont"), FontSize = 12.5, Foreground = Ui.Brush("Ink") });
                    if (IntuneAppStatus.ErrorHint(x.ErrorCode) is { Length: > 0 } hint) error.Children.Add(new TextBlock { Text = hint, Style = S("Hint"), Margin = new Thickness(0, 2, 0, 0) });
                }
                Cell(error, row, 3);
                Cell(Text(x.Changed?.ToString("g", culture) ?? ""), row, 4);
                row++;
            }
            return grid;
        }

        private TextBlock Text(string text, bool semibold = false) => new TextBlock
        {
            Text = text ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 10, 6), Foreground = Ui.Brush(semibold ? "Ink" : "Ink2"),
            FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal
        };

        /// <summary>The status as text (tab-separated rows), to paste in a ticket or a spreadsheet.</summary>
        private void CopyReport()
        {
            if (_status == null) return;
            Ui.CopyText(ReportText(_status, _title));
            MainWindow.Current?.Toast(T("Common_Copied"), "ok");
        }

        internal static string ReportText(AppDeployment d, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine(T("AppStatus_Heading", title));
            string tenant = IntuneConnection.TenantLabel;
            sb.AppendLine(string.IsNullOrWhiteSpace(tenant) ? T("AppStatus_ReadTime", d.Read.ToString("g", Loc.Instance.Culture)) : T("AppStatus_ReadAt", d.Read.ToString("g", Loc.Instance.Culture), tenant));
            sb.AppendLine(string.Join("  ·  ", new[] { InstallOutcome.Installed, InstallOutcome.Failed, InstallOutcome.Pending, InstallOutcome.NotInstalled, InstallOutcome.NotApplicable }
                .Select(o => $"{OutcomeLabel(o)}: {d.Count(o)}")));
            if (!d.ReportRead && d.ReportProblem.Length > 0) sb.AppendLine(d.ReportProblem);
            foreach (string a in d.Assignments) sb.AppendLine("- " + a);
            sb.AppendLine();
            sb.AppendLine(string.Join("\t", T("AppStatus_ColDevice"), T("AppStatus_ColUser"), T("AppStatus_ColState"), T("AppStatus_ColDetail"), T("AppStatus_ColError"), T("AppStatus_ColChanged")));
            foreach (var x in Ordered(d.Devices))
                sb.AppendLine(string.Join("\t", x.Device, x.User, OutcomeLabel(x.Outcome), x.Detail, x.ErrorCode, x.Changed?.ToString("g", Loc.Instance.Culture) ?? ""));
            return sb.ToString();
        }

        /// <summary>
        /// The package was published to this tenant. A domain and a GUID cannot be compared here: only two of the same kind
        /// that differ say it is another tenant.
        /// </summary>
        private static bool SameTenant(string tenant)
        {
            tenant = tenant.Trim();
            var known = new[] { IntuneConnection.TenantId, IntuneConnection.SessionTenant, IntuneConnection.TenantLabel }
                .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            if (known.Count == 0 || known.Any(t => t.Equals(tenant, StringComparison.OrdinalIgnoreCase))) return true;
            bool guid = Guid.TryParse(tenant, out _);
            return !known.Any(t => Guid.TryParse(t, out _) == guid);
        }

        /// <summary>Capture mode: a published 7-Zip with a pilot assignment, its supersedence and devices in every state.</summary>
        public static AppDeployment Sample()
        {
            var d = new AppDeployment
            {
                AppId = "6c1f0e2a-4b7d-4f3e-9a51-2d8e7c0b5f14", Name = "7-Zip", Version = "26.06", PublishingState = "published",
                ReportRead = true, AssignmentsRead = true, Read = DateTime.Now
            };
            d.Assignments.Add(T("AppStatus_Assignment", T("AppStatus_IntentRequired"), T("AppStatus_Group", "3f2b9c1e-7a4d-4e8b-b6c5-0d9e1f2a3b4c")));
            d.Assignments.Add(T("AppStatus_Assignment", T("AppStatus_IntentAvailable"), T("AppStatus_AllUsers")));
            d.Relations.Add(new AppRelation { Id = "1d7e4c2b-9f3a-4b8e-a5c6-7e2f1d0b9a83", Name = "7-Zip", Version = "25.01", Supersedes = true, Type = "update" });
            var now = DateTime.Now;
            void Add(string device, string user, InstallOutcome outcome, int minutesAgo, string error = "", string detail = "")
                => d.Devices.Add(new DeviceInstallStatus { Device = device, User = user, Outcome = outcome, Changed = now.AddMinutes(-minutesAgo), ErrorCode = error, Detail = detail, AppVersion = "26.06" });
            Add("CTS-LT-0142", "ana.souza@contoso.com", InstallOutcome.Failed, 35, "0x87D1041C");
            Add("CTS-LT-0187", "bruno.lima@contoso.com", InstallOutcome.Failed, 52, "0x80070642");
            Add("CTS-LT-0203", "carla.mendes@contoso.com", InstallOutcome.Pending, 8, "", Loc.Instance.IsPortuguese ? "Baixando o conteúdo" : "Downloading content");
            Add("CTS-DT-0031", "diego.rocha@contoso.com", InstallOutcome.Installed, 14);
            Add("CTS-LT-0099", "elisa.prado@contoso.com", InstallOutcome.Installed, 21);
            Add("CTS-LT-0110", "felipe.costa@contoso.com", InstallOutcome.Installed, 26);
            Add("CTS-DT-0057", "gabriela.nunes@contoso.com", InstallOutcome.Installed, 40);
            Add("CTS-LT-0164", "henrique.alves@contoso.com", InstallOutcome.Installed, 63);
            Add("CTS-LT-0172", "isabela.martins@contoso.com", InstallOutcome.Installed, 75);
            Add("CTS-LT-0221", "joao.ribeiro@contoso.com", InstallOutcome.Installed, 96);
            Add("CTS-LT-0008", "karina.teixeira@contoso.com", InstallOutcome.NotInstalled, 120);
            Add("CTS-ARM-0004", "lucas.freitas@contoso.com", InstallOutcome.NotApplicable, 180);
            d.Total = d.Devices.Count;
            return d;
        }
    }
}
