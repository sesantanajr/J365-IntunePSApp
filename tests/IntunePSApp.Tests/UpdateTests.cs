using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using J365.IntunePSApp.Core;
using Xunit;

namespace J365.IntunePSApp.Tests
{
    public class AppUpdaterTests
    {
        private const string ReleaseJson = @"{
  ""tag_name"": ""v2.9.1"",
  ""html_url"": ""https://github.com/sesantanajr/J365-IntunePSApp/releases/tag/v2.9.1"",
  ""assets"": [
    { ""name"": ""J365-IntunePSApp-2.9.1-source.zip"", ""size"": 100, ""browser_download_url"": ""https://example.test/source.zip"" },
    { ""name"": ""J365-IntunePSApp-2.9.1-win-x64.zip"", ""size"": 56789012, ""browser_download_url"": ""https://example.test/app.zip"" },
    { ""name"": ""SHA256SUMS.txt"", ""size"": 200, ""browser_download_url"": ""https://example.test/SHA256SUMS.txt"" }
  ]
}";

        [Fact]
        public void A_release_gives_the_version_the_application_zip_and_its_checksums()
        {
            var r = AppUpdater.ParseRelease(ReleaseJson);
            Assert.Equal("2.9.1", r.Version);
            Assert.Equal("J365-IntunePSApp-2.9.1-win-x64.zip", r.ZipName);
            Assert.Equal("https://example.test/app.zip", r.ZipUrl);
            Assert.Equal(56789012, r.ZipSize);
            Assert.Equal("https://example.test/SHA256SUMS.txt", r.SumsUrl);
        }

        [Fact]
        public void A_release_without_the_application_zip_or_a_version_is_refused()
        {
            Assert.Throws<InvalidDataException>(() => AppUpdater.ParseRelease(@"{ ""tag_name"": ""v2.9.1"", ""assets"": [] }"));
            Assert.Throws<InvalidDataException>(() => AppUpdater.ParseRelease(ReleaseJson.Replace("v2.9.1\"", "nightly\"")));
        }

        [Fact]
        public void Checksums_are_read_for_the_exact_file_name()
        {
            string a = new string('a', 64), b = new string('B', 64);
            string sums = $"{a}  J365-IntunePSApp-2.9.1-source.zip\r\n{b} *J365-IntunePSApp-2.9.1-win-x64.zip\r\n";
            Assert.Equal(b, AppUpdater.HashFor(sums, "J365-IntunePSApp-2.9.1-win-x64.zip"));
            Assert.Equal(a.ToUpperInvariant(), AppUpdater.HashFor(sums, "j365-intunepsapp-2.9.1-source.zip"));
            Assert.Null(AppUpdater.HashFor(sums, "other.zip"));
            Assert.Null(AppUpdater.HashFor(null, "other.zip"));
        }

        [Fact]
        public void The_executable_is_taken_from_the_zip_only_when_it_is_the_announced_version()
        {
            using var tmp = new TempFolder();
            string exe = Path.Combine(AppContext.BaseDirectory, AppUpdater.ExeName);   // carries the version of this build
            string zip = Path.Combine(tmp.Path, "release.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(exe, AppUpdater.ExeName);
                using var w = new StreamWriter(z.CreateEntry("README.md").Open());
                w.Write("new readme");
            }
            string dest = Path.Combine(tmp.Path, "app.exe.new");
            AppUpdater.ExtractExe(zip, dest, AppInfo.Version);
            Assert.Equal(new FileInfo(exe).Length, new FileInfo(dest).Length);

            File.Delete(dest);
            Assert.Throws<InvalidDataException>(() => AppUpdater.ExtractExe(zip, dest, "9.9.9"));
            Assert.False(File.Exists(dest));
            Assert.False(File.Exists(dest + ".part"));

            // Only files the folder already has are refreshed (a portable copy keeps its README current).
            string folder = Path.Combine(tmp.Path, "portable");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "README.md"), "old readme");
            AppUpdater.RefreshCompanionFiles(zip, folder);
            Assert.Equal("new readme", File.ReadAllText(Path.Combine(folder, "README.md")));
            Assert.False(File.Exists(Path.Combine(folder, AppUpdater.ExeName)));
        }

        [Fact]
        public void A_running_executable_is_replaced_by_renaming_it()
        {
            using var tmp = new TempFolder();
            string target = Path.Combine(tmp.Path, "app.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), target);
            string newFile = Path.Combine(tmp.Path, "app.exe.new");
            File.WriteAllText(newFile, "new version");

            using var running = Process.Start(new ProcessStartInfo(target, "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            try
            {
                // Overwriting the running file fails...
                Assert.ThrowsAny<Exception>(() => File.Copy(newFile, target, true));
                // ...but the replacement goes through: the running file is renamed and the new one takes its name.
                AppUpdater.Replace(newFile, target);
                Assert.Equal("new version", File.ReadAllText(target));
                Assert.True(File.Exists(target + ".old"));
                Assert.False(File.Exists(newFile));
                Assert.False(running.HasExited);
            }
            finally
            {
                try { running.Kill(); running.WaitForExit(10000); } catch { }
            }

            // Not running: a plain replace, nothing renamed.
            File.Delete(target + ".old");
            File.WriteAllText(newFile, "newer version");
            AppUpdater.Replace(newFile, target);
            Assert.Equal("newer version", File.ReadAllText(target));
            Assert.False(File.Exists(target + ".old"));
        }
    }
}
