using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Asks whether the link of an installer still gives the file, as the application will ask when it downloads it.
/// Vendors remove old files and time-limited links expire long before the winget manifest changes (about 2.5% of the
/// links of the database answered 404 in November 2026, Autodesk DWG TrueView among them), and a link that does not
/// answer is a package that cannot be built.
///
/// Only an answer that the file is gone counts against a link: 404 or 410 (confirmed with a GET, because some servers
/// refuse HEAD with a 404), or a host name that no longer exists (asked twice). A refusal (403, 429), a server error or
/// a timeout says nothing about the file: the link stays usable. Each host gets a few requests at a time.
/// </summary>
public sealed class LinkChecker : IDisposable
{
    public enum State { Ok, Gone, Unknown }

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _all;
    private readonly int _perHost;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<(State State, string Detail)>> _results = new(StringComparer.Ordinal);

    public LinkChecker(int parallel = 48, int perHost = 8, TimeSpan? timeout = null, HttpMessageHandler handler = null)
    {
        _http = handler == null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10, AutomaticDecompression = DecompressionMethods.None, PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            : new HttpClient(handler);
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(25);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("J365-IntunePSApp-CatalogBuilder/3.0 (+https://github.com/sesantanajr/J365-IntunePSApp)");
        _all = new SemaphoreSlim(parallel);
        _perHost = perHost;
    }

    public int Checked => _results.Count;

    /// <summary>What a link answers; each address is asked once per run.</summary>
    public Task<(State State, string Detail)> CheckAsync(string url) => _results.GetOrAdd(url ?? "", u => ProbeAsync(u));

    /// <summary>Asks every link, a few per host at a time; <paramref name="report"/> hears every 1000 answers.</summary>
    public async Task CheckAllAsync(IEnumerable<string> urls, Action<int> report = null)
    {
        int done = 0;
        await Task.WhenAll(urls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.Ordinal).Select(async u =>
        {
            await CheckAsync(u);
            int n = Interlocked.Increment(ref done);
            if (n % 1000 == 0) report?.Invoke(n);
        }));
    }

    /// <summary>The answer already known for a link (Unknown when it was not asked).</summary>
    public (State State, string Detail) Known(string url)
        => url != null && _results.TryGetValue(url, out var t) && t.IsCompletedSuccessfully ? t.Result : (State.Unknown, "");

    private async Task<(State State, string Detail)> ProbeAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return (State.Unknown, "address");
        var host = _hosts.GetOrAdd(uri.Host, _ => new SemaphoreSlim(_perHost));
        await _all.WaitAsync();
        await host.WaitAsync();
        try
        {
            var first = await AskAsync(uri, HttpMethod.Head);
            if (first.State == State.Ok) return first;
            // A refused or failed HEAD, and a 404 to HEAD, are asked again with a GET of one byte: the file decides.
            var second = await AskAsync(uri, HttpMethod.Get);
            if (second.State == State.Gone && second.Detail == "dns")
            {
                // A host name that does not resolve is asked once more a little later: one failed lookup is not proof.
                await Task.Delay(TimeSpan.FromSeconds(3));
                second = await AskAsync(uri, HttpMethod.Get);
            }
            return second;
        }
        finally
        {
            host.Release();
            _all.Release();
        }
    }

    private async Task<(State State, string Detail)> AskAsync(Uri uri, HttpMethod method)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            if (method == HttpMethod.Get) request.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return Classify((int)response.StatusCode);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.NameResolutionError) { return (State.Gone, "dns"); }
        catch (HttpRequestException ex) { return (State.Unknown, ex.HttpRequestError.ToString()); }
        catch (TaskCanceledException) { return (State.Unknown, "timeout"); }
        catch (Exception ex) { return (State.Unknown, ex.GetType().Name); }
    }

    /// <summary>2xx and 3xx: the file is there; 404 and 410: it is gone; anything else says nothing about it.</summary>
    public static (State State, string Detail) Classify(int status)
        => status is >= 200 and < 400 ? (State.Ok, status.ToString())
            : status is 404 or 410 ? (State.Gone, status.ToString())
            : (State.Unknown, status.ToString());

    public void Dispose()
    {
        _http.Dispose();
        _all.Dispose();
        foreach (var h in _hosts.Values) h.Dispose();
    }
}
