using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>
/// 3.0.5: the links of the database that no longer give the file. About 2% of the installer links of winget answered 404
/// (Autodesk DWG TrueView 2027 among them): the database marks them every day, and the application does not offer a
/// download that would fail.
/// </summary>
public class Release305Tests
{
    private static string T(string key, params object[] args) => Loc.T(key, args);

    private static CatalogInstaller Exe(string url, string arch = "x64", string gone = null, string locale = null)
        => new CatalogInstaller { Type = "exe", Arch = arch, Url = url, Gone = gone, Locale = locale, Silent = "/S" };

    private static CatalogApp App(params CatalogInstaller[] installers)
        => new CatalogApp { Id = "Contoso.Viewer", Name = "Contoso Viewer", Publisher = "Contoso", Version = "2027", Installers = installers.ToList() };

    [Fact]
    public void An_application_whose_links_are_all_gone_cannot_be_packaged_and_says_why()
    {
        var app = App(Exe("https://contoso.example/a.exe", gone: "404"), Exe("https://contoso.example/b.exe", arch: "x86", gone: "dns"));
        Assert.False(app.IsSupported);
        Assert.False(app.CanDeploy);
        Assert.True(app.LinksGone);
        Assert.Equal(T("Catalog_LinkGoneLabel"), app.SourceLabel);
        Assert.All(app.Installers, i => Assert.False(i.CanDownload));
        Assert.Equal("HTTP 404", app.Installers[0].GoneLabel);
        Assert.Equal(T("Catalog_GoneDns"), app.Installers[1].GoneLabel);
        Assert.Null(CatalogService.PickInstaller(app, "x64", "en-US"));
        // A file someone downloaded by hand is still recognised: the switches and the curation of the database apply.
        Assert.Same(app.Installers[0], CatalogService.PickInstaller(app, "x64", "en-US", downloadable: false));
    }

    [Fact]
    public void An_installer_whose_link_works_is_chosen_before_a_preferred_one_that_is_gone()
    {
        var gone = Exe("https://contoso.example/pt.exe", gone: "404", locale: "pt-BR");
        var works = Exe("https://contoso.example/en.exe", locale: "en-US");
        var app = App(gone, works);
        Assert.True(app.IsSupported);
        Assert.False(app.LinksGone);
        Assert.Same(works, CatalogService.PickInstaller(app, "x64", "pt-BR"));
        var plan = BatchPackager.Plan(new[] { app }, new AppConfig { PreferredArch = "x64", PreferredLocale = "pt-BR" });
        Assert.Same(works, plan.Single().Installer);
    }

    [Fact]
    public void The_database_says_which_links_are_gone_and_which_version_took_the_place_of_the_newest()
    {
        const string json = "{\"id\":\"Contoso.Viewer\",\"n\":\"Contoso Viewer\",\"v\":\"1.1.0\",\"fb\":\"1.2.0\","
            + "\"i\":[{\"a\":\"x64\",\"t\":\"exe\",\"u\":\"https://contoso.example/old.exe\",\"g\":\"410\"},{\"a\":\"x64\",\"t\":\"msi\",\"u\":\"https://contoso.example/v.msi\"}]}";
        var app = JsonSerializer.Deserialize<CatalogApp>(json);
        Assert.Equal("1.2.0", app.FallbackFrom);
        Assert.Equal("410", app.Installers[0].Gone);
        Assert.True(app.Installers[1].IsAvailable);
        Assert.Equal("msi", CatalogService.PickInstaller(app, "x64", "en-US").Type);
    }

    [Fact]
    public void A_batch_skips_an_application_without_a_working_link_with_the_reason()
    {
        var app = App(Exe("https://contoso.example/a.exe", gone: "404"));
        var items = BatchPackager.Plan(new[] { app }, new AppConfig());
        Assert.Null(items[0].Installer);
        Assert.Equal(T("Batch_LinkGone"), items[0].InstallerLabel);
        // Nothing is downloaded: the reason is known before.
        BatchPackager.RunAsync(items, new BatchOptions { Publish = false }, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(BatchState.Skipped, items[0].State);
        Assert.Equal(T("Batch_LinkGone"), items[0].Message);
    }

    /// <summary>A local server that answers every request with the same status and body, and counts the requests.</summary>
    private sealed class FileServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        public string Base { get; }
        public int Requests;

        public FileServer(int status, byte[] body)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Base = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Base);
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); } catch { return; }
                    Interlocked.Increment(ref Requests);
                    ctx.Response.StatusCode = status;
                    if (status == 200) { ctx.Response.ContentLength64 = body.Length; await ctx.Response.OutputStream.WriteAsync(body); }
                    ctx.Response.Close();
                }
            });
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public void A_file_the_server_says_is_gone_is_asked_once(int status)
    {
        using var server = new FileServer(status, Array.Empty<byte>());
        using var tmp = new TempFolder();
        string dest = Path.Combine(tmp.Path, "setup.exe");
        var started = DateTime.UtcNow;
        var ex = Assert.Throws<DownloadGoneException>(() => Http.DownloadAsync(server.Base + "setup.exe", dest, null, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal(status, ex.Status);
        Assert.Equal(1, server.Requests);                                // no retry: the answer is final
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));  // the retries waited 2 + 4 s
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void A_file_that_is_not_the_registered_one_is_never_used()
    {
        using var server = new FileServer(200, new byte[] { 1, 2, 3 });
        using var tmp = new TempFolder();
        string dest = Path.Combine(tmp.Path, "setup.exe");
        Assert.Throws<HashMismatchException>(() => Http.DownloadAsync(server.Base + "setup.exe", dest, null, CancellationToken.None, new string('0', 64)).GetAwaiter().GetResult());
        Assert.False(File.Exists(dest));
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public void The_texts_explain_a_gone_link_in_both_languages()
    {
        foreach (var code in new[] { Loc.EnglishCode, Loc.PortugueseCode })
            Lang.With(code, () =>
            {
                Assert.Contains("404", Loc.T("Catalog_DownloadGone", "Contoso Viewer", 404));
                Assert.Contains("Contoso Viewer", Loc.T("Catalog_DownloadChanged", "Contoso Viewer"));
                Assert.Contains("HTTP 404", Loc.T("Catalog_InstallerGone", "HTTP 404"));
                Assert.Contains("1.2.0", Loc.T("Catalog_FallbackNote", "1.2.0", "1.1.0"));
            });
        Lang.With(Loc.PortugueseCode, () => Assert.Contains("link fora do ar", Loc.T("Catalog_HiddenUnsupported", 3)));
    }
}
