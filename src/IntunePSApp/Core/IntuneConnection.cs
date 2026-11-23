using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>A sign-in failure, already explained in the interface language.</summary>
    public class AuthException : Exception
    {
        public string Code { get; }
        public AuthException(string message, string code = "") : base(message) { Code = code ?? ""; }
    }

    /// <summary>Tokens of one sign-in and what the ID token says about the account.</summary>
    public sealed class TokenSet
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime ExpiresUtc { get; set; }
        public Dictionary<string, string> Claims { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string Account => Claim("preferred_username") ?? Claim("upn") ?? Claim("email") ?? "";
        public string DisplayName => Claim("name") ?? "";
        public string TenantId => Claim("tid") ?? "";
        /// <summary>A token of the application itself (certificate mode), not of a user.</summary>
        public bool App { get; set; }
        private string Claim(string name) => Claims.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        /// <summary>Claims of a JWT (payload only: the token came straight from the token endpoint over HTTPS).</summary>
        public static Dictionary<string, string> ReadClaims(string jwt)
        {
            var claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var parts = (jwt ?? "").Split('.');
                if (parts.Length < 2) return claims;
                string p = parts[1].Replace('-', '+').Replace('_', '/');
                p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
                using var doc = JsonDocument.Parse(Convert.FromBase64String(p));
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number) claims[prop.Name] = prop.Value.ToString();
                    else if (prop.Value.ValueKind == JsonValueKind.Array)
                        claims[prop.Name] = string.Join(" ", prop.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()));
            }
            catch { }
            return claims;
        }
    }

    /// <summary>
    /// Microsoft Entra ID sign-in for a public client: authorization code with PKCE in the default browser, answered on a
    /// loopback port (no secret, no password typed in the application). J365_LOGIN_URL and J365_GRAPH_URL point it to a
    /// national cloud or to a test server.
    /// </summary>
    public static class EntraAuth
    {
        /// <summary>Microsoft Graph Command Line Tools: Microsoft's public client used by Connect-MgGraph. Only used to create the app registration.</summary>
        public const string GraphCliClientId = "14d82eec-204b-4c2f-b7e8-296a70dab67e";
        public const string GraphAppId = "00000003-0000-0000-c000-000000000000";

        public static string LoginUrl => (Environment.GetEnvironmentVariable("J365_LOGIN_URL") is { Length: > 0 } u ? u : "https://login.microsoftonline.com").TrimEnd('/');
        public static string GraphUrl => (Environment.GetEnvironmentVariable("J365_GRAPH_URL") is { Length: > 0 } u ? u : "https://graph.microsoft.com").TrimEnd('/');

        /// <summary>Opens the sign-in page. Tests replace it with a call that follows the redirect without a browser.</summary>
        public static Func<string, Task> OpenBrowser { get; set; } = url =>
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return Task.CompletedTask;
        };

        public static async Task<TokenSet> SignInAsync(string tenant, string clientId, string scopes, string loginHint, CancellationToken ct)
        {
            string verifier = RandomUrlToken(32), state = RandomUrlToken(16);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            int port = FreePort();
            // Registered as "http://localhost": Entra ID accepts any port on the loopback address.
            string redirect = $"http://localhost:{port}";
            using var listener = new HttpListener();
            listener.Prefixes.Add(redirect + "/");
            listener.Start();
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["response_type"] = "code", ["redirect_uri"] = redirect, ["response_mode"] = "query",
                ["scope"] = scopes, ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["prompt"] = "select_account"
            };
            if (!string.IsNullOrWhiteSpace(loginHint)) query["login_hint"] = loginHint;
            string url = $"{LoginUrl}/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/authorize?" + string.Join("&", query.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));

            var contextTask = listener.GetContextAsync();
            await OpenBrowser(url);
            var finished = await Task.WhenAny(contextTask, Task.Delay(TimeSpan.FromMinutes(5), ct));
            if (finished != contextTask)
            {
                ct.ThrowIfCancellationRequested();
                throw new AuthException(T("Auth_ErrTimeout"));
            }
            var context = await contextTask;
            var q = context.Request.QueryString;
            bool ok = string.IsNullOrEmpty(q["error"]) && q["state"] == state && !string.IsNullOrEmpty(q["code"]);
            var failure = ok ? null : !string.IsNullOrEmpty(q["error"]) ? Explain(q["error"], q["error_description"], q["error_subcode"]) : new AuthException(T("Auth_ErrUnexpected"));
            await AnswerBrowserAsync(context, failure);
            if (!string.IsNullOrEmpty(q["error"])) throw failure;
            if (q["state"] != state || string.IsNullOrEmpty(q["code"])) throw new AuthException(T("Auth_ErrUnexpected"));
            return await TokenAsync(tenant, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = q["code"], ["redirect_uri"] = redirect,
                ["code_verifier"] = verifier, ["scope"] = scopes
            }, ct);
        }

        /// <summary>
        /// Asks Entra ID whether the registration exists before a browser opens: for an unknown or deleted registration the
        /// sign-in page shows its error and never comes back to the application, which would wait in vain. A device code
        /// request answers at once (the code is not used). Only "not found", "disabled" and "tenant not found" stop here;
        /// any other answer, or no answer, lets the sign-in go on.
        /// </summary>
        public static async Task CheckClientAsync(string tenant, string clientId, string scopes, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            HttpResponseMessage resp;
            var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = scopes });
            try { resp = await Http.Client.PostAsync($"{LoginUrl}/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/devicecode", form, cts.Token); }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested)) { return; }
            using var response = resp;
            if (resp.IsSuccessStatusCode) return;
            string error = "", description = "";
            try
            {
                var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
                error = json?["error"]?.GetValue<string>() ?? "";
                description = json?["error_description"]?.GetValue<string>() ?? "";
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return; }
            if (description.Contains("AADSTS700016") || description.Contains("AADSTS7000112") || description.Contains("AADSTS90002")) throw Explain(error, description);
        }

        public static Task<TokenSet> RefreshAsync(string tenant, string clientId, string refreshToken, string scopes, CancellationToken ct)
            => TokenAsync(tenant, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = refreshToken, ["scope"] = scopes
            }, ct);

        /// <summary>Token endpoint of a tenant (also the audience of a client assertion).</summary>
        public static string TokenUrl(string tenant) => $"{LoginUrl}/{Uri.EscapeDataString(tenant)}/oauth2/v2.0/token";

        /// <summary>
        /// App-only token for Microsoft Graph with the certificate of this computer (client credentials with a signed
        /// assertion): no user, no browser and no secret. It carries the application permissions granted to the
        /// app registration.
        /// </summary>
        public static async Task<TokenSet> ClientCredentialsAsync(string tenant, string clientId, X509Certificate2 certificate, CancellationToken ct)
        {
            var t = await TokenAsync(tenant, new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["scope"] = GraphUrl + "/.default",
                ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ["client_assertion"] = AppCertificate.ClientAssertion(certificate, TokenUrl(tenant), clientId)
            }, ct);
            t.App = true;
            return t;
        }

        private static async Task<TokenSet> TokenAsync(string tenant, Dictionary<string, string> form, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            HttpResponseMessage resp;
            try { resp = await Http.Client.PostAsync(TokenUrl(tenant), new FormUrlEncodedContent(form), cts.Token); }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                throw new AuthException(T("Auth_ErrNetwork", ex.Message), "network");
            }
            using var response = resp;
            string text = await resp.Content.ReadAsStringAsync(ct);
            JsonNode json = null;
            try { json = JsonNode.Parse(text); } catch { }
            string access = json?["access_token"]?.GetValue<string>();
            if (resp.IsSuccessStatusCode && !string.IsNullOrEmpty(access))
            {
                int seconds = 3600;
                try { seconds = (int)(json["expires_in"]?.GetValue<double>() ?? 3600); } catch { try { seconds = int.Parse(json["expires_in"]?.GetValue<string>() ?? "3600"); } catch { } }
                var claims = TokenSet.ReadClaims(json["id_token"]?.GetValue<string>());
                foreach (var kv in TokenSet.ReadClaims(access)) claims.TryAdd(kv.Key, kv.Value);
                return new TokenSet
                {
                    AccessToken = access,
                    RefreshToken = json["refresh_token"]?.GetValue<string>() ?? form.GetValueOrDefault("refresh_token", ""),
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds)),
                    Claims = claims
                };
            }
            throw Explain(json?["error"]?.GetValue<string>() ?? ("HTTP " + (int)resp.StatusCode), json?["error_description"]?.GetValue<string>());
        }

        /// <summary>
        /// A failure of the network rather than a refusal: no answer, a timeout, or a server that is busy or failing
        /// (HTTP 429 and 5xx). "HTTP 400" or "HTTP 403" are answers of the service, not network failures.
        /// </summary>
        public static bool IsNetworkFailure(string code)
        {
            code ??= "";
            if (code is "network" or nameof(HttpRequestException) or nameof(TaskCanceledException) or nameof(OperationCanceledException) or nameof(TimeoutException)) return true;
            if (!code.StartsWith("HTTP ", StringComparison.Ordinal) || !int.TryParse(code.AsSpan(5), out int status)) return false;
            return status == 429 || status >= 500;
        }

        /// <summary>The usual Entra ID errors, in plain words (the AADSTS code stays at the end for the administrator).</summary>
        public static AuthException Explain(string error, string description, string subcode = null)
        {
            string first = (description ?? "").Split('\n')[0].Trim();
            // "access_denied" + "cancel" with no description: the consent page was closed with Cancel, or the account
            // could not grant the permission ("Need admin approval" > "Return to the application"), or Entra ID refused
            // to add the permission without showing anything. The application explains the three cases.
            if (error == "access_denied" && string.Equals(subcode, "cancel", StringComparison.OrdinalIgnoreCase) && !first.Contains("AADSTS53003"))
                return new AuthException(T("Auth_ErrCancelled") + (first.Length > 0 ? $" [{Shorten(first)}]" : ""), "cancel");
            var known = new (string Code, string Key)[]
            {
                ("AADSTS65001", "Auth_Err65001"), ("AADSTS90094", "Auth_Err90094"), ("AADSTS90008", "Auth_Err90094"),
                ("AADSTS50011", "Auth_Err50011"), ("AADSTS700016", "Auth_Err700016"), ("AADSTS7000218", "Auth_Err7000218"),
                ("AADSTS50020", "Auth_Err50020"), ("AADSTS50034", "Auth_Err50020"), ("AADSTS53003", "Auth_Err53003"),
                ("AADSTS50105", "Auth_Err50105"), ("AADSTS65004", "Auth_Err65004"), ("AADSTS90002", "Auth_Err90002"),
                ("AADSTS50126", "Auth_Err50126"), ("AADSTS70008", "Auth_Err70008"), ("AADSTS700082", "Auth_Err70008"), ("AADSTS7000112", "Auth_Err7000112"),
                ("AADSTS700027", "Auth_Err700027"), ("AADSTS700024", "Auth_Err700024")
            };
            foreach (var (code, key) in known)
                if (first.Contains(code, StringComparison.OrdinalIgnoreCase) || (error ?? "").Contains(code, StringComparison.OrdinalIgnoreCase))
                    return new AuthException(T(key) + (first.Length > 0 ? $" [{Shorten(first)}]" : ""), code);
            if (error == "access_denied") return new AuthException(T("Auth_ErrDenied") + (first.Length > 0 ? $" [{Shorten(first)}]" : ""), error);
            return new AuthException(T("Auth_ErrGeneric", first.Length > 0 ? Shorten(first) : error ?? "?"), error ?? "");
        }

        private static string Shorten(string s) => s.Length > 240 ? s.Substring(0, 240) + "…" : s;

        /// <summary>The page the browser shows after the sign-in: done, or what to do next (the application explains the details).</summary>
        private static async Task AnswerBrowserAsync(HttpListenerContext context, AuthException failure)
        {
            try
            {
                bool ok = failure == null;
                bool cancelled = failure?.Code == "cancel";
                string title = ok ? T("Auth_PageOkTitle") : cancelled ? T("Auth_PageCancelTitle") : T("Auth_PageFailTitle");
                string text = ok ? T("Auth_PageOk", AppInfo.Name) : cancelled ? T("Auth_PageCancel", AppInfo.Name) : T("Auth_PageFail", AppInfo.Name);
                string accent = ok ? "#0F8A5F" : "#B4580A";
                string mark = ok ? "&#10003;" : "!";
                string html = "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>" + WebUtility.HtmlEncode(AppInfo.Name) + "</title></head>"
                    + "<body style=\"font-family:'Segoe UI Variable Text','Segoe UI',Arial,sans-serif;background:radial-gradient(circle at 15% 0%,#173062 0%,#0A1122 55%);color:#fff;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0\">"
                    + "<div style=\"background:#fff;color:#0B1324;border-radius:18px;padding:36px 42px;max-width:540px;margin:24px;box-shadow:0 24px 60px rgba(0,0,0,.35)\">"
                    + "<div style=\"display:flex;align-items:center;gap:12px\"><div style=\"width:38px;height:38px;border-radius:12px;background:" + accent + "1A;color:" + accent + ";display:flex;align-items:center;justify-content:center;font-weight:700;font-size:20px\">" + mark + "</div>"
                    + "<div style=\"font-size:12px;letter-spacing:.1em;color:#2457E6;font-weight:600\">" + WebUtility.HtmlEncode(AppInfo.Name.ToUpperInvariant()) + "</div></div>"
                    + "<h2 style=\"margin:18px 0 10px;font-size:22px\">" + WebUtility.HtmlEncode(title) + "</h2><p style=\"color:#5F6C84;line-height:1.55;margin:0\">" + WebUtility.HtmlEncode(text) + "</p>"
                    + "<p style=\"color:#8E9AB0;font-size:13px;margin:18px 0 0\">" + WebUtility.HtmlEncode(T("Auth_PageClose")) + "</p></div></body></html>";
                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch { }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static string RandomUrlToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
        private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The steps of the automatic setup, in order (the connection window shows each one).</summary>
    public enum SetupStep { AdminSignIn, Permission, Registration, ServicePrincipal, Consent, Certificate, Activation, UserSignIn, Test }

    /// <summary>A step of the automatic setup that starts, with an optional detail line.</summary>
    public sealed class SetupProgress
    {
        public SetupProgress(SetupStep step, string detail = "") { Step = step; Detail = detail ?? ""; }
        public SetupStep Step { get; }
        public string Detail { get; }
    }

    /// <summary>What the automatic setup did in the tenant.</summary>
    public sealed class RegistrationResult
    {
        public string TenantId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string Admin { get; set; } = "";
        /// <summary>The tenant as people read it (the domain of the administrator).</summary>
        public string TenantName { get; set; } = "";
        public bool Reused { get; set; }
        public bool ConsentGranted { get; set; }
        public string ConsentUrl { get; set; } = "";
        /// <summary>Certificate mode: the application permission and the certificate of this computer.</summary>
        public bool Certificate { get; set; }
        public bool RoleGranted { get; set; }
        public string CertificateThumbprint { get; set; } = "";
        /// <summary>Certificate mode: the new certificate, to make the connection once it obtained a token.</summary>
        public PendingCertificate Pending { get; set; }
        /// <summary>What the tenant has of Intune, read with the administrator's sign-in.</summary>
        public IntunePlanState Plan { get; set; }
    }

    /// <summary>A certificate created by the setup and not in use yet: it becomes the connection once it obtained a token.</summary>
    public sealed class PendingCertificate
    {
        public string TenantId { get; init; } = "";
        public string ClientId { get; init; } = "";
        public string Thumbprint { get; init; } = "";
        public string TenantName { get; init; } = "";
    }

    /// <summary>
    /// The connection to Microsoft Intune: the app registration of the tenant (created automatically or typed by hand) and
    /// the session of the signed-in user. The refresh token is kept encrypted for the Windows user (DPAPI), so publishing
    /// does not ask to sign in every time; nothing else about the account is stored.
    /// </summary>
    public static class IntuneConnection
    {
        public const string RegistrationName = "J365 IntunePSApp";
        public const string RegistrationTag = "J365IntunePSApp";
        public const string IntunePermission = "DeviceManagementApps.ReadWrite.All";

        /// <summary>First word of the connection code that an administrator shares with the other technicians.</summary>
        public const string CodePrefix = "J365-CONNECT";

        /// <summary>Privileged Identity Management > My roles: where an eligible administrator role is activated.</summary>
        public const string PimUrl = "https://portal.azure.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade/~/aadmigratedroles";
        public const string CloudShellUrl = "https://shell.azure.com";
        public static string IntuneScopes => $"{EntraAuth.GraphUrl}/{IntunePermission} offline_access openid profile";
        public const string DelegatedMode = "delegated";
        public const string CertificateMode = "certificate";

        /// <summary>
        /// What the administrator's sign-in of the setup asks for (Microsoft Graph Command Line Tools): creating the
        /// registration and its delegated consent, for the certificate mode the grant of the application permission, and
        /// reading the subscriptions of the tenant (does it have Intune?). Nothing of this sign-in is kept.
        /// </summary>
        private static string AdminScopes(bool certificate) =>
            $"{EntraAuth.GraphUrl}/Application.ReadWrite.All {EntraAuth.GraphUrl}/DelegatedPermissionGrant.ReadWrite.All "
            + (certificate ? $"{EntraAuth.GraphUrl}/AppRoleAssignment.ReadWrite.All " : "")
            + $"{EntraAuth.GraphUrl}/{IntuneSubscription.ReadPermission} offline_access openid profile";

        private sealed class Saved
        {
            public string TenantId { get; set; } = "";
            public string ClientId { get; set; } = "";
            public string Account { get; set; } = "";
            public string Name { get; set; } = "";
            public string Token { get; set; } = "";
            public DateTime SignedInUtc { get; set; }
        }

        private static readonly object Gate = new object();
        private static TokenSet _current;
        private static Saved _saved;
        private static bool _loaded;

        public static event Action Changed;

        internal static void RaiseChanged() => Changed?.Invoke();

        private static string SessionFile => Path.Combine(AppPaths.Data, "intune-session.dat");

        public static string TenantId => _trial != null ? _trial.Tenant : ConfigStore.Current.IntuneTenantId?.Trim() ?? "";
        public static string ClientId => _trial != null ? _trial.Client : ConfigStore.Current.IntuneClientId?.Trim() ?? "";
        public static bool IsConfigured => IntunePublisher.IsTenant(TenantId) && IntunePublisher.IsGuid(ClientId);

        /// <summary>
        /// Connected as the application with the certificate of this computer (app-only): publishing needs no user, so it
        /// does not depend on the role, the PIM activation or the Intune license of an account.
        /// </summary>
        public static bool UsesCertificate => _trial == null && string.Equals(ConfigStore.Current.IntuneAuthMode, CertificateMode, StringComparison.OrdinalIgnoreCase) && CertificateThumbprint.Length > 0;

        public static string CertificateThumbprint => AppCertificate.Normalize(ConfigStore.Current.IntuneCertificateThumbprint);

        /// <summary>The tenant as people read it: its domain when the setup knew it, otherwise the tenant in Settings.</summary>
        public static string TenantLabel => _trial != null ? (_trial.TenantName.Length > 0 ? _trial.TenantName : TenantId)
            : ConfigStore.Current.IntuneTenantName is { Length: > 0 } n ? n : TenantId;

        /// <summary>
        /// What the last use of the connection found wrong with the registration itself (deleted or disabled in Entra ID,
        /// the tenant not found, the certificate no longer registered): Settings and the sidebar say so until the connection
        /// works again or changes. Empty when nothing is known to be wrong.
        /// </summary>
        public static string Problem => _problem.Length > 0 && _problemKey == ConnectionKey() ? _problem : "";

        private static string _problem = "", _problemKey = "";

        /// <summary>The connection a problem belongs to: a problem of a registration tried and dropped never shows on another.</summary>
        private static string ConnectionKey() => $"{TenantId}|{ClientId}|{(UsesCertificate ? CertificateThumbprint : "")}".ToUpperInvariant();

        /// <summary>
        /// The connection cannot publish until it is made again: nothing configured, the registration gone or disabled, or
        /// a certificate that cannot be used. (A user connection without a session only needs to sign in.)
        /// </summary>
        public static bool NeedsSetup => !IsConfigured || Problem.Length > 0 || (UsesCertificate && !IsSignedIn);

        /// <summary>Remembers a failure that only a new connection fixes, so the interface can say so before the next try.</summary>
        private static void Note(AuthException ex)
        {
            if (ex.Code is not ("AADSTS700016" or "AADSTS7000112" or "AADSTS90002" or "AADSTS700027")) return;
            _problem = ex.Message;
            _problemKey = ConnectionKey();
            Changed?.Invoke();
        }

        private static void ClearProblem()
        {
            if (Problem.Length == 0) return;
            _problem = "";
            Changed?.Invoke();
        }

        /// <summary>
        /// Intune refused the last request of this connection (the sign-in worked): the status says so until Intune
        /// answers again or the connection changes. Why is <see cref="IntuneSubscription"/> and <see cref="IntuneDiagnosis"/>.
        /// </summary>
        public static bool Refused => _refusedKey.Length > 0 && _refusedKey == ConnectionKey();

        private static string _refusedKey = "";

        /// <summary>
        /// Notes how Intune answered a request of this connection: an answer, which also proves that the tenant has Intune,
        /// or a refusal, after which an Intune known only from earlier answers is not taken for granted any more (a trial
        /// may have ended).
        /// </summary>
        public static void NoteIntune(bool refused)
        {
            string key = refused ? ConnectionKey() : "";
            bool changed = key != _refusedKey;
            _refusedKey = key;
            if (refused) IntuneSubscription.NoteRefused();
            else if (IsConfigured) IntuneSubscription.NoteAnswered(TenantId);
            if (changed) Changed?.Invoke();
        }

        /// <summary>A request to Intune: its refusal or its answer is noted for the status.</summary>
        private static async Task<TResult> AskIntuneAsync<TResult>(Func<Task<TResult>> request)
        {
            try
            {
                var result = await request();
                NoteIntune(refused: false);
                return result;
            }
            catch (GraphException ex) when (IntuneDiagnosis.IsRefusal(ex))
            {
                NoteIntune(refused: true);
                throw;
            }
        }

        /// <summary>A failure of the moment (network, proxy, a busy or failing server): the session is kept for the next try.</summary>
        private static bool IsTransient(string code)
            => code is "network" or "temporarily_unavailable" or "server_error" || code.StartsWith("HTTP ", StringComparison.Ordinal) || EntraAuth.IsNetworkFailure(code);
        /// <summary>The last token in use (to explain a refusal of Intune), or null.</summary>
        public static TokenSet LastToken { get { lock (Gate) return _current; } }

        /// <summary>
        /// Signed in with the registration in Settings. An app registration belongs to one tenant, so the client id of
        /// the saved session identifies it (the tenant in Settings may be a domain or an id).
        /// </summary>
        public static bool IsSignedIn
        {
            get
            {
                if (UsesCertificate) return IsConfigured && CertificateUsable();
                var s = Load();
                return s != null && s.Token.Length > 0 && IsConfigured && string.Equals(s.ClientId, ClientId, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static (string Thumbprint, bool Usable, DateTime Checked) _usable;

        /// <summary>The certificate in use opens its key; read again every few seconds (the status is asked for often).</summary>
        private static bool CertificateUsable()
        {
            string thumbprint = CertificateThumbprint;
            lock (Gate)
                if (_usable.Thumbprint == thumbprint && _usable.Checked > DateTime.UtcNow.AddSeconds(-5)) return _usable.Usable;
            bool usable = AppCertificate.IsUsable(thumbprint);
            lock (Gate) _usable = (thumbprint, usable, DateTime.UtcNow);
            return usable;
        }

        /// <summary>Who publishes: the signed-in account, or the application with the certificate of this computer.</summary>
        public static string Account => UsesCertificate ? T("Intune_CertIdentity", TenantLabel) : UserAccount;
        /// <summary>The account of the last user sign-in (also the hint of the next one).</summary>
        public static string UserAccount => Load()?.Account ?? "";
        public static string SessionTenant => UsesCertificate ? TenantId : Load()?.TenantId ?? "";

        /// <summary>
        /// "J365-CONNECT tenant=... client=...": the two values another technician needs to use the same registration
        /// (each one then signs in with his own account). Empty when nothing is configured.
        /// </summary>
        public static string ConnectionCode
        {
            get
            {
                if (!IsConfigured) return "";
                string tenant = SessionTenant.Length > 0 && IsSignedIn ? SessionTenant : TenantId;
                return $"{CodePrefix} tenant={tenant} client={ClientId}";
            }
        }

        /// <summary>Reads a connection code (also the last line printed by the Cloud Shell command).</summary>
        public static bool TryParseConnectionCode(string text, out string tenant, out string client)
        {
            tenant = client = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = Regex.Match(text, @"tenant\s*=\s*([A-Za-z0-9][A-Za-z0-9.\-]*)", RegexOptions.IgnoreCase);
            var c = Regex.Match(text, @"client\s*=\s*([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})", RegexOptions.IgnoreCase);
            if (!t.Success || !c.Success) return false;
            tenant = t.Groups[1].Value.TrimEnd('.');
            client = c.Groups[1].Value;
            return IntunePublisher.IsTenant(tenant) && IntunePublisher.IsGuid(client);
        }

        /// <summary>
        /// A registration being tried (<see cref="BeginTrial"/>). It lives in memory only: Settings keep the connection in
        /// use until the trial is kept, so a save in the middle of it (or the application closed) never writes it.
        /// </summary>
        public sealed class RegistrationSnapshot
        {
            internal string Tenant = "", Client = "", TenantName = "";
            /// <summary>The session file as it was (null: none), put back when the trial is not kept, even after a sign-in.</summary>
            internal byte[] Session;
        }

        private static RegistrationSnapshot _trial;

        /// <summary>
        /// Uses another app registration for the next sign-in without saving it or ending the current session (the
        /// automatic setup with an account, a code, IDs, Cloud Shell). <see cref="EndTrial"/> makes it the connection once
        /// the whole connection worked, or drops it, so a wrong code, a cancel or a failure leaves the connection in use
        /// as it was. Returns the trial, to end it.
        /// </summary>
        public static RegistrationSnapshot BeginTrial(string tenant, string client, string tenantName = "")
        {
            var trial = new RegistrationSnapshot { Tenant = tenant?.Trim() ?? "", Client = client?.Trim() ?? "", TenantName = tenantName?.Trim() ?? "" };
            lock (Gate)
            {
                try { trial.Session = File.Exists(SessionFile) ? File.ReadAllBytes(SessionFile) : null; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { trial.Session = null; }
                _trial = trial;
                _current = null;
            }
            return trial;
        }

        /// <summary>Ends <see cref="BeginTrial"/>: <paramref name="keep"/> makes the registration tried the connection of this computer.</summary>
        public static void EndTrial(RegistrationSnapshot trial, bool keep)
        {
            if (trial == null) return;
            lock (Gate) if (ReferenceEquals(_trial, trial)) _trial = null;
            var cfg = ConfigStore.Current;
            if (keep)
            {
                // One connection at a time: a certificate of the connection it replaces goes (the user agreed first).
                if (AppCertificate.Normalize(cfg.IntuneCertificateThumbprint) is { Length: > 0 } old) DeleteLocalCertificate(old);
                cfg.IntuneCertificateThumbprint = "";
                cfg.IntuneAuthMode = DelegatedMode;
                cfg.IntuneTenantId = trial.Tenant;
                cfg.IntuneClientId = trial.Client;
                cfg.IntuneTenantName = trial.TenantName;
                lock (Gate) _usable = default;
                _problem = "";
                ConfigStore.Save();
            }
            else
            {
                // Nothing of the trial stays, the session included: a sign-in that happened during it is undone.
                lock (Gate)
                {
                    try
                    {
                        if (trial.Session != null) File.WriteAllBytes(SessionFile, trial.Session);
                        else if (File.Exists(SessionFile)) File.Delete(SessionFile);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    _loaded = false;
                    _saved = null;
                    _current = null;
                }
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Disconnects this computer: the session, the certificate with its key (its public part stays in the registration
        /// until it expires or an administrator removes it) and the registration saved here. The next connection starts
        /// from the beginning; the registration in Entra ID is not changed.
        /// </summary>
        public static void Disconnect()
        {
            var cfg = ConfigStore.Current;
            if (AppCertificate.Normalize(cfg.IntuneCertificateThumbprint) is { Length: > 0 } t) DeleteLocalCertificate(t);
            cfg.IntuneCertificateThumbprint = "";
            cfg.IntuneAuthMode = DelegatedMode;
            cfg.IntuneTenantId = "";
            cfg.IntuneClientId = "";
            cfg.IntuneTenantName = "";
            IntuneSubscription.Clear();
            lock (Gate) _usable = default;
            ConfigStore.Save();
            _problem = "";
            _refusedKey = "";
            SignOut();
        }

        /// <summary>
        /// Makes a new certificate the connection of this computer once it obtained a token. The certificate in use until
        /// then is removed only now, so a renewal that fails leaves the previous connection working.
        /// </summary>
        public static async Task ActivateCertificateAsync(PendingCertificate pending, CancellationToken ct)
        {
            var token = await CertificateTokenAsync(pending.TenantId, pending.ClientId, pending.Thumbprint, ct);
            var cfg = ConfigStore.Current;
            string old = AppCertificate.Normalize(cfg.IntuneCertificateThumbprint);
            cfg.IntuneTenantId = pending.TenantId;
            cfg.IntuneClientId = pending.ClientId;
            cfg.IntuneTenantName = pending.TenantName;
            cfg.IntuneAuthMode = CertificateMode;
            cfg.IntuneCertificateThumbprint = pending.Thumbprint;
            ConfigStore.Save();
            if (old.Length > 0 && old != AppCertificate.Normalize(pending.Thumbprint)) DeleteLocalCertificate(old);
            // One connection at a time: a user session of the previous connection goes too.
            DeleteSession();
            _problem = "";
            lock (Gate)
            {
                _current = token;
                _appTokenKey = AppTokenKey(pending.TenantId, pending.ClientId, pending.Thumbprint);
                _usable = default;
            }
            Log.Ok(T("Log_CertificateActive", pending.Thumbprint, pending.TenantName));
            Changed?.Invoke();
        }

        /// <summary>A new certificate that never became the connection (setup cancelled, failed or abandoned): removed again.</summary>
        public static void DiscardCertificate(PendingCertificate pending)
        {
            // Never the certificate in use.
            if (AppCertificate.Normalize(pending?.Thumbprint) is { Length: > 0 } t && t != CertificateThumbprint) DeleteLocalCertificate(t);
        }

        private static void DeleteLocalCertificate(string thumbprint)
        {
            try { AppCertificate.Delete(thumbprint); }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException) { Log.Warn(T("Cert_ErrDelete", ex.Message)); }
        }

        private static Saved Load()
        {
            lock (Gate)
            {
                if (_loaded) return _saved;
                _loaded = true;
                try
                {
                    if (File.Exists(SessionFile))
                    {
                        var plain = Unprotect(File.ReadAllBytes(SessionFile));
                        _saved = JsonSerializer.Deserialize<Saved>(plain);
                    }
                }
                catch { _saved = null; }
                return _saved;
            }
        }

        private static void Save(TokenSet t, string tenant, string client)
        {
            var s = new Saved
            {
                TenantId = string.IsNullOrEmpty(t.TenantId) ? tenant : t.TenantId,
                ClientId = client,
                Account = t.Account,
                Name = t.DisplayName,
                Token = t.RefreshToken ?? "",
                SignedInUtc = DateTime.UtcNow
            };
            lock (Gate)
            {
                _saved = s;
                _loaded = true;
                _current = t;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SessionFile));
                    File.WriteAllBytes(SessionFile, Protect(JsonSerializer.SerializeToUtf8Bytes(s)));
                }
                catch (Exception ex) { Log.Warn(T("Auth_SessionNotSaved", ex.Message)); }
            }
            Changed?.Invoke();
        }

        public static void SignOut()
        {
            DeleteSession();
            _refusedKey = "";
            Changed?.Invoke();
        }

        private static void DeleteSession()
        {
            lock (Gate)
            {
                _saved = null;
                _current = null;
                _loaded = true;
                try { if (File.Exists(SessionFile)) File.Delete(SessionFile); } catch { }
            }
        }

        /// <summary>Signs in (browser) with the registration in Settings and keeps the session.</summary>
        public static async Task<TokenSet> SignInAsync(CancellationToken ct, string loginHint = null)
        {
            if (!IsConfigured) throw new AuthException(T("Auth_ErrNotConfigured"));
            if (UsesCertificate) return await AppTokenAsync(ct, force: true);
            TokenSet t;
            try
            {
                // A deleted or unknown registration is found before the browser opens: its sign-in page would show the
                // error and never come back to the application.
                await EntraAuth.CheckClientAsync(TenantId, ClientId, IntuneScopes, ct);
                t = await EntraAuth.SignInAsync(TenantId, ClientId, IntuneScopes, loginHint ?? Load()?.Account, ct);
            }
            catch (AuthException ex) { Note(ex); throw; }
            Save(t, TenantId, ClientId);
            ClearProblem();
            // Another account may have the Intune role the previous one lacked: the next request says.
            _refusedKey = "";
            Log.Ok(T("Log_IntuneSignedIn", t.Account, t.TenantId.Length > 0 ? t.TenantId : TenantId));
            return t;
        }

        /// <summary>A fresh token set of the saved session (renewed with its refresh token); null when there is no session.</summary>
        public static async Task<TokenSet> RenewAsync(CancellationToken ct)
        {
            // The certificate explains itself when it cannot be used (missing, expired, its key gone).
            if (UsesCertificate) return await AppTokenAsync(ct);
            if (!IsSignedIn) return null;
            if (_current != null && !_current.App && _current.ExpiresUtc > DateTime.UtcNow.AddMinutes(10) && !string.IsNullOrEmpty(_current.RefreshToken)) return _current;
            var saved = Load();
            try
            {
                var t = await EntraAuth.RefreshAsync(saved.TenantId.Length > 0 ? saved.TenantId : TenantId, ClientId, saved.Token, IntuneScopes, ct);
                if (string.IsNullOrEmpty(t.Account)) t.Claims["preferred_username"] = saved.Account;
                if (string.IsNullOrEmpty(t.TenantId)) t.Claims["tid"] = saved.TenantId;
                Save(t, TenantId, ClientId);
                ClearProblem();
                return t;
            }
            catch (AuthException ex) when (!IsTransient(ex.Code))
            {
                // Expired, revoked or blocked: the next action signs in again. A failure of the moment keeps the session.
                SignOut();
                Note(ex);
                throw;
            }
        }

        /// <summary>
        /// A token of the application (certificate mode), reused until 5 minutes before it expires. Each request is a
        /// fresh assertion signed with the key of the certificate; nothing is stored.
        /// </summary>
        private static async Task<TokenSet> AppTokenAsync(CancellationToken ct, bool force = false)
        {
            if (!IsConfigured) throw new AuthException(T("Auth_ErrNotConfigured"));
            // A token is reused only for the same tenant, registration and certificate.
            string key = AppTokenKey(TenantId, ClientId, CertificateThumbprint);
            lock (Gate)
                if (!force && _current is { App: true } c && _appTokenKey == key && c.ExpiresUtc > DateTime.UtcNow.AddMinutes(5)) return c;
            TokenSet t;
            try { t = await CertificateTokenAsync(TenantId, ClientId, CertificateThumbprint, ct); }
            catch (AuthException ex) { Note(ex); throw; }
            lock (Gate) { _current = t; _appTokenKey = key; }
            ClearProblem();
            return t;
        }

        private static string _appTokenKey = "";
        private static string AppTokenKey(string tenant, string client, string thumbprint) => $"{tenant?.Trim()}|{client?.Trim()}|{AppCertificate.Normalize(thumbprint)}".ToUpperInvariant();

        /// <summary>A token with the certificate <paramref name="thumbprint"/>, explained when the certificate cannot be used.</summary>
        private static async Task<TokenSet> CertificateTokenAsync(string tenant, string client, string thumbprint, CancellationToken ct)
        {
            using var certificate = AppCertificate.Find(thumbprint) ?? throw new AuthException(T("Auth_ErrCertMissing"), "certificate_missing");
            if (certificate.NotAfter <= DateTime.Now)
                throw new AuthException(T("Auth_ErrCertExpired", certificate.NotAfter.ToString("d", J365.IntunePSApp.Localization.Loc.Instance.Culture)), "certificate_expired");
            try { return await EntraAuth.ClientCredentialsAsync(tenant, client, certificate, ct); }
            catch (CryptographicException ex)
            {
                // The certificate is listed but its key is gone (a cleared TPM, a removed key).
                throw new AuthException(T("Auth_ErrCertMissing") + $" [{ex.Message}]", "certificate_missing");
            }
        }

        /// <summary>
        /// Checks the connection quietly (at start-up): the registration still exists and accepts this computer, so a
        /// registration deleted in Entra ID shows up in Settings at once instead of at the next publication. A failure of
        /// the network, or anything else, is left for the next real use.
        /// </summary>
        public static async Task CheckAsync(CancellationToken ct)
        {
            // A certificate that 2.4.2 kept beside a user connection: nothing reaches it any more, so it does not stay.
            var cfg = ConfigStore.Current;
            if (_trial == null && !string.Equals(cfg.IntuneAuthMode, CertificateMode, StringComparison.OrdinalIgnoreCase)
                && AppCertificate.Normalize(cfg.IntuneCertificateThumbprint) is { Length: > 0 } leftover)
            {
                DeleteLocalCertificate(leftover);
                cfg.IntuneCertificateThumbprint = "";
                ConfigStore.Save();
                Log.Info(T("Log_CertificateLeftover", leftover));
            }
            if (!IsConfigured) return;
            try
            {
                if (UsesCertificate) { if (CertificateUsable()) await AppTokenAsync(ct); }
                else if (IsSignedIn) await RenewAsync(ct);
                else await EntraAuth.CheckClientAsync(TenantId, ClientId, IntuneScopes, ct);
            }
            catch (AuthException ex) { Note(ex); }
            catch (Exception ex) when (ex is GraphException or HttpRequestException or OperationCanceledException or IOException) { }
        }

        /// <summary>
        /// Creates (or reuses) the app registration "J365 IntunePSApp" in the tenant of the administrator who signs in:
        /// public client with the loopback redirect, the delegated permission DeviceManagementApps.ReadWrite.All and the
        /// admin consent for the organisation. With <paramref name="certificate"/> the registration also gets the same
        /// permission as an application permission (granted by the administrator) and the public part of a certificate
        /// created on this computer, so publishing runs as the application. Settings do not change here: the new
        /// connection takes over once it is proven (<see cref="BeginTrial"/> for an account, ActivateCertificateAsync).
        /// </summary>
        public static async Task<RegistrationResult> CreateRegistrationAsync(IProgress<SetupProgress> progress, CancellationToken ct, string adminHint = null, bool certificate = false)
        {
            progress?.Report(new SetupProgress(SetupStep.AdminSignIn));
            var admin = await EntraAuth.SignInAsync("organizations", EntraAuth.GraphCliClientId, AdminScopes(certificate), adminHint, ct);
            string tenant = admin.TenantId;
            if (string.IsNullOrEmpty(tenant)) throw new AuthException(T("Auth_ErrNoTenant"));
            var g = new Graph(admin.AccessToken);
            var result = new RegistrationResult { TenantId = tenant, Admin = admin.Account, Certificate = certificate };
            progress?.Report(new SetupProgress(SetupStep.Permission, admin.Account));
            // Does the tenant have Intune? Without it every publication is refused, whatever the connection: said at once.
            var reading = await IntuneSubscription.TryReadAsync(admin, ct);
            result.Plan = reading.State;
            if (reading.State != IntunePlanState.Unknown) IntuneSubscription.NoteRead(tenant, reading.State, reading.Plans);
            try { return await CreateRegistrationCoreAsync(g, result, progress, ct); }
            catch (GraphException ex) when (ex.Status is 401 or 403)
            {
                // Signed in, but the account has no directory role that may create applications.
                throw new AuthException(T("Connect_ErrInsufficient", admin.Account) + $" [{ex.Message}]", "insufficient_privileges");
            }
        }

        private static async Task<RegistrationResult> CreateRegistrationCoreAsync(Graph g, RegistrationResult result, IProgress<SetupProgress> progress, CancellationToken ct)
        {
            string tenant = result.TenantId;
            var graphSp = await g.GetAsync($"/v1.0/servicePrincipals(appId='{EntraAuth.GraphAppId}')?$select=id,oauth2PermissionScopes,appRoles", ct);
            string graphSpId = graphSp?["id"]?.GetValue<string>() ?? throw new InvalidDataException(T("Connect_ErrGraphSp"));
            string scopeId = graphSp["oauth2PermissionScopes"]?.AsArray()
                .FirstOrDefault(s => s?["value"]?.GetValue<string>() == IntunePermission)?["id"]?.GetValue<string>()
                ?? throw new InvalidDataException(T("Connect_ErrScope", IntunePermission));
            string roleId = !result.Certificate ? null : graphSp["appRoles"]?.AsArray()
                .FirstOrDefault(r => r?["value"]?.GetValue<string>() == IntunePermission)?["id"]?.GetValue<string>()
                ?? throw new InvalidDataException(T("Connect_ErrScope", IntunePermission));

            progress?.Report(new SetupProgress(SetupStep.Registration));
            string filter = Uri.EscapeDataString($"displayName eq '{RegistrationName}'");
            var found = (await g.GetAsync($"/v1.0/applications?$filter={filter}&$select=id,appId,displayName,tags,requiredResourceAccess", ct))?["value"]?.AsArray();
            var existing = found?.FirstOrDefault(a => a?["tags"]?.AsArray().Any(x => x?.GetValue<string>() == RegistrationTag) == true) ?? found?.FirstOrDefault();
            var body = new JsonObject
            {
                ["displayName"] = RegistrationName,
                ["description"] = "Publishes Win32 apps packaged with J365 IntunePSApp to Microsoft Intune: as the signed-in user (delegated permission) or, with a certificate of the technician's computer, as the application.",
                ["signInAudience"] = "AzureADMyOrg",
                ["isFallbackPublicClient"] = true,
                ["publicClient"] = new JsonObject { ["redirectUris"] = new JsonArray("http://localhost") },
                ["requiredResourceAccess"] = MergeAccess(existing?["requiredResourceAccess"] as JsonArray, scopeId, roleId),
                ["tags"] = new JsonArray(RegistrationTag),
                ["notes"] = $"Created by {AppInfo.Name} {AppInfo.Version} ({AppInfo.RepoUrl}) on {DateTime.UtcNow:yyyy-MM-dd}."
            };
            string appId, objectId;
            if (existing != null)
            {
                // Same registration from an earlier setup: its configuration is set again (redirect, permissions, public client).
                result.Reused = true;
                appId = existing["appId"]!.GetValue<string>();
                objectId = existing["id"]!.GetValue<string>();
                body.Remove("notes");
                await g.SendAsync(HttpMethod.Patch, $"/v1.0/applications/{objectId}", body, ct);
            }
            else
            {
                // Not sent twice: a timeout or a 5xx may come after the registration was created, and a second one would be
                // a duplicate. The setup can simply be run again: it finds the registration by its tag and reuses it.
                var created = await g.SendAsync(HttpMethod.Post, "/v1.0/applications", body, ct, repeatable: false);
                appId = created?["appId"]?.GetValue<string>() ?? throw new InvalidDataException(T("Connect_ErrCreate"));
                objectId = created?["id"]?.GetValue<string>() ?? throw new InvalidDataException(T("Connect_ErrCreate"));
            }
            result.ClientId = appId;

            progress?.Report(new SetupProgress(SetupStep.ServicePrincipal, appId));
            string spId = null;
            for (int attempt = 1; spId == null; attempt++)
            {
                var sps = (await g.GetAsync($"/v1.0/servicePrincipals?$filter={Uri.EscapeDataString($"appId eq '{appId}'")}&$select=id", ct))?["value"]?.AsArray();
                spId = sps?.FirstOrDefault()?["id"]?.GetValue<string>();
                if (spId != null) break;
                try { spId = (await g.SendAsync(HttpMethod.Post, "/v1.0/servicePrincipals", new JsonObject { ["appId"] = appId }, ct))?["id"]?.GetValue<string>(); }
                catch (GraphException ex) when (attempt < 8 && ex.Status is 400 or 404 or 409)
                {
                    // A new application takes a few seconds to reach every replica of the directory; 409: a retried
                    // request already created it. The next turn reads it.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, 2 * attempt)), ct);
                }
            }

            progress?.Report(new SetupProgress(SetupStep.Consent));
            try
            {
                var grants = (await g.GetAsync($"/v1.0/oauth2PermissionGrants?$filter={Uri.EscapeDataString($"clientId eq '{spId}' and consentType eq 'AllPrincipals'")}", ct))?["value"]?.AsArray();
                var grant = grants?.FirstOrDefault(x => x?["resourceId"]?.GetValue<string>() == graphSpId);
                if (grant == null)
                {
                    try
                    {
                        await g.SendAsync(HttpMethod.Post, "/v1.0/oauth2PermissionGrants", new JsonObject
                        {
                            ["clientId"] = spId, ["consentType"] = "AllPrincipals", ["resourceId"] = graphSpId, ["scope"] = IntunePermission
                        }, ct);
                    }
                    // A retried request that had already been applied ("Permission entry already exists"): the consent is there.
                    catch (GraphException ex) when (ex.Status == 409) { }
                }
                else if (!(grant["scope"]?.GetValue<string>() ?? "").Split(' ').Contains(IntunePermission))
                    await g.SendAsync(HttpMethod.Patch, $"/v1.0/oauth2PermissionGrants/{grant["id"]!.GetValue<string>()}", new JsonObject
                    {
                        ["scope"] = ((grant["scope"]?.GetValue<string>() ?? "") + " " + IntunePermission).Trim()
                    }, ct);
                result.ConsentGranted = true;
            }
            catch (GraphException ex) when (ex.Status is 401 or 403)
            {
                // The administrator may create apps but not grant consent for Microsoft Graph: a Global Administrator
                // (or Privileged Role Administrator) grants it with the link.
                result.ConsentGranted = false;
                Log.Warn(T("Connect_ConsentDenied", ex.Message));
            }
            if (roleId != null)
            {
                try
                {
                    // The application permission is granted by assigning the app role of Microsoft Graph to the
                    // enterprise application (what "Grant admin consent" does for an application permission).
                    var assigned = (await g.GetAsync($"/v1.0/servicePrincipals/{spId}/appRoleAssignments", ct))?["value"]?.AsArray();
                    if (assigned?.Any(a => a?["appRoleId"]?.GetValue<string>() == roleId && a?["resourceId"]?.GetValue<string>() == graphSpId) != true)
                    {
                        try
                        {
                            await g.SendAsync(HttpMethod.Post, $"/v1.0/servicePrincipals/{spId}/appRoleAssignments", new JsonObject
                            {
                                ["principalId"] = spId, ["resourceId"] = graphSpId, ["appRoleId"] = roleId
                            }, ct);
                        }
                        // A retried request that had already been applied ("Permission being assigned already exists").
                        catch (GraphException ex) when (ex.Status is 400 or 409 && ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) { }
                    }
                    result.RoleGranted = true;
                }
                catch (GraphException ex) when (ex.Status is 401 or 403)
                {
                    // An application permission of Microsoft Graph takes a Global Administrator or a Privileged Role
                    // Administrator: the consent link grants it.
                    result.RoleGranted = false;
                    Log.Warn(T("Connect_ConsentDenied", ex.Message));
                }
            }
            result.ConsentUrl = $"{EntraAuth.LoginUrl}/{tenant}/adminconsent?client_id={appId}";

            string label = result.Admin.Contains('@') ? result.Admin[(result.Admin.IndexOf('@') + 1)..] : tenant;
            result.TenantName = label;
            if (result.Certificate)
            {
                // A new certificate, added next to every key of the registration. It becomes the connection only once it
                // obtained a token (ActivateCertificateAsync): until then the connection in use keeps working.
                progress?.Report(new SetupProgress(SetupStep.Certificate));
                int months = AppCertificate.Months(ConfigStore.Current.CertificateMonths);
                var created = await Task.Run(() => AppCertificate.Create(label, months: months), ct);
                string thumbprint = created.Thumbprint;
                try
                {
                    using (created) await UpdateKeysAsync(g, objectId, created, "", ct);
                }
                catch (GraphException ex) when (ex.Status == 400 && Regex.IsMatch(ex.Message, "lifetime|polic", RegexOptions.IgnoreCase))
                {
                    // An app management policy of the tenant limits how long the credentials of applications may last.
                    DeleteLocalCertificate(thumbprint);
                    throw new AuthException(T("Cert_ErrLifetimePolicy", months, ex.Message), "certificate_lifetime");
                }
                catch
                {
                    DeleteLocalCertificate(thumbprint);
                    throw;
                }
                result.CertificateThumbprint = thumbprint;
                result.Pending = new PendingCertificate { TenantId = tenant, ClientId = appId, Thumbprint = thumbprint, TenantName = label };
                Log.Ok(T("Log_RegistrationReady", RegistrationName, appId, tenant));
                return result;
            }

            // The user connection takes over once the user signed in with it (BeginTrial / EndTrial).
            Log.Ok(T("Log_RegistrationReady", RegistrationName, appId, tenant));
            return result;
        }

        /// <summary>
        /// The permissions of the registration with what this application needs added: what an administrator added by
        /// hand is kept (the setup never removes a permission).
        /// </summary>
        private static JsonArray MergeAccess(JsonArray current, string scopeId, string roleId)
        {
            var result = new JsonArray();
            JsonObject graph = null;
            foreach (var r in current ?? new JsonArray())
            {
                if (r is not JsonObject o) continue;
                var copy = (JsonObject)o.DeepClone();
                if (graph == null && copy["resourceAppId"]?.GetValue<string>() == EntraAuth.GraphAppId) graph = copy;
                result.Add(copy);
            }
            if (graph == null)
            {
                graph = new JsonObject { ["resourceAppId"] = EntraAuth.GraphAppId };
                result.Add(graph);
            }
            if (graph["resourceAccess"] is not JsonArray access)
            {
                access = new JsonArray();
                graph["resourceAccess"] = access;
            }
            foreach (var (id, type) in new[] { (scopeId, "Scope"), (roleId, "Role") })
                if (id != null && !access.Any(a => a?["id"]?.GetValue<string>() == id && a?["type"]?.GetValue<string>() == type))
                    access.Add(new JsonObject { ["id"] = id, ["type"] = type });
            return result;
        }

        /// <summary>
        /// Adds the public part of <paramref name="add"/> to the keys of the registration, and removes the key of the
        /// previous certificate of this computer and expired keys added by this application. Every other key is sent back
        /// as it is (keyCredentials is replaced as a whole).
        /// </summary>
        private static async Task UpdateKeysAsync(Graph g, string objectId, X509Certificate2 add, string removeThumbprint, CancellationToken ct)
        {
            var current = (await g.GetAsync($"/v1.0/applications/{objectId}?$select=keyCredentials", ct))?["keyCredentials"]?.AsArray() ?? new JsonArray();
            var keys = new JsonArray();
            bool changed = false;
            foreach (var k in current)
            {
                if (k is not JsonObject key) continue;
                string data = key["key"]?.GetValue<string>();
                // A credential without its data cannot be sent back: nothing is changed rather than one being lost.
                if (string.IsNullOrEmpty(data)) throw new InvalidDataException(T("Connect_ErrKeys"));
                string thumbprint = "";
                try { thumbprint = Convert.ToHexString(SHA1.HashData(Convert.FromBase64String(data))); } catch (FormatException) { }
                bool ours = (key["displayName"]?.GetValue<string>() ?? "").StartsWith(RegistrationName, StringComparison.Ordinal);
                bool expired = DateTimeOffset.TryParse(key["endDateTime"]?.GetValue<string>(), out var end) && end < DateTimeOffset.UtcNow;
                if ((removeThumbprint.Length > 0 && thumbprint == removeThumbprint) || (ours && expired)) { changed = true; continue; }
                keys.Add(key.DeepClone());
            }
            if (add != null)
            {
                keys.Add(new JsonObject
                {
                    ["type"] = "AsymmetricX509Cert", ["usage"] = "Verify", ["key"] = AppCertificate.PublicKeyBase64(add),
                    ["displayName"] = $"{RegistrationName} - {Environment.MachineName}"
                });
                changed = true;
            }
            if (changed) await g.SendAsync(HttpMethod.Patch, $"/v1.0/applications/{objectId}", new JsonObject { ["keyCredentials"] = keys }, ct);
        }

        /// <summary>Reads one app of the tenant: proves the session and the Intune permission work.</summary>
        public static async Task<string> TestAsync(CancellationToken ct)
        {
            var t = await RenewAsync(ct) ?? throw new AuthException(T("Auth_ErrNotSignedIn"));
            var g = new Graph(t.AccessToken);
            var list = await AskIntuneAsync(() => g.GetAsync("/beta/deviceAppManagement/mobileApps?$top=1&$select=id", ct));
            // Intune answered: whatever was noted about this connection is past.
            ClearProblem();
            return T("Auth_TestOk", Account, list?["value"]?.AsArray().Count ?? 0);
        }

        /// <summary>
        /// Win32 apps of the tenant whose name starts with the name of the application ("7-Zip" finds "7-Zip 26.06" and
        /// "7-Zip 25.01"): the earlier versions this package can update, so a new version does not become a second app.
        /// </summary>
        public static async Task<List<(string Id, string Name, string Version)>> FindRelatedAppsAsync(string appName, CancellationToken ct)
        {
            var t = await RenewAsync(ct) ?? throw new AuthException(T("Auth_ErrNotSignedIn"));
            var g = new Graph(t.AccessToken);
            var result = new List<(string Id, string Name, string Version)>();
            string name = (appName ?? "").Trim();
            if (name.Length == 0) return result;
            // No $select: displayVersion is a property of Win32 apps only, and Intune refuses to select it on the list of
            // every app ("Could not find a property named 'displayVersion' on type 'microsoft.graph.mobileApp'").
            string path = "/beta/deviceAppManagement/mobileApps?$filter=" + Uri.EscapeDataString("isof('microsoft.graph.win32LobApp')") + "&$top=999";
            for (int page = 0; path != null && page < 20; page++)
            {
                string current = path;
                var data = await AskIntuneAsync(() => g.GetAsync(current, ct));
                foreach (var a in data?["value"]?.AsArray() ?? new JsonArray())
                {
                    string dn = a?["displayName"]?.GetValue<string>() ?? "";
                    if (IsRelatedName(dn, name))
                        result.Add((a?["id"]?.GetValue<string>() ?? "", dn, a?["displayVersion"]?.GetValue<string>() ?? ""));
                }
                string next = data?["@odata.nextLink"]?.GetValue<string>();
                path = next != null && next.StartsWith(EntraAuth.GraphUrl, StringComparison.OrdinalIgnoreCase) ? next.Substring(EntraAuth.GraphUrl.Length) : null;
            }
            // The app with exactly this name first (the one a new version updates), then the newest.
            return result
                .OrderByDescending(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(r => r.Version, Comparer<string>.Create(VersionUtil.Compare))
                .ToList();
        }

        /// <summary>
        /// An app of the tenant named like the application, as a whole word: "Git" finds "Git" and "Git 2.45", not "GitHub
        /// Desktop" (whose content the dialog would otherwise offer to replace).
        /// </summary>
        internal static bool IsRelatedName(string displayName, string appName)
        {
            string dn = (displayName ?? "").Trim(), name = (appName ?? "").Trim();
            if (name.Length == 0 || !dn.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return false;
            if (dn.Length == name.Length) return true;
            // "+" and "#" belong to names ("Notepad++" is not "Notepad", "C#" not "C").
            char next = dn[name.Length];
            return !char.IsLetterOrDigit(next) && next != '+' && next != '#';
        }

        // ------------------------------------------------------------------ DPAPI (current Windows user)

        private static byte[] Protect(byte[] data) => Dpapi(data, true);
        private static byte[] Unprotect(byte[] data) => Dpapi(data, false);

        private static byte[] Dpapi(byte[] data, bool protect)
        {
            var input = new NativeMethods.DATA_BLOB();
            var output = new NativeMethods.DATA_BLOB();
            var entropy = Encoding.UTF8.GetBytes("J365.IntunePSApp.IntuneSession");
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            var pent = System.Runtime.InteropServices.GCHandle.Alloc(entropy, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                input.cbData = data.Length; input.pbData = pin.AddrOfPinnedObject();
                var ent = new NativeMethods.DATA_BLOB { cbData = entropy.Length, pbData = pent.AddrOfPinnedObject() };
                bool ok = protect
                    ? NativeMethods.CryptProtectData(ref input, "J365 IntunePSApp", ref ent, IntPtr.Zero, IntPtr.Zero, 0x1, ref output)
                    : NativeMethods.CryptUnprotectData(ref input, IntPtr.Zero, ref ent, IntPtr.Zero, IntPtr.Zero, 0x1, ref output);
                if (!ok) throw new CryptographicException(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                var result = new byte[output.cbData];
                System.Runtime.InteropServices.Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                pin.Free(); pent.Free();
                if (output.pbData != IntPtr.Zero) NativeMethods.LocalFree(output.pbData);
            }
        }
    }

    public class GraphException : Exception
    {
        public int Status { get; }
        /// <summary>The "code" of the Graph error (Forbidden, InvalidAuthenticationToken...).</summary>
        public string Code { get; }
        public GraphException(int status, string message, string code = "") : base(message) { Status = status; Code = code ?? ""; }

        /// <summary>
        /// Intune wraps its own error in the message as JSON, sometimes twice ({"ErrorCode":"Forbidden","Message":"{\"_version\":3,
        /// \"Message\":\"An error has occurred - Operation ID ... - Activity ID: ... - Url: ...\"}"}): every level is opened, and
        /// the sentence and the activity id for support are kept, not the internal address nor an empty operation id.
        /// </summary>
        public static string CleanMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            string m = message.Trim();
            for (int depth = 0; depth < 4 && m.StartsWith("{", StringComparison.Ordinal); depth++)
            {
                string inner = null;
                try
                {
                    var node = JsonNode.Parse(m);
                    inner = (node?["Message"] ?? node?["message"])?.GetValue<string>();
                }
                catch
                {
                    // Not JSON (cut short): the sentence is read from the text.
                    var found = Regex.Match(m, "\"[Mm]essage\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)");
                    if (found.Success)
                    {
                        try { inner = Regex.Unescape(found.Groups[1].Value); }
                        catch { inner = found.Groups[1].Value; }
                    }
                }
                if (string.IsNullOrWhiteSpace(inner)) break;
                m = inner.Trim();
            }
            int url = m.IndexOf(" - Url:", StringComparison.Ordinal);
            if (url > 0) m = m.Substring(0, url);
            m = Regex.Replace(m, @"\s*-?\s*Operation ID \(for customer support\):\s*0{8}-0{4}-0{4}-0{4}-0{12}", "");
            return m.Trim();
        }

        /// <summary>The activity id Intune gives for support ("Activity ID: ..."), or an empty string.</summary>
        public static string ActivityId(string message)
            => Regex.Match(message ?? "", @"Activity ID:\s*([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})").Groups[1].Value;
    }

    /// <summary>Minimal Microsoft Graph client: JSON in and out, retries on throttling and transient errors.</summary>
    internal sealed class Graph
    {
        private readonly string _token;
        public Graph(string accessToken) { _token = accessToken; }

        public Task<JsonNode> GetAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Get, path, null, ct);

        /// <summary>
        /// Sends a request; throttling (429) is always retried. <paramref name="repeatable"/> false is for a request that
        /// creates something (POST of an app or of a registration): after a timeout or a 5xx answer the object may already
        /// exist, so it is not sent again (that made duplicates) and the caller looks for it instead.
        /// </summary>
        public async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode body, CancellationToken ct, bool repeatable = true)
        {
            for (int attempt = 1; ; attempt++)
            {
                using var req = new HttpRequestMessage(method, EntraAuth.GraphUrl + path);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
                if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(100));
                HttpResponseMessage resp;
                try { resp = await Http.Client.SendAsync(req, cts.Token); }
                catch (Exception ex) when (repeatable && attempt < 4 && !ct.IsCancellationRequested && (ex is HttpRequestException || ex is OperationCanceledException))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                    continue;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new GraphException(504, $"{method} {path.Split('?')[0]}: timeout", "timeout");
                }
                using var response = resp;
                string text = await resp.Content.ReadAsStringAsync(ct);
                int status = (int)resp.StatusCode;
                if ((status == 429 || (repeatable && status >= 500)) && attempt < 5)
                {
                    var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
                    await Task.Delay(wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait, ct);
                    continue;
                }
                if (!resp.IsSuccessStatusCode)
                {
                    string message = $"HTTP {status}", code = "";
                    try
                    {
                        var error = JsonNode.Parse(text)?["error"];
                        code = error?["code"]?.GetValue<string>() ?? "";
                        message = GraphException.CleanMessage(error?["message"]?.GetValue<string>()) ?? message;
                    }
                    catch { }
                    throw new GraphException(status, $"{method} {path.Split('?')[0]}: {message}", code);
                }
                if (string.IsNullOrWhiteSpace(text)) return null;
                try { return JsonNode.Parse(text); } catch { return null; }
            }
        }
    }
}
