using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using J365.IntunePSApp.Core;
using J365.IntunePSApp.Localization;
using Xunit;

namespace J365.IntunePSApp.Tests;

/// <summary>
/// Local stand-in for the Microsoft Graph calls of Intune: those of the publication (an app created, its content uploaded
/// to "Azure Storage" and committed, its supersedence and assignment) and those of the status (the app, its assignments
/// and relationships, the device install report, its export). It records every request.
/// </summary>
internal sealed class IntuneGraphMock : IDisposable
{
    private readonly HttpListener _listener = new HttpListener();
    private bool _committed;
    private int _exportPolls;
    public string Base { get; }
    public List<(string Method, string Path, string Body)> Calls { get; } = new List<(string, string, string)>();

    public const string NewAppId = "aaaaaaaa-0000-4000-8000-000000000001";
    /// <summary>Win32 apps of the tenant: the lookup by name answers them, and each is read by its id.</summary>
    public List<JsonObject> Existing { get; } = new List<JsonObject>();
    public bool RefuseRelationships { get; set; }

    public JsonObject App { get; set; }
    public JsonArray Assignments { get; set; } = new JsonArray();
    public JsonArray Relationships { get; set; } = new JsonArray();
    /// <summary>Report functions this "tenant" does not have: Microsoft Graph answers 400 "Resource not found for the segment".</summary>
    public HashSet<string> MissingFunctions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>403: Intune refuses the report to this connection.</summary>
    public int ReportStatus { get; set; } = 200;
    public static readonly string[] ReportColumns = { "DeviceName", "UserPrincipalName", "InstallState", "InstallStateDetail", "AppInstallState", "ErrorCode", "HexErrorCode", "LastModifiedDateTime", "DeviceId", "UserId" };
    /// <summary>Rows of the report, values in the order of <see cref="ReportColumns"/>.</summary>
    public List<object[]> Rows { get; } = new List<object[]>();

    public IntuneGraphMock()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
        Base = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Base + "/");
        _listener.Start();
        Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { return; }
                try { Handle(ctx); } catch { try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { } }
            }
        });
    }

    public static JsonObject Win32App(string id, string name, string version, string created) => new JsonObject
    {
        ["@odata.type"] = "#microsoft.graph.win32LobApp", ["id"] = id, ["displayName"] = name, ["displayVersion"] = version,
        ["createdDateTime"] = created, ["notes"] = "Packaged with J365 IntunePSApp"
    };

    private static void Reply(HttpListenerContext ctx, int status, string json = null, string type = "application/json")
    {
        ctx.Response.StatusCode = status;
        if (json != null)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = type;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        ctx.Response.Close();
    }

    private static string Error(string code, string message) => new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        string path = Uri.UnescapeDataString(req.Url.AbsolutePath);
        string query = Uri.UnescapeDataString(req.Url.Query);
        string body = new StreamReader(req.InputStream, Encoding.UTF8).ReadToEnd();
        lock (Calls) Calls.Add((req.HttpMethod, path + query, body));

        if (path.EndsWith("/oauth2/v2.0/token"))
        {
            Reply(ctx, 200, new JsonObject { ["token_type"] = "Bearer", ["access_token"] = "mock-access", ["refresh_token"] = "mock-refresh-2", ["expires_in"] = 3600 }.ToJsonString());
            return;
        }
        // "Azure Storage": the signed upload address of the content, and the download of an exported report.
        if (path.StartsWith("/blob/")) { Reply(ctx, 201); return; }
        if (path == "/download/report.zip")
        {
            var objects = new JsonArray(Rows.Select(r =>
            {
                var o = new JsonObject();
                for (int i = 0; i < ReportColumns.Length; i++) o[ReportColumns[i]] = JsonValue.Create(r[i]?.ToString());
                return (JsonNode)o;
            }).ToArray());
            using var zipped = new MemoryStream();
            using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
            using (var writer = new StreamWriter(zip.CreateEntry("DeviceInstallStatusByApp_00000000.json").Open(), new UTF8Encoding(false)))
                writer.Write(new JsonObject { ["values"] = objects }.ToJsonString());
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/zip";
            ctx.Response.OutputStream.Write(zipped.ToArray(), 0, (int)zipped.Length);
            ctx.Response.Close();
            return;
        }
        string auth = req.Headers["Authorization"] ?? "";
        if (auth != "Bearer mock-access") { Reply(ctx, 401, Error("InvalidAuthenticationToken", "no token")); return; }

        const string apps = "/beta/deviceAppManagement/mobileApps";
        const string reports = "/beta/deviceManagement/reports/";
        string method = req.HttpMethod;
        if (path == apps && method == "GET") { Reply(ctx, 200, new JsonObject { ["value"] = new JsonArray(Existing.Select(a => (JsonNode)a.DeepClone()).ToArray()) }.ToJsonString()); return; }
        if (path == apps && method == "POST")
        {
            var created = JsonNode.Parse(body)!.AsObject();
            created["id"] = NewAppId;
            Reply(ctx, 201, created.ToJsonString());
            return;
        }
        if (path.StartsWith(apps + "/"))
        {
            string rest = path.Substring(apps.Length + 1);
            string id = rest.Split('/')[0];
            string tail = rest.Substring(id.Length);
            if (tail.Length == 0 && method == "GET")
            {
                if (query.Contains("$select=publishingState")) { Reply(ctx, 200, "{\"publishingState\":\"published\"}"); return; }
                var found = Existing.FirstOrDefault(a => (string)a["id"] == id) ?? (App != null && (string)App["id"] == id ? App : null);
                if (found == null) Reply(ctx, 404, Error("ResourceNotFound", "The app was not found."));
                else Reply(ctx, 200, found.ToJsonString());
                return;
            }
            if (tail.Length == 0 && (method == "PATCH" || method == "DELETE")) { Reply(ctx, 204); return; }
            if (tail == "/microsoft.graph.win32LobApp/contentVersions" && method == "POST") { Reply(ctx, 201, "{\"id\":\"1\"}"); return; }
            if (tail == "/microsoft.graph.win32LobApp/contentVersions/1/files" && method == "POST") { Reply(ctx, 201, "{\"id\":\"file-1\"}"); return; }
            if (tail == "/microsoft.graph.win32LobApp/contentVersions/1/files/file-1" && method == "GET")
            {
                Reply(ctx, 200, new JsonObject { ["uploadState"] = _committed ? "commitFileSuccess" : "azureStorageUriRequestSuccess", ["azureStorageUri"] = Base + "/blob/content?sv=1" }.ToJsonString());
                return;
            }
            if (tail == "/microsoft.graph.win32LobApp/contentVersions/1/files/file-1/commit" && method == "POST") { _committed = true; Reply(ctx, 204); return; }
            if (tail == "/updateRelationships" && method == "POST")
            {
                if (RefuseRelationships) Reply(ctx, 400, Error("BadRequest", "The supersedence graph has too many nodes."));
                else Reply(ctx, 204);
                return;
            }
            if (tail == "/assign" && method == "POST") { Reply(ctx, 204); return; }
            if (tail == "/assignments" && method == "GET") { Reply(ctx, 200, new JsonObject { ["value"] = Assignments.DeepClone() }.ToJsonString()); return; }
            if (tail == "/relationships" && method == "GET") { Reply(ctx, 200, new JsonObject { ["value"] = Relationships.DeepClone() }.ToJsonString()); return; }
        }
        if (path.StartsWith(reports))
        {
            string function = path.Substring(reports.Length);
            if (method == "GET" && function.StartsWith("exportJobs('"))
            {
                Reply(ctx, 200, _exportPolls++ == 0 ? "{\"id\":\"job-1\",\"status\":\"inProgress\"}"
                    : new JsonObject { ["id"] = "job-1", ["status"] = "completed", ["url"] = Base + "/download/report.zip" }.ToJsonString());
                return;
            }
            if (MissingFunctions.Contains(function)) { Reply(ctx, 400, Error("BadRequest", $"Resource not found for the segment '{function}'.")); return; }
            if (ReportStatus != 200) { Reply(ctx, ReportStatus, Error("Forbidden", "Application is not authorized to perform this operation.")); return; }
            if (function == "exportJobs") { Reply(ctx, 201, "{\"id\":\"job-1\",\"status\":\"notStarted\"}"); return; }
            var ask = JsonNode.Parse(body)!;
            int skip = (int)ask["skip"]!, top = (int)ask["top"]!;
            static JsonNode Value(object v) => v switch { int n => JsonValue.Create(n), string s => JsonValue.Create(s), _ => null };
            var values = new JsonArray(Rows.Skip(skip).Take(top).Select(r => (JsonNode)new JsonArray(r.Select(Value).ToArray())).ToArray());
            var schema = new JsonArray(ReportColumns.Select(c => (JsonNode)new JsonObject { ["Column"] = c, ["PropertyType"] = "String" }).ToArray());
            // The report itself is sent as a stream, as Intune does.
            Reply(ctx, 200, new JsonObject { ["TotalRowCount"] = Rows.Count, ["Schema"] = schema, ["Values"] = values }.ToJsonString(), "application/octet-stream");
            return;
        }
        Reply(ctx, 404, Error("NotFound", method + " " + path));
    }

    public List<(string Method, string Path, string Body)> Requests(string method, string pathPart)
    {
        lock (Calls) return Calls.Where(c => c.Method == method && c.Path.Contains(pathPart)).ToList();
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}

/// <summary>2.8.0: a new version that supersedes the earlier one in Intune, and the install status of a published app.</summary>
public class Release28Tests
{
    private const string Tenant = "bbbbbbbb-1111-2222-3333-444444444444";
    private const string Client = "cccccccc-1111-2222-3333-444444444444";

    // ------------------------------------------------------------------ supersedence: the options

    [Fact]
    public void Supersedence_reaches_the_publishing_script_with_its_type()
    {
        var o = new PublishOptions { TenantId = Tenant, ClientId = Client, IfExists = "Supersede" };
        string args = IntunePublisher.Arguments(@"C:\p\Publish-ToIntune.ps1", o);
        Assert.Contains("-IfExists Supersede", args);
        // Update is the default of the script: it is not repeated.
        Assert.DoesNotContain("-SupersedenceType", args);
        o.SupersedenceType = "Replace";
        o.AppId = "1d7e4c2b-9f3a-4b8e-a5c6-7e2f1d0b9a83";
        args = IntunePublisher.Arguments(@"C:\p\Publish-ToIntune.ps1", o);
        Assert.Contains("-IfExists Supersede -SupersedenceType Replace", args);
        Assert.Contains("-AppId 1d7e4c2b-9f3a-4b8e-a5c6-7e2f1d0b9a83", args);
        Assert.Null(IntunePublisher.Check(o));
        o.SupersedenceType = "Sideways";
        Assert.Equal("Publish_InvalidSupersedence", IntunePublisher.Check(o));
        // The type only matters to a supersedence.
        o.IfExists = "Update";
        Assert.Null(IntunePublisher.Check(o));
        Assert.DoesNotContain("-SupersedenceType", IntunePublisher.Arguments(@"C:\p\Publish-ToIntune.ps1", o));
    }

    [Fact]
    public void A_batch_supersedes_in_any_case_and_refuses_unknown_choices()
    {
        var b = new BatchOptions { IfExists = "supersede", SupersedenceType = "replace" };
        var po = b.PublishOptions(certificate: false);
        Assert.Equal("Supersede", po.IfExists);
        Assert.Equal("Replace", po.SupersedenceType);
        Assert.Equal("Update", new BatchOptions { IfExists = "whatever" }.PublishOptions(false).IfExists);
        Assert.Equal("CreateNew", BatchOptions.Choice(" createnew ", "Update", "Supersede", "CreateNew", "Fail"));
        Assert.Null(BatchOptions.Choice("sideways", "Update", "Replace"));
    }

    [Fact]
    public void The_outcome_tells_the_supersedence_and_its_failure() => Lang.With("en", () =>
    {
        var r = new PublishResult
        {
            Status = "published", Action = "created", DisplayName = "7-Zip", TenantId = "contoso.onmicrosoft.com",
            SupersededAppId = "old-2", SupersededName = "7-Zip", SupersededVersion = "25.01", SupersedenceType = "update", Supersedence = ""
        };
        Assert.Contains(Loc.T("Ed_PublishSupersedesUpdate", "7-Zip 25.01"), PublishTexts.Summary(r));
        r.SupersedenceType = "replace";
        Assert.Contains(Loc.T("Ed_PublishSupersedesReplace", "7-Zip 25.01"), PublishTexts.Summary(r));
        r.Supersedence = "failed: Microsoft Graph answered HTTP 400 (BadRequest)";
        Assert.True(r.SupersedenceFailed);
        Assert.Contains(Loc.T("Ed_PublishSupersedeFailed", "7-Zip 25.01", "Microsoft Graph answered HTTP 400 (BadRequest)"), PublishTexts.Summary(r));
        // Names made before 2.5 carry the version: it is not repeated.
        Assert.Equal("7-Zip 25.01", PublishTexts.AppLabel("7-Zip 25.01", "25.01"));
        Assert.Equal("7-Zip", PublishTexts.AppLabel("7-Zip", ""));
        Assert.Equal(Loc.T("Publish_StepSuperseding"), PublishTexts.StepText("Superseding \"7-Zip\" 25.01..."));
    });

    // ------------------------------------------------------------------ supersedence: the publishing script, end to end

    private const string DetectionXml = "<ApplicationInfo ToolVersion=\"1.8.6\"><Name>Invoke-AppDeployToolkit.exe</Name>"
        + "<UnencryptedContentSize>2048</UnencryptedContentSize><FileName>IntunePackage.intunewin</FileName><SetupFile>Invoke-AppDeployToolkit.exe</SetupFile>"
        + "<EncryptionInfo><EncryptionKey>a2V5</EncryptionKey><MacKey>bWFj</MacKey><InitializationVector>aXY=</InitializationVector><Mac>bWFj</Mac>"
        + "<ProfileIdentifier>ProfileVersion1</ProfileIdentifier><FileDigest>ZGlnZXN0</FileDigest><FileDigestAlgorithm>SHA256</FileDigestAlgorithm></EncryptionInfo></ApplicationInfo>";

    /// <summary>The Intune folder of a package "Contoso Tool 2.0.0", with its .intunewin, detection, intune-app.json and the current script.</summary>
    private static string Package(TempFolder tmp)
    {
        string intune = Path.Combine(tmp.Path, PackageLayout.IntuneDir);
        Directory.CreateDirectory(intune);
        using (var zip = ZipFile.Open(Path.Combine(intune, "Contoso_Tool_2.0.0.intunewin"), ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("IntuneWinPackage/Metadata/Detection.xml").Open(), new UTF8Encoding(false))) w.Write(DetectionXml);
            using var content = zip.CreateEntry("IntuneWinPackage/Contents/IntunePackage.intunewin").Open();
            content.Write(new byte[3000], 0, 3000);
        }
        File.WriteAllText(Path.Combine(intune, "Detection_Contoso_Tool.ps1"), "Write-Output 'Detected'", new UTF8Encoding(false));
        var meta = new JsonObject
        {
            ["displayName"] = "Contoso Tool", ["appVersion"] = "2.0.0", ["publisher"] = "Contoso", ["description"] = "Line-of-business tool.",
            ["intuneWinFile"] = "Contoso_Tool_2.0.0.intunewin", ["detectionScript"] = "Detection_Contoso_Tool.ps1",
            ["installCommandLine"] = "Invoke-AppDeployToolkit.exe -DeploymentType Install", ["uninstallCommandLine"] = "Invoke-AppDeployToolkit.exe -DeploymentType Uninstall",
            ["installExperience"] = "system", ["architecture"] = "x64", ["minimumSupportedWindowsRelease"] = "W10_1607",
            ["restartBehavior"] = "basedOnReturnCode", ["notifications"] = "showAll", ["maximumInstallationTimeInMinutes"] = 60, ["notes"] = "Packaged with J365 IntunePSApp"
        };
        File.WriteAllText(Path.Combine(intune, PackageLayout.MetadataFile), meta.ToJsonString(), new UTF8Encoding(false));
        return IntunePublisher.Prepare(intune);
    }

    /// <summary>Publish-ToIntune.ps1 in Windows PowerShell 5.1, as the application runs it (no window, the session handed over).</summary>
    private static (int ExitCode, string Output, PublishResult Result) Publish(string script, IntuneGraphMock mock, string arguments)
    {
        var psi = ToolsService.PowerShellStartInfo($"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -TenantId {Tenant} -ClientId {Client} -Force -GraphUrl \"{mock.Base}\" -LoginUrl \"{mock.Base}/login\" {arguments}");
        psi.Environment["J365_GRAPH_REFRESH_TOKEN"] = "mock-refresh";
        var run = ToolsService.RunPowerShell(psi, 180000);
        Assert.False(run.TimedOut, "The publication did not finish.");
        return (run.ExitCode, run.Output, IntunePublisher.ReadResult(Path.GetDirectoryName(script)));
    }

    [Fact]
    public void The_new_app_supersedes_the_newest_earlier_version_before_it_is_assigned()
    {
        using var tmp = new TempFolder();
        using var mock = new IntuneGraphMock();
        // Two earlier versions: the newest by version (1.10.0), not by text ("1.2.0" > "1.10.0") nor by list order.
        mock.Existing.Add(IntuneGraphMock.Win32App("old-2", "Contoso Tool", "1.10.0", "2026-03-10T10:00:00Z"));
        mock.Existing.Add(IntuneGraphMock.Win32App("old-1", "Contoso Tool", "1.2.0", "2026-05-10T10:00:00Z"));
        string script = Package(tmp);
        var (exit, output, r) = Publish(script, mock, "-IfExists Supersede -SupersedenceType Replace -AssignIntent required -AssignTo Group -GroupId 3f2b9c1e-7a4d-4e8b-b6c5-0d9e1f2a3b4c");

        Assert.True(exit == 0, output);
        Assert.NotNull(r);
        Assert.True(r.IsPublished, r.Error);
        Assert.Equal("created", r.Action);
        Assert.Equal(IntuneGraphMock.NewAppId, r.AppId);
        Assert.Equal("old-2", r.SupersededAppId);
        Assert.Equal("1.10.0", r.SupersededVersion);
        Assert.Equal("replace", r.SupersedenceType);
        Assert.True(string.IsNullOrEmpty(r.Supersedence));
        Assert.False(r.SupersedenceFailed);

        // The link, as the Intune admin center makes it: the relationships of the new app.
        var link = Assert.Single(mock.Requests("POST", "/updateRelationships"));
        Assert.Contains("/mobileApps/" + IntuneGraphMock.NewAppId + "/updateRelationships", link.Path);
        var relation = JsonNode.Parse(link.Body)!["relationships"]!.AsArray().Single()!;
        Assert.Equal("#microsoft.graph.mobileAppSupersedence", (string)relation["@odata.type"]);
        Assert.Equal("old-2", (string)relation["targetId"]);
        Assert.Equal("replace", (string)relation["supersedenceType"]);
        // Linked once the content is committed, before the assignment; nothing is deleted, and the earlier apps are untouched.
        int Index(string method, string part) { lock (mock.Calls) return mock.Calls.FindIndex(c => c.Method == method && c.Path.Contains(part)); }
        Assert.True(Index("POST", "/commit") < Index("POST", "/updateRelationships"));
        Assert.True(Index("POST", "/updateRelationships") < Index("POST", "/assign"));
        Assert.Empty(mock.Requests("DELETE", "/mobileApps/"));
        Assert.Empty(mock.Requests("PATCH", "/mobileApps/old-"));
        Assert.Contains("Supersedes \"Contoso Tool\" 1.10.0 (replace)", output);
    }

    [Fact]
    public void A_refused_supersedence_keeps_the_published_app_and_says_why()
    {
        using var tmp = new TempFolder();
        using var mock = new IntuneGraphMock { RefuseRelationships = true };
        mock.Existing.Add(IntuneGraphMock.Win32App("1d7e4c2b-9f3a-4b8e-a5c6-7e2f1d0b9a83", "Contoso Tool", "1.9.0", "2026-03-10T10:00:00Z"));
        string script = Package(tmp);
        // The earlier version chosen in the application: read by its id, not looked up by name.
        var (exit, output, r) = Publish(script, mock, "-IfExists Supersede -AppId 1d7e4c2b-9f3a-4b8e-a5c6-7e2f1d0b9a83");

        Assert.True(exit == 0, output);
        Assert.True(r.IsPublished, r.Error);
        Assert.Equal("1d7e4c2b-9f3a-4b8e-a5c6-7e2f1d0b9a83", r.SupersededAppId);
        Assert.Equal("update", r.SupersedenceType);
        Assert.True(r.SupersedenceFailed);
        Assert.Contains("too many nodes", r.Supersedence);
        Assert.Single(mock.Requests("GET", "/mobileApps/1d7e4c2b-9f3a-4b8e-a5c6-7e2f1d0b9a83"));
        Assert.Empty(mock.Requests("GET", "$filter="));
        // The complete app stays.
        Assert.Empty(mock.Requests("DELETE", "/mobileApps/"));
        Assert.Contains("Properties > Supersedence", output);
    }

    [Fact]
    public void Without_an_earlier_version_the_app_is_created_without_supersedence()
    {
        using var tmp = new TempFolder();
        using var mock = new IntuneGraphMock();
        string script = Package(tmp);
        var (exit, output, r) = Publish(script, mock, "-IfExists Supersede");

        Assert.True(exit == 0, output);
        Assert.True(r.IsPublished, r.Error);
        Assert.Equal("created", r.Action);
        Assert.False(r.Supersedes);
        Assert.Empty(mock.Requests("POST", "/updateRelationships"));
        Assert.Contains("no earlier version to supersede", output);
    }

    [Fact]
    public void The_publishing_script_stays_ascii_and_parses()
    {
        string script = PackageBuilder.EmbeddedText("Publish-ToIntune.ps1");
        Assert.True(Ps.IsAscii(script));
        Ps.AssertParses(script);
        Assert.Contains("[ValidateSet('Ask', 'Fail', 'Update', 'CreateNew', 'Supersede')]", script);
        Assert.Contains("'#microsoft.graph.mobileAppSupersedence'", script);
    }

    // ------------------------------------------------------------------ the status of a published app

    private const string AppId = "6c1f0e2a-4b7d-4f3e-9a51-2d8e7c0b5f14";

    private static IntuneGraphMock StatusMock()
    {
        var mock = new IntuneGraphMock
        {
            App = new JsonObject { ["@odata.type"] = "#microsoft.graph.win32LobApp", ["id"] = AppId, ["displayName"] = "Contoso Tool", ["displayVersion"] = "2.0.0", ["publishingState"] = "published" },
            Assignments = new JsonArray(
                new JsonObject { ["intent"] = "required", ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget", ["groupId"] = "g-pilot" } },
                new JsonObject { ["intent"] = "available", ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.allLicensedUsersAssignmentTarget" } }),
            Relationships = new JsonArray(
                new JsonObject { ["@odata.type"] = "#microsoft.graph.mobileAppSupersedence", ["targetId"] = "old-2", ["targetDisplayName"] = "Contoso Tool", ["targetDisplayVersion"] = "1.10.0", ["targetType"] = "child", ["supersedenceType"] = "update" },
                new JsonObject { ["@odata.type"] = "#microsoft.graph.mobileAppDependency", ["targetId"] = "dep-1", ["targetDisplayName"] = "Runtime", ["targetType"] = "child" })
        };
        // 120 devices: 100 installed, 12 failed (one of them by number), 5 pending, 3 not applicable.
        for (int i = 0; i < 120; i++)
        {
            object[] row = i < 100 ? new object[] { $"PC-{i:000}", $"user{i}@contoso.com", "Installed", "noAdditionalDetails", 1, 0, "", "2026-11-20T10:00:00Z", $"d-{i}", $"u-{i}" }
                : i < 111 ? new object[] { $"PC-{i:000}", $"user{i}@contoso.com", "Failed", "noAdditionalDetails", 2, -2016345060, "", "2026-11-21T10:00:00Z", $"d-{i}", $"u-{i}" }
                : i < 112 ? new object[] { $"PC-{i:000}", $"user{i}@contoso.com", "", "", 2, 0, "0x80070652", "2026-11-21T11:00:00Z", $"d-{i}", $"u-{i}" }
                : i < 117 ? new object[] { $"PC-{i:000}", $"user{i}@contoso.com", "Install Pending", "contentDownloaded", 5, 0, "", "2026-11-22T10:00:00Z", $"d-{i}", $"u-{i}" }
                : new object[] { $"PC-{i:000}", $"user{i}@contoso.com", "Not Applicable", "", -1, 0, "", "2026-11-22T12:00:00Z", $"d-{i}", $"u-{i}" };
            mock.Rows.Add(row);
        }
        return mock;
    }

    /// <summary>Reads the status from the mock (blocking: the texts are compared in the language set by the test).</summary>
    private static AppDeployment ReadStatus(IntuneGraphMock mock)
    {
        string before = Environment.GetEnvironmentVariable("J365_GRAPH_URL");
        Environment.SetEnvironmentVariable("J365_GRAPH_URL", mock.Base);
        try { return Task.Run(() => IntuneAppStatus.ReadAsync(new Graph("mock-access"), AppId, CancellationToken.None)).GetAwaiter().GetResult(); }
        finally { Environment.SetEnvironmentVariable("J365_GRAPH_URL", before); }
    }

    [Fact]
    public void The_status_reads_every_page_of_the_install_report_after_a_renamed_function() => Lang.With("en", () =>
    {
        using var mock = StatusMock();
        mock.MissingFunctions.Add("retrieveDeviceAppInstallationStatusReport");
        var d = ReadStatus(mock);

        Assert.Equal("Contoso Tool", d.Name);
        Assert.Equal("2.0.0", d.Version);
        Assert.True(d.ReportRead, d.ReportProblem);
        Assert.Equal(120, d.Total);
        Assert.Equal(120, d.Devices.Count);
        Assert.False(d.Partial);
        Assert.Equal(100, d.Count(InstallOutcome.Installed));
        Assert.Equal(12, d.Count(InstallOutcome.Failed));
        Assert.Equal(5, d.Count(InstallOutcome.Pending));
        Assert.Equal(3, d.Count(InstallOutcome.NotApplicable));
        // Error codes as Intune shows them; "no details" is no detail.
        Assert.Equal("0x87D1041C", d.Devices.First(x => x.Device == "PC-100").ErrorCode);
        Assert.Equal("0x80070652", d.Devices.First(x => x.Device == "PC-111").ErrorCode);
        Assert.Equal("", d.Devices.First(x => x.Device == "PC-000").Detail);
        Assert.Equal("contentDownloaded", d.Devices.First(x => x.Device == "PC-112").Detail);
        // The renamed function was tried once; the report then came in pages of 50.
        Assert.Single(mock.Requests("POST", "retrieveDeviceAppInstallationStatusReport"));
        var pages = mock.Requests("POST", "getDeviceInstallStatusReport");
        Assert.Equal(new[] { 0, 50, 100 }, pages.Select(p => (int)JsonNode.Parse(p.Body)!["skip"]!).ToArray());
        Assert.Equal("(ApplicationId eq '" + AppId + "')", (string)JsonNode.Parse(pages[0].Body)!["filter"]);
        // Assignments and the supersedence (the dependency is not shown).
        Assert.Equal(new[] { "Required for the group g-pilot", "Available for all users" }, d.Assignments.ToArray());
        var relation = Assert.Single(d.Relations);
        Assert.True(relation.Supersedes);
        Assert.Equal("1.10.0", relation.Version);
        Assert.Equal("update", relation.Type);
    });

    [Fact]
    public void A_refused_report_still_shows_the_app_and_its_assignments() => Lang.With("en", () =>
    {
        using var mock = StatusMock();
        mock.ReportStatus = 403;
        var d = ReadStatus(mock);
        Assert.False(d.ReportRead);
        Assert.StartsWith("Intune did not let this connection read the install report", d.ReportProblem);
        Assert.Equal(2, d.Assignments.Count);
        Assert.Empty(d.Devices);
        // A refusal is not a missing function: the export is not tried.
        Assert.Empty(mock.Requests("POST", "exportJobs"));
    });

    [Fact]
    public void The_report_is_exported_when_no_function_returns_it() => Lang.With("en", () =>
    {
        using var mock = StatusMock();
        mock.MissingFunctions.Add("retrieveDeviceAppInstallationStatusReport");
        mock.MissingFunctions.Add("getDeviceInstallStatusReport");
        var d = ReadStatus(mock);
        Assert.True(d.ReportRead, d.ReportProblem);
        Assert.Equal(120, d.Devices.Count);
        Assert.Equal(12, d.Count(InstallOutcome.Failed));
        var job = JsonNode.Parse(Assert.Single(mock.Requests("POST", "exportJobs")).Body)!;
        Assert.Equal("DeviceInstallStatusByApp", (string)job["reportName"]);
        Assert.Equal("json", (string)job["format"]);
        Assert.True(mock.Requests("GET", "exportJobs('job-1')").Count >= 2);
    });

    [Fact]
    public void An_app_deleted_from_intune_says_so() => Lang.With("en", () =>
    {
        using var mock = StatusMock();
        mock.App = null;
        var graph = Assert.IsType<GraphException>(Record.Exception(() => ReadStatus(mock)));
        Assert.Equal(404, graph.Status);
        Assert.Equal("The app is not in Intune any more: it was deleted, or it belongs to another tenant.", graph.Message);
    });

    [Theory]
    [InlineData("Installed", "", InstallOutcome.Installed)]
    [InlineData("installed", "1", InstallOutcome.Installed)]
    [InlineData("", "2", InstallOutcome.Failed)]
    [InlineData("Uninstall Failed", "", InstallOutcome.Failed)]
    [InlineData("", "4", InstallOutcome.Failed)]
    [InlineData("pendingInstall", "", InstallOutcome.Pending)]
    [InlineData("Install Pending", "5", InstallOutcome.Pending)]
    [InlineData("Not Installed", "", InstallOutcome.NotInstalled)]
    [InlineData("", "3", InstallOutcome.NotInstalled)]
    [InlineData("notApplicable", "", InstallOutcome.NotApplicable)]
    [InlineData("", "-1", InstallOutcome.NotApplicable)]
    [InlineData("unknown", "99", InstallOutcome.Unknown)]
    [InlineData("", "", InstallOutcome.Unknown)]
    public void Install_states_are_read_in_every_form(string state, string appState, InstallOutcome expected)
        => Assert.Equal(expected, IntuneAppStatus.OutcomeOf(state, appState));

    [Fact]
    public void Error_codes_are_shown_as_intune_shows_them_with_what_they_mean() => Lang.With("en", () =>
    {
        Assert.Equal("0x87D1041C", IntuneAppStatus.HexCode("-2016345060"));
        Assert.Equal("0x87D1041C", IntuneAppStatus.HexCode("0x87d1041c"));
        Assert.Equal("", IntuneAppStatus.HexCode("0"));
        Assert.Equal("", IntuneAppStatus.HexCode("0x00000000"));
        Assert.Equal(Loc.T("AppStatus_Err87D1041C"), IntuneAppStatus.ErrorHint("0x87D1041C"));
        Assert.Equal(Loc.T("AppStatus_Err1602"), IntuneAppStatus.ErrorHint("0x80070642"));
        Assert.Equal(Loc.T("AppStatus_Err1618"), IntuneAppStatus.ErrorHint("0x80070652"));
        // Any other error of Windows: its number and the text of Windows.
        Assert.StartsWith("Windows error 5: ", IntuneAppStatus.ErrorHint("0x80070005"));
        Assert.Equal("", IntuneAppStatus.ErrorHint("0x87D30065"));
        Assert.Equal("", IntuneAppStatus.ErrorHint(""));
    });

    [Fact]
    public void A_report_with_rows_as_objects_or_in_a_zip_is_read_too()
    {
        var objects = JsonNode.Parse("{\"values\":[{\"DeviceName\":\"PC-1\",\"UserPrincipalName\":\"ana@contoso.com\",\"InstallState\":\"failed\",\"HexErrorCode\":\"0x87D1041C\"},{\"DeviceName\":\"PC-2\",\"InstallState\":\"installed\"}]}");
        var rows = IntuneAppStatus.ParseReport(objects, out int total);
        Assert.Equal(2, total);
        Assert.Equal(InstallOutcome.Failed, rows[0].Status.Outcome);
        Assert.Equal("0x87D1041C", rows[0].Status.ErrorCode);
        using var zipped = new MemoryStream();
        using (var zip = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("report.json").Open())) w.Write(objects!.ToJsonString());
        Assert.Equal(2, IntuneAppStatus.ParseReport(IntuneAppStatus.ParseExport(zipped.ToArray()), out _).Count);
        Assert.Empty(IntuneAppStatus.ParseReport(null, out int none));
        Assert.Equal(0, none);
    }

    [Fact]
    public void Published_packages_offer_their_status_in_the_library()
    {
        using var tmp = new TempFolder();
        foreach (var (folder, status) in new[] { ("Contoso_Tool_2.0.0", "published"), ("Contoso_Tool_1.9.0", "failed") })
        {
            tmp.File(Path.Combine(folder, PackageLayout.ProjectFile), "{\"AppName\":\"Contoso Tool\",\"AppVersion\":\"" + folder.Split('_').Last() + "\"}");
            tmp.File(Path.Combine(folder, PackageLayout.IntuneDir, IntunePublisher.ResultFileName),
                new JsonObject { ["status"] = status, ["action"] = "created", ["appId"] = AppId, ["tenantId"] = "contoso.onmicrosoft.com" }.ToJsonString());
        }
        var items = PackageBuilder.History(tmp.Path);
        Assert.True(items.Single(i => i.AppVersion == "2.0.0").IsPublished);
        Assert.Equal(AppId, items.Single(i => i.AppVersion == "2.0.0").Publication.AppId);
        Assert.False(items.Single(i => i.AppVersion == "1.9.0").IsPublished);
    }

    [Fact]
    public void The_status_report_and_the_capture_samples_read_well() => Lang.With("en", () =>
    {
        var sample = Views.AppStatusWindow.Sample();
        string text = Views.AppStatusWindow.ReportText(sample, "7-Zip 26.06");
        Assert.Contains("7-Zip 26.06 in Intune", text);
        Assert.Contains("Installed: 7", text);
        Assert.Contains("Failed: 2", text);
        // Devices with a problem first.
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("CTS-")).ToList();
        Assert.Equal(12, lines.Count);
        Assert.Contains("0x87D1041C", lines[0] + lines[1]);
        Assert.StartsWith("CTS-ARM-0004", lines.Last());
        Assert.Equal("25.01", SnapshotRunner.EarlierVersion("26.06"));
        Assert.Equal("24.01", SnapshotRunner.EarlierVersion("25.01"));
        Assert.Equal("2.2.0", SnapshotRunner.EarlierVersion("3.2.0"));
        Assert.Equal("0.9", SnapshotRunner.EarlierVersion("0.5"));
        Assert.Equal("0.9", SnapshotRunner.EarlierVersion("beta"));
    });
}
