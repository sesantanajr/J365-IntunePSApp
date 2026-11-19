// J365 IntunePSApp - Catalog Builder
// Gera o banco de dados de aplicativos (catalog.json.gz) a partir do repositorio publico
// microsoft/winget-pkgs (licenca MIT), mantendo apenas a versao mais recente de cada pacote.
//
// Uso:
//   dotnet run --project tools/CatalogBuilder -c Release -- --out out [--curated catalog/curated.json] [--source <url|arquivo.tar.gz>]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace J365.CatalogBuilder;

public static class Program
{
    private const string DefaultSource = "https://codeload.github.com/microsoft/winget-pkgs/tar.gz/refs/heads/master";

    public static async Task<int> Main(string[] args)
    {
        string outDir = Arg(args, "--out") ?? "out";
        string source = Arg(args, "--source") ?? DefaultSource;
        string curatedPath = Arg(args, "--curated");
        Directory.CreateDirectory(outDir);
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"Fonte: {source}");
        var best = new Dictionary<string, VersionDir>(StringComparer.OrdinalIgnoreCase);
        string commit = null;
        long entries = 0, dirs = 0;

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(60) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("J365-IntunePSApp-CatalogBuilder/1.0");
            Stream input = source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? await http.GetStreamAsync(source)
                : File.OpenRead(source);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var tar = new TarReader(gz, leaveOpen: false);

            string currentDir = null;
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            void Flush()
            {
                if (currentDir == null || files.Count == 0) return;
                dirs++;
                var segs = currentDir.Split('/');
                if (segs.Length < 3) return; // letra/.../versao
                string version = segs[^1];
                string id = string.Join('.', segs.Skip(1).Take(segs.Length - 2));
                if (!best.TryGetValue(id, out var cur) || VersionCompare(version, cur.Version) > 0)
                {
                    best[id] = new VersionDir { Id = id, Version = version, Files = new Dictionary<string, string>(files) };
                }
            }

            TarEntry e;
            while ((e = tar.GetNextEntry()) != null)
            {
                entries++;
                if (e is PaxGlobalExtendedAttributesTarEntry g)
                {
                    if (g.GlobalExtendedAttributes.TryGetValue("comment", out var c)) commit = c;
                    continue;
                }
                if (e.EntryType != TarEntryType.RegularFile && e.EntryType != TarEntryType.V7RegularFile) continue;
                string name = e.Name;
                int mi = name.IndexOf("/manifests/", StringComparison.Ordinal);
                if (mi < 0 || !name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = name[(mi + 11)..];
                int slash = rel.LastIndexOf('/');
                if (slash <= 0) continue;
                string dir = rel[..slash];
                if (!string.Equals(dir, currentDir, StringComparison.Ordinal))
                {
                    Flush();
                    currentDir = dir;
                    files.Clear();
                }
                if (e.DataStream == null) continue;
                using var sr = new StreamReader(e.DataStream, Encoding.UTF8);
                files[rel[(slash + 1)..]] = sr.ReadToEnd();
                if (entries % 50000 == 0) Console.WriteLine($"  {entries:N0} entradas lidas, {best.Count:N0} pacotes ({sw.Elapsed:mm\\:ss})");
            }
            Flush();
        }
        Console.WriteLine($"Leitura concluida: {entries:N0} entradas, {dirs:N0} versoes, {best.Count:N0} pacotes ({sw.Elapsed:mm\\:ss}).");

        Dictionary<string, CuratedEntry> curated = new(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(curatedPath) && File.Exists(curatedPath))
        {
            var cf = JsonSerializer.Deserialize<CuratedFile>(File.ReadAllText(curatedPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (cf?.Apps != null) foreach (var kv in cf.Apps) curated[kv.Key] = kv.Value;
            Console.WriteLine($"Curadoria: {curated.Count} aplicativos com informacoes extras.");
        }

        var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        var apps = new List<CatalogApp>(best.Count);
        int errors = 0;
        foreach (var vd in best.Values)
        {
            try
            {
                var app = BuildApp(vd, deserializer);
                if (app == null) continue;
                if (curated.TryGetValue(app.Id, out var x)) app.Extra = x;
                apps.Add(app);
            }
            catch (Exception ex)
            {
                errors++;
                if (errors <= 20) Console.WriteLine($"  [aviso] {vd.Id} {vd.Version}: {ex.Message}");
            }
        }
        apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var catalog = new CatalogFile
        {
            Schema = 1,
            Generated = DateTime.UtcNow,
            Source = "microsoft/winget-pkgs",
            Commit = commit,
            Count = apps.Count,
            Apps = apps
        };
        var opts = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };
        string gzPath = Path.Combine(outDir, "catalog.json.gz");
        using (var fs = File.Create(gzPath))
        using (var gzOut = new GZipStream(fs, CompressionLevel.SmallestSize))
        {
            await JsonSerializer.SerializeAsync(gzOut, catalog, opts);
        }
        var meta = new { schema = 1, generated = catalog.Generated, count = catalog.Count, source = catalog.Source, commit, sizeBytes = new FileInfo(gzPath).Length };
        File.WriteAllText(Path.Combine(outDir, "catalog-meta.json"), JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Catalogo: {apps.Count:N0} aplicativos, {errors} erros, {new FileInfo(gzPath).Length / 1024.0 / 1024.0:N1} MB compactado ({sw.Elapsed:mm\\:ss}).");
        return 0;
    }

    private static string Arg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // ------------------------------------------------------------------ versoes

    public static int VersionCompare(string a, string b)
    {
        var pa = (a ?? "").Split('.');
        var pb = (b ?? "").Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            string x = i < pa.Length ? pa[i] : "0";
            string y = i < pb.Length ? pb[i] : "0";
            SplitPart(x, out long nx, out string sx);
            SplitPart(y, out long ny, out string sy);
            if (nx != ny) return nx.CompareTo(ny);
            if (sx.Length == 0 && sy.Length > 0) return 1;   // 1.0 > 1.0-beta
            if (sx.Length > 0 && sy.Length == 0) return -1;
            int c = string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return 0;
    }

    private static void SplitPart(string p, out long num, out string suffix)
    {
        int i = 0;
        while (i < p.Length && char.IsDigit(p[i])) i++;
        num = 0;
        if (i > 0) long.TryParse(p.AsSpan(0, Math.Min(i, 18)), out num);
        suffix = p[i..];
    }

    // ------------------------------------------------------------------ manifests

    private static CatalogApp BuildApp(VersionDir vd, IDeserializer des)
    {
        Dictionary<object, object> installerDoc = null, defaultLocale = null, ptLocale = null;
        foreach (var text in vd.Files.Values)
        {
            var doc = des.Deserialize<Dictionary<object, object>>(text);
            if (doc == null) continue;
            string type = S(doc, "ManifestType");
            switch (type)
            {
                case "singleton": installerDoc = doc; defaultLocale = doc; break;
                case "installer": installerDoc = doc; break;
                case "defaultLocale": defaultLocale = doc; break;
                case "locale":
                    var loc = S(doc, "PackageLocale") ?? "";
                    if (loc.StartsWith("pt", StringComparison.OrdinalIgnoreCase) && (ptLocale == null || loc.Equals("pt-BR", StringComparison.OrdinalIgnoreCase))) ptLocale = doc;
                    break;
            }
        }
        if (installerDoc == null || defaultLocale == null) return null;

        var app = new CatalogApp
        {
            Id = S(installerDoc, "PackageIdentifier") ?? vd.Id,
            Version = S(installerDoc, "PackageVersion") ?? vd.Version,
            Name = S(defaultLocale, "PackageName") ?? vd.Id,
            Publisher = S(defaultLocale, "Publisher"),
            Description = Trim(S(ptLocale, "ShortDescription") ?? S(defaultLocale, "ShortDescription"), 260),
            Homepage = S(defaultLocale, "PackageUrl") ?? S(defaultLocale, "PublisherUrl"),
            License = Trim(S(defaultLocale, "License"), 60),
            Moniker = S(defaultLocale, "Moniker"),
        };
        var tags = L(defaultLocale, "Tags").Select(t => t?.ToString()).Where(t => !string.IsNullOrWhiteSpace(t)).Take(10).ToList();
        if (tags.Count > 0) app.Tags = tags;

        var installers = new List<CatalogInstaller>();
        foreach (var item in L(installerDoc, "Installers").OfType<Dictionary<object, object>>())
        {
            object Get(string k) => item.TryGetValue(k, out var v) && v != null ? v : (installerDoc.TryGetValue(k, out var r) ? r : null);
            string GetS(string k) => Get(k)?.ToString();

            var inst = new CatalogInstaller
            {
                Arch = GetS("Architecture"),
                Type = GetS("InstallerType")?.ToLowerInvariant(),
                NestedType = GetS("NestedInstallerType")?.ToLowerInvariant(),
                Scope = GetS("Scope"),
                Locale = GetS("InstallerLocale"),
                Url = GetS("InstallerUrl"),
                Sha256 = GetS("InstallerSha256")?.ToUpperInvariant(),
                ProductCode = GetS("ProductCode"),
                Elevation = GetS("ElevationRequirement"),
            };
            if (inst.Type == "msstore" || string.IsNullOrEmpty(inst.Url)) continue;

            var sw = new Dictionary<object, object>();
            if (installerDoc.TryGetValue("InstallerSwitches", out var rs) && rs is Dictionary<object, object> rsd) foreach (var kv in rsd) sw[kv.Key] = kv.Value;
            if (item.TryGetValue("InstallerSwitches", out var isw) && isw is Dictionary<object, object> iswd) foreach (var kv in iswd) sw[kv.Key] = kv.Value;
            inst.Silent = S(sw, "Silent");
            inst.SilentWithProgress = S(sw, "SilentWithProgress");
            inst.Custom = S(sw, "Custom");

            var codes = (Get("InstallerSuccessCodes") as List<object>)?.Select(o => long.TryParse(o?.ToString(), out var c) ? c : (long?)null).Where(c => c.HasValue).Select(c => c.Value).ToList();
            if (codes != null && codes.Count > 0) inst.SuccessCodes = codes;

            var arp = new List<ArpEntry>();
            foreach (var a in ((Get("AppsAndFeaturesEntries") as List<object>) ?? new List<object>()).OfType<Dictionary<object, object>>())
            {
                var entry = new ArpEntry
                {
                    DisplayName = S(a, "DisplayName"),
                    DisplayVersion = S(a, "DisplayVersion"),
                    Publisher = S(a, "Publisher"),
                    ProductCode = S(a, "ProductCode"),
                    UpgradeCode = S(a, "UpgradeCode"),
                };
                if (entry.DisplayName != null || entry.DisplayVersion != null || entry.ProductCode != null || entry.UpgradeCode != null) arp.Add(entry);
            }
            if (arp.Count > 0) inst.Arp = arp;
            installers.Add(inst);
        }
        if (installers.Count == 0) return null;
        app.Installers = installers;
        return app;
    }

    private static string S(Dictionary<object, object> d, string key)
    {
        if (d == null || !d.TryGetValue(key, out var v) || v == null) return null;
        var s = v.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static List<object> L(Dictionary<object, object> d, string key)
        => d != null && d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();

    private static string Trim(string s, int max)
    {
        if (s == null) return null;
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}

public class VersionDir
{
    public string Id;
    public string Version;
    public Dictionary<string, string> Files;
}

public class CatalogFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("generated")] public DateTime Generated { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; }
    [JsonPropertyName("commit")] public string Commit { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("apps")] public List<CatalogApp> Apps { get; set; }
}

public class CatalogApp
{
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("n")] public string Name { get; set; }
    [JsonPropertyName("p")] public string Publisher { get; set; }
    [JsonPropertyName("v")] public string Version { get; set; }
    [JsonPropertyName("d")] public string Description { get; set; }
    [JsonPropertyName("hp")] public string Homepage { get; set; }
    [JsonPropertyName("lic")] public string License { get; set; }
    [JsonPropertyName("m")] public string Moniker { get; set; }
    [JsonPropertyName("tg")] public List<string> Tags { get; set; }
    [JsonPropertyName("i")] public List<CatalogInstaller> Installers { get; set; }
    [JsonPropertyName("x")] public CuratedEntry Extra { get; set; }
}

public class CatalogInstaller
{
    [JsonPropertyName("a")] public string Arch { get; set; }
    [JsonPropertyName("t")] public string Type { get; set; }
    [JsonPropertyName("nt")] public string NestedType { get; set; }
    [JsonPropertyName("s")] public string Scope { get; set; }
    [JsonPropertyName("loc")] public string Locale { get; set; }
    [JsonPropertyName("u")] public string Url { get; set; }
    [JsonPropertyName("h")] public string Sha256 { get; set; }
    [JsonPropertyName("pc")] public string ProductCode { get; set; }
    [JsonPropertyName("sw")] public string Silent { get; set; }
    [JsonPropertyName("swp")] public string SilentWithProgress { get; set; }
    [JsonPropertyName("cu")] public string Custom { get; set; }
    [JsonPropertyName("el")] public string Elevation { get; set; }
    [JsonPropertyName("sc")] public List<long> SuccessCodes { get; set; }
    [JsonPropertyName("arp")] public List<ArpEntry> Arp { get; set; }
}

public class ArpEntry
{
    [JsonPropertyName("dn")] public string DisplayName { get; set; }
    [JsonPropertyName("dv")] public string DisplayVersion { get; set; }
    [JsonPropertyName("pub")] public string Publisher { get; set; }
    [JsonPropertyName("pc")] public string ProductCode { get; set; }
    [JsonPropertyName("uc")] public string UpgradeCode { get; set; }
}

public class CuratedFile
{
    [JsonPropertyName("apps")] public Dictionary<string, CuratedEntry> Apps { get; set; }
}

public class CuratedEntry
{
    [JsonPropertyName("processes")] public List<string> Processes { get; set; }
    [JsonPropertyName("namePattern")] public string NamePattern { get; set; }
    [JsonPropertyName("uninstallArgs")] public string UninstallArgs { get; set; }
    [JsonPropertyName("installArgs")] public string InstallArgs { get; set; }
    [JsonPropertyName("detectionFile")] public string DetectionFile { get; set; }
    [JsonPropertyName("checkVersion")] public bool? CheckVersion { get; set; }
    [JsonPropertyName("removePrevious")] public bool? RemovePrevious { get; set; }
    [JsonPropertyName("notes")] public string Notes { get; set; }
}
