using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using J365.IntunePSApp.Core;
using Xunit;

namespace J365.IntunePSApp.Tests
{
    public class IntuneWinBuilderTests
    {
        private static string SampleSource(TempFolder tmp)
        {
            string src = Path.Combine(tmp.Path, "src");
            Directory.CreateDirectory(Path.Combine(src, "Files"));
            Directory.CreateDirectory(Path.Combine(src, "SupportFiles")); // empty folder must survive
            File.WriteAllText(Path.Combine(src, IntuneWinBuilder.SetupFile), "launcher");
            File.WriteAllBytes(Path.Combine(src, "Files", "setup.bin"), Enumerable.Range(0, 300000).Select(i => (byte)(i * 31)).ToArray());
            File.WriteAllText(Path.Combine(src, "Files", "readme.txt"), "olá");
            return src;
        }

        [Fact]
        public void BuildNative_creates_a_package_that_validates()
        {
            using var tmp = new TempFolder();
            string src = SampleSource(tmp), output = Path.Combine(tmp.Path, "out", "app.intunewin");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            IntuneWinBuilder.BuildNative(src, IntuneWinBuilder.SetupFile, output);

            var result = IntuneWinBuilder.Validate(output);
            Assert.True(result.Valid);
            Assert.Equal(IntuneWinBuilder.SetupFile, result.SetupFile);
            Assert.Equal(4, result.FileCount); // 3 files + 1 empty folder entry

            using var zip = ZipFile.OpenRead(output);
            Assert.Equal(new[] { "IntuneWinPackage/Contents/IntunePackage.intunewin", "IntuneWinPackage/Metadata/Detection.xml" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n));
            using var reader = new StreamReader(zip.GetEntry("IntuneWinPackage/Metadata/Detection.xml").Open());
            string xml = reader.ReadToEnd();
            Assert.Contains("<FileDigestAlgorithm>SHA256</FileDigestAlgorithm>", xml);
            Assert.Contains("<ProfileIdentifier>ProfileVersion1</ProfileIdentifier>", xml);
            Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(output))); // work folder removed
        }

        [Fact]
        public void Validate_detects_a_tampered_package()
        {
            using var tmp = new TempFolder();
            string src = SampleSource(tmp), output = Path.Combine(tmp.Path, "app.intunewin");
            IntuneWinBuilder.BuildNative(src, IntuneWinBuilder.SetupFile, output);
            var bytes = File.ReadAllBytes(output);
            bytes[bytes.Length / 2] ^= 0xFF; // flips one byte of the (stored, not compressed) encrypted content
            string broken = Path.Combine(tmp.Path, "broken.intunewin");
            File.WriteAllBytes(broken, bytes);
            bool valid;
            try { valid = IntuneWinBuilder.Validate(broken).Valid; }
            catch { valid = false; }
            Assert.False(valid);
        }
    }

    public class PackageBuilderTests
    {
        [Fact]
        public void Validate_lists_what_is_missing_in_an_empty_package()
        {
            var v = PackageBuilder.Validate(new PackageSettings());
            Assert.False(v.IsValid);
            Assert.True(v.Errors.Count >= 4);
        }

        [Fact]
        public void Validate_accepts_a_complete_package_and_warns_about_exe_switches()
        {
            using var tmp = new TempFolder();
            var s = Fakes.Settings();
            s.InstallerPath = tmp.File(@"installer\setup.exe", "x");
            Assert.True(PackageBuilder.Validate(s).IsValid, string.Join("\n", PackageBuilder.Validate(s).Errors));
            s.InstallArgs = "";
            s.UninstallExeArgs = "";
            var v = PackageBuilder.Validate(s);
            Assert.True(v.IsValid);
            Assert.Equal(2, v.Warnings.Count);
        }

        [Fact]
        public void Validate_catches_the_common_mistakes()
        {
            using var tmp = new TempFolder();
            var s = Fakes.Settings();
            s.InstallerPath = tmp.File(@"installer\setup.exe", "x");

            s.UninstallPattern = "*a*";
            Assert.False(PackageBuilder.Validate(s).IsValid);
            s.UninstallPattern = "Contoso App*";

            s.ProcessesToClose.Add(@"C:\bad\path.exe");
            Assert.False(PackageBuilder.Validate(s).IsValid);
            s.ProcessesToClose.RemoveAt(s.ProcessesToClose.Count - 1);

            s.AccentColor = "blue";
            Assert.False(PackageBuilder.Validate(s).IsValid);
            s.AccentColor = "#0A5CA8";

            s.DetectionVersion = "latest";
            Assert.False(PackageBuilder.Validate(s).IsValid);
            s.DetectionVersion = "1.2.3";

            // Two extra files with the same name would overwrite each other inside SupportFiles.
            s.ExtraFiles.Add(tmp.File(@"a\config.json", "{}"));
            s.ExtraFiles.Add(tmp.File(@"b\config.json", "{}"));
            Assert.False(PackageBuilder.Validate(s).IsValid);
            s.ExtraFiles.RemoveAt(1);

            // An action that points to a file that is not shipped with the package.
            s.Actions.Add(new PackageAction { Type = "CopyFile", Params = { ["Source"] = "licence.key", ["Destination"] = @"%ProgramData%\Contoso" } });
            Assert.False(PackageBuilder.Validate(s).IsValid);
            s.Actions[0].Params["Source"] = "config.json";
            Assert.True(PackageBuilder.Validate(s).IsValid, string.Join("\n", PackageBuilder.Validate(s).Errors));

            // Detection by a per-user path while installing as System only warns.
            s.DetectionMethod = "File";
            s.DetectionFilePath = @"%LOCALAPPDATA%\Contoso\app.exe";
            var v = PackageBuilder.Validate(s);
            Assert.True(v.IsValid);
            Assert.Contains(v.Warnings, w => w.Contains("%ProgramFiles%"));
        }

        [Fact]
        public void Including_the_installer_folder_is_refused_for_shared_folders()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Assert.True(PackageBuilder.IsSharedFolder(Path.Combine(profile, "Downloads")));
            Assert.True(PackageBuilder.IsSharedFolder(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
            Assert.True(PackageBuilder.IsSharedFolder(Path.GetPathRoot(Environment.SystemDirectory)));
            Assert.True(PackageBuilder.IsSharedFolder(Path.GetTempPath()));
            Assert.True(PackageBuilder.IsSharedFolder(""));
            using var tmp = new TempFolder();
            Assert.False(PackageBuilder.IsSharedFolder(tmp.Path));

            var s = Fakes.Settings();
            s.IncludeInstallerFolder = true;
            s.InstallerPath = tmp.File(@"own-folder\setup.exe", "x");
            Assert.True(PackageBuilder.Validate(s).IsValid);
        }

        [Fact]
        public void Projects_saved_by_version_1_are_migrated_when_loaded()
        {
            using var tmp = new TempFolder();
            string file = tmp.File("projeto.json", @"{
  ""InstallerPath"": ""C:\\Installers\\setup.exe"",
  ""AppName"": ""Contoso App"", ""AppVersion"": ""1.0"", ""UninstallPattern"": ""Contoso App*"",
  ""Actions"": [
    { ""Type"": ""CreateShortcut"", ""Phase"": ""PostInstall"", ""ContinueOnError"": true, ""Params"": { ""Location"": ""Área de trabalho (todos os usuários)"", ""Name"": ""App"", ""Target"": ""C:\\App\\app.exe"" } },
    { ""Type"": ""StopService"", ""Phase"": ""PostInstall"", ""Params"": { ""Name"": ""svc"", ""Mode"": ""Parar e desativar"" } }
  ]
}");
            var s = PackageBuilder.LoadProject(file);
            Assert.Equal(PackageSettings.CurrentSchema, s.SchemaVersion);
            Assert.Equal("EXE", s.InstallerType);
            Assert.Equal("W10_1607", s.MinimumOs);
            Assert.Equal("Desktop", s.Actions[0].P("Location"));
            Assert.Equal("StopDisable", s.Actions[1].P("Mode"));
            Assert.NotNull(s.ExtraFiles);
            Assert.Equal("", s.CatalogVersion);
        }

        [Fact]
        public void Settings_survive_a_save_and_load_round_trip()
        {
            using var tmp = new TempFolder();
            var s = Fakes.Settings();
            s.CatalogId = "Contoso.App"; s.CatalogVersion = "1.2.3"; s.WelcomeMessage = "Olá, \"mundo\"";
            s.Actions.Add(new PackageAction { Type = "Wait", Params = { ["Seconds"] = "5" } });
            string file = Path.Combine(tmp.Path, PackageLayout.ProjectFile);
            Json.Save(file, s);
            var loaded = PackageBuilder.LoadProject(file);
            Assert.Equal(JsonSerializer.Serialize(s, Json.Options).Replace("\"SchemaVersion\": 0", "\"SchemaVersion\": 2"), JsonSerializer.Serialize(loaded, Json.Options));
            Assert.False(File.Exists(file + ".tmp"));
        }

        [Fact]
        public void Layout_helpers_find_current_and_legacy_names()
        {
            using var tmp = new TempFolder();
            string v2 = Path.Combine(tmp.Path, "v2"), v1 = Path.Combine(tmp.Path, "v1");
            foreach (var f in new[] { @"v2\project.json", @"v2\Intune\App.intunewin", @"v2\Intune\Detection_App.ps1", @"v2\Intune\Intune-Instructions.html", @"v2\Intune\AppIcon.png", @"v2\Package\Invoke-AppDeployToolkit.exe",
                                      @"v1\projeto.json", @"v1\Intune\App.intunewin", @"v1\Intune\Deteccao_App.ps1", @"v1\Intune\INSTRUCOES_INTUNE.html", @"v1\Intune\Icone_App.png", @"v1\Pacote_PSADT\Invoke-AppDeployToolkit.exe" })
                tmp.File(f, "{}");
            foreach (var dir in new[] { v2, v1 })
            {
                Assert.NotNull(PackageLayout.FindProject(dir));
                Assert.NotNull(PackageLayout.FindIntuneWin(dir));
                Assert.NotNull(PackageLayout.FindDetection(dir));
                Assert.NotNull(PackageLayout.FindInstructions(dir));
                Assert.NotNull(PackageLayout.FindIcon(dir));
                Assert.NotNull(PackageLayout.FindPackageDir(dir));
            }
            Assert.Null(PackageLayout.FindProject(tmp.Path));
            Assert.Null(PackageLayout.FindIntuneWin(tmp.Path));

            var history = PackageBuilder.History(tmp.Path);
            Assert.Equal(2, history.Count);
            var result = PackageBuilder.TryLoadResult(Path.Combine(v1, "projeto.json"), Fakes.Settings());
            Assert.NotNull(result);
            Assert.EndsWith("Pacote_PSADT", result.PsadtFolder);
            Assert.Null(result.PublishScript); // packages made by 1.x have no publish script
        }

        [Theory]
        [InlineData("W10_22H2", "Windows 10 22H2")]
        [InlineData("W11_21H2", "Windows 11 21H2")]
        [InlineData("W10_1607", "Windows 10 1607")]
        public void MinimumOsLabel_is_readable(string code, string expected) => Assert.Equal(expected, PackageBuilder.MinimumOsLabel(code));

        [Theory]
        [InlineData("x64", "x64")]
        [InlineData("arm64", "arm64")]
        [InlineData("x86", "x64x86")]
        [InlineData("", "x64x86")]
        public void IntuneArchitecture_maps_the_package_architecture(string arch, string expected)
        {
            var s = Fakes.Settings();
            s.AppArch = arch;
            Assert.Equal(expected, PackageBuilder.IntuneArchitecture(s));
        }

        [Fact]
        public void NewerCatalogVersion_is_null_without_a_database_match()
        {
            var s = Fakes.Settings();
            s.CatalogId = "No.Such.Package";
            Assert.Null(PackageBuilder.NewerCatalogVersion(s));
        }
    }

    public class SummaryAndSandboxTests
    {
        private static (PackageSettings Settings, BuildResult Result) Sample(TempFolder tmp)
        {
            var s = Fakes.Settings("MSI");
            s.MsiProductCode = "{6F9619FF-8B86-D011-B42D-00C04FC964FF}";
            s.Homepage = "https://contoso.example";
            s.CompanyName = "Contoso <Ltd>";
            var r = new BuildResult
            {
                PackageFolder = tmp.Path,
                IntuneFolder = Path.Combine(tmp.Path, "Intune"),
                IntuneWinFile = Path.Combine(tmp.Path, "Intune", "Contoso_App_1.2.3.intunewin"),
                DetectionScript = Path.Combine(tmp.Path, "Intune", "Detection_Contoso_App_1.2.3.ps1"),
                PsadtVersion = "4.1.8"
            };
            Directory.CreateDirectory(r.IntuneFolder);
            return (s, r);
        }

        [Theory]
        [InlineData("en", "Install command", "Windows 10 22H2")]
        [InlineData("pt-BR", "Comando de instalação", "Windows 10 22H2")]
        public void Instructions_are_written_in_the_interface_language(string lang, string expectedField, string expectedOs)
        {
            using var tmp = new TempFolder();
            var (s, r) = Sample(tmp);
            Lang.With(lang, () =>
            {
                var (html, txt) = SummaryWriter.Write(s, r, r.IntuneFolder);
                string h = File.ReadAllText(html), t = File.ReadAllText(txt);
                Assert.Contains($"<html lang=\"{lang}\">", h);
                Assert.Contains(expectedField, h);
                Assert.Contains(expectedField, t);
                Assert.Contains(expectedOs, t);
                Assert.Contains("Invoke-AppDeployToolkit.exe -DeploymentType Install", t);
                Assert.Contains("Contoso &lt;Ltd&gt;", h);          // HTML is encoded
                Assert.DoesNotContain("Contoso <Ltd>", h);
                Assert.Contains("{6F9619FF-8B86-D011-B42D-00C04FC964FF}", t); // native MSI rule alternative
                Assert.DoesNotContain("[Sum_", h);
                Assert.DoesNotContain("[Sum_", t);
            });
        }

        [Fact]
        public void Sandbox_configuration_maps_the_package_read_only()
        {
            using var tmp = new TempFolder();
            string pkg = Path.Combine(tmp.Path, "App & Co_1.0");
            Directory.CreateDirectory(pkg);
            string wsb = SandboxService.CreateConfig(pkg);
            string xml = File.ReadAllText(wsb);
            Assert.EndsWith(".wsb", wsb);
            Assert.Contains("<ReadOnly>true</ReadOnly>", xml);
            Assert.Contains("App &amp; Co_1.0</HostFolder>", xml);
            Assert.Contains(@"C:\J365Package\Sandbox\Sandbox-Test.ps1", xml);
            System.Xml.Linq.XDocument.Parse(xml); // well-formed
        }

        [Fact]
        public void Setup_items_start_pending_and_report_what_is_missing()
        {
            var items = SetupService.CreateItems();
            Assert.Equal(new[] { SetupService.Catalog, SetupService.Psadt, SetupService.Iwau }, items.Select(i => i.Key));
            Assert.Single(items, i => i.Required);
            Assert.All(items, i => Assert.Equal(SetupState.Pending, i.State));
            SetupService.Inspect(items); // nothing downloaded in the test data folder
            Assert.Equal(SetupState.Failed, items.Single(i => i.Key == SetupService.Psadt).State);
            Assert.Equal(SetupState.Warning, items.Single(i => i.Key == SetupService.Iwau).State);
            Assert.True(SetupService.NeedsSetup);
        }
    }
}
