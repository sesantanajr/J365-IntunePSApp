using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using J365.IntunePSApp.Core;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    /// <summary>A step of the connection, as the progress list shows it.</summary>
    public sealed class ConnectStepRow : INotifyPropertyChanged
    {
        private string _state = "Pending", _detail = "";
        public SetupStep Step { get; init; }
        public int Number { get; init; }
        public string Title { get; init; } = "";
        /// <summary>Pending, Working, Done, Warning or Failed (the template picks icon and colors).</summary>
        public string State { get => _state; set { _state = value; Raise(nameof(State)); } }
        public string Detail { get => _detail; set { _detail = value ?? ""; Raise(nameof(Detail)); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Connects the application to Microsoft Intune, three ways: the automatic setup (an administrator signs in; the
    /// registration is created and approved), a registration that already exists (connection code or IDs), or the same
    /// registration created with the Azure CLI of Azure Cloud Shell. Every step is shown, and a failure comes with its
    /// likely causes and the action that fixes each one.
    /// </summary>
    public partial class ConnectWindow : DialogWindow
    {
        private enum Page { Choose, Code, Shell, Progress, Result }
        private enum Flow { Auto, Cert, Code, Shell }

        private readonly ObservableCollection<ConnectStepRow> _steps = new ObservableCollection<ConnectStepRow>();
        private Page _page;
        private Flow _flow = Flow.Auto;
        private CancellationTokenSource _cts;
        private bool _running;
        private bool _succeeded;
        private string _consentUrl;
        private string _errorDetails = "";
        private ConnectStepRow _current;
        /// <summary>Why the last test of Intune failed, to explain it on the result page.</summary>
        private Exception _testFailure;
        /// <summary>A certificate the setup created that is not the connection yet (it becomes it once it obtains a token).</summary>
        private PendingCertificate _pending;
        /// <summary>A connection with an account is replacing the certificate: only one that reads Intune may take its place.</summary>
        private bool _replacingCertificate;

        /// <summary>True when the window closed with the application connected (signed in).</summary>
        public bool Connected => _succeeded;

        public ConnectWindow(string start = null)
        {
            InitializeComponent();
            StepList.ItemsSource = _steps;
            TxtAdminHint.Text = IntuneConnection.UserAccount;
            TxtScript.Text = ConnectScripts.Bash;
            if (start == "code") OptCode.IsChecked = true;
            else if (start == "delegated") OptAuto.IsChecked = true;
            Closing += (s, e) =>
            {
                if (!_running) return;
                if (!Ui.Confirm(T("Connect_ConfirmStop"), T("Connect_Stop"), T("Common_No"))) { e.Cancel = true; return; }
                _cts?.Cancel();
            };
            // A new certificate that never became the connection does not stay on the computer.
            Closed += (s, e) => DiscardPending();
            ShowPage(Page.Choose);
        }

        /// <summary>Opens the window over <paramref name="owner"/> (the active window when null). True when connected.</summary>
        public static bool Run(Window owner = null, string start = null)
        {
            var w = new ConnectWindow(start);
            owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) ?? Application.Current?.MainWindow;
            if (owner != null && owner.IsLoaded && owner.IsVisible && owner != w) w.Owner = owner; else Ui.SetOwner(w);
            w.ShowDialog();
            return w.Connected;
        }

        // ------------------------------------------------------------------ pages

        private void ShowPage(Page page)
        {
            _page = page;
            PageScroll?.ScrollToTop();
            PageChoose.Visibility = page == Page.Choose ? Visibility.Visible : Visibility.Collapsed;
            PageCode.Visibility = page == Page.Code ? Visibility.Visible : Visibility.Collapsed;
            PageShell.Visibility = page == Page.Shell ? Visibility.Visible : Visibility.Collapsed;
            PageProgress.Visibility = page == Page.Progress ? Visibility.Visible : Visibility.Collapsed;
            PageResult.Visibility = page == Page.Result ? Visibility.Visible : Visibility.Collapsed;
            BtnExtra.Visibility = Visibility.Collapsed;
            BtnPrimary.IsEnabled = BtnSecondary.IsEnabled = true;
            BtnSecondary.Visibility = Visibility.Visible;
            switch (page)
            {
                case Page.Choose:
                    SetHeader("", T("Connect_Title"), T("Connect_Subtitle"), "BrandSoft", "BrandText");
                    BtnPrimary.Content = T("Connect_Continue");
                    BtnSecondary.Content = T("Common_Cancel");
                    AutoInfo.Visibility = OptAuto.IsChecked == true || OptCert.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case Page.Code:
                    SetHeader("", T("Connect_OptCode"), T("Connect_CodeSubtitle"), "InfoSoft", "Info");
                    BtnPrimary.Content = T("Connect_Connect");
                    BtnSecondary.Content = T("Connect_Back");
                    Dispatcher.BeginInvoke(new Action(() => TxtCode.Focus()), System.Windows.Threading.DispatcherPriority.Input);
                    break;
                case Page.Shell:
                    SetHeader("", T("Connect_OptShell"), T("Connect_ShellSubtitle"), "Subtle2", "Ink2");
                    BtnPrimary.Content = T("Connect_Connect");
                    BtnSecondary.Content = T("Connect_Back");
                    break;
                case Page.Progress:
                    SetHeader("", T("Connect_ProgressTitle"), T("Connect_ProgressSubtitle"), "BrandSoft", "BrandText");
                    BtnPrimary.Content = T("Connect_Connecting");
                    BtnPrimary.IsEnabled = false;
                    BtnSecondary.Content = T("Common_Cancel");
                    break;
            }
        }

        private void SetHeader(string glyph, string title, string text, string soft, string strong)
        {
            HeaderIcon.Text = glyph;
            HeaderTitle.Text = title;
            HeaderText.Text = text;
            HeaderText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            HeaderTile.SetResourceReference(Border.BackgroundProperty, soft);
            HeaderIcon.SetResourceReference(TextBlock.ForegroundProperty, strong);
        }

        private void Method_Checked(object sender, RoutedEventArgs e)
        {
            // Both automatic ways sign in an administrator first; the certificate needs a role that grants app permissions.
            bool automatic = OptAuto?.IsChecked == true || OptCert?.IsChecked == true;
            if (AutoInfo != null) AutoInfo.Visibility = automatic ? Visibility.Visible : Visibility.Collapsed;
            if (TxtReq1 != null) TxtReq1.Text = T(OptCert?.IsChecked == true ? "Connect_Req1Cert" : "Connect_Req1");
        }

        private void Primary_Click(object sender, RoutedEventArgs e)
        {
            switch (_page)
            {
                case Page.Choose:
                    if (OptCode.IsChecked == true) ShowPage(Page.Code);
                    else _ = RunAutoAsync(certificate: OptCert.IsChecked == true);
                    break;
                case Page.Code: _ = ConnectWithCodeAsync(TxtCode.Text, TxtTenant.Text, TxtClient.Text, CodeError, Flow.Code); break;
                case Page.Shell: _ = ConnectWithCodeAsync(TxtShellResult.Text, null, null, ShellError, Flow.Shell); break;
                case Page.Result:
                    if (_succeeded) { DialogResult = true; return; }
                    // Try again: the same way as before.
                    // A new certificate that did not activate in time: only its sign-in and the test run again.
                    if (_flow == Flow.Cert && _pending != null) _ = RetryActivationAsync();
                    else if (_flow is Flow.Auto or Flow.Cert) _ = RunAutoAsync(_flow == Flow.Cert);
                    else ShowPage(_flow == Flow.Shell ? Page.Shell : Page.Code);
                    break;
            }
        }

        private void Secondary_Click(object sender, RoutedEventArgs e)
        {
            switch (_page)
            {
                case Page.Choose: DialogResult = false; break;
                case Page.Code:
                case Page.Shell: ShowPage(Page.Choose); break;
                case Page.Progress: _cts?.Cancel(); break;
                case Page.Result: ShowPage(Page.Choose); break;
            }
        }

        private void Extra_Click(object sender, RoutedEventArgs e)
        {
            string text = _succeeded ? IntuneConnection.ConnectionCode : _errorDetails;
            Ui.CopyText(text);
            BtnExtraText.Text = T("Common_Copied");
        }

        private void Pim_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(IntuneConnection.PimUrl);
        private void ShellLink_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Shell);

        /// <summary>
        /// One connection at a time: a connection with an account replaces the certificate of this computer once it works,
        /// so the user agrees first. True when it may go on.
        /// </summary>
        private bool ConfirmReplaceCertificate()
            => !IntuneConnection.UsesCertificate || Ui.Confirm(T("Connect_ReplaceCert"), T("Connect_Continue"), T("Common_Cancel"));
        private void OpenShell_Click(object sender, RoutedEventArgs e) => MainWindow.OpenUrl(IntuneConnection.CloudShellUrl);

        private void ShellKind_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtScript == null) return;
            TxtScript.Text = ShellPwsh.IsChecked == true ? ConnectScripts.PowerShell : ConnectScripts.Bash;
            CopyScriptText.Text = T("Connect_CopyCommand");
        }

        private void CopyScript_Click(object sender, RoutedEventArgs e)
        {
            Ui.CopyText(TxtScript.Text);
            CopyScriptText.Text = T("Common_Copied");
        }

        private void Code_Changed(object sender, TextChangedEventArgs e)
        {
            // A pasted code fills the two IDs, so the user sees what will be used.
            if (sender == TxtCode && IntuneConnection.TryParseConnectionCode(TxtCode.Text, out var tenant, out var client))
            {
                TxtTenant.Text = tenant;
                TxtClient.Text = client;
            }
            CodeError.Visibility = ShellError.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------ automatic setup

        private void BuildSteps(params SetupStep[] steps)
        {
            _steps.Clear();
            _current = null;
            int n = 1;
            foreach (var s in steps) _steps.Add(new ConnectStepRow { Step = s, Number = n++, Title = StepTitle(s) });
        }

        private static string StepTitle(SetupStep s) => s switch
        {
            SetupStep.AdminSignIn => T("Connect_S_AdminSignIn"),
            SetupStep.Permission => T("Connect_S_Permission"),
            SetupStep.Registration => T("Connect_S_Registration", IntuneConnection.RegistrationName),
            SetupStep.ServicePrincipal => T("Connect_S_ServicePrincipal"),
            SetupStep.Consent => T("Connect_S_Consent"),
            SetupStep.Certificate => T("Connect_S_Certificate"),
            SetupStep.Activation => T("Connect_S_Activation"),
            SetupStep.UserSignIn => T("Connect_S_UserSignIn"),
            _ => T("Connect_S_Test")
        };

        /// <summary>Marks every earlier step done and <paramref name="step"/> as working.</summary>
        private void Advance(SetupStep step, string detail = "")
        {
            foreach (var row in _steps)
            {
                if (row.Step < step && (row.State == "Working" || row.State == "Pending")) row.State = "Done";
                if (row.Step == step) { row.State = "Working"; row.Detail = detail; _current = row; }
            }
            BrowserHint.Visibility = step is SetupStep.AdminSignIn or SetupStep.UserSignIn ? Visibility.Visible : Visibility.Collapsed;
            BrowserHintText.Text = step == SetupStep.AdminSignIn ? T("Connect_BrowserHintAdmin") : T("Connect_BrowserHintUser");
        }

        private ConnectStepRow Row(SetupStep step) => _steps.FirstOrDefault(r => r.Step == step);

        private async Task RunAutoAsync(bool certificate = false)
        {
            if (!certificate && !ConfirmReplaceCertificate()) return;
            _replacingCertificate = !certificate && IntuneConnection.UsesCertificate;
            _flow = certificate ? Flow.Cert : Flow.Auto;
            _consentUrl = null;
            _succeeded = false;
            _testFailure = null;
            DiscardPending();
            if (certificate)
                BuildSteps(SetupStep.AdminSignIn, SetupStep.Permission, SetupStep.Registration, SetupStep.ServicePrincipal, SetupStep.Consent,
                           SetupStep.Certificate, SetupStep.Activation, SetupStep.Test);
            else
                BuildSteps(SetupStep.AdminSignIn, SetupStep.Permission, SetupStep.Registration, SetupStep.ServicePrincipal, SetupStep.Consent,
                           SetupStep.Activation, SetupStep.UserSignIn, SetupStep.Test);
            ShowPage(Page.Progress);
            // Marked now, not when the first progress report arrives: a failure before it names the right step.
            Advance(SetupStep.AdminSignIn);
            _cts = new CancellationTokenSource();
            _running = true;
            IntuneConnection.RegistrationSnapshot previous = null;
            try
            {
                var progress = new Progress<SetupProgress>(p =>
                {
                    string detail = p.Step == SetupStep.Permission && p.Detail.Length > 0 ? "" : p.Detail;
                    if (p.Step == SetupStep.Permission && p.Detail.Length > 0 && Row(SetupStep.AdminSignIn) is ConnectStepRow admin) admin.Detail = T("Connect_SignedInAs", p.Detail);
                    Advance(p.Step, p.Step == SetupStep.ServicePrincipal ? "" : detail);
                });
                var r = await IntuneConnection.CreateRegistrationAsync(progress, _cts.Token, TxtAdminHint.Text.Trim(), certificate);
                if (Row(SetupStep.Registration) is ConnectStepRow reg) reg.Detail = (r.Reused ? T("Connect_Reused") : T("Connect_Created")) + "  ·  " + r.ClientId;
                var consent = Row(SetupStep.Consent);
                if (r.ConsentGranted && (!certificate || r.RoleGranted)) consent.State = "Done";
                else
                {
                    // Created, but the administrator may not approve permissions of Microsoft Graph: a Global
                    // Administrator does it with the link (the sign-in below then asks nothing).
                    consent.State = "Warning";
                    consent.Detail = certificate && !r.RoleGranted ? T("Connect_RoleMissingDetail") : T("Connect_ConsentMissingDetail");
                    _consentUrl = r.ConsentUrl;
                }
                if (certificate)
                {
                    _pending = r.Pending;
                    if (Row(SetupStep.Certificate) is ConnectStepRow cert && AppCertificate.Describe(r.CertificateThumbprint) is { } d)
                        cert.Detail = T(d.Hardware ? "Connect_CertTpm" : "Connect_CertSoftware", d.NotAfter.ToString("d", J365.IntunePSApp.Localization.Loc.Instance.Culture));
                    await ActivateAppAsync();
                }
                else
                {
                    // The registration becomes the connection of this computer once the user signed in with it; a cancel
                    // or a failure leaves the connection in use as it was.
                    previous = IntuneConnection.BeginTrial(r.TenantId, r.ClientId, r.TenantName);
                    await ActivateAndSignInAsync(newRegistration: !r.Reused, loginHint: r.Admin);
                }
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { DiscardPending(); ShowCancelled(); }
            catch (Exception ex) { ShowFailure(ex); }
            finally
            {
                _running = false;
                if (previous != null) IntuneConnection.EndTrial(previous, keep: _succeeded);
            }
        }

        /// <summary>Last steps of every way: the new registration becomes usable, the user signs in, Intune answers.</summary>
        private async Task ActivateAndSignInAsync(bool newRegistration, string loginHint)
        {
            var ct = _cts.Token;
            Advance(SetupStep.Activation, newRegistration ? T("Connect_ActivationWait") : "");
            if (newRegistration) await Task.Delay(TimeSpan.FromSeconds(12), ct);
            Advance(SetupStep.UserSignIn);
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await IntuneConnection.SignInAsync(ct, string.IsNullOrWhiteSpace(loginHint) ? null : loginHint);
                    break;
                }
                catch (AuthException ex) when (ex.Code == "AADSTS700016" && attempt < 4)
                {
                    // A brand-new registration takes a little while to reach every sign-in server.
                    _current.Detail = T("Connect_ActivationRetry");
                    await Task.Delay(TimeSpan.FromSeconds(20), ct);
                }
            }
            Row(SetupStep.UserSignIn).Detail = T("Connect_SignedInAs", IntuneConnection.Account);
            Advance(SetupStep.Test);
            try
            {
                string answer = await IntuneConnection.TestAsync(ct);
                var test = Row(SetupStep.Test);
                test.State = "Done";
                test.Detail = T("Connect_TestOk");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // Connected, but Intune refused the read: the account has no Intune role (or the consent is missing).
                var test = Row(SetupStep.Test);
                test.State = "Warning";
                test.Detail = T("Connect_TestWarn", ex.Message);
                _testFailure = ex;
            }
            // The certificate is replaced only by a connection that reads Intune: otherwise it stays in use.
            if (_replacingCertificate && _testFailure != null)
                throw new AuthException(T("Connect_CertificateKept", _testFailure.Message), "certificate_kept");
            foreach (var row in _steps) if (row.State is "Pending" or "Working") row.State = "Done";
            ShowSuccess();
        }

        /// <summary>
        /// Last steps of the certificate: the new key and the application permission reach every server of Entra ID and
        /// Intune (a minute or two for a new registration), then a read of Intune proves the connection.
        /// </summary>
        private async Task ActivateAppAsync()
        {
            var ct = _cts.Token;
            Advance(SetupStep.Activation, T("Connect_ActivationAppWait"));
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    // The new certificate becomes the connection only once it obtained a token; the one in use until
                    // then (a renewal) keeps working if this never happens.
                    if (_pending != null)
                    {
                        await IntuneConnection.ActivateCertificateAsync(_pending, ct);
                        _pending = null;
                    }
                    else await IntuneConnection.SignInAsync(ct);
                    break;
                }
                catch (AuthException ex) when (attempt < 8 && ex.Code is "AADSTS700027" or "AADSTS700016" or "AADSTS7000112")
                {
                    // A key added a moment ago is not known yet by every sign-in server.
                    _current.Detail = T("Connect_ActivationRetry");
                    await Task.Delay(TimeSpan.FromSeconds(15), ct);
                }
            }
            Advance(SetupStep.Test);
            var test = Row(SetupStep.Test);
            const int tries = 6;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await IntuneConnection.TestAsync(ct);
                    test.State = "Done";
                    test.Detail = T("Connect_TestOk");
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) when (attempt < tries && IntuneDiagnosis.IsRefusal(ex))
                {
                    // A new application permission takes a little while to reach Intune: each try asks for a new token.
                    test.Detail = T("Connect_TestRetry", attempt, tries - 1);
                    await Task.Delay(TimeSpan.FromSeconds(20), ct);
                    try { await IntuneConnection.SignInAsync(ct); } catch (AuthException) { }
                }
                catch (Exception ex)
                {
                    test.State = "Warning";
                    test.Detail = T("Connect_TestWarn", ex.Message);
                    _testFailure = ex;
                    break;
                }
            }
            foreach (var row in _steps) if (row.State is "Pending" or "Working") row.State = "Done";
            ShowSuccess();
        }

        /// <summary>"Try again" after a new certificate did not activate: its sign-in and the test run again, nothing is recreated.</summary>
        private async Task RetryActivationAsync()
        {
            ShowPage(Page.Progress);
            _cts = new CancellationTokenSource();
            _running = true;
            _testFailure = null;
            try { await ActivateAppAsync(); }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { DiscardPending(); ShowCancelled(); }
            catch (Exception ex) { ShowFailure(ex); }
            finally { _running = false; }
        }

        private void DiscardPending()
        {
            if (_pending == null) return;
            IntuneConnection.DiscardCertificate(_pending);
            _pending = null;
        }

        // ------------------------------------------------------------------ existing registration (code, IDs, Cloud Shell)

        private async Task ConnectWithCodeAsync(string code, string tenantField, string clientField, TextBlock error, Flow flow)
        {
            string tenant = tenantField?.Trim() ?? "", client = clientField?.Trim() ?? "";
            if (IntuneConnection.TryParseConnectionCode(code, out var t, out var c)) { tenant = t; client = c; }
            if (!IntunePublisher.IsTenant(tenant) || !IntunePublisher.IsGuid(client))
            {
                error.Text = flow == Flow.Shell ? T("Connect_ShellInvalid") : T("Connect_CodeInvalid");
                error.Visibility = Visibility.Visible;
                return;
            }
            if (!ConfirmReplaceCertificate()) return;
            _replacingCertificate = IntuneConnection.UsesCertificate;
            _flow = flow;
            _succeeded = false;
            _testFailure = null;
            DiscardPending();
            _consentUrl = $"{EntraAuth.LoginUrl}/{tenant}/adminconsent?client_id={client}";
            // Tried first and saved only once the sign-in works: a wrong code, a cancel or a failure leaves the connection
            // that was working (registration and session) as it was.
            var previous = IntuneConnection.BeginTrial(tenant, client, tenant.Contains('.') ? tenant : "");
            BuildSteps(SetupStep.Activation, SetupStep.UserSignIn, SetupStep.Test);
            ShowPage(Page.Progress);
            _cts = new CancellationTokenSource();
            _running = true;
            try { await ActivateAndSignInAsync(newRegistration: flow == Flow.Shell, loginHint: null); }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested) { ShowCancelled(); }
            catch (Exception ex) { ShowFailure(ex); }
            finally
            {
                _running = false;
                // Kept when the whole connection worked (a cancel during the test puts everything back, the session too).
                IntuneConnection.EndTrial(previous, keep: _succeeded);
            }
        }

        // ------------------------------------------------------------------ result

        private void ShowSuccess(bool log = true)
        {
            _succeeded = true;
            ShowResultPage();
            ResultBox.Style = Ui.Style("NoticeSuccess");
            ResultIcon.Text = "";
            ResultIcon.SetResourceReference(TextBlock.ForegroundProperty, "Success");
            ResultTitle.Text = T("Connect_DoneTitle");
            bool warning = _steps.Any(r => r.State == "Warning");
            ResultText.Text = warning ? T("Connect_DoneWithWarnings") : IntuneConnection.UsesCertificate ? T("Connect_DoneCertText") : T("Connect_DoneText");
            SuccessDetails.Visibility = Visibility.Visible;
            DetailAccount.Text = IntuneConnection.Account;
            DetailTenant.Text = IntuneConnection.SessionTenant.Length > 0 ? IntuneConnection.SessionTenant : IntuneConnection.TenantId;
            DetailClient.Text = IntuneConnection.ClientId;
            Causes.Children.Clear();
            if (!string.IsNullOrEmpty(_consentUrl) && _steps.Any(r => r.Step == SetupStep.Consent && r.State == "Warning"))
                AddCause(T("Connect_CauseConsentTitle"), T("Connect_CauseConsentText"), ("Connect_OpenConsentLink", () => MainWindow.OpenUrl(_consentUrl)), ("Connect_CopyConsentLink", () => Ui.CopyText(_consentUrl)));
            if (_testFailure != null && IntuneDiagnosis.IsRefusal(_testFailure))
            {
                // Connected, but Intune refused: what to check, and the way that does not depend on the account.
                bool certificate = IntuneConnection.UsesCertificate;
                var actions = certificate
                    ? new (string, Action)[0]
                    : new (string, Action)[]
                    {
                        ("Connect_OpenPim", () => MainWindow.OpenUrl(IntuneConnection.PimUrl)),
                        ("Connect_UseCertificate", () => { OptCert.IsChecked = true; _ = RunAutoAsync(certificate: true); })
                    };
                AddCause(T("Connect_CauseIntuneTitle"), IntuneDiagnosis.Explain(IntuneConnection.LastToken, certificate), actions);
            }
            CausesTitle.Visibility = Causes.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetHeader("", T("Connect_DoneHeader"), IntuneConnection.Account, "SuccessSoft", "Success");
            BtnPrimary.Content = T("Connect_Finish");
            BtnSecondary.Visibility = Visibility.Collapsed;
            BtnExtra.Visibility = Visibility.Visible;
            BtnExtraText.Text = T("Settings_IntuneCopyCode");
            if (log) Log.Ok(T("Log_IntuneConnected", IntuneConnection.Account, IntuneConnection.ClientId));
        }

        private void ShowCancelled()
        {
            if (_current != null && _current.State == "Working") { _current.State = "Failed"; _current.Detail = T("Connect_Cancelled"); }
            ShowPage(Page.Choose);
        }

        private void ShowFailure(Exception ex)
        {
            _succeeded = false;
            var failedStep = _current?.Step ?? SetupStep.AdminSignIn;
            if (_current != null) { _current.State = "Failed"; _current.Detail = ""; }
            string code = (ex as AuthException)?.Code ?? (ex is GraphException g ? "HTTP " + g.Status : ex.GetType().Name);
            _errorDetails = $"{AppInfo.Name} {AppInfo.Version}\n{StepTitle(failedStep)}\n{code}\n{ex.Message}";
            if (!App.HasArg("--snapshot")) Log.Warn(T("Connect_Failed", ex.Message));

            ShowResultPage();
            ResultBox.Style = Ui.Style("NoticeDanger");
            ResultIcon.Text = "";
            ResultIcon.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            ResultTitle.Text = T("Connect_FailedTitle", StepTitle(failedStep));
            ResultText.Text = ex.Message;
            SuccessDetails.Visibility = Visibility.Collapsed;
            Causes.Children.Clear();
            BuildCauses(code, failedStep);
            CausesTitle.Visibility = Causes.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetHeader("", T("Connect_FailedHeader"), T("Connect_FailedSubtitle"), "DangerSoft", "Danger");
            BtnPrimary.Content = T("Connect_TryAgain");
            BtnSecondary.Content = T("Connect_OtherOptions");
            BtnExtra.Visibility = Visibility.Visible;
            BtnExtraText.Text = T("Connect_CopyDetails");
        }

        private void ShowResultPage()
        {
            ShowPage(Page.Result);
            BtnSecondary.Visibility = Visibility.Visible;
        }

        /// <summary>The likely causes of the failure, each with the action that fixes it.</summary>
        private void BuildCauses(string code, SetupStep step)
        {
            var useShell = ("Connect_UseShell", (Action)(() => ShowPage(Page.Shell)));
            var otherAccount = ("Connect_OtherAccount", (Action)(() => { TxtAdminHint.Text = ""; _ = RunAutoAsync(_flow == Flow.Cert); }));
            var openPim = ("Connect_OpenPim", (Action)(() => MainWindow.OpenUrl(IntuneConnection.PimUrl)));
            bool adminStep = step <= SetupStep.Certificate;
            switch (code)
            {
                case "cancel" when adminStep:
                    AddCause(T("Connect_CauseCancelTitle"), T("Connect_CauseCancelText"));
                    AddCause(T("Connect_CauseApprovalTitle"), T("Connect_CauseApprovalText"), openPim, otherAccount);
                    AddCause(T("Connect_CauseBlockedTitle"), T("Connect_CauseBlockedText"), useShell);
                    break;
                case "cancel":
                case "AADSTS65001":
                case "AADSTS90094":
                case "AADSTS65004":
                    AddCause(T("Connect_CauseUserCancelTitle"), T("Connect_CauseUserCancelText"));
                    if (!string.IsNullOrEmpty(_consentUrl))
                        AddCause(T("Connect_CauseConsentTitle"), T("Connect_CauseConsentText"), ("Connect_OpenConsentLink", () => MainWindow.OpenUrl(_consentUrl)), ("Connect_CopyConsentLink", () => Ui.CopyText(_consentUrl)));
                    break;
                case "insufficient_privileges":
                    AddCause(T("Connect_CauseRoleTitle"), T("Connect_CauseRoleText"), openPim, otherAccount);
                    AddCause(T("Connect_CauseBlockedTitle"), T("Connect_CauseShellAlternative"), useShell);
                    break;
                case "AADSTS53003":
                    AddCause(T("Connect_CauseCaTitle"), T("Connect_CauseCaText"));
                    if (adminStep) AddCause(T("Connect_CauseBlockedTitle"), T("Connect_CauseShellAlternative"), useShell);
                    break;
                case "AADSTS50105":
                case "AADSTS7000112":
                case "AADSTS500014":
                    if (adminStep) AddCause(T("Connect_CauseGraphCliTitle"), T("Connect_CauseGraphCliText"), useShell);
                    else AddCause(T("Connect_CauseAssignmentTitle"), T("Connect_CauseAssignmentText"));
                    break;
                case "AADSTS700016":
                    AddCause(T("Connect_CauseNotFoundTitle"), T("Connect_CauseNotFoundText"));
                    break;
                default:
                    if (EntraAuth.IsNetworkFailure(code))
                        AddCause(T("Connect_CauseNetworkTitle"), T("Connect_CauseNetworkText"));
                    if (adminStep) AddCause(T("Connect_CauseBlockedTitle"), T("Connect_CauseShellAlternative"), useShell);
                    break;
            }
        }

        private void AddCause(string title, string text, params (string Key, Action Action)[] actions)
        {
            var box = new Border { Style = Ui.Style("Inset"), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(16, 12, 16, 12) };
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title, Style = Ui.Style("H3"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = text, Style = Ui.Style("MutedText"), Margin = new Thickness(0, 3, 0, 0) });
            if (actions.Length > 0)
            {
                var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                foreach (var (key, action) in actions)
                {
                    var b = new Button { Content = T(key), Style = Ui.Style("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 5, 12, 5), MinHeight = 30 };
                    b.Click += (s, e) => action();
                    buttons.Children.Add(b);
                }
                panel.Children.Add(buttons);
            }
            box.Child = panel;
            Causes.Children.Add(box);
        }

        // ------------------------------------------------------------------ capture mode (documentation screenshots)

        private const string SampleAccount = "admin@contoso.com";
        private const string SampleTenant = "8f2b6a4e-1c3d-4e5f-9a7b-2c4d6e8f0a1b";
        private const string SampleClient = "3c9e7a21-5b4d-4f6a-8e2c-1a3b5c7d9e0f";

        /// <summary>Draws a page with sample content for the screenshots (--snapshot). Nothing is sent anywhere.</summary>
        internal void SnapshotPage(string page)
        {
            // The recommended way, with the certificate.
            void AllSteps() => BuildSteps(SetupStep.AdminSignIn, SetupStep.Permission, SetupStep.Registration, SetupStep.ServicePrincipal, SetupStep.Consent,
                                          SetupStep.Certificate, SetupStep.Activation, SetupStep.Test);
            switch (page)
            {
                case "shell":
                    ShowPage(Page.Shell);
                    break;
                case "progress":
                    AllSteps();
                    ShowPage(Page.Progress);
                    Advance(SetupStep.ServicePrincipal);
                    Row(SetupStep.AdminSignIn).Detail = T("Connect_SignedInAs", SampleAccount);
                    Row(SetupStep.Registration).Detail = T("Connect_Created") + "  ·  " + SampleClient;
                    BrowserHint.Visibility = Visibility.Collapsed;
                    break;
                case "failed":
                    AllSteps();
                    ShowPage(Page.Progress);
                    Advance(SetupStep.AdminSignIn);
                    ShowFailure(EntraAuth.Explain("access_denied", null, "cancel"));
                    break;
                case "done":
                    AllSteps();
                    ShowPage(Page.Progress);
                    Advance(SetupStep.Test);
                    foreach (var row in _steps) row.State = "Done";
                    Row(SetupStep.AdminSignIn).Detail = T("Connect_SignedInAs", SampleAccount);
                    Row(SetupStep.Registration).Detail = T("Connect_Created") + "  ·  " + SampleClient;
                    Row(SetupStep.Certificate).Detail = T("Connect_CertTpm", new DateTime(2027, 11, 19).ToString("d", J365.IntunePSApp.Localization.Loc.Instance.Culture));
                    Row(SetupStep.Test).Detail = T("Connect_TestOk");
                    ShowSuccess(log: false);
                    ResultText.Text = T("Connect_DoneCertText");
                    DetailAccount.Text = T("Intune_CertIdentity", "contoso.com");
                    DetailTenant.Text = SampleTenant;
                    DetailClient.Text = SampleClient;
                    HeaderText.Text = T("Intune_CertIdentity", "contoso.com");
                    break;
            }
        }

        internal void CloseForSnapshot()
        {
            _running = false;
            Close();
        }
    }
}
