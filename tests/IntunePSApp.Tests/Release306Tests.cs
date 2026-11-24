using System;
using System.IO;
using System.Text;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>
/// 3.0.6: installations that collide (two apps of the same product, Windows Installer busy) are tried again instead of
/// failing, a copy that cannot be removed ends with a code of its own, and two apps of one product are found by their mark.
/// </summary>
public class Release306Tests
{
    private static string Install(string script)
    {
        int start = script.IndexOf("function Install-ADTDeployment", StringComparison.Ordinal);
        int end = script.IndexOf("function Uninstall-ADTDeployment", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        return script.Substring(start, end - start);
    }

    [Fact]
    public void An_exe_setup_waits_for_Windows_Installer_and_asks_Intune_to_retry_when_it_collides()
    {
        var s = Fakes.Settings();
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Ps.AssertParses(script);
        string install = Install(script);
        Assert.Contains("Start-ADTProcess -FilePath (Join-Path -Path $adtSession.DirFiles -ChildPath 'setup.exe') -ArgumentList '/S'", install);
        Assert.Contains("-WaitForMsiExec", install);
        Assert.Contains("if (Test-J365InstallerBusy -SetupName 'setup.exe') { Exit-J365Deployment -ExitCode 1618", install);
        Assert.Contains("function Test-J365InstallerBusy", script);
        Assert.Contains("Test-ADTMutexAvailability -MutexName 'Global\\_MSIExecute'", script);
        Assert.Contains("function Exit-J365Deployment", script);
    }

    [Fact]
    public void An_msi_is_left_to_the_toolkit_which_waits_for_Windows_Installer_itself()
    {
        var s = Fakes.Settings("MSI");
        string install = Install(ScriptGenerator.DeployScript(Fakes.Template, s, "app.msi"));
        Assert.Contains("Start-ADTMsiProcess -Action 'Install'", install);
        Assert.DoesNotContain("-WaitForMsiExec", install);
        Assert.DoesNotContain("Test-J365InstallerBusy -SetupName 'app.msi') { Exit-J365Deployment -ExitCode 1618 -Message ('The setup failed", install);
    }

    [Fact]
    public void The_codes_of_the_package_are_in_the_range_the_toolkit_leaves_to_a_deployment_script()
    {
        // PSAppDeployToolkit 4: 60000-68999 reserved (60001 = any error), 69000-69999 for Invoke-AppDeployToolkit.ps1.
        Assert.InRange(ScriptGenerator.ExitRemovalFailed, 69000, 69999);
        Assert.InRange(ScriptGenerator.ExitUninstallLeftCopy, 69000, 69999);
        Assert.Equal(1618, ScriptGenerator.ExitRetry);   // "Retry" among the default return codes of Intune
        var s = Fakes.Settings();
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Assert.Contains("Exit-J365Deployment -ExitCode 69001 -Message ('Still installed after the removal: '", Install(script));
        Assert.Contains("Exit-J365Deployment -ExitCode 69002 -Message ('Still installed after the uninstallation", script);
        Assert.DoesNotContain("throw ('Still installed after", script);
    }

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

    /// <summary>The installation of the .exe as generated, with stand-ins of the toolkit: a setup that fails with 2.</summary>
    private static string RunSetup(bool msiBusy)
    {
        string script = ScriptGenerator.DeployScript(Fakes.Template, Fakes.Settings(), "setup.exe");
        int start = script.IndexOf("function Test-J365InstallerBusy", StringComparison.Ordinal);
        int end = script.IndexOf("function Install-ADTDeployment", StringComparison.Ordinal);
        string helpers = script.Substring(start, end - start);
        int from = script.IndexOf("    ## -WaitForMsiExec", StringComparison.Ordinal);
        int to = script.IndexOf("        throw\r\n    }", from, StringComparison.Ordinal) + "        throw\r\n    }".Length;
        string block = script.Substring(from, to - from);
        string code = "$ErrorActionPreference = 'Stop'\r\n$script:exitCode = 0\r\n$script:args = ''\r\n"
            + "$adtSession = [pscustomobject]@{ DirFiles = [System.IO.Path]::GetTempPath() }\r\n"
            + "function Start-ADTProcess { param($FilePath, $ArgumentList, [switch]$WaitForMsiExec, [switch]$WaitForChildProcesses) $script:args = [string]$WaitForMsiExec; $script:exitCode = 2; throw 'The process exited with 2.' }\r\n"
            + $"function Test-ADTMutexAvailability {{ param($MutexName, $MutexWaitTime) {(msiBusy ? "$false" : "$true")} }}\r\n"
            + "function Write-ADTLogEntry { param($Message, $Severity) }\r\n"
            + "function Close-ADTSession { param($ExitCode) $script:exitCode = $ExitCode; throw ('EXIT ' + $ExitCode) }\r\n"
            + helpers + "\r\n"
            + "try {\r\n" + block + "\r\n'OK' } catch { 'STOPPED: ' + $_.Exception.Message }\r\n"
            + "'waited: ' + $script:args\r\n'code: ' + $script:exitCode";
        return RunPowerShell(code);
    }

    [Fact]
    public void A_setup_that_fails_while_Windows_Installer_is_busy_becomes_a_retry()
    {
        var busy = RunSetup(msiBusy: true).Split('\n');
        Assert.Equal("STOPPED: EXIT 1618", busy[0].Trim());
        Assert.Equal("waited: True", busy[1].Trim());
        Assert.Equal("code: 1618", busy[2].Trim());
        // Nothing else installing: the failure stays a failure, with the code of the setup.
        var free = RunSetup(msiBusy: false).Split('\n');
        Assert.Equal("STOPPED: The process exited with 2.", free[0].Trim());
        Assert.Equal("code: 2", free[2].Trim());
    }

    [Fact]
    public void The_notes_of_a_package_name_its_mark_and_find_the_apps_of_the_same_product()
    {
        var s = Fakes.Settings();
        string note = ScriptGenerator.MarkNote(s);
        Assert.Equal("J365 package mark: " + ScriptGenerator.MarkDisplayPath(s), note);
        string notes = "Packaged with J365 IntunePSApp 3.0.6 (PSAppDeployToolkit 4.1.8)\n" + note;
        Assert.Equal(note, ScriptGenerator.MarkNoteIn(notes));
        Assert.Equal(note, ScriptGenerator.MarkNoteIn(notes.Replace("\n", "\r\n")));
        Assert.Null(ScriptGenerator.MarkNoteIn("Packaged with J365 IntunePSApp"));   // published before 3.0.6
        Assert.Null(ScriptGenerator.MarkNoteIn(null));
        Assert.True(IntuneConnection.HasMarkNote(notes, note));
        Assert.True(IntuneConnection.HasMarkNote(notes.ToUpperInvariant(), note));
        Assert.False(IntuneConnection.HasMarkNote(notes, "J365 package mark: HKLM\\SOFTWARE\\J365\\IntunePSApp\\Apps\\Other"));
        Assert.False(IntuneConnection.HasMarkNote(notes, null));
    }

    [Fact]
    public void The_publishing_script_looks_for_an_app_of_the_same_product_under_another_name()
    {
        string script = PackageBuilder.EmbeddedText("Publish-ToIntune.ps1");
        Ps.AssertParses(script);
        Assert.True(Ps.IsAscii(script));
        Assert.Contains("function Find-AppsWithMark", script);
        Assert.Contains("Where-Object { $_ -like 'J365 package mark: *' }", script);
        Assert.Contains("$existing = @(Find-AppsWithMark $markLine)", script);
        Assert.Contains("Another app of this product will exist", script);
    }

    [Theory]
    [InlineData("0x8007EA61", "AppStatus_Err60001")]
    [InlineData("0x80070D89", "AppStatus_Err69001")]
    [InlineData("69001", "AppStatus_Err69001")]
    [InlineData("69002", "AppStatus_Err69002")]
    [InlineData("0x80070002", "AppStatus_ErrExit2")]
    [InlineData("0x80070652", "AppStatus_Err1618")]
    public void The_status_window_explains_the_codes_of_a_failed_installation(string code, string key)
        => Assert.Equal(Loc.T(key), IntuneAppStatus.ErrorHint(IntuneAppStatus.HexCode(code)));
}
