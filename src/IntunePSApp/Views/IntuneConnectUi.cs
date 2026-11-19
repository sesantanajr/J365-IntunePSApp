using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using J365.IntunePSApp.Core;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    /// <summary>The connection flows shared by Settings and the publishing dialog: automatic setup and sign-in.</summary>
    public static class IntuneConnectUi
    {
        /// <summary>
        /// Creates the app registration in the tenant of the administrator who signs in, then signs in with it.
        /// <paramref name="status"/> receives each step. Returns true when the application is connected.
        /// </summary>
        public static async Task<bool> SetupAsync(Action<string> status, CancellationToken ct)
        {
            if (!Ui.Confirm(T("Connect_Confirm", IntuneConnection.RegistrationName, IntuneConnection.IntunePermission), T("Connect_Start"), T("Common_Cancel"))) return false;
            var progress = new Progress<string>(s => status?.Invoke(s));
            RegistrationResult r;
            try { r = await IntuneConnection.CreateRegistrationAsync(progress, ct); }
            catch (OperationCanceledException) { status?.Invoke(""); return false; }
            catch (Exception ex)
            {
                status?.Invoke("");
                Log.Error(T("Connect_Failed", ex.Message));
                Ui.Error(T("Connect_Failed", ex.Message));
                return false;
            }

            if (!r.ConsentGranted)
            {
                // The registration exists; only the consent is missing. A Global Administrator grants it with the link.
                if (Ui.Confirm(T("Connect_NoConsent", r.Admin, r.ConsentUrl), T("Connect_OpenConsent"), T("Common_Later"))) MainWindow.OpenUrl(r.ConsentUrl);
            }

            // A new application takes a few seconds to be accepted by the sign-in service.
            if (!r.Reused)
            {
                status?.Invoke(T("Connect_StepWait"));
                try { await Task.Delay(TimeSpan.FromSeconds(12), ct); } catch (OperationCanceledException) { return false; }
            }
            status?.Invoke(T("Connect_StepUserSignIn"));
            bool ok = await SignInAsync(r.Admin, ct, quiet: true);
            status?.Invoke("");
            if (ok) Ui.Info(T("Connect_Done", r.Reused ? T("Connect_Reused") : T("Connect_Created"), IntuneConnection.RegistrationName, r.ClientId, r.TenantId, IntuneConnection.Account));
            return ok;
        }

        /// <summary>Signs in with the registration in Settings. Returns true when signed in.</summary>
        public static async Task<bool> SignInAsync(string loginHint, CancellationToken ct, bool quiet = false)
        {
            if (!IntuneConnection.IsConfigured) { Ui.Warn(T("Auth_ErrNotConfigured")); return false; }
            try
            {
                await IntuneConnection.SignInAsync(ct, loginHint);
                if (!quiet) MainWindow.Current?.Toast(T("Auth_SignedIn", IntuneConnection.Account), "ok");
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (AuthException ex)
            {
                Log.Warn(ex.Message);
                // A brand-new registration may still be unknown to the sign-in service: one more try a little later.
                if (ex.Code == "AADSTS700016" && quiet)
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(20), ct); await IntuneConnection.SignInAsync(ct, loginHint); return true; }
                    catch (Exception again) { Ui.Error(T("Auth_Failed", again.Message)); return false; }
                }
                Ui.Error(T("Auth_Failed", ex.Message));
                return false;
            }
            catch (Exception ex)
            {
                Log.Warn(ex.Message);
                Ui.Error(T("Auth_Failed", ex.Message));
                return false;
            }
        }
    }
}
