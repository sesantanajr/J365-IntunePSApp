using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using J365.IntunePSApp.Core;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Views
{
    /// <summary>
    /// Sign-in with the registration in Settings, shared by Settings and the publishing dialog. The setup itself (automatic,
    /// connection code or Azure Cloud Shell) is the connection window, <see cref="ConnectWindow"/>.
    /// </summary>
    public static class IntuneConnectUi
    {
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
