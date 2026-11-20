using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using J365.IntunePSApp.Core;
using Xunit;

namespace J365.IntunePSApp.Tests
{
    [CollectionDefinition("Environment", DisableParallelization = true)]
    public class EnvironmentCollection { }

    /// <summary>
    /// Local stand-in for Entra ID (authorize/token, with PKCE) and the Microsoft Graph calls of the automatic setup.
    /// It records what the application sends.
    /// </summary>
    internal sealed class EntraMock : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private string _challenge = "";
        private int _spPosts;
        public string Base { get; }
        public bool DenyConsent { get; set; }
        public bool ExistingRegistration { get; set; }
        public List<(string Method, string Path, string Body)> Calls { get; } = new List<(string, string, string)>();
        public const string NewAppId = "aaaaaaaa-1111-2222-3333-444444444444";
        public const string TenantGuid = "bbbbbbbb-1111-2222-3333-444444444444";

        public EntraMock()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Base = $"http://localhost:{port}";
            _listener.Prefixes.Add(Base + "/");
            _listener.Start();
            Task.Run(LoopAsync);
        }

        private static string Jwt(object claims)
        {
            string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return B64("{\"alg\":\"none\"}") + "." + B64(System.Text.Json.JsonSerializer.Serialize(claims)) + ".sig";
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { return; }
                try { Handle(ctx); } catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
            }
        }

        private void Reply(HttpListenerContext ctx, int status, object body)
        {
            ctx.Response.StatusCode = status;
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body is string s ? s : System.Text.Json.JsonSerializer.Serialize(body));
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            ctx.Response.Close();
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            string path = Uri.UnescapeDataString(req.Url.AbsolutePath);
            string query = Uri.UnescapeDataString(req.Url.Query);
            string body = new StreamReader(req.InputStream, Encoding.UTF8).ReadToEnd();
            lock (Calls) Calls.Add((req.HttpMethod, path + query, body));

            if (path.EndsWith("/oauth2/v2.0/authorize"))
            {
                _challenge = req.QueryString["code_challenge"];
                ctx.Response.StatusCode = 302;
                ctx.Response.RedirectLocation = req.QueryString["redirect_uri"] + "?code=mockcode&state=" + Uri.EscapeDataString(req.QueryString["state"]);
                ctx.Response.Close();
                return;
            }
            if (path.EndsWith("/oauth2/v2.0/token"))
            {
                var form = body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p.Length > 1 ? p[1] : ""));
                if (form["grant_type"] == "authorization_code")
                {
                    string expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    if (form["code"] != "mockcode" || expected != _challenge) { Reply(ctx, 400, new { error = "invalid_grant", error_description = "AADSTS50148: PKCE mismatch" }); return; }
                }
                Reply(ctx, 200, new
                {
                    token_type = "Bearer", access_token = "mock-access", refresh_token = "mock-refresh-" + form["grant_type"], expires_in = 3600,
                    id_token = Jwt(new { tid = TenantGuid, preferred_username = "admin@contoso.onmicrosoft.com", name = "Contoso Admin" })
                });
                return;
            }
            if (req.Headers["Authorization"] != "Bearer mock-access") { Reply(ctx, 401, new { error = new { code = "InvalidAuthenticationToken", message = "no token" } }); return; }

            if (path.StartsWith("/v1.0/servicePrincipals(appId="))
                Reply(ctx, 200, new { id = "graph-sp", oauth2PermissionScopes = new[] { new { id = "scope-user-read", value = "User.Read" }, new { id = "scope-intune-apps", value = "DeviceManagementApps.ReadWrite.All" } } });
            else if (req.HttpMethod == "GET" && path == "/v1.0/applications")
                Reply(ctx, 200, ExistingRegistration ? (object)new { value = new[] { new { id = "app-obj-0", appId = NewAppId, displayName = "J365 IntunePSApp", tags = new[] { "J365IntunePSApp" } } } } : new { value = Array.Empty<object>() });
            else if (req.HttpMethod == "POST" && path == "/v1.0/applications") Reply(ctx, 201, new { id = "app-obj-1", appId = NewAppId });
            else if (req.HttpMethod == "PATCH" && path.StartsWith("/v1.0/applications/")) Reply(ctx, 204, null);
            else if (req.HttpMethod == "GET" && path == "/v1.0/servicePrincipals") Reply(ctx, 200, new { value = Array.Empty<object>() });
            else if (req.HttpMethod == "POST" && path == "/v1.0/servicePrincipals")
            {
                // The first try meets the replication delay of a new application, like the real directory.
                if (++_spPosts == 1) Reply(ctx, 400, new { error = new { code = "Request_BadRequest", message = "The appId of the service principal does not reference a valid application object." } });
                else Reply(ctx, 201, new { id = "sp-1" });
            }
            else if (req.HttpMethod == "GET" && path == "/v1.0/oauth2PermissionGrants") Reply(ctx, 200, new { value = Array.Empty<object>() });
            else if (req.HttpMethod == "POST" && path == "/v1.0/oauth2PermissionGrants")
            {
                if (DenyConsent) Reply(ctx, 403, new { error = new { code = "Authorization_RequestDenied", message = "Insufficient privileges to complete the operation." } });
                else Reply(ctx, 201, new { id = "grant-1" });
            }
            else if (req.HttpMethod == "GET" && path == "/beta/deviceAppManagement/mobileApps")
            {
                // The Win32 apps of the tenant: two versions of 7-Zip and another application.
                Reply(ctx, 200, new { value = new[]
                {
                    new { id = "app-2", displayName = "7-Zip 25.01", displayVersion = "25.01" },
                    new { id = "app-1", displayName = "7-Zip 26.06", displayVersion = "26.06" },
                    new { id = "app-3", displayName = "Notepad++ 8.6", displayVersion = "8.6" }
                } });
            }
            else Reply(ctx, 404, new { error = new { code = "NotFound", message = path } });
        }

        public (string Method, string Path, string Body) Last(string method, string path)
        {
            lock (Calls) return Calls.LastOrDefault(c => c.Method == method && c.Path.StartsWith(path));
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }

    [Collection("Environment")]
    public class IntuneConnectionTests
    {
        private static readonly HttpClient Browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });

        /// <summary>Runs <paramref name="body"/> against the mock: sign-in and Graph go there, no browser opens.</summary>
        private static async Task WithMockAsync(Func<EntraMock, Task> body, Action<EntraMock> configure = null)
        {
            using var mock = new EntraMock();
            configure?.Invoke(mock);
            var cfg = ConfigStore.Current;
            string tenant = cfg.IntuneTenantId, client = cfg.IntuneClientId;
            var open = EntraAuth.OpenBrowser;
            Environment.SetEnvironmentVariable("J365_LOGIN_URL", mock.Base + "/login");
            Environment.SetEnvironmentVariable("J365_GRAPH_URL", mock.Base);
            // The "browser" follows the redirect of the sign-in page to the loopback address of the application.
            EntraAuth.OpenBrowser = url => { _ = Browser.GetAsync(url); return Task.CompletedTask; };
            try { await body(mock); }
            finally
            {
                EntraAuth.OpenBrowser = open;
                Environment.SetEnvironmentVariable("J365_LOGIN_URL", null);
                Environment.SetEnvironmentVariable("J365_GRAPH_URL", null);
                IntuneConnection.SignOut();
                cfg.IntuneTenantId = tenant;
                cfg.IntuneClientId = client;
            }
        }

        [Fact]
        public Task Automatic_setup_creates_the_registration_grants_consent_and_signs_in() => WithMockAsync(async mock =>
        {
            var r = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None);
            Assert.Equal(EntraMock.NewAppId, r.ClientId);
            Assert.Equal(EntraMock.TenantGuid, r.TenantId);
            Assert.Equal("admin@contoso.onmicrosoft.com", r.Admin);
            Assert.True(r.ConsentGranted);
            Assert.False(r.Reused);
            Assert.Equal(EntraMock.TenantGuid, ConfigStore.Current.IntuneTenantId);
            Assert.Equal(EntraMock.NewAppId, ConfigStore.Current.IntuneClientId);

            // The admin sign-in used Microsoft Graph Command Line Tools with PKCE, for the two setup permissions only.
            var authorize = mock.Last("GET", "/login/organizations/oauth2/v2.0/authorize");
            Assert.Contains("client_id=" + EntraAuth.GraphCliClientId, authorize.Path);
            Assert.Contains("Application.ReadWrite.All", authorize.Path);
            Assert.Contains("DelegatedPermissionGrant.ReadWrite.All", authorize.Path);
            Assert.Contains("code_challenge_method=S256", authorize.Path);

            // The registration: one tenant, public client on the loopback address, the Intune permission, no secret.
            var app = JsonNode.Parse(mock.Last("POST", "/v1.0/applications").Body);
            Assert.Equal("J365 IntunePSApp", (string)app["displayName"]);
            Assert.Equal("AzureADMyOrg", (string)app["signInAudience"]);
            Assert.True((bool)app["isFallbackPublicClient"]);
            Assert.Equal("http://localhost", (string)app["publicClient"]["redirectUris"][0]);
            Assert.Equal("scope-intune-apps", (string)app["requiredResourceAccess"][0]["resourceAccess"][0]["id"]);
            Assert.Equal("Scope", (string)app["requiredResourceAccess"][0]["resourceAccess"][0]["type"]);
            Assert.Null(app["passwordCredentials"]);

            // The enterprise application was created after the replication delay, then the consent for the organisation.
            Assert.Equal(2, mock.Calls.Count(c => c.Method == "POST" && c.Path == "/v1.0/servicePrincipals"));
            var grant = JsonNode.Parse(mock.Last("POST", "/v1.0/oauth2PermissionGrants").Body);
            Assert.Equal("sp-1", (string)grant["clientId"]);
            Assert.Equal("graph-sp", (string)grant["resourceId"]);
            Assert.Equal("AllPrincipals", (string)grant["consentType"]);
            Assert.Equal("DeviceManagementApps.ReadWrite.All", (string)grant["scope"]);

            // Then the user signs in with the new registration; the session is kept encrypted.
            var t = await IntuneConnection.SignInAsync(CancellationToken.None);
            Assert.True(IntuneConnection.IsSignedIn);
            Assert.Equal("admin@contoso.onmicrosoft.com", IntuneConnection.Account);
            Assert.Contains("client_id=" + EntraMock.NewAppId, mock.Last("GET", $"/login/{EntraMock.TenantGuid}/oauth2/v2.0/authorize").Path);
            string file = Path.Combine(AppPaths.Data, "intune-session.dat");
            Assert.True(File.Exists(file));
            Assert.DoesNotContain("mock-refresh", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            Assert.DoesNotContain("admin@contoso", Encoding.UTF8.GetString(File.ReadAllBytes(file)));

            Assert.Contains("admin@contoso.onmicrosoft.com", await IntuneConnection.TestAsync(CancellationToken.None));
            // Earlier versions of the same application, newest first (a new version updates one of them).
            var apps = await IntuneConnection.FindRelatedAppsAsync("7-Zip", CancellationToken.None);
            Assert.Equal(new[] { "app-1", "app-2" }, apps.Select(a => a.Id).ToArray());
            Assert.Equal("26.06", apps[0].Version);

            IntuneConnection.SignOut();
            Assert.False(IntuneConnection.IsSignedIn);
            Assert.False(File.Exists(file));
        });

        [Fact]
        public Task Without_the_right_to_consent_the_registration_is_kept_and_a_consent_link_is_given() => WithMockAsync(async mock =>
        {
            var r = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None);
            Assert.False(r.ConsentGranted);
            Assert.EndsWith($"/{EntraMock.TenantGuid}/adminconsent?client_id={EntraMock.NewAppId}", r.ConsentUrl);
            Assert.Equal(EntraMock.NewAppId, ConfigStore.Current.IntuneClientId);
        }, m => m.DenyConsent = true);

        [Fact]
        public Task A_registration_from_an_earlier_setup_is_reused_and_repaired() => WithMockAsync(async mock =>
        {
            var r = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None);
            Assert.True(r.Reused);
            Assert.Equal(EntraMock.NewAppId, r.ClientId);
            Assert.DoesNotContain(mock.Calls, c => c.Method == "POST" && c.Path == "/v1.0/applications");
            var patch = JsonNode.Parse(mock.Last("PATCH", "/v1.0/applications/app-obj-0").Body);
            Assert.Equal("http://localhost", (string)patch["publicClient"]["redirectUris"][0]);
        }, m => m.ExistingRegistration = true);

        [Fact]
        public Task A_registration_that_is_tried_and_fails_leaves_the_working_connection_as_it_was() => WithMockAsync(async mock =>
        {
            // A working connection: the registration of the setup, signed in.
            await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None);
            await IntuneConnection.SignInAsync(CancellationToken.None);
            Assert.True(IntuneConnection.IsSignedIn);
            string tenant = ConfigStore.Current.IntuneTenantId, client = ConfigStore.Current.IntuneClientId;
            string file = Path.Combine(AppPaths.Data, "intune-session.dat");

            // Another registration is tried (a pasted code) and its sign-in does not complete.
            var previous = IntuneConnection.BeginTrial("fabrikam.onmicrosoft.com", "11111111-2222-3333-4444-555555555555");
            Assert.Equal("11111111-2222-3333-4444-555555555555", IntuneConnection.ClientId);
            Assert.False(IntuneConnection.IsSignedIn);
            IntuneConnection.EndTrial(previous, keep: false);

            // Everything as it was: registration, session file and account.
            Assert.Equal(tenant, ConfigStore.Current.IntuneTenantId);
            Assert.Equal(client, ConfigStore.Current.IntuneClientId);
            Assert.True(File.Exists(file));
            Assert.True(IntuneConnection.IsSignedIn);
            Assert.Equal("admin@contoso.onmicrosoft.com", IntuneConnection.Account);
        });

        [Fact]
        public async Task A_sign_in_server_that_does_not_answer_is_a_network_failure()
        {
            // A port where nothing listens: the request fails before any answer.
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            Environment.SetEnvironmentVariable("J365_LOGIN_URL", $"http://127.0.0.1:{port}");
            try
            {
                var ex = await Assert.ThrowsAsync<AuthException>(() => EntraAuth.RefreshAsync("contoso.onmicrosoft.com", "11111111-2222-3333-4444-555555555555", "x", "openid", CancellationToken.None));
                Assert.Equal("network", ex.Code);
                Assert.True(EntraAuth.IsNetworkFailure(ex.Code));
            }
            finally { Environment.SetEnvironmentVariable("J365_LOGIN_URL", null); }
        }

        [Fact]
        public void Sign_in_errors_are_explained_in_plain_words()
        {
            var e = EntraAuth.Explain("invalid_client", "AADSTS7000218: The request body must contain the following parameter: 'client_assertion' or 'client_secret'.");
            Assert.Equal("AADSTS7000218", e.Code);
            Assert.Contains(Localization.Loc.T("Auth_Err7000218"), e.Message);
            Assert.Contains("AADSTS7000218", e.Message);
            Assert.Equal("AADSTS53003", EntraAuth.Explain("access_denied", "AADSTS53003: Access has been blocked by Conditional Access policies.").Code);
            Assert.Equal("AADSTS700016", EntraAuth.Explain("unauthorized_client", "AADSTS700016: Application with identifier 'x' was not found").Code);
            Assert.Contains("weird", EntraAuth.Explain("server_error", "weird failure").Message);
        }

        [Fact]
        public void Claims_are_read_from_the_id_token()
        {
            string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var claims = TokenSet.ReadClaims(B64("{}") + "." + B64("{\"tid\":\"t-1\",\"preferred_username\":\"ana@contoso.com\",\"exp\":123}") + ".x");
            Assert.Equal("t-1", claims["tid"]);
            Assert.Equal("ana@contoso.com", new TokenSet { Claims = claims }.Account);
            Assert.Empty(TokenSet.ReadClaims("not a token"));
        }
    }
}
