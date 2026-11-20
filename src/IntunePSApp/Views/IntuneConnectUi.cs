using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// "Check the subscription": an administrator of the tenant signs in in the browser (read only, nothing is kept) and
        /// the subscriptions of the tenant are read. The result is remembered and said; null when it could not be read.
        /// </summary>
        public static async Task<IntunePlanState?> CheckPlanAsync(CancellationToken ct)
        {
            IntunePlanState state;
            try { state = await IntuneSubscription.CheckAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
            catch (OperationCanceledException ex)
            {
                // Microsoft Graph did not answer in time (its own timeout, after the retries).
                Log.Warn(ex.Message);
                Ui.Error(T("Plan_CheckFailed", T("Plan_ErrTimeout")));
                return null;
            }
            catch (Exception ex)
            {
                Log.Warn(ex.Message);
                Ui.Error(T("Plan_CheckFailed", ex.Message));
                return null;
            }
            string nl2 = Environment.NewLine + Environment.NewLine;
            // The plans of this check (an unknown answer is not remembered, but what it found is shown).
            string plans = PlanList(IntuneSubscription.LastCheck?.Plans);
            switch (state)
            {
                case IntunePlanState.Active: Ui.Info(T("Plan_ResultActive", IntuneConnection.TenantLabel) + nl2 + plans); break;
                case IntunePlanState.Unknown: Ui.Info(T("Plan_ResultUnclear", IntuneConnection.TenantLabel) + nl2 + plans); break;
                default:
                    Ui.Warn(T("Plan_ResultTitle") + nl2 + IntuneDiagnosis.Causes(null, IntuneConnection.UsesCertificate)[0] + nl2 + plans + nl2
                        + T(state == IntunePlanState.Inactive ? "Plan_HowToInactive" : "Plan_HowToNone"));
                    break;
            }
            return state;
        }

        /// <summary>The plans of Intune a reading found, one per line, with their status: what the answer rests on.</summary>
        public static string PlanList(IReadOnlyList<IntunePlanFound> plans = null)
        {
            plans ??= IntuneSubscription.Plans;
            if (plans.Count == 0) return T("Plan_FoundNone");
            return T("Plan_Found") + Environment.NewLine + string.Join(Environment.NewLine, plans.Select(p => "•  " + IntuneSubscription.Describe(p)));
        }

        /// <summary>
        /// Intune refused a request: the explanation (certain when the subscriptions of the tenant are known), what Intune
        /// said when it says more than "An error has occurred", and what identifies the request for the support of Microsoft.
        /// </summary>
        public static string RefusalText(Exception ex) => RefusalText(ex?.Message);

        /// <inheritdoc cref="RefusalText(Exception)"/>
        public static string RefusalText(string message)
        {
            string text = IntuneDiagnosis.Explain(IntuneConnection.LastToken, IntuneConnection.UsesCertificate);
            string said = IntuneDiagnosis.AnswerOf(message);
            if (said.Length > 0) text += Environment.NewLine + Environment.NewLine + T("Intune_Answer", said);
            string support = IntuneDiagnosis.SupportLine(message);
            return support.Length > 0 ? text + Environment.NewLine + Environment.NewLine + support : text;
        }
    }
}
