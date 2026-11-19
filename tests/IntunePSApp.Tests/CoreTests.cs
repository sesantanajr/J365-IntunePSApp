using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using J365.IntunePSApp.Core;
using Xunit;

namespace J365.IntunePSApp.Tests
{
    public class VersionUtilTests
    {
        [Theory]
        [InlineData("1.0", "1.0", 0)]
        [InlineData("26.06", "26.06.00.0", 0)]
        [InlineData("v2.0.0", "2.0.0", 0)]
        [InlineData("1.10", "1.9", 1)]
        [InlineData("1.2.3", "1.2.10", -1)]
        [InlineData("2.0", "1.99.99", 1)]
        [InlineData("1.0", "1.0-beta", 1)]
        [InlineData("1.0-alpha", "1.0-beta", -1)]
        [InlineData("131.0.6778.86", "131.0.6778.85", 1)]
        [InlineData("", "1", -1)]
        [InlineData(null, null, 0)]
        public void Compare_orders_versions_like_winget(string a, string b, int expected)
            => Assert.Equal(expected, System.Math.Sign(VersionUtil.Compare(a, b)));
    }

    public class NamesTests
    {
        [Theory]
        [InlineData("7-Zip 26.06 (x64 edition)", "7-Zip")]
        [InlineData("Notepad++ (64-bit x64)", "Notepad++")]
        [InlineData("Google Chrome", "Google Chrome")]
        [InlineData("VLC media player 3.0.21", "VLC media player")]
        [InlineData("", "")]
        public void CleanAppName_removes_version_and_architecture(string input, string expected) => Assert.Equal(expected, Names.CleanAppName(input));

        [Fact]
        public void NamePattern_adds_a_wildcard_and_escapes_brackets()
        {
            Assert.Equal("7-Zip*", Names.NamePattern("7-Zip 26.06 (x64)"));
            Assert.Equal("App*", Names.NamePattern("App [Beta]"));          // trailing bracket text is dropped
            Assert.Equal("`[Beta`] App*", Names.NamePattern("[Beta] App")); // brackets inside the name are literal for -like
            Assert.Equal("", Names.NamePattern(""));
        }

        [Theory]
        [InlineData("7-Zip 26.06", "7-Zip_26.06")]
        [InlineData("a/b:c*d?", "a_b_c_d")]
        [InlineData("   ", "Package")]
        [InlineData(null, "Package")]
        public void Safe_builds_a_valid_file_name(string input, string expected) => Assert.Equal(expected, Names.Safe(input));

        [Fact]
        public void ParseProcessList_cleans_and_removes_duplicates()
            => Assert.Equal(new[] { "chrome", "msedge", "7zFM" }, Names.ParseProcessList("chrome.exe; MSEDGE, \"chrome\"\n7zFM.EXE,,").Select(p => p == "MSEDGE" ? "msedge" : p));

        [Theory]
        [InlineData("26.06.00.0", "26.06")]
        [InlineData("1.0.0", "1.0")]
        [InlineData("1.0.1", "1.0.1")]
        public void TrimVersionZeros_keeps_at_least_two_parts(string input, string expected) => Assert.Equal(expected, Names.TrimVersionZeros(input));

        [Theory]
        [InlineData("version 24.08 (x64)", "24.08")]
        [InlineData("v7", "7")]
        [InlineData("none", "")]
        public void CleanVersion_extracts_the_number(string input, string expected) => Assert.Equal(expected, Names.CleanVersion(input));

        [Theory]
        [InlineData("Google Chrome", "GC")]
        [InlineData("7-Zip", "7Z")]
        [InlineData("Zoom", "ZO")]
        [InlineData("", "?")]
        public void Initials_uses_the_first_two_words(string input, string expected) => Assert.Equal(expected, Names.Initials(input));
    }

    public class InstallerAnalyzerTests
    {
        [Theory]
        [InlineData("7-Zip*", "7-Zip 26.06 (x64)", true)]
        [InlineData("7-Zip*", "PeaZip", false)]
        [InlineData("Google Chrome", "Google Chrome", true)]
        [InlineData("Google Chrome", "Google Chrome Beta", false)]
        [InlineData("App `[Beta`]*", "App [Beta] 2", true)]
        [InlineData("a?c", "abc", true)]
        [InlineData("a.c", "abc", false)]
        public void WildcardToRegex_behaves_like_the_PowerShell_like_operator(string pattern, string text, bool expected)
            => Assert.Equal(expected, InstallerAnalyzer.WildcardToRegex(pattern).IsMatch(text));

        [Theory]
        [InlineData("Nullsoft Install System v3.08", "NSIS")]
        [InlineData("Inno Setup Setup Data (6.2.0)", "Inno Setup")]
        [InlineData("nothing recognisable here", InstallerAnalyzer.TechUnknown)]
        public void DetectTechnology_reads_the_installer_stub(string marker, string expected)
        {
            using var tmp = new TempFolder();
            string file = Path.Combine(tmp.Path, "setup.exe");
            var bytes = new List<byte>(new byte[4096]);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(marker));
            bytes.AddRange(new byte[4096]);
            File.WriteAllBytes(file, bytes.ToArray());
            Assert.Equal(expected, InstallerAnalyzer.DetectTechnology(file, FileVersionInfo.GetVersionInfo(file)));
        }

        [Fact]
        public void ApplyNewVersion_keeps_the_customisations()
        {
            var s = Fakes.Settings();
            s.WelcomeMessage = "Hello";
            s.Actions.Add(new PackageAction { Type = "Wait", Params = { ["Seconds"] = "5" } });
            var info = new InstallerInfo { Path = @"C:\new\setup.exe", Type = "EXE", AppVersion = "2.0", DetectionVersion = "2.0.0", InstallArgs = "/quiet", AppName = "Other name" };
            InstallerAnalyzer.ApplyNewVersion(s, info);
            Assert.Equal(@"C:\new\setup.exe", s.InstallerPath);
            Assert.Equal("2.0", s.AppVersion);
            Assert.Equal("2.0.0", s.DetectionVersion);
            Assert.Equal("Contoso App", s.AppName);      // not replaced
            Assert.Equal("/S", s.InstallArgs);           // user's switches win
            Assert.Equal("Hello", s.WelcomeMessage);
            Assert.Single(s.Actions);
        }
    }

    public class CatalogServiceTests
    {
        private static CatalogApp App() => new CatalogApp
        {
            Id = "Contoso.App", Name = "Contoso App", Publisher = "Contoso", Version = "2.0",
            Installers =
            {
                new CatalogInstaller { Arch = "x86", Type = "inno", Scope = "user" },
                new CatalogInstaller { Arch = "x64", Type = "inno", Scope = "machine" },
                new CatalogInstaller { Arch = "x64", Type = "wix", Scope = "machine", Custom = "ALLUSERS=1", ProductCode = "{11111111-1111-1111-1111-111111111111}" },
                new CatalogInstaller { Arch = "x64", Type = "msix" },
            }
        };

        [Fact]
        public void PickInstaller_prefers_architecture_machine_scope_and_msi()
        {
            var best = CatalogService.PickInstaller(App(), "x64", "en-US");
            Assert.Equal("wix", best.Type);
            Assert.Equal("inno", CatalogService.PickInstaller(App(), "x64", "en-US", preferMsi: false).Type);
            Assert.Equal("x86", CatalogService.PickInstaller(App(), "x86", "en-US").Arch);
            Assert.Null(CatalogService.PickInstaller(new CatalogApp { Installers = { new CatalogInstaller { Type = "msix" } } }));
        }

        [Fact]
        public void SilentArgsFor_uses_the_defaults_of_each_technology()
        {
            Assert.Equal("ALLUSERS=1", CatalogService.SilentArgsFor(new CatalogInstaller { Type = "wix", Custom = "ALLUSERS=1" }));
            Assert.Equal("", CatalogService.SilentArgsFor(new CatalogInstaller { Type = "msi" }));
            Assert.Equal("/S", CatalogService.SilentArgsFor(new CatalogInstaller { Type = "nullsoft" }));
            Assert.Equal("/quiet /norestart", CatalogService.SilentArgsFor(new CatalogInstaller { Type = "burn" }));
            Assert.EndsWith("/ALLUSERS", CatalogService.SilentArgsFor(new CatalogInstaller { Type = "inno", Scope = "machine" }));
            Assert.DoesNotContain("/ALLUSERS", CatalogService.SilentArgsFor(new CatalogInstaller { Type = "inno", Scope = "user" }));
            Assert.Equal("/s /custom", CatalogService.SilentArgsFor(new CatalogInstaller { Type = "exe", Silent = "/s", Custom = "/custom" }));
            Assert.Equal("", CatalogService.SilentArgsFor(null));
        }

        [Fact]
        public void Apply_fills_the_analysis_from_the_database()
        {
            var app = App();
            app.Extra = new CuratedEntry { Processes = new List<string> { "contoso" }, NamePattern = "Contoso*", UninstallArgs = "/S", CheckVersion = false, Notes = "nota", NotesEn = "note" };
            var info = new InstallerInfo { Type = "EXE", Technology = InstallerAnalyzer.TechUnknown, AppVersion = "1.0" };
            CatalogService.Apply(info, app, app.Installers[1], "download");
            Assert.Equal("Contoso.App", info.CatalogId);
            Assert.Equal("2.0", info.CatalogVersion);
            Assert.Equal("2.0", info.AppVersion);
            Assert.Equal("Inno Setup", info.Technology);
            Assert.Equal("Contoso*", info.UninstallPattern);
            Assert.Equal(new[] { "contoso" }, info.Processes);
            Assert.False(info.DetectionCheckVersion);
            Assert.True(info.IsCatalogExact);

            var similar = new InstallerInfo { Type = "EXE", AppVersion = "1.0" };
            CatalogService.Apply(similar, app, null, "name");
            Assert.False(similar.IsCatalogExact);
            Assert.Null(similar.CatalogVersion);
            Assert.Equal("1.0", similar.AppVersion);
        }

        [Fact]
        public void Apply_uses_the_full_version_text_of_the_database_when_it_is_the_same_version()
        {
            var app = App();
            app.Version = "2.8.0";
            var trimmed = new InstallerInfo { Type = "MSI", AppVersion = "2.8" };
            CatalogService.Apply(trimmed, app, app.Installers[2], "productcode");
            Assert.Equal("2.8.0", trimmed.AppVersion);

            var ownScheme = new InstallerInfo { Type = "MSI", AppVersion = "68.0.1" }; // MSI with its own version numbering
            CatalogService.Apply(ownScheme, app, app.Installers[2], "productcode");
            Assert.Equal("68.0.1", ownScheme.AppVersion);
            Assert.Equal("2.8.0", ownScheme.CatalogVersion);
        }

        [Fact]
        public void Dependencies_are_the_common_ones_plus_those_of_the_installer()
        {
            var app = App();
            app.Dependencies = new List<string> { "Common.Runtime" };
            app.Installers[1].Dependencies = new List<string> { "Runtime.x64", "common.runtime" };
            var deps = CatalogService.Instance.DependenciesOf(app, app.Installers[1]);
            Assert.Equal(new[] { "Common.Runtime", "Runtime.x64" }, deps.Select(d => d.Id));
            Assert.All(deps, d => { Assert.Null(d.App); Assert.False(d.CanInclude); }); // not in the (empty) test database
            Assert.Equal(new[] { "Common.Runtime" }, CatalogService.Instance.DependenciesOf(app, app.Installers[0]).Select(d => d.Id));
            Assert.Empty(CatalogService.Instance.DependenciesOf(null, null));
            Assert.Empty(CatalogService.Instance.DependenciesOf("No.Such.App", "x64", "EXE"));
        }

        [Fact]
        public void Curated_notes_follow_the_interface_language()
        {
            var entry = new CuratedEntry { Notes = "nota", NotesEn = "note" };
            Lang.With("en", () => Assert.Equal("note", entry.LocalizedNotes));
            Lang.With("pt-BR", () => Assert.Equal("nota", entry.LocalizedNotes));
            Lang.With("en", () => Assert.Equal("nota", new CuratedEntry { Notes = "nota" }.LocalizedNotes));
        }

        [Fact]
        public void Descriptions_follow_the_interface_language()
        {
            var app = new CatalogApp { Description = "A tool", DescriptionPt = "Uma ferramenta" };
            Lang.With("en", () => Assert.Equal("A tool", app.LocalizedDescription));
            Lang.With("pt-BR", () => Assert.Equal("Uma ferramenta", app.LocalizedDescription));
            Lang.With("pt-BR", () => Assert.Equal("A tool", new CatalogApp { Description = "A tool" }.LocalizedDescription));
        }
    }

    public class IntunePublisherTests
    {
        [Theory]
        [InlineData("contoso.onmicrosoft.com", true)]
        [InlineData("contoso.com", true)]
        [InlineData("6f9619ff-8b86-d011-b42d-00c04fc964ff", true)]
        [InlineData("contoso", false)]
        [InlineData("a b.com", false)]
        [InlineData("x\" -Evil", false)]
        [InlineData("", false)]
        public void IsTenant_accepts_only_guids_and_domains(string value, bool expected) => Assert.Equal(expected, IntunePublisher.IsTenant(value));

        [Fact]
        public void CommandLine_quotes_the_script_and_ids()
        {
            string cmd = IntunePublisher.CommandLine(@"C:\Out\App 1.0\Intune\Publish-ToIntune.ps1", " contoso.com ", "6f9619ff-8b86-d011-b42d-00c04fc964ff", deviceCode: true);
            Assert.Contains("-File \"C:\\Out\\App 1.0\\Intune\\Publish-ToIntune.ps1\"", cmd);
            Assert.Contains("-TenantId \"contoso.com\"", cmd);
            Assert.EndsWith("-DeviceCode", cmd);
            Assert.Throws<System.ArgumentException>(() => IntunePublisher.Launch("x.ps1", "not a tenant", "nope", false));
        }
    }
}
