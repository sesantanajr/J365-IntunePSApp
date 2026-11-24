using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>
/// 3.0.0: two ways to install (silent, or notifying the user) and nothing to defer; whatever is installed is removed
/// before the installation, and the package says what it did when a copy could not be removed; the release no longer
/// does what makes a security product look twice (a compressed executable, files "unblocked" after a download).
/// The removal runs here for real, in Windows PowerShell 5.1, against stand-ins of the toolkit.
/// </summary>
public class Release30Tests
{
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

    // ------------------------------------------------------------------ two modes

    [Theory]
    [InlineData("Silent", "Silent")]
    [InlineData(" silent ", "Silent")]
    [InlineData("Notify", "Notify")]
    [InlineData("Auto", "Notify")]
    [InlineData("WhenNeeded", "Notify")]
    [InlineData("Interactive", "Notify")]
    [InlineData("", "Notify")]
    [InlineData(null, "Notify")]
    [InlineData("Loud", "Notify")]
    public void There_are_two_modes_and_everything_else_notifies(string stored, string expected)
        => Assert.Equal(expected, DeployModes.Normalize(stored));

    [Fact]
    public void The_command_line_knows_the_two_modes_by_name()
    {
        Assert.True(DeployModes.IsKnown("silent"));
        Assert.True(DeployModes.IsKnown("Notify"));
        Assert.False(DeployModes.IsKnown("Loud"));
        Assert.False(DeployModes.IsKnown(""));
        Assert.Equal(new[] { "Notify", "Silent" }, DeployModes.All);
    }

    [Fact]
    public void A_project_of_2_8_opens_with_its_mode_and_without_what_3_0_removed()
    {
        const string old = @"{
  ""SchemaVersion"": 2, ""AppName"": ""7-Zip"", ""AppVersion"": ""26.00"", ""InstallerType"": ""EXE"",
  ""DeployMode"": ""WhenNeeded"", ""IntuneNotifications"": ""showReboot"", ""AllowDefer"": true, ""DeferTimes"": 3,
  ""RemovePreviousVersions"": true, ""KeepNewerVersion"": true, ""KeepSameVersion"": false, ""UninstallPattern"": ""7-Zip*""
}";
        using var tmp = new TempFolder();
        var s = PackageBuilder.LoadProject(tmp.File("project.json", old));
        Assert.Equal("Notify", s.DeployMode);
        Assert.Equal(PackageSettings.CurrentSchema, s.SchemaVersion);
        Assert.Equal(3, PackageSettings.CurrentSchema);
        Assert.True(s.RemovePreviousVersions);
        // The removal is mandatory unless the package says otherwise.
        Assert.False(s.InstallIfRemovalFails);
        string saved = JsonSerializer.Serialize(s, Json.Options);
        foreach (var gone in new[] { "AllowDefer", "DeferTimes", "IntuneNotifications", "KeepSameVersion" }) Assert.DoesNotContain(gone, saved);
        Assert.Contains("\"InstallIfRemovalFails\": false", saved);

        var silent = JsonSerializer.Deserialize<PackageSettings>("{ \"DeployMode\": \"silent\" }", Json.Options);
        PackageBuilder.Migrate(silent);
        Assert.Equal("Silent", silent.DeployMode);
    }

    [Fact]
    public void Nothing_in_a_generated_script_lets_the_user_defer()
    {
        foreach (var mode in DeployModes.All)
        {
            var s = Fakes.Settings();
            s.DeployMode = mode;
            s.WelcomeMessage = "Save your work";
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            Ps.AssertParses(script);
            Assert.DoesNotContain("AllowDefer", script);
            Assert.DoesNotContain("DeferTimes", script);
            Assert.DoesNotContain("DeferDays", script);
        }
        // The company defaults and the batch have nothing about it either.
        string config = JsonSerializer.Serialize(new AppConfig(), Json.Options);
        Assert.DoesNotContain("Defer", config);
        Assert.DoesNotContain("IntuneNotifications", config);
        Assert.DoesNotContain("Defer", JsonSerializer.Serialize(new BatchOptions(), Json.Options));
    }

    [Fact]
    public void A_package_that_notifies_closes_the_applications_after_a_countdown_and_shows_the_toasts()
    {
        var s = Fakes.Settings();
        s.CloseAppsCountdown = 120;
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Assert.Contains("$saiwParams.Add('CloseProcessesCountdown', 120)", script);
        Assert.Contains("PersistPrompt = $true", script);
        Assert.Contains("Show-ADTInstallationProgress", script);
        Assert.Equal("showAll", PackageBuilder.IntuneNotifications(s));
        Assert.Equal("basedOnReturnCode", PackageBuilder.RestartBehavior(s));

        s.DeployMode = DeployModes.Silent;
        Assert.DoesNotContain("CloseProcessesCountdown', 120", ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe"));
        Assert.Equal("hideAll", PackageBuilder.IntuneNotifications(s));
        Assert.Equal("suppress", PackageBuilder.RestartBehavior(s));
    }

    [Fact]
    public void The_welcome_message_is_only_warned_about_where_it_would_appear()
    {
        using var tmp = new TempFolder();
        var s = Fakes.Settings();
        s.InstallerPath = tmp.File("setup.exe", "x");
        s.WelcomeMessage = "Save your work";
        string warning = Loc.T("Val_Warn_WelcomeNotShown");
        Assert.DoesNotContain(warning, PackageBuilder.Validate(s).Warnings);   // an application to close: the dialog appears
        s.ProcessesToClose.Clear();
        Assert.Contains(warning, PackageBuilder.Validate(s).Warnings);
        s.DeployMode = DeployModes.Silent;                                       // silent: the other warning says it
        Assert.DoesNotContain(warning, PackageBuilder.Validate(s).Warnings);
        Assert.Contains(Loc.T("Val_Warn_SilentHidesDialogs"), PackageBuilder.Validate(s).Warnings);
    }

    // ------------------------------------------------------------------ removal of what is installed

    /// <summary>Stand-ins of the toolkit: what is "installed", an uninstaller that records how it was called, and the log.</summary>
    private const string FakeToolkit = @"$ErrorActionPreference = 'Stop'
$script:calls = New-Object System.Collections.ArrayList
$script:log = New-Object System.Collections.ArrayList
$script:exitCode = 0
function New-FakeApp([string]$Name, [switch]$Msi, [string]$Quiet, [string]$Key = 'Contoso', [string]$File, [switch]$Stays, [switch]$Fails)
{
    [pscustomobject]@{ DisplayName = $Name; DisplayVersion = '1.0'; WindowsInstaller = [bool]$Msi; QuietUninstallString = $Quiet; PSChildName = $Key
        UninstallStringFilePath = $File; ProductCode = $null; Stays = [bool]$Stays; Fails = [bool]$Fails }
}
function Get-ADTApplication { param($Name, $NameMatch, $ProductCode, $ApplicationType) $script:installed }
function Uninstall-ADTApplication
{
    param($InstalledApplication, $ArgumentList, [switch]$WaitForChildProcesses)
    $null = $script:calls.Add(('{0}|{1}|{2}' -f $InstalledApplication.DisplayName, ($ArgumentList -join ' '), [bool]$WaitForChildProcesses))
    if ($InstalledApplication.Fails) { $script:exitCode = 1603; throw 'The uninstaller returned 1603.' }
    if (-not $InstalledApplication.Stays) { $script:installed = @($script:installed | Where-Object { $_.DisplayName -ne $InstalledApplication.DisplayName }) }
}
function Write-ADTLogEntry { param($Message, $Severity) $null = $script:log.Add([string]$Message) }
$adtSession = New-Object psobject
$adtSession | Add-Member -MemberType ScriptMethod -Name GetExitCode -Value { $script:exitCode }
$adtSession | Add-Member -MemberType ScriptMethod -Name SetExitCode -Value { param($code) $script:exitCode = $code }
";

    /// <summary>The functions the generated script uses to remove what is installed, without the wait for background uninstallers.</summary>
    private static string Helpers(string script)
    {
        int start = script.IndexOf("function Get-J365InstalledCopies", StringComparison.Ordinal);
        int end = script.IndexOf("function Install-ADTDeployment", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "The removal functions come before the installation.");
        string helpers = script.Substring(start, end - start);
        Assert.Contains($"AddSeconds({ScriptGenerator.RemovalWaitSeconds})", helpers);
        return helpers.Replace($"AddSeconds({ScriptGenerator.RemovalWaitSeconds})", "AddSeconds(0)");
    }

    private static string Block(string script, string from, string to)
    {
        int start = script.IndexOf(from, StringComparison.Ordinal);
        int end = script.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "Block not found: " + from);
        return script.Substring(start, end - start);
    }

    [Fact]
    public void Each_copy_is_removed_the_way_it_can_be_removed_without_a_window()
    {
        using var tmp = new TempFolder();
        // An NSIS uninstaller carries the name of its stub in the manifest; the other file is a setup nobody recognises.
        string nsis = tmp.File("uninst-nsis.exe", "MZ" + new string('x', 5000) + "<assemblyIdentity name=\"Nullsoft.NSIS.exehead\" type=\"win32\"/>");
        string unknown = tmp.File("uninst-unknown.exe", "MZ" + new string('y', 5000));
        var s = Fakes.Settings();
        s.UninstallExeArgs = "";
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Ps.AssertParses(script);
        string run = FakeToolkit + Helpers(script) + $@"
$script:installed = @(
    (New-FakeApp 'Msi App' -Msi),
    (New-FakeApp 'Quiet App' -Quiet '""C:\App\unins.exe"" /quiet'),
    (New-FakeApp 'Inno App' -Key '{{8E1F2A3B-0000-4000-8000-000000000001}}_is1' -File 'C:\missing\unins000.exe'),
    (New-FakeApp 'Nsis App' -File '{nsis}'),
    (New-FakeApp 'Unknown App' -File '{unknown}'),
    (New-FakeApp 'Gone App' -File 'C:\missing\uninstall.exe')
)
foreach ($app in @($script:installed)) {{ Remove-J365InstalledCopy -Application $app }}
$script:calls -join ';'
@($script:log | Where-Object {{ $_ -like '*was not removed*' }}).Count
@($script:installed).Count";
        var lines = RunPowerShell(run).Split('\n').Select(l => l.Trim()).ToArray();
        Assert.Equal("Msi App||False;Quiet App||True;Inno App|/VERYSILENT /SUPPRESSMSGBOXES /NORESTART|True;Nsis App|/S|True", lines[0]);
        // The two nobody knows how to silence were not started (as SYSTEM their window would wait forever), and say so.
        Assert.Equal("2", lines[1]);
        Assert.Equal("2", lines[2]);
    }

    [Fact]
    public void The_switches_of_the_package_go_to_every_exe_uninstaller_and_never_to_an_msi()
    {
        var s = Fakes.Settings();
        s.UninstallExeArgs = "/x /y";
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        string run = FakeToolkit + Helpers(script) + @"
$script:installed = @((New-FakeApp 'Msi App' -Msi), (New-FakeApp 'Quiet App' -Quiet 'unins.exe /quiet'), (New-FakeApp 'Plain App'))
foreach ($app in @($script:installed)) { Remove-J365InstalledCopy -Application $app }
$script:calls -join ';'";
        Assert.Equal("Msi App||False;Quiet App|/x /y|True;Plain App|/x /y|True", RunPowerShell(run));
        // With switches of its own the package does not guess a technology.
        Assert.DoesNotContain("Get-J365SilentSwitches -Application $Application", Block(script, "function Remove-J365InstalledCopy", "function Wait-J365CopiesRemoved"));
    }

    /// <summary>Runs the removal that comes before the installation and tells how it ended.</summary>
    private static string[] RemoveBeforeInstall(PackageSettings s, string apps)
    {
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        string block = Block(script, "    $existingApps = @(Get-J365InstalledCopies)", "    ##====");
        string run = FakeToolkit + Helpers(script) + "\r\n$script:installed = @(" + apps + ")\r\n"
            + "try {\r\n" + block + "\r\n'OK' } catch { 'STOPPED: ' + $_.Exception.Message }\r\n"
            + "$script:calls.Count\r\n@($script:installed).Count\r\n$script:exitCode\r\n$script:log -join ' / '";
        return RunPowerShell(run).Split('\n').Select(l => l.Trim()).ToArray();
    }

    [Fact]
    public void Everything_installed_is_removed_before_the_installation()
    {
        var done = RemoveBeforeInstall(Fakes.Settings(), "(New-FakeApp 'Contoso App 1.0'), (New-FakeApp 'Contoso App 1.1' -Msi)");
        Assert.Equal("OK", done[0]);
        Assert.Equal("2", done[1]);   // both uninstallers ran
        Assert.Equal("0", done[2]);   // nothing is left
        Assert.Contains("Versions already installed, removed before installing: Contoso App 1.0 1.0; Contoso App 1.1 1.0", done[4]);

        var nothing = RemoveBeforeInstall(Fakes.Settings(), "");
        Assert.Equal("OK", nothing[0]);
        Assert.Equal("0", nothing[1]);
        Assert.Contains("nothing to remove", nothing[4]);
    }

    [Fact]
    public void A_copy_that_cannot_be_removed_stops_the_installation()
    {
        // The uninstaller "succeeds" and the program is still there.
        var stays = RemoveBeforeInstall(Fakes.Settings(), "(New-FakeApp 'Contoso App' -Stays)");
        Assert.StartsWith("STOPPED: Still installed after the removal: Contoso App 1.0.", stays[0]);
        Assert.Contains("two versions do not end up side by side", stays[0]);

        // The uninstaller fails: logged, its exit code does not become the exit code of the installation, and what
        // decides is that the program is still installed.
        var fails = RemoveBeforeInstall(Fakes.Settings(), "(New-FakeApp 'Contoso App' -Fails -Stays)");
        Assert.StartsWith("STOPPED: Still installed after the removal", fails[0]);
        Assert.Equal("0", fails[3]);
        Assert.Contains("Could not remove the previous version: The uninstaller returned 1603.", fails[4]);

        // Nobody knows how to silence it: it is not started, and the installation stops as well.
        var s = Fakes.Settings();
        s.UninstallExeArgs = "";
        var unknown = RemoveBeforeInstall(s, "(New-FakeApp 'Contoso App')");
        Assert.StartsWith("STOPPED: Still installed after the removal", unknown[0]);
        Assert.Equal("0", unknown[1]);
    }

    [Fact]
    public void A_package_allowed_to_install_over_goes_on_and_says_so()
    {
        var s = Fakes.Settings();
        s.InstallIfRemovalFails = true;
        var over = RemoveBeforeInstall(s, "(New-FakeApp 'Contoso App' -Fails -Stays)");
        Assert.Equal("OK", over[0]);
        Assert.Equal("1", over[2]);
        Assert.Equal("0", over[3]);
        Assert.Contains("Still installed after the removal (the new version is installed over it): Contoso App 1.0", over[4]);
    }

    [Fact]
    public void The_uninstallation_removes_every_copy_and_fails_when_one_stays()
    {
        var s = Fakes.Settings();
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        string block = Block(script, "    $installedCopies = @(Get-J365InstalledCopies)", "    ## Removes the package mark");
        string Run(string apps) => RunPowerShell(FakeToolkit + Helpers(script) + "\r\n$script:installed = @(" + apps + ")\r\n"
            + "try {\r\n" + block + "\r\n'OK:' + $script:calls.Count } catch { 'FAILED: ' + $_.Exception.Message }");
        Assert.Equal("OK:2", Run("(New-FakeApp 'Contoso App 1.0'), (New-FakeApp 'Contoso App 2.0' -Msi)"));
        Assert.Equal("OK:0", Run(""));
        Assert.StartsWith("FAILED: Still installed after the uninstallation", Run("(New-FakeApp 'Contoso App' -Stays)"));
        // A failed uninstaller fails the uninstallation (nothing swallows it there).
        Assert.Equal("FAILED: The uninstaller returned 1603.", Run("(New-FakeApp 'Contoso App' -Fails)"));
    }

    [Fact]
    public void A_mandatory_removal_without_switches_is_warned_before_generating()
    {
        using var tmp = new TempFolder();
        var s = Fakes.Settings();
        s.InstallerPath = tmp.File("setup.exe", "x");
        string warning = Loc.T("Val_Warn_StrictRemovalNoArgs");
        Assert.DoesNotContain(warning, PackageBuilder.Validate(s).Warnings);     // the package has the switches
        s.UninstallExeArgs = "";
        Assert.Contains(warning, PackageBuilder.Validate(s).Warnings);
        s.InstallIfRemovalFails = true;                                           // allowed to install over
        Assert.DoesNotContain(warning, PackageBuilder.Validate(s).Warnings);
        s.InstallIfRemovalFails = false;
        s.RemovePreviousVersions = false;                                         // nothing is removed
        Assert.DoesNotContain(warning, PackageBuilder.Validate(s).Warnings);
        // An MSI is always removed by its product code.
        var msi = Fakes.Settings("MSI");
        msi.InstallerPath = tmp.File("app.msi", "x");
        msi.UninstallExeArgs = "";
        Assert.DoesNotContain(warning, PackageBuilder.Validate(msi).Warnings);
    }

    [Fact]
    public void New_packages_take_the_removal_policy_of_the_company()
    {
        Assert.False(new AppConfig().NewPackage().InstallIfRemovalFails);
        Assert.True(new AppConfig { InstallIfRemovalFails = true }.NewPackage().InstallIfRemovalFails);
        Assert.True(new AppConfig().NewPackage().RemovePreviousVersions);
    }

    // ------------------------------------------------------------------ what security products look at

    [Fact]
    public void The_release_is_not_a_compressed_executable_and_unblocks_nothing()
    {
        string root = Repo.Root;
        Assert.True(root != null, "Repository root not found from " + AppContext.BaseDirectory);
        string project = File.ReadAllText(Path.Combine(root, "src", "IntunePSApp", "IntunePSApp.csproj"));
        // A compressed single file looks like a packed program to heuristic engines, and starts slower.
        Assert.Contains("<EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>", project);
        // Removing the mark of the web of a downloaded file is what a dropper does; the downloads of this application
        // never carried one (they are verified by signature and hash instead).
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "IntunePSApp"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\")))
            Assert.DoesNotContain("Zone.Identifier", File.ReadAllText(file));
    }
}
