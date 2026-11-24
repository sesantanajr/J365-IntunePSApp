using System;
using System.IO;
using System.Linq;
using System.Text;
using J365.IntunePSApp.Core;
using Xunit;

namespace J365.IntunePSApp.Tests
{
    /// <summary>
    /// Behaviours that protect the devices and the tenant (2.4): no silent downgrade, no false "uninstalled", list
    /// variables appended, values PowerShell expands kept as typed, and the connection code.
    /// </summary>
    public class SafetyTests
    {
        /// <summary>Runs a snippet in Windows PowerShell 5.1 (the PowerShell of Intune) and returns what it printed.</summary>
        private static string RunPowerShell(string code)
        {
            string file = Path.Combine(Path.GetTempPath(), "j365-test-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(file, code, new UTF8Encoding(true));
            try
            {
                var run = ToolsService.RunPowerShell(ToolsService.PowerShellStartInfo($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\""), 60000);
                Assert.False(run.TimedOut);
                return (run.Output ?? "").Trim();
            }
            finally { File.Delete(file); }
        }

        private static PackageSettings Firefox()
        {
            var s = Fakes.Settings();
            s.AppName = "Mozilla Firefox";
            s.AppVersion = "133.0";
            s.DetectionVersion = "133.0";
            s.UninstallPattern = "Mozilla Firefox*";
            return s;
        }

        [Fact]
        public void A_newer_version_already_installed_is_kept_before_anything_is_asked()
        {
            var s = Firefox();
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            Ps.AssertParses(script);
            int check = script.IndexOf("Test-J365NewerVersion -Installed", StringComparison.Ordinal);
            Assert.True(check > 0);
            int welcome = script.IndexOf("Show-ADTInstallationWelcome", StringComparison.Ordinal);
            Assert.True(welcome < 0 || check < welcome, "The check runs before the welcome dialog.");
            Assert.True(check < script.IndexOf("Start-ADTProcess", StringComparison.Ordinal), "The check runs before the installer.");
            // The newer copy is adopted: the mark is written, then the installation stops there.
            string block = script.Substring(check, script.IndexOf("Show-ADTInstallationProgress", check, StringComparison.Ordinal) - check);
            Assert.Contains("SetValue('Version', '133.0')", block);
            Assert.Contains("return", block);

            s.KeepNewerVersion = false;
            Assert.DoesNotContain("Test-J365NewerVersion", ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe"));
        }

        [Fact]
        public void The_newer_version_check_compares_with_the_version_the_package_installs()
        {
            // A lower minimum version in the detection (older copies count as installed) must not keep a copy older than
            // the package: 2.3 installed by hand is updated to 2.5.0, not kept because it is newer than 2.0.
            var s = Firefox();
            s.AppVersion = "2.5.0";
            s.DetectionVersion = "2.0";
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            Assert.Contains("-Package '2.5.0'", script);
            Assert.DoesNotContain("-Package '2.0'", script);
            // The mark still carries the version the detection compares with its minimum.
            Assert.Equal("2.0", ScriptGenerator.MarkVersion(s));

            s.AppVersion = "";
            Assert.Equal("2.0", ScriptGenerator.PackageVersion(s));
            s.AppVersion = "latest";
            Assert.Equal("2.0", ScriptGenerator.PackageVersion(s));
            s.AppVersion = "1.0";
            s.DetectionVersion = "1.0.5";
            Assert.Equal("1.0.5", ScriptGenerator.PackageVersion(s));
            s.DetectionVersion = "";
            Assert.Equal("1.0", ScriptGenerator.PackageVersion(s));
            s.AppVersion = "";
            Assert.Equal("", ScriptGenerator.PackageVersion(s));
        }

        [Theory]
        [InlineData("134.0.1", "133.0", true)]
        [InlineData("133.0", "133.0", false)]
        [InlineData("132.0.2", "133.0", false)]
        [InlineData("1.10", "1.9", true)]
        [InlineData("26.02-v1.5.7-R2", "26.01", true)]
        [InlineData("", "1.0", false)]
        public void The_newer_version_check_compares_numbers_not_text(string installed, string package, bool newer)
        {
            var s = Firefox();
            s.AppVersion = package;
            s.DetectionVersion = package;
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            int start = script.IndexOf("    function Test-J365NewerVersion", StringComparison.Ordinal);
            int end = script.IndexOf("    $keptApps", start, StringComparison.Ordinal);
            string function = script.Substring(start, end - start);
            // Only a newer version counts: the same version is never "newer" (3.0 removes it and installs it again).
            Assert.Equal(newer ? "True" : "False", RunPowerShell(function + $"\r\nTest-J365NewerVersion -Installed '{installed}' -Package '{package}'"));
        }

        [Fact]
        public void Uninstall_fails_instead_of_reporting_success_when_the_application_stays()
        {
            var s = Firefox();
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            Ps.AssertParses(script);
            int verify = script.IndexOf("Exit-J365Deployment -ExitCode 69002 -Message ('Still installed after the uninstallation", StringComparison.Ordinal);
            int markRemoved = script.IndexOf("Package mark removed", StringComparison.Ordinal);
            Assert.True(verify > 0, "The uninstallation checks that the application is gone.");
            Assert.True(verify < markRemoved, "The mark is only removed once the application is gone.");
        }

        [Fact]
        public void A_failed_removal_of_the_previous_version_does_not_leave_its_exit_code()
        {
            var s = Firefox();
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            int block = script.IndexOf("$existingApps =", StringComparison.Ordinal);
            int install = script.IndexOf("Start-ADTProcess", block, StringComparison.Ordinal);
            string removal = script.Substring(block, install - block);
            Assert.Contains("$j365ExitCode = $adtSession.GetExitCode()", removal);
            Assert.Equal(1, removal.Split("$adtSession.SetExitCode($j365ExitCode)").Length - 1);
        }

        [Fact]
        public void The_uninstall_action_waits_for_the_real_uninstaller()
        {
            foreach (string args in new[] { "", "/S" })
            {
                var a = new PackageAction { Type = "UninstallApp", Params = { ["NamePattern"] = "7-Zip*", ["ExeArguments"] = args } };
                var exe = ActionCatalog.Generate(a).Where(l => l.Contains("-ApplicationType 'EXE'")).ToList();
                Assert.NotEmpty(exe);
                Assert.All(exe, l => Assert.Contains("-WaitForChildProcesses", l));
            }
        }

        [Theory]
        [InlineData("Path", "%PATH%;C:\\Tools\\bin", false)]
        [InlineData("PSModulePath", "C:\\Modules", true)]
        [InlineData("JAVA_OPTS", "%JAVA_OPTS% -Xmx2g", false)]
        public void List_variables_are_appended_and_keep_their_variables(string name, string value, bool userContext)
        {
            var a = new PackageAction { Type = "SetEnvironment", Params = { ["Name"] = name, ["Value"] = value } };
            var lines = ActionCatalog.Generate(a, userContext);
            string code = string.Join("\n", lines);
            Ps.AssertParses(code);
            Assert.DoesNotContain("SetEnvironmentVariable", code);
            Assert.Contains("[Microsoft.Win32.RegistryValueKind]::ExpandString", code);
            Assert.Contains("DoNotExpandEnvironmentNames", code);
            Assert.DoesNotContain("'%" + name + "%'", code);
            Assert.Contains(userContext ? "CurrentUser" : "LocalMachine", code);
            // Explorer of the signed-in user and the rest of the deployment are told after the write.
            Assert.True(code.IndexOf("Update-ADTDesktop", StringComparison.Ordinal) > code.IndexOf("$envKey.Close()", StringComparison.Ordinal));
            Assert.Contains("Update-ADTEnvironmentPsProvider", code);
            Assert.NotEmpty(ActionCatalog.Warnings(a, userContext));
        }

        [Fact]
        public void Plain_variables_are_still_set_directly()
        {
            var a = new PackageAction { Type = "SetEnvironment", Params = { ["Name"] = "CONTOSO_HOME", ["Value"] = "C:\\Contoso" } };
            Assert.Contains(ActionCatalog.Generate(a), l => l.Contains("SetEnvironmentVariable('CONTOSO_HOME', 'C:\\Contoso', 'Machine')"));
            Assert.Contains(ActionCatalog.Generate(a), l => l.TrimStart().StartsWith("Update-ADTDesktop", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("Microsoft Graph answered HTTP 401 (Forbidden) to GET /deviceAppManagement/mobileApps: An error has occurred", true)]
        [InlineData("Microsoft Graph answered HTTP 403 (Forbidden) to POST /deviceAppManagement/mobileApps: An error has occurred", true)]
        [InlineData("Microsoft Graph answered HTTP 401 (InvalidAuthenticationToken) to GET /x: Access token has expired", false)]
        [InlineData("Microsoft Graph answered HTTP 400 (BadRequest) to GET /x: Parsing OData Select and Expand failed", false)]
        [InlineData("Sign-in failed: AADSTS50076", false)]
        [InlineData("", false)]
        // The upload goes to Azure Storage, not to Intune: its 403 (an expired link, a proxy) is not a refusal of Intune.
        [InlineData("The upload failed after 4 attempts: HTTP 403", false)]
        [InlineData("The upload could not be finished (HTTP 403).", false)]
        // What Microsoft Graph answers a tenant without Intune for some requests.
        [InlineData("Microsoft Graph answered HTTP 400 (BadRequest) to GET /deviceManagement/x: Request not applicable to target tenant.", true)]
        public void Refusals_of_Intune_are_told_apart_from_sign_in_errors(string text, bool refusal) => Assert.Equal(refusal, IntuneDiagnosis.IsRefusal(text));

        [Fact]
        public void The_error_of_Intune_keeps_its_sentence_and_ids_but_not_its_internal_address()
        {
            string intune = "{\r\n  \"_version\": 3,\r\n  \"Message\": \"An error has occurred - Operation ID (for customer support): 00000000-0000-0000-0000-000000000000 - Activity ID: 184af700-7dff-44d6-ba38-5af761eb24d3 - Url: https://proxy.msua08.manage.microsoft.com/AppLifecycle_2611/StatelessAppMetadataFEService/deviceAppManagement/mobileApps?api-version=5026-08-19\",\r\n  \"CustomApiErrorPhrase\": \"\",\r\n  \"RetryAfter\": null\r\n}";
            // The operation id of Intune is always empty: only the activity id identifies the request.
            Assert.Equal("An error has occurred - Activity ID: 184af700-7dff-44d6-ba38-5af761eb24d3", GraphException.CleanMessage(intune));
            Assert.Equal("Plain message", GraphException.CleanMessage("Plain message"));
            Assert.Null(GraphException.CleanMessage(""));
        }

        [Fact]
        public void An_error_of_Intune_nested_twice_is_opened_to_its_sentence()
        {
            // How Intune refuses a tenant without a subscription: its error as JSON, inside the error of another service.
            string inner = "{\r\n  \"_version\": 3,\r\n  \"Message\": \"An error has occurred - Operation ID (for customer support): 00000000-0000-0000-0000-000000000000 - Activity ID: bf1a4756-a3a2-43d1-bcc9-f5b988cd0e51 - Url: https://fef.msua06.manage.microsoft.com/StatelessAppMetadataFEService/deviceAppManagement/mobileApps?api-version=5025-09-12\",\r\n  \"CustomApiErrorPhrase\": \"\",\r\n  \"RetryAfter\": null,\r\n  \"ErrorSourceService\": \"\",\r\n  \"HttpHeaders\": \"{}\"\r\n}";
            string outer = new System.Text.Json.Nodes.JsonObject { ["ErrorCode"] = "Forbidden", ["Message"] = inner, ["Target"] = null }.ToJsonString();
            const string clean = "An error has occurred - Activity ID: bf1a4756-a3a2-43d1-bcc9-f5b988cd0e51";
            Assert.Equal(clean, GraphException.CleanMessage(outer));
            Assert.Equal(clean, GraphException.CleanMessage(inner));
            // Cut short, so no longer JSON: the sentence is still found.
            Assert.Equal(clean, GraphException.CleanMessage(inner.Substring(0, inner.IndexOf("CustomApiErrorPhrase"))));
            Assert.Equal("bf1a4756-a3a2-43d1-bcc9-f5b988cd0e51", GraphException.ActivityId(clean));
            Assert.Equal("", GraphException.ActivityId("no id here"));
        }

        [Fact]
        public void The_plans_of_the_tenant_say_whether_it_has_Intune()
        {
            System.Text.Json.Nodes.JsonArray Plans(params (string Id, string Service, string Status)[] p) => new System.Text.Json.Nodes.JsonArray(p.Select(x =>
                (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject { ["servicePlanId"] = x.Id, ["service"] = x.Service, ["capabilityStatus"] = x.Status }).ToArray());
            const string intune = "c1ec4a95-1f05-45b3-a911-aa3fa01094f5", basic = "882e1d05-acd1-4ccb-8708-6ee03664b117", exchange = "9aaf7827-d63c-4b61-89c3-182f06f82e5c";
            Assert.Equal(IntunePlanState.Active, IntuneSubscription.Classify(Plans((exchange, "exchange", "Enabled"), (intune, "SCO", "Enabled"))));
            // In its grace period a subscription still works.
            Assert.Equal(IntunePlanState.Active, IntuneSubscription.Classify(Plans((intune, "SCO", "Warning"))));
            // Microsoft 365 Business Premium, by its plan.
            Assert.Equal(IntunePlanState.Active, IntuneSubscription.Classify(Plans(("8E9FF0FF-AA7A-4B20-83C1-2F636B600AC2", "", "Enabled"))));
            // Another plan of the Intune service, one this application does not know: it cannot say either way.
            Assert.Equal(IntunePlanState.Unknown, IntuneSubscription.Classify(Plans(("d9923fe3-a2de-4d29-a5be-e3e83bb786be", "SCO", "Enabled"), (basic, "SCO", "Enabled"))));
            Assert.Equal(IntunePlanState.BasicOnly, IntuneSubscription.Classify(Plans((exchange, "exchange", "Enabled"), (basic, "SCO", "Enabled"))));
            Assert.Equal(IntunePlanState.Inactive, IntuneSubscription.Classify(Plans((intune, "SCO", "Suspended"), (basic, "SCO", "Enabled"))));
            Assert.Equal(IntunePlanState.Inactive, IntuneSubscription.Classify(Plans((intune, "SCO", "Deleted"))));
            Assert.Equal(IntunePlanState.None, IntuneSubscription.Classify(Plans((exchange, "exchange", "Enabled"))));
            Assert.Equal(IntunePlanState.None, IntuneSubscription.Classify(new System.Text.Json.Nodes.JsonArray()));
        }

        [Fact]
        public void The_check_shows_the_plans_of_Intune_it_rests_on()
        {
            System.Text.Json.Nodes.JsonObject Plan(string id, string service, string status, string assigned) =>
                new System.Text.Json.Nodes.JsonObject { ["servicePlanId"] = id, ["service"] = service, ["capabilityStatus"] = status, ["assignedDateTime"] = assigned };
            const string intune = "c1ec4a95-1f05-45b3-a911-aa3fa01094f5", basic = "882e1d05-acd1-4ccb-8708-6ee03664b117";
            // A trial of Intune Plan 1 that ended (listed twice by two subscriptions), the basic mobility, and Exchange.
            var plans = new System.Text.Json.Nodes.JsonArray(
                Plan(basic, "SCO", "Enabled", "2025-01-10T12:00:00Z"),
                Plan(intune, "SCO", "Suspended", "2026-10-21T09:30:00Z"),
                Plan(intune.ToUpperInvariant(), "SCO", "Suspended", "2026-09-01T09:30:00Z"),
                Plan("9aaf7827-d63c-4b61-89c3-182f06f82e5c", "exchange", "Enabled", "2025-01-10T12:00:00Z"));
            var found = IntuneSubscription.Found(plans);
            // Exchange is not listed; the plan of Intune once, with its most recent date; the basic mobility after it.
            Assert.Equal(new[] { intune, basic }, found.Select(f => f.Id));
            Assert.Equal("Suspended", found[0].Status);
            Assert.Equal(new DateTime(2026, 10, 21, 9, 30, 0, DateTimeKind.Utc), found[0].Assigned);
            Lang.With("pt-BR", () =>
            {
                string line = IntuneSubscription.Describe(found[0]);
                Assert.StartsWith("Microsoft Intune Plan 1: suspenso (atribuído em ", line);
                Assert.Equal("Mobilidade e Segurança Básicas: ativo", IntuneSubscription.Describe(new IntunePlanFound { Id = basic, Status = "Enabled" }));
                Assert.Equal("Outro plano do Intune (d9923fe3): vencido, no período de carência (ainda funciona)",
                    IntuneSubscription.Describe(new IntunePlanFound { Id = "d9923fe3-a2de-4d29-a5be-e3e83bb786be", Status = "Warning" }));
            });
            // An active plan comes first.
            var active = IntuneSubscription.Found(new System.Text.Json.Nodes.JsonArray(Plan(intune, "SCO", "Deleted", "2024-01-01T00:00:00Z"), Plan(intune, "SCO", "Enabled", "2023-01-01T00:00:00Z")));
            Assert.Equal("Enabled", active[0].Status);
        }

        [Fact]
        public void A_refusal_of_the_publishing_script_gives_its_request_and_activity_id()
        {
            string error = "Microsoft Graph answered HTTP 401 (UnknownError) to GET /deviceAppManagement/mobileApps: An error has occurred - Activity ID: 32f20c83-f999-4b7b-aa7a-e485513d188a\nIntune refused the application. Check that the tenant has an active Microsoft Intune subscription.";
            Assert.True(IntuneDiagnosis.IsRefusal(error));
            var parsed = IntuneDiagnosis.ParseScriptError(error).Value;
            Assert.Equal(401, parsed.Status);
            Assert.Equal("UnknownError", parsed.Code);
            Assert.Equal("GET /deviceAppManagement/mobileApps", parsed.Request);
            Assert.Equal("32f20c83-f999-4b7b-aa7a-e485513d188a", parsed.ActivityId);
            Assert.Equal(J365.IntunePSApp.Localization.Loc.T("Intune_Support", "GET /deviceAppManagement/mobileApps · Activity ID 32f20c83-f999-4b7b-aa7a-e485513d188a"), IntuneDiagnosis.SupportLine(error));
            // An error of the application itself (GraphException) gives the same.
            Assert.Equal(J365.IntunePSApp.Localization.Loc.T("Intune_Support", "GET /beta/deviceAppManagement/mobileApps · Activity ID 32f20c83-f999-4b7b-aa7a-e485513d188a"),
                IntuneDiagnosis.SupportLine("GET /beta/deviceAppManagement/mobileApps: An error has occurred - Activity ID: 32f20c83-f999-4b7b-aa7a-e485513d188a"));
            Assert.Null(IntuneDiagnosis.ParseScriptError("The file could not be read."));
            Assert.Equal("", IntuneDiagnosis.SupportLine("The file could not be read."));
            // "An error has occurred" says nothing: only a sentence that says more is shown.
            Assert.Equal("", IntuneDiagnosis.AnswerOf(error));
            Assert.Equal("Application is not authorized to perform this operation. Application must have one of the following scopes: DeviceManagementApps.ReadWrite.All",
                IntuneDiagnosis.AnswerOf("GET /beta/deviceAppManagement/mobileApps: Application is not authorized to perform this operation. Application must have one of the following scopes: DeviceManagementApps.ReadWrite.All - Activity ID: 32f20c83-f999-4b7b-aa7a-e485513d188a"));
        }

        [Fact]
        public void The_client_assertion_is_a_PS256_JWT_signed_with_a_key_that_cannot_be_exported()
        {
            AppCertificate.UseTpm = false;
            var cert = AppCertificate.Create("contoso.com");
            try
            {
                Assert.Equal(AppCertificate.CommonName, cert.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false));
                Assert.InRange((cert.NotAfter - DateTime.Now).TotalDays, 360, 366);
                Assert.True(AppCertificate.IsUsable(cert.Thumbprint));
                Assert.False(AppCertificate.Describe(cert.Thumbprint).Hardware);
                Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx));

                string jwt = AppCertificate.ClientAssertion(cert, "https://login.example/tenant/oauth2/v2.0/token", "client-1");
                var parts = jwt.Split('.');
                Assert.Equal(3, parts.Length);
                byte[] Bytes(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')); }
                var header = System.Text.Json.Nodes.JsonNode.Parse(Bytes(parts[0]));
                var payload = System.Text.Json.Nodes.JsonNode.Parse(Bytes(parts[1]));
                Assert.Equal("PS256", (string)header["alg"]);
                Assert.Equal(Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(cert.RawData)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), (string)header["x5t#S256"]);
                Assert.Equal("https://login.example/tenant/oauth2/v2.0/token", (string)payload["aud"]);
                Assert.Equal("client-1", (string)payload["iss"]);
                Assert.Equal("client-1", (string)payload["sub"]);
                Assert.InRange((long)payload["exp"] - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 500, 610);
                using var rsa = System.Security.Cryptography.X509Certificates.RSACertificateExtensions.GetRSAPublicKey(cert);
                Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Bytes(parts[2]), System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pss));
            }
            finally
            {
                AppCertificate.Delete(cert.Thumbprint);
                Assert.Null(AppCertificate.Find(cert.Thumbprint));
                cert.Dispose();
                AppCertificate.UseTpm = true;
            }
        }

        [Theory]
        [InlineData("ab cd ef 01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01", "ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
        [InlineData("not a thumbprint", "")]
        [InlineData("ABCDEF", "")]
        [InlineData(null, "")]
        public void Thumbprints_are_normalized_or_refused(string text, string expected) => Assert.Equal(expected, AppCertificate.Normalize(text));

        [Theory]
        [InlineData("network", true)]
        [InlineData("HttpRequestException", true)]
        [InlineData("TaskCanceledException", true)]
        [InlineData("HTTP 503", true)]
        [InlineData("HTTP 429", true)]
        [InlineData("HTTP 403", false)]
        [InlineData("HTTP 400", false)]
        [InlineData("AADSTS50105", false)]
        [InlineData("cancel", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_failures_of_the_network_suggest_checking_the_network(string code, bool network)
        {
            Assert.Equal(network, EntraAuth.IsNetworkFailure(code));
        }

        [Fact]
        public void Values_that_PowerShell_expands_reach_it_as_typed()
        {
            const string name = "Acme \u201CCorp\u201D \u201Ebest\u201D $env:x `n \"quoted\"";
            string escaped = ScriptGenerator.EscapeExpandable(name);
            // The way the toolkit reads its config: a single-quoted literal, expanded as a double-quoted string.
            // Compared in PowerShell itself (the console encoding of the test runner does not matter).
            string same = RunPowerShell("$v = " + ScriptGenerator.Lit(escaped) + "\r\n$typed = " + ScriptGenerator.Lit(name) + "\r\n$ExecutionContext.InvokeCommand.ExpandString($v) -ceq $typed");
            Assert.Equal("True", same);
        }

        [Fact]
        public void The_connection_code_round_trips_and_rejects_bad_input()
        {
            const string tenant = "8f2b6a4e-1c3d-4e5f-9a7b-2c4d6e8f0a1b", client = "3c9e7a21-5b4d-4f6a-8e2c-1a3b5c7d9e0f";
            Assert.True(IntuneConnection.TryParseConnectionCode($"J365-CONNECT tenant={tenant} client={client}", out var t, out var c));
            Assert.Equal(tenant, t);
            Assert.Equal(client, c);
            // The Cloud Shell output may come with the prompt or other lines around it, and domains are accepted.
            Assert.True(IntuneConnection.TryParseConnectionCode($"azure$ ...\nJ365-CONNECT tenant=contoso.onmicrosoft.com client={client}\n", out t, out _));
            Assert.Equal("contoso.onmicrosoft.com", t);
            Assert.False(IntuneConnection.TryParseConnectionCode("J365-CONNECT tenant= client=", out _, out _));
            Assert.False(IntuneConnection.TryParseConnectionCode($"J365-CONNECT tenant={tenant} client=not-a-guid", out _, out _));
            Assert.False(IntuneConnection.TryParseConnectionCode("", out _, out _));
        }

        [Fact]
        public void A_cancelled_sign_in_is_recognised()
        {
            var e = EntraAuth.Explain("access_denied", null, "cancel");
            Assert.Equal("cancel", e.Code);
            Assert.False(string.IsNullOrWhiteSpace(e.Message));
            // Conditional Access keeps its own explanation even with the same error.
            Assert.Equal("AADSTS53003", EntraAuth.Explain("access_denied", "AADSTS53003: Access has been blocked by Conditional Access policies.", "cancel").Code);
            Assert.Equal("AADSTS7000112", EntraAuth.Explain("unauthorized_client", "AADSTS7000112: Application is disabled.").Code);
        }

        [Fact]
        public void A_toolkit_copy_without_the_publisher_signature_is_refused()
        {
            string root = Path.Combine(Path.GetTempPath(), "j365-psadt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "PSAppDeployToolkit"));
            try
            {
                Assert.Throws<InvalidDataException>(() => ToolsService.VerifyPsadt(root));
                File.WriteAllText(Path.Combine(root, "PSAppDeployToolkit", "PSAppDeployToolkit.psm1"), "function Open-ADTSession { }");
                Assert.Throws<InvalidDataException>(() => ToolsService.VerifyPsadt(root));
            }
            finally { Directory.Delete(root, true); }

            // The official copy, when this machine has one (the demo data of the screenshots).
            string official = Path.Combine("C:" + Path.DirectorySeparatorChar, "Users", "Public", "J365-IntunePSApp-Demo", "IntunePSApp-Data", "tools", "PSAppDeployToolkit", "4.1.8");
            if (Directory.Exists(official)) Assert.Contains("Patch My PC", ToolsService.VerifyPsadt(official));
        }

        [Theory]
        [InlineData("SignPath Foundation", true)]
        [InlineData("Open Source Developer, Sérgio Sant'Ana Junior", true)]
        [InlineData("Open Source Developer, Sergio Sant Ana Junior", true)]
        [InlineData("Jornada 365", true)]
        [InlineData("Contoso Ltd", false)]
        [InlineData("", false)]
        public void Updates_accept_the_publishers_of_the_project(string signer, bool accepted)
        {
            var running = ("Someone Else", "CN=Someone Else");
            Assert.Equal(accepted, AppUpdater.IsTrustedPublisher((signer, "CN=" + signer), running));
            // The publisher of the running copy is always accepted.
            Assert.True(AppUpdater.IsTrustedPublisher(("Contoso Ltd", "CN=Contoso Ltd, O=Contoso"), ("Contoso Ltd", "CN=Contoso Ltd, O=Contoso")));
        }

        [Fact]
        public void Companion_scripts_are_written_unchanged_so_their_signature_stays_valid()
        {
            // Releases embed the scripts signed: what the application writes must be the same text, signature block included.
            string signedLike = "Write-Host 'J365 IntunePSApp'" + System.Environment.NewLine + "# SIG # Begin signature block" + System.Environment.NewLine
                                + "# MIIabc" + System.Environment.NewLine + "# SIG # End signature block" + System.Environment.NewLine;
            Assert.Equal(signedLike, PackageBuilder.Rebrand(signedLike));
            Assert.Equal(PackageBuilder.EmbeddedText(PackageLayout.PublishScript), PackageBuilder.CompanionScript(PackageLayout.PublishScript));
            Assert.Equal(PackageBuilder.EmbeddedText(PackageLayout.SandboxScript), PackageBuilder.CompanionScript(PackageLayout.SandboxScript));
        }

        [Fact]
        public void The_cloud_shell_commands_print_the_connection_code()
        {
            Assert.Contains("J365-CONNECT tenant=$T client=$APP", ConnectScripts.Bash);
            Assert.Contains("J365-CONNECT tenant=$t client=$app", ConnectScripts.PowerShell);
            foreach (var script in new[] { ConnectScripts.Bash, ConnectScripts.PowerShell })
            {
                Assert.Contains("--is-fallback-public-client true", script);
                Assert.Contains("--public-client-redirect-uris http://localhost", script);
                Assert.Contains("DeviceManagementApps.ReadWrite.All", script);
                Assert.Contains("az ad app permission grant", script);
                Assert.DoesNotContain("credential reset", script);
            }
            Ps.AssertParses(ConnectScripts.PowerShell);
        }
    }
}
