using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
        /// <summary>Intune answers every read with its 401 "Forbidden" (a user without an active Intune role, for example).</summary>
        public bool IntuneRefuses { get; set; }
        /// <summary>The keyCredentials of the application: a key of another tool, then whatever the setup sends.</summary>
        public List<JsonNode> KeyCredentials { get; } = new List<JsonNode>();
        public List<JsonNode> RoleAssignments { get; } = new List<JsonNode>();
        /// <summary>Registrations deleted in Entra ID: the device code and token endpoints answer AADSTS700016 for them.</summary>
        public HashSet<string> DeletedClients { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int ValidAssertions { get; private set; }
        public string LastAssertionAlg { get; private set; } = "";
        /// <summary>The token given to the application (certificate): a JWT with the application permission.</summary>
        public string AppToken { get; } = Jwt(new { tid = TenantGuid, idtyp = "app", roles = new[] { "DeviceManagementApps.ReadWrite.All" } });

        /// <summary>The 401 of Intune for a caller it refuses: its own error as JSON inside the message of Graph.</summary>
        public const string IntuneForbidden = "{\"error\":{\"code\":\"Forbidden\",\"message\":\"{\\r\\n  \\\"_version\\\": 3,\\r\\n  \\\"Message\\\": \\\"An error has occurred - Operation ID (for customer support): 00000000-0000-0000-0000-000000000000 - Activity ID: 184af700-7dff-44d6-ba38-5af761eb24d3 - Url: https://proxy.msua08.manage.microsoft.com/AppLifecycle_2611/StatelessAppMetadataFEService/deviceAppManagement/mobileApps?api-version=5026-08-19\\\"\\r\\n}\"}}";
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
            // A certificate that another tool added to the registration: the setup must send it back unchanged.
            using (var rsa = RSA.Create(2048))
            {
                using var other = new CertificateRequest("CN=Other tool", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
                KeyCredentials.Add(new JsonObject
                {
                    ["keyId"] = "11111111-0000-0000-0000-000000000000", ["displayName"] = "Other tool", ["type"] = "AsymmetricX509Cert", ["usage"] = "Verify",
                    ["key"] = Convert.ToBase64String(other.RawData), ["endDateTime"] = DateTimeOffset.UtcNow.AddYears(1).ToString("o")
                });
            }
            Task.Run(LoopAsync);
        }

        private static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        }

        private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>
        /// Checks a client assertion as Entra ID does: a key registered on the application (x5t#S256), the signature
        /// (PS256 or RS256), the audience (the token endpoint), issuer and subject (the client) and the time window.
        /// Null when it is valid, otherwise the AADSTS error.
        /// </summary>
        private string CheckAssertion(string audience, Dictionary<string, string> form)
        {
            if (form.GetValueOrDefault("client_assertion_type") != "urn:ietf:params:oauth:client-assertion-type:jwt-bearer") return "AADSTS7000216: client_assertion_type missing";
            var parts = form.GetValueOrDefault("client_assertion", "").Split('.');
            if (parts.Length != 3) return "AADSTS50027: invalid JWT";
            var header = JsonNode.Parse(FromBase64Url(parts[0]));
            var payload = JsonNode.Parse(FromBase64Url(parts[1]));
            string alg = (string)header["alg"], x5t = (string)header["x5t#S256"];
            string key;
            lock (KeyCredentials) key = KeyCredentials.Select(k => (string)k["key"]).FirstOrDefault(k => Base64Url(SHA256.HashData(Convert.FromBase64String(k))) == x5t);
            if (key == null) return "AADSTS700027: The certificate with identifier used to sign the client assertion is not registered on application.";
            using var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(key));
            using var rsa = cert.GetRSAPublicKey();
            var padding = alg == "PS256" ? RSASignaturePadding.Pss : alg == "RS256" ? RSASignaturePadding.Pkcs1 : null;
            if (padding == null || !rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromBase64Url(parts[2]), HashAlgorithmName.SHA256, padding))
                return "AADSTS700027: Client assertion failed signature validation.";
            if ((string)payload["aud"] != audience) return "AADSTS700021: wrong audience " + (string)payload["aud"];
            if ((string)payload["iss"] != form["client_id"] || (string)payload["sub"] != form["client_id"]) return "AADSTS700023: wrong issuer or subject";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if ((long)payload["exp"] < now || (long)payload["nbf"] > now + 5 || (long)payload["exp"] - now > 3600) return "AADSTS700024: Client assertion is not within its valid time range.";
            if (form.GetValueOrDefault("scope") != Base + "/.default") return "AADSTS70011: wrong scope " + form.GetValueOrDefault("scope");
            ValidAssertions++;
            LastAssertionAlg = alg;
            return null;
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
            if (path.EndsWith("/oauth2/v2.0/devicecode") || path.EndsWith("/oauth2/v2.0/token"))
            {
                var form = body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => WebUtility.UrlDecode(p[0]), p => WebUtility.UrlDecode(p.Length > 1 ? p[1] : ""));
                if (DeletedClients.Contains(form.GetValueOrDefault("client_id", "")))
                {
                    Reply(ctx, 400, new { error = "unauthorized_client", error_description = $"AADSTS700016: Application with identifier '{form["client_id"]}' was not found in the directory 'Contoso'. This can happen if the application has not been installed by the administrator of the tenant." });
                    return;
                }
                if (path.EndsWith("/devicecode"))
                {
                    Reply(ctx, 200, new { device_code = "mock-device-code", user_code = "ABCD-EFGH", verification_uri = "https://microsoft.com/devicelogin", expires_in = 900, interval = 5, message = "mock" });
                    return;
                }
                if (form["grant_type"] == "client_credentials")
                {
                    string error = CheckAssertion(Base + path, form);
                    if (error != null) Reply(ctx, 401, new { error = "invalid_client", error_description = error });
                    else Reply(ctx, 200, new { token_type = "Bearer", access_token = AppToken, expires_in = 3599 });
                    return;
                }
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
            string auth = req.Headers["Authorization"];
            if (auth != "Bearer mock-access" && auth != "Bearer " + AppToken) { Reply(ctx, 401, new { error = new { code = "InvalidAuthenticationToken", message = "no token" } }); return; }

            if (path.StartsWith("/v1.0/servicePrincipals(appId="))
                Reply(ctx, 200, new
                {
                    id = "graph-sp",
                    oauth2PermissionScopes = new[] { new { id = "scope-user-read", value = "User.Read" }, new { id = "scope-intune-apps", value = "DeviceManagementApps.ReadWrite.All" } },
                    appRoles = new[] { new { id = "role-user-read", value = "User.Read.All" }, new { id = "role-intune-apps", value = "DeviceManagementApps.ReadWrite.All" } }
                });
            else if (req.HttpMethod == "GET" && path.StartsWith("/v1.0/applications/"))
            {
                lock (KeyCredentials) Reply(ctx, 200, new JsonObject { ["keyCredentials"] = new JsonArray(KeyCredentials.Select(k => k.DeepClone()).ToArray()) });
            }
            else if (path.StartsWith("/v1.0/servicePrincipals/") && path.EndsWith("/appRoleAssignments"))
            {
                if (req.HttpMethod == "GET") lock (RoleAssignments) Reply(ctx, 200, new JsonObject { ["value"] = new JsonArray(RoleAssignments.Select(a => a.DeepClone()).ToArray()) });
                else if (DenyConsent) Reply(ctx, 403, new { error = new { code = "Authorization_RequestDenied", message = "Insufficient privileges to complete the operation." } });
                else { lock (RoleAssignments) RoleAssignments.Add(JsonNode.Parse(body)); Reply(ctx, 201, new { id = "assignment-1" }); }
            }
            else if (req.HttpMethod == "GET" && path == "/v1.0/applications")
                Reply(ctx, 200, ExistingRegistration ? (object)new { value = new[] { new { id = "app-obj-0", appId = NewAppId, displayName = "J365 IntunePSApp", tags = new[] { "J365IntunePSApp" } } } } : new { value = Array.Empty<object>() });
            else if (req.HttpMethod == "POST" && path == "/v1.0/applications") Reply(ctx, 201, new { id = "app-obj-1", appId = NewAppId });
            else if (req.HttpMethod == "PATCH" && path.StartsWith("/v1.0/applications/"))
            {
                // keyCredentials is replaced as a whole, as in Microsoft Graph.
                if (JsonNode.Parse(body)?["keyCredentials"] is JsonArray keys)
                    lock (KeyCredentials) { KeyCredentials.Clear(); KeyCredentials.AddRange(keys.Select(k => k.DeepClone())); }
                Reply(ctx, 204, null);
            }
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
                if (IntuneRefuses) { Reply(ctx, 401, IntuneForbidden); return; }
                // As in Intune: a property of Win32 apps cannot be selected on the list of every app.
                if (query.Contains("$select=") && query.Contains("displayVersion"))
                {
                    Reply(ctx, 400, new { error = new { code = "BadRequest", message = "Parsing OData Select and Expand failed: Could not find a property named 'displayVersion' on type 'microsoft.graph.mobileApp'." } });
                    return;
                }
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

        /// <summary>The automatic setup with an account, as the connection window runs it: the registration, then a sign-in that is kept.</summary>
        private static async Task<RegistrationResult> ConnectWithAccountAsync()
        {
            var r = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None);
            var trial = IntuneConnection.BeginTrial(r.TenantId, r.ClientId, r.TenantName);
            await IntuneConnection.SignInAsync(CancellationToken.None);
            IntuneConnection.EndTrial(trial, keep: true);
            return r;
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
            Assert.Equal("contoso.onmicrosoft.com", r.TenantName);
            // Nothing in Settings changes until a sign-in with the new registration worked.
            Assert.NotEqual(EntraMock.NewAppId, ConfigStore.Current.IntuneClientId);

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

            // Then the user signs in with the new registration (a trial, as the connection window does): kept, it is the
            // connection of this computer, and the session is kept encrypted.
            var trial = IntuneConnection.BeginTrial(r.TenantId, r.ClientId, r.TenantName);
            var t = await IntuneConnection.SignInAsync(CancellationToken.None);
            IntuneConnection.EndTrial(trial, keep: true);
            Assert.Equal(EntraMock.TenantGuid, ConfigStore.Current.IntuneTenantId);
            Assert.Equal(EntraMock.NewAppId, ConfigStore.Current.IntuneClientId);
            Assert.Equal("contoso.onmicrosoft.com", IntuneConnection.TenantLabel);
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
            Assert.Equal(EntraMock.NewAppId, r.ClientId);
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
            await ConnectWithAccountAsync();
            Assert.True(IntuneConnection.IsSignedIn);
            string tenant = ConfigStore.Current.IntuneTenantId, client = ConfigStore.Current.IntuneClientId;
            string file = Path.Combine(AppPaths.Data, "intune-session.dat");

            // Another registration is tried (a pasted code); even a sign-in during the trial is undone when it is not kept.
            byte[] session = File.ReadAllBytes(file);
            var previous = IntuneConnection.BeginTrial("fabrikam.onmicrosoft.com", "11111111-2222-3333-4444-555555555555");
            Assert.Equal("11111111-2222-3333-4444-555555555555", IntuneConnection.ClientId);
            Assert.False(IntuneConnection.IsSignedIn);
            await IntuneConnection.SignInAsync(CancellationToken.None);
            Assert.True(IntuneConnection.IsSignedIn);
            IntuneConnection.EndTrial(previous, keep: false);
            Assert.Equal(session, File.ReadAllBytes(file));

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
        public Task The_certificate_setup_grants_the_application_permission_and_connects_without_a_user() => WithMockAsync(async mock =>
        {
            AppCertificate.UseTpm = false;
            var created = new List<string>();
            try
            {
                var r = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None, certificate: true);
                string thumbprint = r.CertificateThumbprint;
                created.Add(thumbprint);
                Assert.True(r.ConsentGranted);
                Assert.True(r.RoleGranted);
                Assert.Equal(40, thumbprint.Length);
                // Created and registered, but not the connection until it obtained a token.
                Assert.NotNull(r.Pending);
                Assert.False(IntuneConnection.UsesCertificate);

                // The admin sign-in asked to grant the application permission.
                Assert.Contains("AppRoleAssignment.ReadWrite.All", mock.Last("GET", "/login/organizations/oauth2/v2.0/authorize").Path);
                // The registration lists the delegated and the application permission.
                var access = JsonNode.Parse(mock.Last("POST", "/v1.0/applications").Body)["requiredResourceAccess"][0]["resourceAccess"].AsArray();
                Assert.Contains(access, a => (string)a["id"] == "scope-intune-apps" && (string)a["type"] == "Scope");
                Assert.Contains(access, a => (string)a["id"] == "role-intune-apps" && (string)a["type"] == "Role");
                // The app role of Microsoft Graph went to the enterprise application (admin consent of an application permission).
                var role = JsonNode.Parse(mock.Last("POST", "/v1.0/servicePrincipals/sp-1/appRoleAssignments").Body);
                Assert.Equal("role-intune-apps", (string)role["appRoleId"]);
                Assert.Equal("graph-sp", (string)role["resourceId"]);
                Assert.Equal("sp-1", (string)role["principalId"]);

                // Only the public part went to Entra ID; the key of another tool was sent back unchanged.
                var keys = JsonNode.Parse(mock.Last("PATCH", "/v1.0/applications/app-obj-1").Body)["keyCredentials"].AsArray();
                Assert.Equal(2, keys.Count);
                Assert.Contains(keys, k => (string)k["displayName"] == "Other tool");
                using (var cert = AppCertificate.Find(thumbprint))
                {
                    Assert.NotNull(cert);
                    Assert.Contains(keys, k => (string)k["key"] == Convert.ToBase64String(cert.RawData) && (string)k["type"] == "AsymmetricX509Cert" && (string)k["usage"] == "Verify");
                    // The key cannot leave this computer.
                    Assert.ThrowsAny<CryptographicException>(() => cert.Export(X509ContentType.Pfx));
                }

                // The first token (an assertion signed with the key, PS256, checked by the mock) makes it the connection.
                await IntuneConnection.ActivateCertificateAsync(r.Pending, CancellationToken.None);
                Assert.True(IntuneConnection.UsesCertificate);
                Assert.Equal(thumbprint, IntuneConnection.CertificateThumbprint);
                Assert.Equal("contoso.onmicrosoft.com", IntuneConnection.TenantLabel);
                Assert.True(IntuneConnection.IsSignedIn);
                Assert.Contains("contoso.onmicrosoft.com", IntuneConnection.Account);
                Assert.Contains("contoso.onmicrosoft.com", await IntuneConnection.TestAsync(CancellationToken.None));
                Assert.Equal(1, mock.ValidAssertions);
                Assert.Equal("PS256", mock.LastAssertionAlg);
                Assert.True(IntuneConnection.LastToken.App);
                var apps = await IntuneConnection.FindRelatedAppsAsync("7-Zip", CancellationToken.None);
                Assert.Equal(new[] { "app-1", "app-2" }, apps.Select(a => a.Id).ToArray());
                Assert.Equal(1, mock.ValidAssertions);   // the token is reused while it is valid

                // A renewal: the new certificate is added next to the one in use, which keeps working until the new one
                // obtained a token; only then is the previous one removed from this computer.
                var renewal = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None, certificate: true);
                created.Add(renewal.CertificateThumbprint);
                Assert.Equal(thumbprint, IntuneConnection.CertificateThumbprint);
                Assert.True(IntuneConnection.IsSignedIn);
                Assert.Equal(3, mock.KeyCredentials.Count);
                await IntuneConnection.ActivateCertificateAsync(renewal.Pending, CancellationToken.None);
                Assert.Null(AppCertificate.Find(thumbprint));
                thumbprint = renewal.CertificateThumbprint;
                Assert.Equal(thumbprint, IntuneConnection.CertificateThumbprint);

                // A renewal that is abandoned (cancelled, failed) leaves the connection as it was.
                var abandoned = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None, certificate: true);
                created.Add(abandoned.CertificateThumbprint);
                IntuneConnection.DiscardCertificate(abandoned.Pending);
                Assert.Null(AppCertificate.Find(abandoned.CertificateThumbprint));
                Assert.Equal(thumbprint, IntuneConnection.CertificateThumbprint);
                Assert.True(IntuneConnection.IsSignedIn);

                // A connection with an account replaces the certificate only once it works: a trial that is not kept
                // changes nothing...
                var trial = IntuneConnection.BeginTrial(EntraMock.TenantGuid, EntraMock.NewAppId, "contoso.onmicrosoft.com");
                Assert.False(IntuneConnection.UsesCertificate);
                IntuneConnection.EndTrial(trial, keep: false);
                Assert.True(IntuneConnection.UsesCertificate);
                Assert.True(AppCertificate.Exists(thumbprint));
                Assert.True(IntuneConnection.IsSignedIn);
                // ...a kept one removes the certificate of this computer (one connection at a time).
                trial = IntuneConnection.BeginTrial(EntraMock.TenantGuid, EntraMock.NewAppId, "contoso.onmicrosoft.com");
                await IntuneConnection.SignInAsync(CancellationToken.None);
                IntuneConnection.EndTrial(trial, keep: true);
                Assert.False(IntuneConnection.UsesCertificate);
                Assert.Null(AppCertificate.Find(thumbprint));
                Assert.True(IntuneConnection.IsSignedIn);

                // Disconnect clears everything saved here; the next connection starts from the beginning.
                IntuneConnection.Disconnect();
                Assert.False(IntuneConnection.IsConfigured);
                Assert.False(IntuneConnection.IsSignedIn);
                Assert.False(File.Exists(Path.Combine(AppPaths.Data, "intune-session.dat")));
            }
            finally
            {
                foreach (var t in created) AppCertificate.Delete(t);
                var cfg = ConfigStore.Current;
                cfg.IntuneAuthMode = IntuneConnection.DelegatedMode;
                cfg.IntuneCertificateThumbprint = "";
                cfg.IntuneTenantName = "";
                AppCertificate.UseTpm = true;
            }
        });

        [Fact]
        public Task Without_the_right_to_grant_it_the_application_permission_is_reported_with_the_consent_link() => WithMockAsync(async mock =>
        {
            AppCertificate.UseTpm = false;
            RegistrationResult r = null;
            try
            {
                r = await IntuneConnection.CreateRegistrationAsync(null, CancellationToken.None, certificate: true);
                Assert.False(r.RoleGranted);
                Assert.EndsWith($"/adminconsent?client_id={EntraMock.NewAppId}", r.ConsentUrl);
                // The certificate is in place: the consent link is the only thing left to do.
                Assert.True(AppCertificate.Exists(r.CertificateThumbprint));
            }
            finally
            {
                if (r?.Pending != null) IntuneConnection.DiscardCertificate(r.Pending);
                if (r != null) Assert.False(AppCertificate.Exists(r.CertificateThumbprint));
                AppCertificate.UseTpm = true;
            }
        }, m => m.DenyConsent = true);

        [Fact]
        public Task A_deleted_registration_is_found_before_the_browser_opens_and_Disconnect_starts_over() => WithMockAsync(async mock =>
        {
            await ConnectWithAccountAsync();
            Assert.True(IntuneConnection.IsSignedIn);

            // The registration is deleted in Entra ID, and the session is gone (signed out, or expired).
            mock.DeletedClients.Add(EntraMock.NewAppId);
            IntuneConnection.SignOut();
            Assert.True(IntuneConnection.IsConfigured);

            // The check at start-up finds it, before anything is tried.
            await IntuneConnection.CheckAsync(CancellationToken.None);
            Assert.Contains("AADSTS700016", IntuneConnection.Problem);

            // A sign-in stops before a browser opens: Entra ID would show its error there and never come back.
            int Authorizations() { lock (mock.Calls) return mock.Calls.Count(c => c.Path.Contains($"/{EntraMock.TenantGuid}/oauth2/v2.0/authorize")); }
            int before = Authorizations();
            var ex = await Assert.ThrowsAsync<AuthException>(() => IntuneConnection.SignInAsync(CancellationToken.None));
            Assert.Equal("AADSTS700016", ex.Code);
            Assert.Equal(before, Authorizations());

            // Disconnect clears the connection; the automatic setup then creates a new registration and connects again.
            IntuneConnection.Disconnect();
            Assert.False(IntuneConnection.IsConfigured);
            Assert.Equal("", IntuneConnection.Problem);
            mock.DeletedClients.Clear();
            await ConnectWithAccountAsync();
            Assert.True(IntuneConnection.IsSignedIn);
            Assert.Equal("", IntuneConnection.Problem);
        });

        [Fact]
        public Task A_failed_trial_leaves_no_problem_on_the_connection_it_puts_back() => WithMockAsync(async mock =>
        {
            await ConnectWithAccountAsync();
            const string deleted = "11111111-2222-3333-4444-555555555555";
            mock.DeletedClients.Add(deleted);

            // A code of a registration that was deleted: the trial notes the problem of that registration...
            var trial = IntuneConnection.BeginTrial(EntraMock.TenantGuid, deleted);
            Assert.Equal(deleted, IntuneConnection.ClientId);
            // ...and lives in memory only: a save in the middle of it keeps the connection in use in the settings.
            ConfigStore.Save();
            Assert.Equal(EntraMock.NewAppId, ConfigStore.Current.IntuneClientId);
            await Assert.ThrowsAsync<AuthException>(() => IntuneConnection.SignInAsync(CancellationToken.None));
            Assert.Contains("AADSTS700016", IntuneConnection.Problem);

            // Dropped: the connection in use comes back as it was, without the problem of the registration tried.
            IntuneConnection.EndTrial(trial, keep: false);
            Assert.Equal(EntraMock.NewAppId, IntuneConnection.ClientId);
            Assert.Equal("", IntuneConnection.Problem);
            Assert.True(IntuneConnection.IsSignedIn);
            Assert.False(IntuneConnection.NeedsSetup);
        });

        [Fact]
        public async Task A_certificate_left_beside_a_user_connection_by_2_4_2_is_removed_at_start_up()
        {
            AppCertificate.UseTpm = false;
            var cert = AppCertificate.Create("contoso.com");
            var cfg = ConfigStore.Current;
            string mode = cfg.IntuneAuthMode;
            try
            {
                cfg.IntuneAuthMode = IntuneConnection.DelegatedMode;
                cfg.IntuneCertificateThumbprint = cert.Thumbprint;
                await IntuneConnection.CheckAsync(CancellationToken.None);
                Assert.Null(AppCertificate.Find(cert.Thumbprint));
                Assert.Equal("", cfg.IntuneCertificateThumbprint);
            }
            finally
            {
                AppCertificate.Delete(cert.Thumbprint);
                cert.Dispose();
                cfg.IntuneAuthMode = mode;
                cfg.IntuneCertificateThumbprint = "";
                AppCertificate.UseTpm = true;
            }
        }

        [Fact]
        public Task A_refusal_of_Intune_is_cleaned_up_and_explained() => WithMockAsync(async mock =>
        {
            await ConnectWithAccountAsync();
            mock.IntuneRefuses = true;
            var ex = await Assert.ThrowsAsync<GraphException>(() => IntuneConnection.TestAsync(CancellationToken.None));
            Assert.Equal(401, ex.Status);
            Assert.Equal("Forbidden", ex.Code);
            // The ids for support stay; the internal address of Intune does not.
            Assert.Contains("Activity ID: 184af700-7dff-44d6-ba38-5af761eb24d3", ex.Message);
            Assert.DoesNotContain("Url:", ex.Message);
            Assert.True(IntuneDiagnosis.IsRefusal(ex));
            // A user without an active Intune role: the role (PIM), the license, and the certificate as the way around.
            var causes = IntuneDiagnosis.Causes(IntuneConnection.LastToken, certificate: false);
            Assert.Contains(J365.IntunePSApp.Localization.Loc.T("Diag_RoleMissing"), causes);
            Assert.Contains(J365.IntunePSApp.Localization.Loc.T("Diag_UnlicensedAdmins"), causes);
            Assert.Equal(J365.IntunePSApp.Localization.Loc.T("Diag_UseCertificate"), causes.Last());
            // The application: its permission and the tenant, never a role or a license of a user.
            var app = new TokenSet { App = true, Claims = TokenSet.ReadClaims(mock.AppToken) };
            Assert.Equal(new[] { J365.IntunePSApp.Localization.Loc.T("Diag_TenantLicense") }, IntuneDiagnosis.Causes(app, certificate: true));
        });

        [Fact]
        public async Task The_publishing_script_signs_in_with_the_certificate_in_Windows_PowerShell()
        {
            // The functions of Publish-ToIntune.ps1 that sign in, run in Windows PowerShell 5.1 against the mock, which
            // checks the assertion the script signs (PS256, key of the certificate, audience, client, time).
            using var mock = new EntraMock();
            AppCertificate.UseTpm = false;
            var cert = AppCertificate.Create("contoso.com");
            try
            {
                lock (mock.KeyCredentials) mock.KeyCredentials.Add(new JsonObject { ["displayName"] = "J365 IntunePSApp - TEST", ["key"] = AppCertificate.PublicKeyBase64(cert) });
                string script = PackageBuilder.CompanionScript(PackageLayout.PublishScript);
                var functions = new[] { "ConvertTo-Base64Url", "Invoke-Http", "ConvertTo-FormBody", "Get-SignInHelp", "Set-Token", "Invoke-TokenRequest", "Connect-WithCertificate" }
                    .Select(name => System.Text.RegularExpressions.Regex.Match(script, @"(?ms)^function " + name + @"\b.*?^\}\r?$").Value).ToList();
                Assert.All(functions, f => Assert.False(string.IsNullOrEmpty(f)));
                string code = "$ErrorActionPreference = 'Stop'\r\n"
                    + $"$TenantId = '{EntraMock.TenantGuid}'; $ClientId = '{EntraMock.NewAppId}'; $CertificateThumbprint = '{cert.Thumbprint}'\r\n"
                    + $"$LoginUrl = '{mock.Base}/login'; $GraphUrl = '{mock.Base}'; $script:Token = $null\r\n"
                    + string.Join("\r\n", functions)
                    // An error is printed too, so a failure shows its reason.
                    + "\r\ntry { Connect-WithCertificate; 'TOKEN=' + $script:Token.AccessToken } catch { 'ERROR: ' + $_.Exception.Message }\r\n";
                string file = Path.Combine(Path.GetTempPath(), "j365-cert-" + Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(file, code, new UTF8Encoding(true));
                try
                {
                    var run = await Task.Run(() => ToolsService.RunPowerShell(ToolsService.PowerShellStartInfo($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\""), 60000));
                    Assert.False(run.TimedOut);
                    Assert.Contains("TOKEN=" + mock.AppToken, run.Output);
                    Assert.Equal(1, mock.ValidAssertions);
                    Assert.Equal("PS256", mock.LastAssertionAlg);
                }
                finally { File.Delete(file); }
            }
            finally
            {
                AppCertificate.Delete(cert.Thumbprint);
                cert.Dispose();
                AppCertificate.UseTpm = true;
            }
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
