using System;
using System.IO;
using System.Text;
using System.Text.Json;
using J365.IntunePSApp.Core;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>
/// 2.7.0, from the logs of a real deployment (Adobe Acrobat Reader 26.003.22110 through Intune): the device already had
/// that same version, installed some other way; the package removed it (4 minutes) and installed it again (5 minutes)
/// after asking the user to close it. The same version of the same product is now kept and only marked.
/// </summary>
public class Release27PackagingTests
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

    private static PackageSettings Acrobat()
    {
        var s = Fakes.Settings("EXE");
        s.AppName = "Adobe Acrobat Reader";
        s.AppVendor = "Adobe";
        s.AppVersion = "26.003.22110";
        s.DetectionVersion = "26.003.22110";
        s.AppArch = "x64";
        s.UninstallPattern = "Adobe Acrobat (64-bit)";
        s.DetectionNamePattern = "Adobe Acrobat (64-bit)";
        s.CatalogId = "Adobe.Acrobat.Reader.64-bit";
        s.MsiProductCode = "{AC76BA86-1033-FF00-7760-BC15014EA700}";
        s.UninstallExeArgs = "";
        return s;
    }

    /// <summary>
    /// Runs the "kept" block of the generated script against fake installed applications (what Get-ADTApplication would
    /// return) and gives how many were kept and why ("newer", "same" or "none").
    /// </summary>
    private static string Kept(PackageSettings s, params string[] apps)
    {
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        int start = script.IndexOf("    function Test-J365NewerVersion", StringComparison.Ordinal);
        int end = script.IndexOf("    if ($keptApps.Count -gt 0)", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "The kept block is generated.");
        var fake = new StringBuilder("function Get-ADTApplication { param($Name, $NameMatch) @(\r\n");
        foreach (var a in apps)
        {
            // "DisplayVersion|ProductCode|Is64BitApplication"
            var p = a.Split('|');
            fake.Append($"  [pscustomobject]@{{ DisplayName = 'Adobe Acrobat (64-bit)'; DisplayVersion = '{p[0]}'; ProductCode = {(p[1].Length == 0 ? "$null" : $"[guid]'{p[1]}'")}; Is64BitApplication = ${p[2]} }}\r\n");
        }
        fake.Append(") }\r\n");
        string run = fake + script.Substring(start, end - start)
            + "\r\nif ($keptApps.Count -eq 0) { 'none' } elseif ($keptReason -like 'The same*') { 'same:' + $keptApps.Count } else { 'newer:' + $keptApps.Count }";
        return RunPowerShell(run);
    }

    [Fact]
    public void The_same_version_of_the_same_msi_product_is_kept_and_marked()
    {
        var s = Acrobat();
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Ps.AssertParses(script);
        Assert.Contains("$keptReason = 'The same version is already installed and is kept", script);
        // The block that keeps it writes the mark and stops before the welcome dialog and the removal.
        int same = script.IndexOf("-OrSame)", StringComparison.Ordinal);
        Assert.True(same > 0 && same < script.IndexOf("Show-ADTInstallationWelcome", StringComparison.Ordinal));
        Assert.True(same < script.IndexOf("Uninstall-ADTApplication", StringComparison.Ordinal));

        // The device of the log: the same version with the same product code.
        Assert.Equal("same:1", Kept(s, "26.003.22110|ac76ba86-1033-ff00-7760-bc15014ea700|true"));
        // A newer one is still kept as newer; an older one is replaced.
        Assert.Equal("newer:1", Kept(s, "26.004.20000|ac76ba86-1033-ff00-7760-bc15014ea700|true"));
        Assert.Equal("none", Kept(s, "25.001.20000|ac76ba86-1033-ff00-7760-bc15014ea700|true"));
        // The same version of another product (another edition or architecture: another product code) is replaced.
        Assert.Equal("none", Kept(s, "26.003.22110|ac76ba86-1033-ffff-7760-bc15014ea700|true"));
        Assert.Equal("none", Kept(s, "26.003.22110||true"));
    }

    [Fact]
    public void Without_a_product_code_the_same_version_must_have_the_same_architecture()
    {
        var s = Acrobat();
        s.MsiProductCode = "";
        Assert.Equal("same:1", Kept(s, "26.003.22110||true"));
        Assert.Equal("none", Kept(s, "26.003.22110||false"));    // an x86 copy is replaced by the x64 package

        s.AppArch = "x86";
        Assert.Equal("same:1", Kept(s, "26.003.22110||false"));
        Assert.Equal("none", Kept(s, "26.003.22110||true"));

        s.AppArch = "";                                             // a package for any architecture
        Assert.Equal("same:1", Kept(s, "26.003.22110||true"));
    }

    [Fact]
    public void The_same_version_is_installed_again_when_it_cannot_be_told_apart_or_when_asked()
    {
        var s = Acrobat();
        s.MsiProductCode = "";
        s.AppArch = "arm64";                                        // an x64 copy also registers as 64-bit on ARM64
        Assert.Null(ScriptGenerator.SameProductCondition(s));
        Assert.Equal("none", Kept(s, "26.003.22110||true"));

        s = Acrobat();
        s.KeepSameVersion = false;                                  // repair: install it again
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Assert.DoesNotContain("-OrSame)", script);
        Assert.Contains("Test-J365NewerVersion -Installed", script);
        Assert.Equal("none", Kept(s, "26.003.22110|ac76ba86-1033-ff00-7760-bc15014ea700|true"));

        s.KeepNewerVersion = false;                                 // neither: nothing is kept
        Assert.DoesNotContain("Test-J365NewerVersion", ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe"));
    }

    [Fact]
    public void A_second_build_of_the_same_package_gets_a_short_unique_folder()
    {
        var now = new DateTime(2026, 11, 23, 8, 6, 10);
        Assert.Equal("Adobe_Acrobat_Reader_26.003.22110", PackageDirectory.UniqueName("Adobe_Acrobat_Reader_26.003.22110", 0, now));
        Assert.Equal("Adobe_Acrobat_Reader_26.003.22110_20261123-080610", PackageDirectory.UniqueName("Adobe_Acrobat_Reader_26.003.22110", 1, now));
        Assert.Equal("Adobe_Acrobat_Reader_26.003.22110_20261123-080610-7", PackageDirectory.UniqueName("Adobe_Acrobat_Reader_26.003.22110", 7, now));
        // The folder of the log was 50 characters longer than the name; now at most 19.
        Assert.True(PackageDirectory.UniqueName("x", 99, now).Length - 1 <= 19);

        using var tmp = new TempFolder();
        using var first = PackageDirectory.Reserve(tmp.Path, "Contoso App 1.0");
        Directory.CreateDirectory(Path.Combine(tmp.Path, "Contoso_App_1.0_keep"));
        using var second = PackageDirectory.Reserve(tmp.Path, "Contoso App 1.0");
        Assert.Equal("Contoso_App_1.0", Path.GetFileName(first.Folder));
        Assert.Matches(@"^Contoso_App_1\.0_\d{8}-\d{6}(-\d+)?$", Path.GetFileName(second.Folder));
    }

    [Fact]
    public void A_package_with_a_path_too_long_for_intunewinapputil_goes_to_the_built_in_packager()
    {
        using var tmp = new TempFolder();
        string package = Path.Combine(tmp.Path, "Package");
        string deep = Path.Combine(package, "PSAppDeployToolkit", "Frontend", "v4", "PSAppDeployToolkit.Extensions");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "PSAppDeployToolkit.Extensions.psd1"), "@{}");
        string outFolder = Path.Combine(tmp.Path, "Intune", "_iwau_12345678");
        int longest = IntuneWinBuilder.LongestPath(package, outFolder);
        Assert.Equal(Math.Max(Path.Combine(deep, "PSAppDeployToolkit.Extensions.psd1").Length, Path.Combine(outFolder, "Invoke-AppDeployToolkit.intunewin").Length), longest);
        Assert.True(longest < IntuneWinBuilder.MaxToolPath);

        // A folder like the one of the log (OneDrive of a company, second build of the same package): refused before the
        // tool spends minutes on it.
        string logPath = @"C:\Users\firstname.lastname\OneDrive - Contoso Corporation Brasil S.A\Documents\J365 IntunePSApp\Adobe_Acrobat_Reader_26.003.22110_20261123-080610_354008b2414347409536fd8fff97ce44\Package\PSAppDeployToolkit\Frontend\v4\PSAppDeployToolkit.Extensions\PSAppDeployToolkit.Extensions.psd1";
        Assert.True(logPath.Length > IntuneWinBuilder.MaxToolPath);
        Assert.True(logPath.Replace("_354008b2414347409536fd8fff97ce44", "").Length <= IntuneWinBuilder.MaxToolPath, "The 2.7 folder name keeps it under the limit.");
    }

    [Fact]
    public void A_packages_folder_inside_onedrive_is_recognised()
    {
        string[] names = { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" };
        var before = Array.ConvertAll(names, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("OneDriveCommercial", @"C:\Users\someone\OneDrive - Contoso");
            Environment.SetEnvironmentVariable("OneDriveConsumer", null);
            Environment.SetEnvironmentVariable("OneDrive", @"C:\Users\someone\OneDrive - Contoso\");
            Assert.Equal(@"C:\Users\someone\OneDrive - Contoso", AppPaths.OneDriveRoot(@"C:\Users\someone\OneDrive - Contoso\Documents\J365 IntunePSApp"));
            Assert.Equal(@"C:\Users\someone\OneDrive - Contoso", AppPaths.OneDriveRoot(@"c:\users\someone\onedrive - contoso\"));
            // A folder whose name only starts like it is not inside it.
            Assert.Null(AppPaths.OneDriveRoot(@"C:\Users\someone\OneDrive - Contoso Ltd\Documents"));
            Assert.Null(AppPaths.OneDriveRoot(@"C:\J365\Packages"));
            Assert.Null(AppPaths.OneDriveRoot("  "));
            Assert.Null(AppPaths.OneDriveRoot(null));
        }
        finally
        {
            for (int i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], before[i]);
        }
    }

    [Fact]
    public void Keeping_the_same_version_by_default_changes_no_saved_project_nor_its_build_record()
    {
        var s = Acrobat();
        Assert.Null(s.KeepSameVersion);
        string json = JsonSerializer.Serialize(s, Json.Options);
        Assert.DoesNotContain("KeepSameVersion", json);
        string before = PackageRevision.Fingerprint(s);
        s.KeepSameVersion = false;
        Assert.Contains("\"KeepSameVersion\": false", JsonSerializer.Serialize(s, Json.Options));
        Assert.NotEqual(before, PackageRevision.Fingerprint(s));
        Assert.False(JsonSerializer.Deserialize<PackageSettings>(JsonSerializer.Serialize(s, Json.Options), Json.Options).KeepSameVersion);
    }
}
