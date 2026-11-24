using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>2.7.0: fixes found by the review of the whole code (packaging, publishing, settings, install).</summary>
public class Release27ReviewTests
{
    [Theory]
    [InlineData("Git", "Git", true)]
    [InlineData("Git 2.45.1", "Git", true)]
    [InlineData("git (x64)", "Git", true)]
    [InlineData("GitHub Desktop", "Git", false)]
    [InlineData("Gitea", "Git", false)]
    [InlineData("7-Zip 26.06", "7-Zip", true)]
    [InlineData("Notepad++", "Notepad", false)]
    [InlineData("Anything", "", false)]
    public void Earlier_versions_in_intune_are_the_apps_named_like_the_application_as_a_whole_word(string displayName, string appName, bool related)
        => Assert.Equal(related, IntuneConnection.IsRelatedName(displayName, appName));

    [Fact]
    public void Installers_are_stored_in_the_built_in_intunewin_without_compressing_them_again()
    {
        using var tmp = new TempFolder();
        string big = tmp.File("Files/setup.exe");
        using (var fs = new FileStream(big, FileMode.Open)) fs.SetLength(9L * 1024 * 1024);
        string small = tmp.File("Files/tiny.exe", "MZ");
        string script = tmp.File("Invoke-AppDeployToolkit.ps1", "Write-Host 'x'");
        string msi = tmp.File("Files/app.msi");
        using (var fs = new FileStream(msi, FileMode.Open)) fs.SetLength(12L * 1024 * 1024);
        Assert.Equal(CompressionLevel.NoCompression, IntuneWinBuilder.LevelFor(big));
        Assert.Equal(CompressionLevel.NoCompression, IntuneWinBuilder.LevelFor(msi));
        Assert.Equal(CompressionLevel.Optimal, IntuneWinBuilder.LevelFor(small));
        Assert.Equal(CompressionLevel.Optimal, IntuneWinBuilder.LevelFor(script));
    }

    [Fact]
    public void A_long_application_name_gives_a_shorter_package_folder()
    {
        var s = Fakes.Settings();
        s.AppName = "7-Zip";
        s.AppVersion = "26.06";
        Assert.Equal("7-Zip_26.06", PackageBuilder.FolderName(s));
        s.AppName = "Microsoft Visual C++ 2015-2022 Redistributable (x64) - Additional Runtime";
        s.AppVersion = "14.44.35211.0";
        string name = PackageBuilder.FolderName(s);
        Assert.True(name.Length <= 61, name);
        Assert.StartsWith("Microsoft_Visual_C++_2015-2022", name);
        Assert.EndsWith("_14.44.35211.0", name);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("0 3010", true)]
    [InlineData("-1; 1641", true)]
    [InlineData("2147483647", true)]
    [InlineData("3221225477", false)]
    [InlineData("0x10", false)]
    [InlineData("3010-3015", false)]
    public void Exit_codes_are_whole_32_bit_numbers(string codes, bool valid)
        => Assert.Equal(valid, ScriptGenerator.IsCodeList(codes));

    [Fact]
    public void A_kept_copy_is_looked_for_with_the_name_the_detection_uses_and_gets_the_post_install_actions()
    {
        var s = Fakes.Settings();
        s.UninstallPattern = "Mozilla Firefox*";
        s.DetectionNamePattern = "Mozilla Firefox (x64*";
        s.DetectionMethod = "Registry";
        Assert.Equal("Mozilla Firefox (x64*", ScriptGenerator.KeepPattern(s));
        s.DetectionMethod = "ProductCode";
        Assert.Equal("Mozilla Firefox*", ScriptGenerator.KeepPattern(s));

        s.DetectionMethod = "Registry";
        s.Actions.Add(new PackageAction { Type = "SetRegistry", Phase = "PostInstall", Params = { ["Key"] = @"HKLM\SOFTWARE\Contoso", ["Name"] = "AutoUpdate", ["Value"] = "0", ["Type"] = "DWord" } });
        string script = ScriptGenerator.DeployScript(Fakes.Template, s, "setup.exe");
        Ps.AssertParses(script);
        int kept = script.IndexOf("if ($keptApps.Count -gt 0)", StringComparison.Ordinal);
        int actions = script.IndexOf("The post-install actions of the package also apply to the copy that is kept", StringComparison.Ordinal);
        int mark = script.IndexOf("CreateSubKey(", kept, StringComparison.Ordinal);
        Assert.True(kept > 0 && actions > kept && actions < mark, "The actions run in the kept branch, before the mark.");
    }


    [Fact]
    public void Keeping_a_newer_version_with_a_detection_by_product_code_is_warned()
    {
        var s = Fakes.Settings("MSI");
        s.InstallerPath = "";
        s.DetectionMethod = "ProductCode";
        s.MsiProductCode = "{23170F69-40C1-2702-2600-000001000000}";
        Assert.Contains(Loc.T("Val_Warn_KeepNewerProductCode"), PackageBuilder.Validate(s).Warnings);
        s.KeepNewerVersion = false;
        Assert.DoesNotContain(Loc.T("Val_Warn_KeepNewerProductCode"), PackageBuilder.Validate(s).Warnings);
    }

    [Theory]
    [InlineData("https://github.com/sesantanajr/J365-IntunePSApp/releases/download/catalog/catalog.json.gz", true)]
    [InlineData("https://intranet.contoso.com/j365/catalog.json.gz", true)]
    [InlineData("http://localhost:8080/catalog.json.gz", true)]
    [InlineData("http://127.0.0.1/catalog.json.gz", true)]
    [InlineData("http://intranet.contoso.com/catalog.json.gz", false)]
    [InlineData("ftp://contoso.com/catalog.json.gz", false)]
    [InlineData("catalog.json.gz", false)]
    [InlineData("", false)]
    public void The_application_database_only_comes_over_https(string url, bool allowed)
        => Assert.Equal(allowed, CatalogService.IsAllowedUrl(url));

    [Fact]
    public void Removing_everything_keeps_the_packages_inside_the_data_folder()
    {
        using var tmp = new TempFolder();
        string data = Path.Combine(tmp.Path, "IntunePSApp");
        Directory.CreateDirectory(Path.Combine(data, "tools"));
        tmp.File(@"IntunePSApp\Pacotes\7-Zip_26.06\project.json", "{}");
        Directory.CreateDirectory(Path.Combine(data, "Packages"));            // empty: not kept
        tmp.File(@"IntunePSApp\Custom\Out\Contoso_1.0\project.json", "{}");
        tmp.File(@"IntunePSApp\config.json", "{ \"OutputFolder\": \"" + Path.Combine(data, "Custom", "Out").Replace("\\", "\\\\") + "\" }");
        var keep = InstallService.PackageFoldersIn(data).Select(Path.GetFileName).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "Custom", "Pacotes" }, keep);
    }

    [Fact]
    public void A_damaged_settings_file_is_kept_aside_before_it_is_replaced()
    {
        using var tmp = new TempFolder();
        string file = tmp.File("config.json", "\0\0\0\0");
        var c = Json.Load<AppConfig>(file);
        Assert.NotNull(c);
        Assert.Single(Directory.GetFiles(tmp.Path, "config.json.bad-*"));
        Json.Save(file, new AppConfig { CompanyName = "Contoso" });
        Assert.Equal("Contoso", Json.Load<AppConfig>(file).CompanyName);
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.tmp"));
    }

    [Fact]
    public async Task A_request_that_creates_something_is_not_sent_again_after_a_server_error()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        int posts = 0;
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                int n = Interlocked.Increment(ref posts);
                // First a throttling answer (always repeated), then a server error (never repeated for a creation).
                ctx.Response.StatusCode = n == 1 ? 429 : 500;
                if (n == 1) ctx.Response.AddHeader("Retry-After", "0");
                byte[] body = Encoding.UTF8.GetBytes("{\"error\":{\"code\":\"InternalServerError\",\"message\":\"boom\"}}");
                ctx.Response.OutputStream.Write(body, 0, body.Length);
                ctx.Response.Close();
            }
        });
        string before = Environment.GetEnvironmentVariable("J365_GRAPH_URL");
        Environment.SetEnvironmentVariable("J365_GRAPH_URL", $"http://127.0.0.1:{port}");
        try
        {
            var g = new Graph("token");
            var ex = await Assert.ThrowsAsync<GraphException>(() => g.SendAsync(HttpMethod.Post, "/beta/deviceAppManagement/mobileApps", new JsonObject { ["x"] = 1 }, CancellationToken.None, repeatable: false));
            Assert.Equal(500, ex.Status);
            Assert.Equal(2, posts);
        }
        finally
        {
            Environment.SetEnvironmentVariable("J365_GRAPH_URL", before);
            listener.Stop();
        }
    }

    [Fact]
    public void The_publishing_script_writes_utf8_when_read_by_the_application_and_waits_in_cancellable_steps()
    {
        string script = PackageBuilder.EmbeddedText("Publish-ToIntune.ps1");
        Assert.True(Ps.IsAscii(script));
        Assert.Contains("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)", script);
        Assert.Contains("function Wait-Seconds", script);
        Assert.DoesNotContain("Start-Sleep -Seconds ([Math]::Min($wait, 120))", script);
        Assert.Contains("'AADSTS700027'", script);
        Ps.AssertParses(script);
    }
}
