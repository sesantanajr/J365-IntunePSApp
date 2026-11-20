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
            int end = script.IndexOf("    $newerApps", start, StringComparison.Ordinal);
            string function = script.Substring(start, end - start);
            Assert.Equal(newer ? "True" : "False", RunPowerShell(function + $"\r\nTest-J365NewerVersion -Installed '{installed}' -Package '{package}'"));
        }

        [Fact]
        public void Uninstall_fails_instead_of_reporting_success_when_the_application_stays()
        {
            var s = Firefox();
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            Ps.AssertParses(script);
            int verify = script.IndexOf("throw ('Still installed after the uninstallation", StringComparison.Ordinal);
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
            Assert.Equal(2, removal.Split("$adtSession.SetExitCode($j365ExitCode)").Length - 1);
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
