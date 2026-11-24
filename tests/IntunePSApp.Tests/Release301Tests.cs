using System.Linq;
using System.Text.Json;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>3.0.1: "remove any installed version and install again" as one choice everywhere, and counts in the singular.</summary>
public class Release301Tests
{
    private static string T(string key) => Loc.T(key);

    [Theory]
    [InlineData(true, false, "RemoveAll")]
    [InlineData(true, true, "KeepNewer")]
    [InlineData(false, true, "InstallOver")]
    [InlineData(false, false, "InstallOver")]
    public void One_choice_stands_for_the_two_switches_of_a_package(bool removePrevious, bool keepNewer, string expected)
    {
        var s = new PackageSettings { RemovePreviousVersions = removePrevious, KeepNewerVersion = keepNewer };
        Assert.Equal(expected, ExistingVersions.Of(s));
        // Showing a project and collecting it again changes nothing (installing over keeps "keep newer" as it was).
        var again = s.Clone();
        ExistingVersions.Set(again, ExistingVersions.Of(s));
        Assert.Equal(s.RemovePreviousVersions, again.RemovePreviousVersions);
        Assert.Equal(s.KeepNewerVersion, again.KeepNewerVersion);
    }

    [Fact]
    public void Remove_any_version_removes_the_same_and_newer_versions_and_is_a_company_default()
    {
        var s = new PackageSettings();
        ExistingVersions.Set(s, "removeall");                          // any case, as typed on the command line
        Assert.True(s.RemovePreviousVersions);
        Assert.False(s.KeepNewerVersion);
        Assert.Null(ExistingVersions.Normalize("everything"));

        var cfg = new AppConfig();
        Assert.Equal(ExistingVersions.KeepNewer, ExistingVersions.Of(cfg)); // the default of 3.0 stays
        ExistingVersions.Set(cfg, ExistingVersions.RemoveAll);
        var package = cfg.NewPackage();
        Assert.Equal(ExistingVersions.RemoveAll, ExistingVersions.Of(package));
    }

    [Fact]
    public void A_package_that_removes_any_version_has_no_kept_newer_copy()
    {
        var s = Fakes.Settings();
        ExistingVersions.Set(s, ExistingVersions.KeepNewer);
        string keeps = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Assert.Contains("Test-J365NewerVersion", keeps);
        ExistingVersions.Set(s, ExistingVersions.RemoveAll);
        foreach (var mode in DeployModes.All)
        {
            s.DeployMode = mode;                                         // silent or with notification
            string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
            Assert.DoesNotContain("Test-J365NewerVersion", script);
            Assert.Contains("Versions already installed, removed before installing", script);
            Ps.AssertParses(script);
        }
    }

    private static InstallerInfo Info(bool? removePrevious = null, bool neverRemove = false)
        => new InstallerInfo { Path = @"C:\setup.exe", Type = "EXE", AppName = "Contoso", AppVersion = "2.0", InstallArgs = "/S", UninstallPattern = "Contoso*", RemovePrevious = removePrevious, NeverRemove = neverRemove };

    [Fact]
    public void The_database_protects_shared_components_and_yields_to_remove_any_version()
    {
        var removeAll = new AppConfig();
        ExistingVersions.Set(removeAll, ExistingVersions.RemoveAll);

        // A shared component or a part of Windows (Visual C++ runtime, .NET, WebView2, Edge): never removed.
        var shared = removeAll.NewPackage();
        var sharedInfo = Info(removePrevious: false, neverRemove: true);
        InstallerAnalyzer.ApplyTo(shared, sharedInfo);
        Assert.False(shared.RemovePreviousVersions);
        Assert.Contains(T("Analysis_NeverRemove"), sharedInfo.Notes);

        // An application the database advises not to remove (a browser that updates itself): "remove any version" wins, said.
        var browser = removeAll.NewPackage();
        var browserInfo = Info(removePrevious: false);
        InstallerAnalyzer.ApplyTo(browser, browserInfo);
        Assert.Equal(ExistingVersions.RemoveAll, ExistingVersions.Of(browser));
        Assert.Contains(T("Analysis_RemoveAllOverDatabase"), browserInfo.Notes);

        // With the default of 3.0 the advice of the database stands.
        var advised = new AppConfig().NewPackage();
        InstallerAnalyzer.ApplyTo(advised, Info(removePrevious: false));
        Assert.False(advised.RemovePreviousVersions);
        var plain = new AppConfig().NewPackage();
        InstallerAnalyzer.ApplyTo(plain, Info());
        Assert.Equal(ExistingVersions.KeepNewer, ExistingVersions.Of(plain));
    }

    [Fact]
    public void The_batch_choice_applies_to_every_package_with_the_same_rules()
    {
        var cfg = new AppConfig();
        var any = BatchPackager.Settings(Info(removePrevious: false), cfg, new BatchOptions { ExistingVersions = ExistingVersions.RemoveAll });
        Assert.Equal(ExistingVersions.RemoveAll, ExistingVersions.Of(any));
        var shared = BatchPackager.Settings(Info(neverRemove: true), cfg, new BatchOptions { ExistingVersions = ExistingVersions.RemoveAll });
        Assert.False(shared.RemovePreviousVersions);
        var over = BatchPackager.Settings(Info(), cfg, new BatchOptions { ExistingVersions = ExistingVersions.InstallOver });
        Assert.Equal(ExistingVersions.InstallOver, ExistingVersions.Of(over));
        var asCompany = BatchPackager.Settings(Info(), cfg, new BatchOptions());
        Assert.Equal(ExistingVersions.KeepNewer, ExistingVersions.Of(asCompany));
    }

    [Fact]
    public void The_database_marks_shared_components_as_never_removed()
    {
        using var stream = typeof(PackageBuilder).Assembly.GetManifestResourceStream("curated.json");
        var curated = JsonSerializer.Deserialize<CuratedFile>(stream, Json.Options).Apps;
        foreach (var id in new[] { "Microsoft.Edge", "Microsoft.EdgeWebView2Runtime", "Microsoft.VCRedist.2015+.x64", "Microsoft.DotNet.DesktopRuntime.10" })
            Assert.True(curated[id].NeverRemove == true, id);
        foreach (var id in new[] { "Google.Chrome", "Adobe.Acrobat.Reader.64-bit", "7zip.7zip" })
            Assert.NotEqual(true, curated[id].NeverRemove);

        var app = new CatalogApp { Id = "Microsoft.Edge", Name = "Microsoft Edge", Version = "1.0", Extra = curated["Microsoft.Edge"] };
        var info = new InstallerInfo { Type = "MSI", AppName = "Microsoft Edge" };
        CatalogService.Apply(info, app, new CatalogInstaller { Type = "wix", Arch = "x64" }, "download");
        Assert.True(info.NeverRemove);
        Assert.False(info.RemovePrevious);
    }

    [Theory]
    [InlineData("Batch_Start")]
    [InlineData("Batch_StartPublish")]
    [InlineData("Batch_StartStore")]
    [InlineData("Batch_Heading")]
    [InlineData("Batch_HeadingStore")]
    [InlineData("Batch_SummaryReady")]
    [InlineData("Catalog_BatchCount")]
    public void Counts_of_one_are_said_in_the_singular(string key)
    {
        foreach (var code in new[] { Loc.EnglishCode, Loc.PortugueseCode })
            Lang.With(code, () =>
            {
                Assert.Equal(Loc.T(key + "_One"), Loc.Count(key, 1));
                Assert.Equal(Loc.T(key, 3), Loc.Count(key, 3));
                Assert.Contains("3", Loc.Count(key, 3));
            });
        Lang.With(Loc.PortugueseCode, () =>
        {
            Assert.Equal("Gerar 1 pacote", Loc.Count("Batch_Start", 1));
            Assert.Equal("1 aplicativo selecionado", Loc.Count("Catalog_BatchCount", 1));
        });
    }
}
