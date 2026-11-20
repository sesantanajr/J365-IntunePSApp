using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// Why Intune refused a request that Microsoft Graph accepted (HTTP 401 or 403 "Forbidden" answered by the Intune
    /// service, not an invalid token): the causes worth checking, in order, read from what the token says about the
    /// caller. Intune itself only answers "An error has occurred".
    /// </summary>
    public static class IntuneDiagnosis
    {
        /// <summary>Entra roles that manage Intune apps: Global Administrator and Intune Administrator.</summary>
        private static readonly string[] IntuneRoles = { "62e90394-69f5-4237-9190-012177145e10", "3a2c62db-5318-420d-8d74-23affee5d9d5" };

        /// <summary>Intune (not the sign-in) refused the request.</summary>
        public static bool IsRefusal(Exception ex) => ex is GraphException g && IsRefusal(g.Status, g.Code + " " + g.Message);

        /// <summary>The same for a message of the publishing script ("Microsoft Graph answered HTTP 401 to ...").</summary>
        public static bool IsRefusal(string text)
        {
            var m = Regex.Match(text ?? "", @"HTTP (401|403)\b");
            return m.Success && IsRefusal(int.Parse(m.Groups[1].Value), text);
        }

        private static bool IsRefusal(int status, string text)
            => status == 403 || (status == 401 && !(text ?? "").Contains("InvalidAuthenticationToken", StringComparison.OrdinalIgnoreCase));

        /// <summary>The causes worth checking for the connection in use, most likely first.</summary>
        public static List<string> Causes(TokenSet token, bool certificate)
        {
            var causes = new List<string>();
            var claims = token?.Claims ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Claim(string name) => claims.TryGetValue(name, out var v) ? v ?? "" : "";
            bool Has(string list, string value) => list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(value, StringComparer.OrdinalIgnoreCase);
            if (certificate || token?.App == true)
            {
                // The application: its permission, then the tenant. A user's role or license plays no part.
                if (token == null || !Has(Claim("roles"), IntuneConnection.IntunePermission)) causes.Add(T("Diag_AppRoleMissing", IntuneConnection.IntunePermission));
                causes.Add(T("Diag_TenantLicense"));
                return causes;
            }
            if (token != null && !Has(Claim("scp"), IntuneConnection.IntunePermission)) causes.Add(T("Diag_ScopeMissing", IntuneConnection.IntunePermission));
            string roles = Claim("wids");
            if (roles.Length == 0 || !IntuneRoles.Any(r => Has(roles, r))) causes.Add(T("Diag_RoleMissing"));
            causes.Add(T("Diag_UnlicensedAdmins"));
            causes.Add(T("Diag_TenantLicense"));
            causes.Add(T("Diag_UseCertificate"));
            return causes;
        }

        /// <summary>The explanation shown with the error: an introduction and one line per cause.</summary>
        public static string Explain(TokenSet token, bool certificate)
            => T("Diag_Intro") + "\n" + string.Join("\n", Causes(token, certificate).Select(c => "• " + c));
    }
}
