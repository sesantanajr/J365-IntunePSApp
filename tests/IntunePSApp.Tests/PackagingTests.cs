using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
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
        public void A_cancelled_native_build_leaves_nothing_behind()
        {
            using var tmp = new TempFolder();
            string src = SampleSource(tmp), output = Path.Combine(tmp.Path, "out", "app.intunewin");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var before = Directory.GetDirectories(AppPaths.Temp, "_intunewin_*").Length;
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => IntuneWinBuilder.BuildNative(src, IntuneWinBuilder.SetupFile, output, cts.Token));
            Assert.False(File.Exists(output));
            Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(output)));               // nothing in the output folder
            Assert.Equal(before, Directory.GetDirectories(AppPaths.Temp, "_intunewin_*").Length);      // working copies removed
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

        [Fact]
        public void The_native_packager_and_the_check_say_how_far_they_are()
        {
            using var tmp = new TempFolder();
            string src = SampleSource(tmp), output = Path.Combine(tmp.Path, "app.intunewin");
            var built = new System.Collections.Generic.List<double>();
            var validated = new System.Collections.Generic.List<double>();
            IntuneWinBuilder.BuildNative(src, IntuneWinBuilder.SetupFile, output, default, built.Add);
            Assert.True(IntuneWinBuilder.Validate(output, validated.Add).Valid);
            foreach (var list in new[] { built, validated })
            {
                Assert.NotEmpty(list);
                Assert.Equal(1, list[^1], 3);
                Assert.All(list, v => Assert.InRange(v, 0, 1));
                for (int i = 1; i < list.Count; i++) Assert.True(list[i] >= list[i - 1] - 1e-9, "the progress went back");
            }
        }

        [Fact]
        public void The_build_is_one_bar_that_never_goes_back()
        {
            var reports = new System.Collections.Generic.List<BuildProgressInfo>();
            var bp = new BuildProgress(new SyncProgress<BuildProgressInfo>(reports.Add), new (string, double)[] { ("a", 1), ("b", 3), ("c", 0), ("d", 1) });
            bp.Begin("a", "Copying the installer...");
            Assert.Equal(0, reports[^1].Percent, 3);
            Assert.Equal("Copying the installer", reports[^1].Step);
            bp.Begin("b", "Creating the .intunewin file...");
            Assert.Equal(20, reports[^1].Percent, 3);
            // Within a step, by the bytes it went through (reports come at most every 100 ms).
            var bytes = bp.Bytes(1000);
            System.Threading.Thread.Sleep(150);
            bytes(500);
            Assert.Equal(50, reports[^1].Percent, 3);
            // A step left out counts as done; a step started again does not lower the bar.
            bp.Begin("d", "Checking");
            Assert.Equal(80, reports[^1].Percent, 3);
            bp.Begin("b", "Again");
            Assert.Equal(80, reports[^1].Percent, 3);
            bp.Complete("Finished");
            Assert.Equal(100, reports[^1].Percent, 3);
            Assert.Null(reports[^1].Remaining);
        }

        [Fact]
        public void A_copy_with_progress_keeps_the_content_and_the_date()
        {
            using var tmp = new TempFolder();
            string source = Path.Combine(tmp.Path, "setup.exe"), copy = Path.Combine(tmp.Path, "Files", "setup.exe");
            var data = Enumerable.Range(0, 3 * 1048576 + 17).Select(i => (byte)(i * 7)).ToArray();
            File.WriteAllBytes(source, data);
            File.SetLastWriteTimeUtc(source, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));
            long seen = 0;
            BuildProgress.CopyFile(source, copy, n => seen += n, default);
            Assert.Equal(data, File.ReadAllBytes(copy));
            Assert.Equal(data.Length, seen);
            Assert.Equal(File.GetLastWriteTimeUtc(source), File.GetLastWriteTimeUtc(copy));
            Assert.Equal("45 s", BuildProgress.Duration(TimeSpan.FromSeconds(44.6)));
            Assert.Equal("3 min 05 s", BuildProgress.Duration(TimeSpan.FromSeconds(185)));
        }

        [Fact]
        public void The_texts_of_the_dialogs_are_read_from_the_tables_of_the_toolkit()
        {
            using var tmp = new TempFolder();
            string strings = Path.Combine(tmp.Path, "Strings");
            Directory.CreateDirectory(Path.Combine(strings, "pt-BR"));
            Directory.CreateDirectory(Path.Combine(strings, "fr"));
            // The syntax of the real tables: comments, both quotes, backtick escapes, typographic quotes for quotes.
            File.WriteAllText(Path.Combine(strings, "strings.psd1"), "@{\n" +
                "    # Comment with = and 'quotes'\n" +
                "    CloseAppsPrompt = @{\n" +
                "        Fluent = @{\n" +
                "            DialogMessage = @{\n" +
                "                Install = 'Please save your work before continuing as the following applications will be closed automatically.'\n" +
                "            }\n" +
                "            ButtonRightText = 'Defer'\n" +
                "            CustomMessage = ''\n" +
                "        }\n" +
                "        Classic = @{ ButtonClose = \"Close `\"Programs`\"`nnow\"; ButtonDefer = 'It''s &Defer' }\n" +
                "    }\n" +
                "}\n# SIG # Begin signature block\n# MIIuaQYJ\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(strings, "pt-BR", "strings.psd1"), "@{\n" +
                "    CloseAppsPrompt = @{\n" +
                "        Fluent = @{\n" +
                "            DialogMessage = @{ Install = 'Por favor, salve seu trabalho antes de continuar.' }\n" +
                "            ButtonRightText = ''\n" +
                "        }\n" +
                "        Classic = @{ ButtonClose = \"Clique em `“Fechar programas`”.\"; ButtonDefer = '&“Adiar”' }\n" +
                "    }\n" +
                "}\n", new UTF8Encoding(false));

            var pt = PsadtStrings.Load(strings, "pt-BR");
            Assert.Equal("pt-BR", pt.Culture);
            Assert.Equal("Por favor, salve seu trabalho antes de continuar.", pt.Get("CloseAppsPrompt", "Fluent", "DialogMessage", "Install"));
            // An empty text of the language keeps the English one, like the toolkit.
            Assert.Equal("Defer", pt.Get("closeappsprompt", "fluent", "buttonrighttext"));
            Assert.Equal("Clique em “Fechar programas”.", pt.Get("CloseAppsPrompt", "Classic", "ButtonClose"));
            Assert.Equal("&“Adiar”", pt.Get("CloseAppsPrompt", "Classic", "ButtonDefer"));
            Assert.Null(pt.Get("CloseAppsPrompt", "Fluent", "Missing"));

            // pt-PT has no table of its own here, nor "pt": English. A language without a folder: English.
            var en = PsadtStrings.Load(strings, "de-DE");
            Assert.Equal("", en.Culture);
            Assert.Equal("Close \"Programs\"\nnow", en.Get("CloseAppsPrompt", "Classic", "ButtonClose"));
            Assert.Equal("It's &Defer", en.Get("CloseAppsPrompt", "Classic", "ButtonDefer"));
            Assert.Null(PsadtStrings.Load(Path.Combine(tmp.Path, "none"), "pt-BR"));

            // A doubled quote is one quote, the second of the pair (like PowerShell); a file cut short is a format error.
            Assert.Equal("a’b", PsadtStrings.Parse("@{ K = 'a'’b' }")["K"]);
            Assert.Equal("a'b", PsadtStrings.Parse("@{ K = 'a’'b' }")["K"]);
            Assert.Throws<FormatException>(() => PsadtStrings.Parse("@{ K ="));
            Assert.Throws<FormatException>(() => PsadtStrings.Parse("@{ K = 'open"));
            Assert.Throws<FormatException>(() => PsadtStrings.Parse("@{ "));
        }

        [Fact]
        public void The_preview_shows_what_the_toolkit_shows_at_each_moment_of_a_required_installation()
        {
            using var tmp = new TempFolder();
            string strings = Path.Combine(tmp.Path, "Strings");
            Directory.CreateDirectory(Path.Combine(strings, "pt-BR"));
            File.WriteAllText(Path.Combine(strings, "strings.psd1"), "@{ CloseAppsPrompt = @{ Fluent = @{ ButtonRightText = 'Defer' } } }", new UTF8Encoding(false));
            // The texts of PSAppDeployToolkit 4.1.8 in Portuguese.
            File.WriteAllText(Path.Combine(strings, "pt-BR", "strings.psd1"), "@{\n CloseAppsPrompt = @{\n  Classic = @{\n" +
                "   CountdownClose = @{ Install = 'OBSERVAÇÃO: O(s) programa(s) será(ão) fechado(s) automaticamente em:' }\n" +
                "   DeferralsRemaining = 'Adiamentos restantes:'\n   ButtonClose = 'Fechar &Programas'\n   ButtonDefer = '&Adiar'\n   ButtonContinue = '&“Continuar”'\n  }\n" +
                "  Fluent = @{\n   DialogMessage = @{ Install = 'Por favor, salve seu trabalho antes de continuar, pois os seguintes aplicativos serão fechados automaticamente.' }\n" +
                "   DialogMessageNoProcesses = @{ Install = 'Selecione Instalar para continuar com a instalação.' }\n" +
                "   AutomaticStartCountdown = 'Contagem regressiva para início automático'\n   DeferralsRemaining = 'Adiamentos restantes'\n" +
                "   ButtonLeftText = @{ Install = 'Fechar Aplicativos e Instalar' }\n   ButtonLeftNoProcessesText = @{ Install = 'Instalar' }\n   ButtonRightText = 'Adiar'\n" +
                "   Subtitle = @{ Install = '{Toolkit\\CompanyName} - Instalação do Aplicativo' }\n  }\n }\n}\n", new UTF8Encoding(false));
            var table = PsadtStrings.Load(strings, "pt-BR");
            var adobe = new WelcomePreviewInput
            {
                Mode = "Auto", AppName = "Adobe Acrobat Reader", AppVersion = "26.003.22110", Company = "Jornada 365",
                Processes = new System.Collections.Generic.List<string> { "AcroRd32", "Acrobat", "AcroCEF" },
                AllowDefer = true, DeferTimes = 3, CountdownSeconds = 300
            };
            Assert.Equal(new[] { WelcomeMoment.AppOpen, WelcomeMoment.AppClosed, WelcomeMoment.LastNotice }, WelcomePreview.Moments(adobe));

            // An application of the list open: one row for all its programs, the deferrals left, Close Apps & Install on
            // the left and Defer on the right; no countdown while the user can defer.
            var open = WelcomePreview.Build(adobe, WelcomeMoment.AppOpen, table);
            Assert.True(open.Shown);
            Assert.Equal("Adobe Acrobat Reader", open.Title);
            Assert.Equal("Jornada 365 - Instalação do Aplicativo", open.Subtitle);
            Assert.StartsWith("Por favor, salve seu trabalho", open.Message);
            Assert.Equal("Adobe Acrobat Reader", open.AppName);
            Assert.Equal("acrord32.exe", open.AppExe);
            Assert.Equal(("Adiamentos restantes", "3"), (open.DeferralsLabel, open.DeferralsValue));
            Assert.Null(open.CountdownLabel);
            Assert.Equal(("Fechar Aplicativos e Instalar", "Adiar"), (open.MainButton, open.DeferButton));
            Assert.Equal(("Fechar Programas", "Adiar", "“Continuar”"), (open.ClassicButtonClose, open.ClassicButtonDefer, open.ClassicButtonContinue));
            Assert.Equal("Adiamentos restantes: 3", open.ClassicDeferrals);

            // Nothing open: the dialog still appears in the automatic mode, to install now or defer.
            var closed = WelcomePreview.Build(adobe, WelcomeMoment.AppClosed, table);
            Assert.True(closed.Shown);
            Assert.Equal("Selecione Instalar para continuar com a instalação.", closed.Message);
            Assert.Null(closed.AppName);
            Assert.Equal(("Instalar", "Adiar"), (closed.MainButton, closed.DeferButton));
            Assert.Null(closed.ClassicButtonClose);

            // Deferrals used up: no Defer, the countdown of the package, then the programs are closed.
            var last = WelcomePreview.Build(adobe, WelcomeMoment.LastNotice, table);
            Assert.Null(last.DeferButton);
            Assert.Null(last.DeferralsLabel);
            Assert.Equal(("Contagem regressiva para início automático", "0h 5m 0s"), (last.CountdownLabel, last.CountdownValue));
            Assert.Equal("0:05:00", last.ClassicCountdownValue);
            Assert.False(last.ClassicDeferEnabled);
            Assert.Contains("5 min 00 s", last.Note);

            // The discreet mode stays silent while nothing is open; the silent mode never shows it.
            Assert.False(WelcomePreview.Build(new WelcomePreviewInput { Mode = "WhenNeeded", Processes = adobe.Processes, AllowDefer = true }, WelcomeMoment.AppClosed, table).Shown);
            Assert.False(WelcomePreview.Build(new WelcomePreviewInput { Mode = "Silent", Processes = adobe.Processes, AllowDefer = true }, WelcomeMoment.AppOpen, table).Shown);

            // Without deferral: the first notice already counts down, and with nothing open there is no dialog.
            var noDefer = new WelcomePreviewInput { Mode = "Auto", AppName = "7-Zip", Processes = new System.Collections.Generic.List<string> { "7zFM" }, CountdownSeconds = 60 };
            Assert.Equal(new[] { WelcomeMoment.AppOpen, WelcomeMoment.AppClosed }, WelcomePreview.Moments(noDefer));
            var first = WelcomePreview.Build(noDefer, WelcomeMoment.LastNotice, table);
            Assert.Equal("0h 1m 0s", first.CountdownValue);
            Assert.Null(first.DeferButton);
            Assert.False(WelcomePreview.Build(noDefer, WelcomeMoment.AppClosed, table).Shown);

            // No program to close and no deferral: no welcome dialog at all.
            var nothing = new WelcomePreviewInput { Mode = "Auto", AppName = "Tool" };
            Assert.Equal(new[] { WelcomeMoment.AppClosed }, WelcomePreview.Moments(nothing));
            Assert.False(WelcomePreview.Build(nothing, WelcomeMoment.AppOpen, table).Shown);

            // Without the toolkit on this computer, the texts of this application stand in.
            Assert.Equal(Loc.T("Prev_CloseAndInstall"), WelcomePreview.Build(adobe, WelcomeMoment.AppOpen, null).MainButton);
        }

        /// <summary>Reports at once, on the calling thread (Progress&lt;T&gt; posts them later).</summary>
        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> _report;
            public SyncProgress(Action<T> report) => _report = report;
            public void Report(T value) => _report(value);
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

    public class AppIconTests
    {
        private static (int Width, int Height, bool HasPixels) Describe(string png)
        {
            using var bmp = new System.Drawing.Bitmap(png);
            return (bmp.Width, bmp.Height, bmp.GetPixel(bmp.Width / 2, bmp.Height / 4).A > 0);
        }

        [Fact]
        public void A_package_always_gets_an_icon_even_when_the_installer_has_none()
        {
            using var tmp = new TempFolder();
            string installer = tmp.File("setup.msi", "not a real installer"); // no icon can be read from it
            string icon = Path.Combine(tmp.Path, "Intune", "AppIcon.png");

            Assert.Equal(ImageTools.IconGenerated, ImageTools.SaveAppIcon("", installer, "Contoso App", "Contoso.App", icon));
            var generated = Describe(icon);
            Assert.Equal((256, 256, true), generated);

            // A missing custom file falls back instead of failing the build (validation reports it earlier).
            Assert.Equal(ImageTools.IconGenerated, ImageTools.SaveAppIcon(Path.Combine(tmp.Path, "missing.png"), installer, "Contoso App", "", icon));
            // Names without letters still give a readable tile.
            Assert.Equal(ImageTools.IconGenerated, ImageTools.SaveAppIcon(null, null, "", null, icon));
        }

        [Fact]
        public void An_icon_chosen_by_the_user_wins_and_is_normalised_to_256_px()
        {
            using var tmp = new TempFolder();
            string custom = Path.Combine(tmp.Path, "wide.png");
            using (var bmp = new System.Drawing.Bitmap(400, 100))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(System.Drawing.Color.Red);
                bmp.Save(custom, System.Drawing.Imaging.ImageFormat.Png);
            }
            string icon = Path.Combine(tmp.Path, "AppIcon.png");
            Assert.Equal(ImageTools.IconCustom, ImageTools.SaveAppIcon(custom, tmp.File("setup.exe", "x"), "Contoso App", "", icon));
            using var result = new System.Drawing.Bitmap(icon);
            Assert.Equal(256, result.Width);
            Assert.Equal(256, result.Height);
            Assert.Equal(255, result.GetPixel(128, 128).R);   // the picture, centred
            Assert.Equal(0, result.GetPixel(128, 10).A);      // transparent padding, not stretched
        }

        [Fact]
        public void A_file_that_is_not_an_msi_has_no_msi_icon()
        {
            using var tmp = new TempFolder();
            Assert.Null(MsiReader.ReadIcon(tmp.File("broken.msi", "garbage")));
            Assert.Null(MsiReader.ReadIcon(Path.Combine(tmp.Path, "missing.msi")));
            Assert.False(ImageTools.SaveInstallerIcon(tmp.File("other.msi", ""), Path.Combine(tmp.Path, "icon.png")));
        }

        [Theory]
        [InlineData("\"C:\\Program Files\\7-Zip\\7zFM.exe\",0", "C:\\Program Files\\7-Zip\\7zFM.exe", 0)]
        [InlineData("C:\\Program Files\\App\\app.exe", "C:\\Program Files\\App\\app.exe", 0)]
        [InlineData("C:\\Windows\\system32\\shell32.dll,-154", "C:\\Windows\\system32\\shell32.dll", -154)]
        [InlineData("C:\\Apps\\a,b\\app.exe", "C:\\Apps\\a,b\\app.exe", 0)]
        [InlineData("  \"C:\\App\\app.ico\"  ", "C:\\App\\app.ico", 0)]
        [InlineData("", "", 0)]
        public void Icon_locations_from_installed_apps_are_parsed(string value, string path, int index)
            => Assert.Equal((path, index), ImageTools.ParseIconLocation(value));

        [Fact]
        public void Uninstallers_setups_and_help_shortcuts_are_not_taken_for_the_application()
        {
            foreach (var helper in new[] { "unins000.exe", "Uninstall.exe", "setup.exe", "Update.exe", "crashpad_handler.exe", "vc_redist.x64.exe" })
                Assert.True(MsiReader.IsHelperProgram(helper), helper);
            foreach (var app in new[] { "7zFM.exe", "notepad++.exe", "RemoteHelp.exe", "Teams.exe" })
                Assert.False(MsiReader.IsHelperProgram(app), app);
            foreach (var helper in new[] { "Uninstall 7-Zip", "7-Zip Help", "Read Me", "Desinstalar App", "License" })
                Assert.True(MsiReader.IsHelperShortcut(helper), helper);
            Assert.False(MsiReader.IsHelperShortcut("7-Zip File Manager"));
            Assert.Equal("7zFM.exe", MsiReader.LongName("7ZFM~1.EXE|7zFM.exe"));
            Assert.Equal("app.exe", MsiReader.LongName("app.exe"));
        }

        [Fact]
        public void Installer_icons_are_cached_by_file_name_size_and_date()
        {
            using var tmp = new TempFolder();
            // A real program with an icon, under an installer name (nothing is run).
            string setup = Path.Combine(tmp.Path, "setup.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), setup);
            string a = Path.Combine(tmp.Path, "a.png"), b = Path.Combine(tmp.Path, "b.png");
            Assert.True(ImageTools.SaveInstallerIconCached(setup, a));
            int before = Directory.GetFiles(AppPaths.IconCache).Length;
            Assert.True(ImageTools.SaveInstallerIconCached(setup, b));
            Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
            Assert.Equal(before, Directory.GetFiles(AppPaths.IconCache).Length);

            // No icon: remembered as such, and still no icon the second time.
            string broken = tmp.File("broken.msi", "garbage");
            Assert.False(ImageTools.SaveInstallerIconCached(broken, Path.Combine(tmp.Path, "c.png")));
            Assert.False(ImageTools.SaveInstallerIconCached(broken, Path.Combine(tmp.Path, "c.png")));
            Assert.False(File.Exists(Path.Combine(tmp.Path, "c.png")));
        }

        [Fact]
        public void A_missing_custom_icon_is_a_validation_error()
        {
            using var tmp = new TempFolder();
            var s = Fakes.Settings();
            s.InstallerPath = tmp.File("setup.exe", "x");
            s.AppIconPath = Path.Combine(tmp.Path, "gone.ico");
            Assert.Contains(Loc.T("Val_IconMissing", s.AppIconPath), PackageBuilder.Validate(s).Errors);
            s.AppIconPath = tmp.File("icon.png", "x");
            Assert.DoesNotContain(PackageBuilder.Validate(s).Errors, e => e.Contains("icon.png"));
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
            var doc = System.Xml.Linq.XDocument.Parse(xml); // well-formed

            // The result comes back through a second, writable folder; a click on the console must not pause the test.
            var folders = doc.Descendants("MappedFolder").ToList();
            Assert.Equal(2, folders.Count);
            Assert.Equal(SandboxService.ResultsDir(pkg), folders[1].Element("HostFolder").Value);
            Assert.Equal(@"C:\J365Results", folders[1].Element("SandboxFolder").Value);
            Assert.Equal("false", folders[1].Element("ReadOnly").Value);
            Assert.True(Directory.Exists(SandboxService.ResultsDir(pkg)));
            string command = doc.Descendants("Command").Single().Value;
            Assert.Contains(@"reg add HKCU\Console /v QuickEdit /t REG_DWORD /d 0 /f", command);
            Assert.Contains("powershell.exe -NoExit -ExecutionPolicy Bypass -File", command);
        }

        [Fact]
        public void Sandbox_result_of_a_previous_test_is_discarded_and_the_new_one_is_read()
        {
            using var tmp = new TempFolder();
            Assert.Null(SandboxService.ReadResult(tmp.Path));

            // Same shape Sandbox-Test.ps1 writes with ConvertTo-Json.
            tmp.File(@"Sandbox\Results\result.json", @"{
    ""schema"":  1,
    ""app"":  ""Contoso App 1.2.3"",
    ""passed"":  false,
    ""lastStep"":  ""uninstall"",
    ""lastPassed"":  false,
    ""steps"":  [
        { ""step"":  ""install"", ""passed"":  true, ""exitCode"":  0, ""detected"":  true, ""seconds"":  38 },
        { ""step"":  ""uninstall"", ""passed"":  false, ""exitCode"":  1603, ""detected"":  true, ""seconds"":  12 }
    ]
}");
            var result = SandboxService.ReadResult(tmp.Path);
            Assert.NotNull(result);
            Assert.False(result.Passed);
            Assert.Equal(2, result.Steps.Count);
            Assert.True(result.Steps[0].Passed);
            Assert.Equal(38, result.Steps[0].Seconds);
            Assert.Equal("uninstall", result.Last.Step);
            Assert.Equal(1603, result.Last.ExitCode);
            Assert.True(result.Last.Detected);

            // A file caught half written is "no result yet", not an error.
            tmp.File(@"Sandbox\Results\result.json", "{ \"steps\": [");
            Assert.Null(SandboxService.ReadResult(tmp.Path));

            SandboxService.CreateConfig(tmp.Path);
            Assert.False(File.Exists(SandboxService.ResultFile(tmp.Path)));
        }

        [Fact]
        public void Sandbox_test_needs_a_package_of_this_generation_and_gets_the_current_script()
        {
            using var tmp = new TempFolder();
            Assert.False(SandboxService.CanTest(tmp.Path));
            tmp.File(@"Intune\intune-app.json", "{}");
            Assert.False(SandboxService.CanTest(tmp.Path)); // no Package folder
            tmp.File(@"Package\Invoke-AppDeployToolkit.exe", "x");
            Assert.True(SandboxService.CanTest(tmp.Path));

            string old = tmp.File(@"Sandbox\Sandbox-Test.ps1", "# script of an older version");
            string wsb = SandboxService.Prepare(tmp.Path);
            Assert.True(File.Exists(wsb));
            string script = File.ReadAllText(old);
            Assert.Equal(PackageBuilder.CompanionScript(PackageLayout.SandboxScript), script);
            // The fixes for the test that looked frozen: no QuickEdit pause, no Smart App Control slowdown, a time limit.
            Assert.Contains("function global:Disable-J365QuickEdit", script);
            Assert.Contains("VerifiedAndReputablePolicyState", script);
            Assert.Contains("$TimeoutMinutes", script);
            Assert.Contains("result.json", script);
        }

        [Theory]
        [InlineData("Enterprise", "Client", true)]
        [InlineData("Professional", "Client", true)]
        [InlineData("Education", "Client", true)]
        [InlineData("Core", "Client", false)]
        [InlineData("CoreSingleLanguage", "Client", false)]
        [InlineData("ServerStandard", "Server", false)]
        [InlineData("", "", true)] // unknown: let Windows answer
        public void Windows_Sandbox_is_offered_only_where_the_feature_exists(string edition, string installationType, bool expected)
            => Assert.Equal(expected, SandboxService.IsSupportedEdition(edition, installationType));

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
