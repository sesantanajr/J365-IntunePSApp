using System;
using System.Linq;
using System.Text.RegularExpressions;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>
/// 3.0.3: "reinstall also where the app is already installed". Every package of an application writes the same mark, so a
/// device where an earlier package installed it counted as installed and a new package never ran there.
/// </summary>
public class Release303Tests
{
    private const string Round = "2026-11-24T13:20:00Z";

    private static PackageSettings Reinstalling()
    {
        var s = Fakes.Settings();
        s.ForceReinstall = true;
        s.ReinstallRound = Round;
        ExistingVersions.Set(s, ExistingVersions.RemoveAll);
        return s;
    }

    [Fact]
    public void Without_the_option_the_mark_of_any_package_of_the_app_counts()
    {
        var s = Fakes.Settings();
        Assert.False(ScriptGenerator.Reinstalls(s));
        string detection = ScriptGenerator.DetectionScript(s);
        Assert.Contains("$RequiredRound = ''", detection);
        Assert.DoesNotContain("ReinstallRound', ", ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe"));
        Ps.AssertParses(detection);
    }

    [Fact]
    public void With_the_option_the_mark_carries_the_round_and_the_detection_asks_for_it()
    {
        var s = Reinstalling();
        Assert.True(ScriptGenerator.Reinstalls(s));
        foreach (var mode in DeployModes.All)
        {
            s.DeployMode = mode;
            string deploy = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            Ps.AssertParses(deploy);
            // Written with the rest of the mark, before the key is closed, on every path that writes the mark.
            int writes = Regex.Matches(deploy, Regex.Escape($"$markKey.SetValue('ReinstallRound', '{Round}')")).Count;
            Assert.Equal(Regex.Matches(deploy, Regex.Escape("$markKey.SetValue('Generator'")).Count, writes);
            int value = deploy.IndexOf("SetValue('ReinstallRound'", StringComparison.Ordinal);
            Assert.True(value > 0 && value < deploy.IndexOf("$markKey.Close()", value, StringComparison.Ordinal));
            Assert.Contains("reinstall round " + Round, deploy);
            // The removal of any version stays as chosen: nothing is kept.
            Assert.DoesNotContain("Test-J365NewerVersion", deploy);
        }
        string detection = ScriptGenerator.DetectionScript(s);
        Ps.AssertParses(detection);
        Assert.True(Ps.IsAscii(detection));
        Assert.Contains($"$RequiredRound = '{Round}'", detection);
        Assert.Contains("GetValue('ReinstallRound')", detection);
        Assert.Contains("if ($RequiredRound -and $round -ne $RequiredRound) { return $false }", detection);
    }

    [Fact]
    public void The_option_needs_the_package_mark()
    {
        using var tmp = new TempFolder();
        var s = Reinstalling();
        s.InstallerPath = tmp.File("setup.exe", "x");
        string warning = Loc.T("Val_Warn_ReinstallNeedsMark");
        Assert.DoesNotContain(warning, PackageBuilder.Validate(s).Warnings);
        s.DetectionRequireMark = false;
        Assert.Contains(warning, PackageBuilder.Validate(s).Warnings);
        Assert.False(ScriptGenerator.Reinstalls(s));
        Assert.DoesNotContain("ReinstallRound', ", ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe"));
        Assert.Contains("$RequiredRound = ''", ScriptGenerator.DetectionScript(s));
    }

    [Fact]
    public void Detection_asks_for_the_mark_of_the_round()
    {
        // User context: the mark lives in HKCU, which a test can write. The rule itself (a file) always matches.
        var s = Reinstalling();
        s.AppName = "J365 Test " + Guid.NewGuid().ToString("N").Substring(0, 8);
        s.InstallBehavior = "User";
        s.DetectionMethod = "File";
        s.DetectionFilePath = @"%SystemRoot%\System32\cmd.exe";
        s.DetectionVersion = "1.0";
        string key = ScriptGenerator.MarkKey(s);
        try
        {
            // The mark an earlier package of the application left: installed, but this package has not reinstalled it yet.
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key)) k.SetValue("Version", "1.0");
            var earlier = PackageBuilder.TestDetection(s);
            Assert.False(earlier.Detected, earlier.Output);
            Assert.True(earlier.NoMark, earlier.Output);
            Assert.Contains(Round, earlier.Output);

            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key)) k.SetValue("ReinstallRound", "2026-11-01T08:00:00Z");
            Assert.False(PackageBuilder.TestDetection(s).Detected);

            // The package ran here in this round: installed, and it stays so (no reinstall loop).
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key)) k.SetValue("ReinstallRound", Round);
            var done = PackageBuilder.TestDetection(s);
            Assert.True(done.Detected, done.Output);
            Assert.StartsWith("Detected:", done.Output);

            // Without the option, the mark of any package counts again.
            s.ForceReinstall = false;
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key)) k.DeleteValue("ReinstallRound", false);
            Assert.True(PackageBuilder.TestDetection(s).Detected);
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, false); }
    }

    [Fact]
    public void A_round_is_the_moment_it_was_asked_for()
    {
        string round = ScriptGenerator.NewReinstallRound();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", round);
        Assert.NotEqual(round, ScriptGenerator.RoundLabel(round));
        Assert.Contains("2026", ScriptGenerator.RoundLabel(Round));
        Assert.Equal("not a date", ScriptGenerator.RoundLabel("not a date"));
        Assert.Equal("", ScriptGenerator.RoundLabel(null));
    }

    [Fact]
    public void The_project_keeps_the_option_and_its_round()
    {
        var copy = Reinstalling().Clone();
        Assert.True(copy.ForceReinstall);
        Assert.Equal(Round, copy.ReinstallRound);
        var plain = new PackageSettings();
        Assert.False(plain.ForceReinstall);
        Assert.Equal("", plain.ReinstallRound);
    }

    private static InstallerInfo Info() => new InstallerInfo
    {
        Path = @"C:\setup.exe", Type = "EXE", AppName = "Contoso", AppVersion = "2.0", InstallArgs = "/S", UninstallPattern = "Contoso*"
    };

    [Fact]
    public void A_batch_reinstalls_with_one_round_for_all_its_packages()
    {
        var cfg = new AppConfig();
        var o = new BatchOptions { ForceReinstall = true, ReinstallRound = Round, ExistingVersions = ExistingVersions.RemoveAll };
        var a = BatchPackager.Settings(Info(), cfg, o);
        var b = BatchPackager.Settings(Info(), cfg, o);
        Assert.True(a.ForceReinstall && b.ForceReinstall);
        Assert.Equal(Round, a.ReinstallRound);
        Assert.Equal(a.ReinstallRound, b.ReinstallRound);
        Assert.True(ScriptGenerator.Reinstalls(a));

        var off = BatchPackager.Settings(Info(), cfg, new BatchOptions());
        Assert.False(off.ForceReinstall);
        Assert.False(ScriptGenerator.Reinstalls(off));

        // Asked without a round (the command line before the batch starts): one is made.
        var fresh = BatchPackager.Settings(Info(), cfg, new BatchOptions { ForceReinstall = true });
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T", fresh.ReinstallRound);
    }

    [Fact]
    public void The_texts_name_the_option_the_same_way_everywhere()
    {
        foreach (var code in new[] { Loc.EnglishCode, Loc.PortugueseCode })
            Lang.With(code, () =>
            {
                string option = Loc.T("Ed_Reinstall");
                Assert.Contains(option, Loc.T("Ed_DetMarkHint", "HKLM\\SOFTWARE\\J365\\IntunePSApp\\Apps\\X"));
                Assert.Contains(option, Loc.T("Val_Warn_ReinstallNeedsMark"));
                Assert.Contains("--reinstall", Loc.T("Cli_BatchUsage"));
                Assert.Contains("24", Loc.T("Ed_ReinstallOn", "24/11/2026 10:20"));
            });
    }
}
