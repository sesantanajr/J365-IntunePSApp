using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Why Intune refused a request that Microsoft Graph accepted (HTTP 401 or 403 "Forbidden" answered by the Intune
    /// service, not an invalid token). Intune itself only answers "An error has occurred": when the subscriptions of the
    /// tenant are known (<see cref="IntuneSubscription"/>) a tenant without Intune is said for certain; otherwise the
    /// causes worth checking, in order, read from what the token says about the caller.
    /// </summary>
    public static class IntuneDiagnosis
    {
        /// <summary>Entra roles that manage Intune apps: Global Administrator and Intune Administrator.</summary>
        private static readonly string[] IntuneRoles = { "62e90394-69f5-4237-9190-012177145e10", "3a2c62db-5318-420d-8d74-23affee5d9d5" };

        /// <summary>Intune (not the sign-in) refused the request.</summary>
        public static bool IsRefusal(Exception ex) => ex is GraphException g && IsRefusal(g.Status, g.Code + " " + g.Message);

        /// <summary>
        /// The same for the failure of the publishing script: only an answer of Microsoft Graph ("Microsoft Graph answered
        /// HTTP 401 ... to GET ..."). An "HTTP 403" of the upload (an expired link to the storage, a proxy) is not Intune.
        /// </summary>
        public static bool IsRefusal(string text)
            => ParseScriptError(text) is { } p && IsRefusal(p.Status, p.Code + " " + FirstLine(text));

        /// <summary>
        /// 401 and 403 answered by Intune, not an expired or invalid token; and the 400 Microsoft Graph gives a tenant
        /// without Intune ("Request not applicable to target tenant").
        /// </summary>
        private static bool IsRefusal(int status, string text)
            => status == 403
                || (status == 401 && !(text ?? "").Contains("InvalidAuthenticationToken", StringComparison.OrdinalIgnoreCase))
                || (status == 400 && (text ?? "").Contains("not applicable to target tenant", StringComparison.OrdinalIgnoreCase));

        private static string FirstLine(string text) => (text ?? "").Split('\n')[0].TrimEnd('\r');

        /// <summary>
        /// What Intune said, when it says more than "An error has occurred" (a missing scope, for example): the sentence of
        /// a failure of the application ("GET /x: ...") or of the script, without the activity id. Empty otherwise.
        /// </summary>
        public static string AnswerOf(string message)
        {
            string line = FirstLine(message);
            var script = Regex.Match(line, @"^Microsoft Graph answered HTTP \d{3}(?: \([^)]*\))? to \S+ \S+?: (.*)$");
            var app = Regex.Match(line, @"^(?:GET|POST|PATCH|PUT|DELETE) \S+?: (.*)$");
            string said = script.Success ? script.Groups[1].Value : app.Success ? app.Groups[1].Value : "";
            said = Regex.Replace(said, @"\s*-\s*Activity ID:\s*[0-9a-fA-F-]{36}", "").Trim();
            return said.Length == 0 || said.StartsWith("An error has occurred", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(said, @"^HTTP \d{3}$") ? "" : said;
        }

        /// <summary>The causes worth checking for the connection in use, most likely first; one when it is certain.</summary>
        public static List<string> Causes(TokenSet token, bool certificate)
        {
            // The subscriptions of the tenant say it for certain: nothing about the connection matters then.
            string tenant = IntuneConnection.TenantLabel;
            string date = IntuneSubscription.Checked?.ToString("d", J365.IntunePSApp.Localization.Loc.Instance.Culture) ?? "";
            var plan = IntuneSubscription.State;
            switch (plan)
            {
                case IntunePlanState.None: return new List<string> { T("Diag_PlanNone", tenant, date) };
                case IntunePlanState.BasicOnly: return new List<string> { T("Diag_PlanBasic", tenant, date) };
                case IntunePlanState.Inactive: return new List<string> { T("Diag_PlanInactive", tenant, date) };
            }

            var causes = new List<string>();
            var claims = token?.Claims ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Claim(string name) => claims.TryGetValue(name, out var v) ? v ?? "" : "";
            bool Has(string list, string value) => list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(value, StringComparer.OrdinalIgnoreCase);
            if (certificate || token?.App == true)
            {
                // The application: its permission, then the tenant. A user's role or license plays no part.
                bool role = token != null && Has(Claim("roles"), IntuneConnection.IntunePermission);
                if (!role) causes.Add(T("Diag_AppRoleMissing", IntuneConnection.IntunePermission));
                // With Intune in the tenant and the permission in the token, only time is left (a fresh grant or subscription).
                if (plan != IntunePlanState.Active) causes.Add(T("Diag_TenantLicense"));
                else if (role) causes.Add(T("Diag_PlanActiveWait"));
                return causes;
            }
            if (token != null && !Has(Claim("scp"), IntuneConnection.IntunePermission)) causes.Add(T("Diag_ScopeMissing", IntuneConnection.IntunePermission));
            string roles = Claim("wids");
            if (roles.Length == 0 || !IntuneRoles.Any(r => Has(roles, r))) causes.Add(T("Diag_RoleMissing"));
            causes.Add(T("Diag_UnlicensedAdmins"));
            if (plan != IntunePlanState.Active) causes.Add(T("Diag_TenantLicense"));
            causes.Add(T("Diag_UseCertificate"));
            return causes;
        }

        /// <summary>The explanation shown with the error: an introduction and the cause, or one line per cause.</summary>
        public static string Explain(TokenSet token, bool certificate)
        {
            var causes = Causes(token, certificate);
            return causes.Count == 1
                ? T("Diag_IntroOne") + "\n" + causes[0]
                : T("Diag_Intro") + "\n" + string.Join("\n", causes.Select(c => "• " + c));
        }

        /// <summary>
        /// The parts of a failure of the publishing script ("Microsoft Graph answered HTTP 401 (UnknownError) to GET
        /// /deviceAppManagement/mobileApps: An error has occurred - Activity ID: ...\nIntune refused..."): the status, the
        /// request and the activity id for support. Null when the text is not an answer of Microsoft Graph.
        /// </summary>
        public static (int Status, string Code, string Request, string ActivityId)? ParseScriptError(string text)
        {
            var m = Regex.Match(text ?? "", @"Microsoft Graph answered HTTP (\d{3})(?: \(([^)]*)\))? to (\S+ \S+?):");
            if (!m.Success) return null;
            return (int.Parse(m.Groups[1].Value), m.Groups[2].Value, m.Groups[3].Value, GraphException.ActivityId(text));
        }

        /// <summary>
        /// "For support: GET /deviceAppManagement/mobileApps · Activity ID ...": what identifies a refused request (of the
        /// application or of the publishing script), or an empty string.
        /// </summary>
        public static string SupportLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "";
            string request = ParseScriptError(message)?.Request ?? "";
            if (request.Length == 0)
            {
                var m = Regex.Match(message, @"^(GET|POST|PATCH|PUT|DELETE) (\S+?):");
                if (m.Success) request = m.Groups[1].Value + " " + m.Groups[2].Value;
            }
            string activity = GraphException.ActivityId(message);
            var parts = new[] { request, activity.Length > 0 ? "Activity ID " + activity : "" }.Where(p => p.Length > 0).ToList();
            return parts.Count == 0 ? "" : T("Intune_Support", string.Join(" · ", parts));
        }
    }
}
