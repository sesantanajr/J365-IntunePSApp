using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>What the tenant has of Microsoft Intune, read from its subscriptions.</summary>
    public enum IntunePlanState
    {
        /// <summary>Not read yet, read for another tenant, or plans this application does not know.</summary>
        Unknown,
        /// <summary>Microsoft Intune (Plan 1 or a suite that includes it), active or in its grace period.</summary>
        Active,
        /// <summary>Only "Basic Mobility and Security" (MDM for Office 365): it does not deploy Win32 apps.</summary>
        BasicOnly,
        /// <summary>Intune was there, and its subscription expired or was suspended (a trial that ended).</summary>
        Inactive,
        /// <summary>No Intune at all.</summary>
        None
    }

    /// <summary>
    /// Whether the tenant has Microsoft Intune, read from its subscriptions (organization.assignedPlans). Without it, Intune
    /// refuses every request of a connection that works ("An error has occurred", HTTP 401): this says why for certain.
    /// Read with the sign-in of the administrator at the setup, or on demand ("Check the subscription"); Intune answering
    /// a request proves it too, until Intune refuses again. One tenant is remembered, the one of the connection.
    /// </summary>
    public static class IntuneSubscription
    {
        /// <summary>
        /// Service plans of Microsoft Intune for Win32 apps: Plan 1 (alone, EMS, Microsoft 365 E3/E5/F1/F3), Education,
        /// Business Premium. Another plan of the Intune service ("SCO") is not taken for one: it leaves the answer unknown.
        /// </summary>
        private static readonly string[] IntunePlans = { "c1ec4a95-1f05-45b3-a911-aa3fa01094f5", "da24caf9-af8e-485c-b7c8-e73336da2693", "8e9ff0ff-aa7a-4b20-83c1-2f636b600ac2" };

        /// <summary>"Mobile Device Management for Office 365" (Basic Mobility and Security), in every Microsoft 365 plan.</summary>
        private const string BasicPlan = "882e1d05-acd1-4ccb-8708-6ee03664b117";

        /// <summary>The service name of Intune in assignedPlans.</summary>
        private const string IntuneService = "SCO";

        /// <summary>The permission that reads the subscriptions (read only).</summary>
        public const string ReadPermission = "Organization.Read.All";

        /// <summary>Microsoft 365 admin center > Marketplace: where an administrator buys Intune or starts its free trial.</summary>
        public const string PurchaseUrl = "https://admin.microsoft.com/Adminportal/Home#/catalog";

        /// <summary>A reading of the plans is trusted over a refusal of Intune for this long (a grant may still be on its way).</summary>
        private static readonly TimeSpan ReadTrusted = TimeSpan.FromDays(1);

        /// <summary>
        /// Microsoft Intune, Basic Mobility and Security only, expired, or nothing, from the plans of the tenant; Unknown
        /// when an active plan of the Intune service is not one this application knows (it cannot say either way).
        /// </summary>
        public static IntunePlanState Classify(JsonArray assignedPlans)
        {
            bool basic = false, inactive = false, otherIntune = false;
            foreach (var p in assignedPlans ?? new JsonArray())
            {
                string id = (Text(p?["servicePlanId"]) ?? "").Trim().ToLowerInvariant();
                string service = Text(p?["service"]) ?? "";
                // "Warning" is a subscription that expired and is in its grace period: it still works.
                string status = Text(p?["capabilityStatus"]) ?? "";
                bool on = status.Equals("Enabled", StringComparison.OrdinalIgnoreCase) || status.Equals("Warning", StringComparison.OrdinalIgnoreCase);
                if (id == BasicPlan) basic |= on;
                else if (IntunePlans.Contains(id))
                {
                    if (on) return IntunePlanState.Active;
                    inactive = true;
                }
                else if (on && string.Equals(service, IntuneService, StringComparison.OrdinalIgnoreCase)) otherIntune = true;
            }
            if (otherIntune) return IntunePlanState.Unknown;
            // An Intune that ended says more than the basic mobility every Microsoft 365 tenant has.
            return inactive ? IntunePlanState.Inactive : basic ? IntunePlanState.BasicOnly : IntunePlanState.None;
        }

        private static string Text(JsonNode node)
        {
            try { return node?.GetValue<string>(); } catch { return null; }
        }

        /// <summary>What is known of the tenant of the connection (Unknown when it was read for another tenant).</summary>
        public static IntunePlanState State
        {
            get
            {
                var cfg = ConfigStore.Current;
                if (!IsCurrent(cfg)) return IntunePlanState.Unknown;
                return (cfg.IntunePlan ?? "") switch
                {
                    "active" or "answered" => IntunePlanState.Active,
                    "basic" => IntunePlanState.BasicOnly,
                    "inactive" => IntunePlanState.Inactive,
                    "none" => IntunePlanState.None,
                    _ => IntunePlanState.Unknown
                };
            }
        }

        private static bool IsCurrent(AppConfig cfg)
        {
            string tenant = cfg.IntunePlanTenant ?? "";
            return tenant.Length > 0 && string.Equals(tenant, IntuneConnection.TenantId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The tenant cannot publish Win32 apps until an administrator activates Intune.</summary>
        public static bool Blocks => State is IntunePlanState.None or IntunePlanState.BasicOnly or IntunePlanState.Inactive;

        /// <summary>When <see cref="State"/> was found out (local time), or null.</summary>
        public static DateTime? Checked => State == IntunePlanState.Unknown ? null : ConfigStore.Current.IntunePlanUtc?.ToLocalTime();

        /// <summary>The plans of a tenant were read (setup, "Check the subscription"): remembered, with the date of this reading.</summary>
        public static void NoteRead(string tenant, IntunePlanState state)
        {
            string value = state switch
            {
                IntunePlanState.Active => "active",
                IntunePlanState.BasicOnly => "basic",
                IntunePlanState.Inactive => "inactive",
                IntunePlanState.None => "none",
                _ => ""
            };
            Store(value, tenant);
        }

        /// <summary>
        /// Intune answered a request of the connection: the tenant has Intune. Written only when it changes what is known (a
        /// reading of the plans that says so already stays as it is).
        /// </summary>
        public static void NoteAnswered(string tenant)
        {
            var cfg = ConfigStore.Current;
            if (IsCurrent(cfg) && cfg.IntunePlan is "active" or "answered") return;
            Store("answered", tenant);
        }

        /// <summary>
        /// Intune refused a request of the connection. An Intune known only because it answered before, or read long ago, is
        /// not known any more: the subscription may have ended (a trial), so the causes and "Check the subscription" come back.
        /// </summary>
        public static void NoteRefused()
        {
            var cfg = ConfigStore.Current;
            if (!IsCurrent(cfg)) return;
            bool stale = cfg.IntunePlan == "answered"
                || (cfg.IntunePlan == "active" && (cfg.IntunePlanUtc == null || DateTime.UtcNow - cfg.IntunePlanUtc.Value > ReadTrusted));
            if (stale) Store("", "");
        }

        private static void Store(string value, string tenant)
        {
            var cfg = ConfigStore.Current;
            tenant = value.Length > 0 ? (tenant ?? "").Trim() : "";
            if (value.Length > 0 && tenant.Length == 0) return;
            cfg.IntunePlan = value;
            cfg.IntunePlanTenant = tenant;
            cfg.IntunePlanUtc = value.Length > 0 ? DateTime.UtcNow : null;
            ConfigStore.Save();
            IntuneConnection.RaiseChanged();
        }

        /// <summary>Forgets what was found (the connection was removed).</summary>
        public static void Clear()
        {
            var cfg = ConfigStore.Current;
            cfg.IntunePlan = "";
            cfg.IntunePlanTenant = "";
            cfg.IntunePlanUtc = null;
        }

        /// <summary>
        /// Reads the plans of the tenant with a token of a user that holds <see cref="ReadPermission"/> (or a wider read of
        /// the directory). Null when the token cannot read them: without the permission Microsoft Graph leaves the plans out
        /// instead of refusing, and an empty list would wrongly say "no Intune". A token whose permissions cannot be read (no
        /// "scp" claim) is tried: then only plans that come back count.
        /// </summary>
        public static async Task<IntunePlanState?> ReadAsync(TokenSet token, CancellationToken ct)
        {
            if (token == null || string.IsNullOrEmpty(token.AccessToken)) return null;
            if (token.Claims != null && token.Claims.TryGetValue("scp", out var scp))
            {
                var granted = (scp ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!granted.Any(s => s is ReadPermission or "Organization.ReadWrite.All" or "Directory.Read.All" or "Directory.ReadWrite.All"))
                    return null;
            }
            var org = (await new Graph(token.AccessToken).GetAsync("/v1.0/organization?$select=id,assignedPlans", ct))?["value"]?.AsArray().FirstOrDefault();
            return org?["assignedPlans"] is JsonArray plans ? Classify(plans) : null;
        }

        /// <summary>The same, for the setup: a failure only leaves the subscription unknown.</summary>
        public static async Task<IntunePlanState> TryReadAsync(TokenSet token, CancellationToken ct)
        {
            try { return await ReadAsync(token, ct) ?? IntunePlanState.Unknown; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Warn(T("Log_PlanUnread", ex.Message));
                return IntunePlanState.Unknown;
            }
        }

        /// <summary>
        /// "Check the subscription": an administrator of the tenant signs in (Microsoft Graph Command Line Tools, read only,
        /// nothing is kept) and the plans of the tenant are read and remembered. Unknown when they hold a plan of Intune this
        /// application does not know.
        /// </summary>
        public static async Task<IntunePlanState> CheckAsync(CancellationToken ct)
        {
            string tenant = IntuneConnection.TenantId;
            var token = await EntraAuth.SignInAsync(tenant.Length > 0 ? tenant : "organizations", EntraAuth.GraphCliClientId,
                $"{EntraAuth.GraphUrl}/{ReadPermission} openid profile", IntuneConnection.UsesCertificate ? null : IntuneConnection.UserAccount, ct);
            if (tenant.Length > 0 && !string.IsNullOrEmpty(token.TenantId) && IntunePublisher.IsGuid(tenant)
                && !string.Equals(tenant, token.TenantId, StringComparison.OrdinalIgnoreCase))
                throw new AuthException(T("Plan_ErrOtherTenant", token.Account, IntuneConnection.TenantLabel));
            var state = await ReadAsync(token, ct) ?? throw new AuthException(T("Plan_ErrNoPermission", token.Account, ReadPermission));
            NoteRead(tenant.Length > 0 ? tenant : token.TenantId, state);
            Log.Info(T("Log_PlanChecked", IntuneConnection.TenantLabel, T("Plan_State_" + state)));
            return state;
        }
    }
}
