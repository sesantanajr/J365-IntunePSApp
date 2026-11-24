using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    public enum InstallOutcome { Failed, Pending, NotInstalled, Installed, NotApplicable, Unknown }

    /// <summary>One device in the install report of an app.</summary>
    public sealed class DeviceInstallStatus
    {
        public string Device { get; set; } = "";
        public string User { get; set; } = "";
        /// <summary>The state as Intune reports it (shown when this application does not know it).</summary>
        public string State { get; set; } = "";
        public string Detail { get; set; } = "";
        /// <summary>Hexadecimal (0x87D1041C); empty when there is none.</summary>
        public string ErrorCode { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public DateTime? Changed { get; set; }
        public InstallOutcome Outcome { get; set; } = InstallOutcome.Unknown;
    }

    /// <summary>A supersedence of the app: an earlier app it supersedes, or a later app that supersedes it.</summary>
    public sealed class AppRelation
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        /// <summary>True: this app supersedes the other one. False: the other one supersedes this app.</summary>
        public bool Supersedes { get; set; }
        /// <summary>update | replace</summary>
        public string Type { get; set; } = "";
    }

    /// <summary>What Intune says about one published app: its assignments, supersedence and the install state of every device.</summary>
    public sealed class AppDeployment
    {
        public string AppId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string PublishingState { get; set; } = "";
        public List<string> Assignments { get; } = new List<string>();
        public bool AssignmentsRead { get; set; }
        public List<AppRelation> Relations { get; } = new List<AppRelation>();
        public List<DeviceInstallStatus> Devices { get; } = new List<DeviceInstallStatus>();
        /// <summary>Devices in the report (more than were read when the report is long).</summary>
        public int Total { get; set; }
        public bool ReportRead { get; set; }
        /// <summary>Why the install report could not be read (the rest is still shown); empty when it was read.</summary>
        public string ReportProblem { get; set; } = "";
        public DateTime Read { get; set; } = DateTime.Now;

        public int Count(InstallOutcome outcome) => Devices.Count(d => d.Outcome == outcome);
        public bool Partial => ReportRead && Total > Devices.Count;
        public string PortalUrl => IntuneAppStatus.PortalUrl(AppId);
    }

    /// <summary>
    /// The install status of an app published to Intune, read with the connection of this computer and the permission it
    /// already has (DeviceManagementApps): the app, its assignments and supersedence, and the device install report.
    /// </summary>
    public static class IntuneAppStatus
    {
        /// <summary>Columns of the device install report of one app (DeviceInstallStatusByApp).</summary>
        internal static readonly string[] Columns =
        {
            "DeviceName", "UserPrincipalName", "UserName", "Platform", "AppVersion", "InstallState", "InstallStateDetail",
            "AppInstallState", "AppInstallStateDetails", "ErrorCode", "HexErrorCode", "LastModifiedDateTime", "DeviceId", "UserId"
        };

        /// <summary>
        /// The functions that return that report at once, newest first: Intune renamed it once
        /// (getDeviceInstallStatusReport became retrieveDeviceAppInstallationStatusReport). When neither exists, the report is
        /// exported (exportJobs), the documented way, which takes a little longer.
        /// </summary>
        internal static readonly string[] ReportFunctions = { "retrieveDeviceAppInstallationStatusReport", "getDeviceInstallStatusReport" };

        internal const int PageSize = 50;
        internal const int MaxDevices = 1000;

        public static string PortalUrl(string appId) => "https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/" + appId;

        /// <summary>Reads the status with the connection of this computer.</summary>
        public static async Task<AppDeployment> GetAsync(string appId, CancellationToken ct)
        {
            var t = await IntuneConnection.RenewAsync(ct) ?? throw new AuthException(T("Auth_ErrNotSignedIn"));
            var status = await ReadAsync(new Graph(t.AccessToken), appId, ct);
            IntuneConnection.NoteIntune(refused: false);
            return status;
        }

        internal static async Task<AppDeployment> ReadAsync(Graph g, string appId, CancellationToken ct)
        {
            if (!Guid.TryParse((appId ?? "").Trim(), out var guid)) throw new ArgumentException(T("AppStatus_AppGone"));
            string id = guid.ToString();
            var d = new AppDeployment { AppId = id, Read = DateTime.Now };
            JsonNode app;
            try { app = await g.GetAsync("/beta/deviceAppManagement/mobileApps/" + id, ct); }
            catch (GraphException ex) when (ex.Status == 404) { throw new GraphException(404, T("AppStatus_AppGone"), "NotFound"); }
            d.Name = Text(app?["displayName"]);
            d.Version = Text(app?["displayVersion"]);
            d.PublishingState = Text(app?["publishingState"]);

            // Assignments and supersedence: a part that cannot be read stays empty, the rest is still shown.
            try
            {
                foreach (var a in (await g.GetAsync($"/beta/deviceAppManagement/mobileApps/{id}/assignments", ct))?["value"] as JsonArray ?? new JsonArray())
                    d.Assignments.Add(AssignmentLabel(a));
                d.AssignmentsRead = true;
            }
            catch (GraphException) { }
            try
            {
                foreach (var r in (await g.GetAsync($"/beta/deviceAppManagement/mobileApps/{id}/relationships", ct))?["value"] as JsonArray ?? new JsonArray())
                    if (RelationOf(r) is AppRelation relation) d.Relations.Add(relation);
            }
            catch (GraphException) { }

            await ReadReportAsync(g, d, ct);
            return d;
        }

        /// <summary>"Required for all users", "Available for group 1a2b...".</summary>
        internal static string AssignmentLabel(JsonNode a)
        {
            string intent = Text(a?["intent"]);
            var target = a?["target"];
            string type = Text(target?["@odata.type"]), group = Text(target?["groupId"]);
            string who = type.EndsWith("allLicensedUsersAssignmentTarget", StringComparison.OrdinalIgnoreCase) ? T("AppStatus_AllUsers")
                : type.EndsWith("allDevicesAssignmentTarget", StringComparison.OrdinalIgnoreCase) ? T("AppStatus_AllDevices")
                : type.EndsWith("exclusionGroupAssignmentTarget", StringComparison.OrdinalIgnoreCase) ? T("AppStatus_ExcludedGroup", group)
                : T("AppStatus_Group", group);
            string what = intent.ToLowerInvariant() switch
            {
                "required" => T("AppStatus_IntentRequired"),
                "available" or "availablewithoutenrollment" => T("AppStatus_IntentAvailable"),
                "uninstall" => T("AppStatus_IntentUninstall"),
                _ => intent
            };
            return T("AppStatus_Assignment", what, who);
        }

        /// <summary>A supersedence of the app; null for anything else (a dependency).</summary>
        internal static AppRelation RelationOf(JsonNode r)
        {
            if (!Text(r?["@odata.type"]).EndsWith("mobileAppSupersedence", StringComparison.OrdinalIgnoreCase)) return null;
            return new AppRelation
            {
                Id = Text(r?["targetId"]),
                Name = Text(r?["targetDisplayName"]),
                Version = Text(r?["targetDisplayVersion"]),
                // The superseding app is the parent: "child" is an app this one supersedes, "parent" one that supersedes it.
                Supersedes = Text(r?["targetType"]).Equals("child", StringComparison.OrdinalIgnoreCase),
                Type = Text(r?["supersedenceType"]).ToLowerInvariant()
            };
        }

        // ------------------------------------------------------------------ the device install report

        private static async Task ReadReportAsync(Graph g, AppDeployment d, CancellationToken ct)
        {
            string filter = $"(ApplicationId eq '{d.AppId}')";
            foreach (string function in ReportFunctions)
            {
                try
                {
                    await ReadPagesAsync(g, function, filter, d, ct);
                    d.ReportRead = true;
                    return;
                }
                catch (GraphException ex) when (IsMissingFunction(ex)) { d.Devices.Clear(); d.Total = 0; }
                catch (GraphException ex)
                {
                    d.ReportProblem = Problem(ex);
                    return;
                }
            }
            try
            {
                await ExportAsync(g, filter, d, ct);
                d.ReportRead = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (GraphException ex) { d.ReportProblem = Problem(ex); }
            catch (Exception ex) { d.ReportProblem = T("AppStatus_ReportFailed", ex.Message); }
        }

        /// <summary>A function of the reports that this tenant does not have (renamed or removed), as Microsoft Graph says it.</summary>
        internal static bool IsMissingFunction(GraphException ex)
            => ex.Status == 404 || ex.Status == 405 || (ex.Status == 400 && Regex.IsMatch(ex.Message ?? "", @"segment|OData route|No HTTP resource|not found|does not exist|Unknown function", RegexOptions.IgnoreCase));

        private static string Problem(GraphException ex)
            => ex.Status is 401 or 403 ? T("AppStatus_ReportRefused", ex.Message) : T("AppStatus_ReportFailed", ex.Message);

        private static async Task ReadPagesAsync(Graph g, string function, string filter, AppDeployment d, CancellationToken ct)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            d.Devices.Clear();
            d.Total = 0;
            for (int skip = 0, page = 0; d.Devices.Count < MaxDevices && page < MaxDevices / PageSize; page++)
            {
                var body = new JsonObject
                {
                    ["select"] = new JsonArray(Columns.Select(c => (JsonNode)c).ToArray()),
                    ["skip"] = skip,
                    ["top"] = PageSize,
                    ["filter"] = filter,
                    ["orderBy"] = new JsonArray()
                };
                var report = await g.SendAsync(HttpMethod.Post, "/beta/deviceManagement/reports/" + function, body, ct);
                var rows = ParseReport(report, out int total);
                d.Total = Math.Max(d.Total, total);
                // A service that ignores "skip" sends the first page again: what was seen ends the reading.
                int added = 0;
                foreach (var row in rows)
                    if (seen.Add(row.Key)) { d.Devices.Add(row.Status); added++; }
                if (rows.Count < PageSize || added == 0 || d.Devices.Count >= d.Total) break;
                skip += rows.Count;
            }
            if (d.Devices.Count > MaxDevices) d.Devices.RemoveRange(MaxDevices, d.Devices.Count - MaxDevices);
            d.Total = Math.Max(d.Total, d.Devices.Count);
        }

        /// <summary>
        /// The documented way: an export job of the report, a few seconds later a signed address of a zip with the rows.
        /// </summary>
        private static async Task ExportAsync(Graph g, string filter, AppDeployment d, CancellationToken ct)
        {
            var job = await g.SendAsync(HttpMethod.Post, "/beta/deviceManagement/reports/exportJobs", new JsonObject
            {
                ["reportName"] = "DeviceInstallStatusByApp",
                ["filter"] = filter,
                ["format"] = "json",
                // Every column asked is one of the documented columns of DeviceInstallStatusByApp.
                ["select"] = new JsonArray(Columns.Select(c => (JsonNode)c).ToArray())
            }, ct, repeatable: false);
            string jobId = Text(job?["id"]);
            if (jobId.Length == 0) throw new InvalidDataException(T("AppStatus_ExportFailed"));
            string url = null;
            for (int i = 0; i < 60 && url == null; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(i == 0 ? 1 : 2), ct);
                var state = await g.GetAsync($"/beta/deviceManagement/reports/exportJobs('{Uri.EscapeDataString(jobId)}')", ct);
                string status = Text(state?["status"]);
                if (status.Equals("completed", StringComparison.OrdinalIgnoreCase)) url = Text(state?["url"]);
                else if (status.Equals("failed", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(T("AppStatus_ExportFailed"));
            }
            if (string.IsNullOrEmpty(url)) throw new TimeoutException(T("AppStatus_ExportTimeout"));
            // A signed address of Azure Storage: no authorization header goes with it. Plain http only for a test server.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || (address.Scheme != Uri.UriSchemeHttps && !(address.Scheme == Uri.UriSchemeHttp && EntraAuth.GraphUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException(T("AppStatus_ExportFailed"));
            using var response = await Http.Client.GetAsync(address, ct);
            response.EnsureSuccessStatusCode();
            var rows = ParseReport(ParseExport(await response.Content.ReadAsByteArrayAsync(ct)), out int total);
            d.Devices.Clear();
            d.Devices.AddRange(rows.Take(MaxDevices).Select(r => r.Status));
            d.Total = Math.Max(total, d.Devices.Count);
        }

        /// <summary>The exported report: a zip with one JSON file, or the JSON itself.</summary>
        internal static JsonNode ParseExport(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            if (data.Length > 3 && data[0] == (byte)'P' && data[1] == (byte)'K')
            {
                using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ?? zip.Entries.FirstOrDefault();
                if (entry == null) return null;
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return JsonNode.Parse(reader.ReadToEnd());
            }
            return JsonNode.Parse(Encoding.UTF8.GetString(data));
        }

        /// <summary>
        /// The rows of a report: columns once and rows as arrays (Schema / Values, as the report functions answer), or rows as
        /// objects (an exported report). <paramref name="total"/> is TotalRowCount when the report says it.
        /// </summary>
        internal static List<(string Key, DeviceInstallStatus Status)> ParseReport(JsonNode report, out int total)
        {
            var list = new List<(string, DeviceInstallStatus)>();
            total = 0;
            if (report == null) return list;
            var obj = report as JsonObject;
            if (obj?["Schema"] is JsonArray schema && obj["Values"] is JsonArray values)
            {
                var names = schema.Select(c => c is JsonObject column ? Text(column["Column"]) : Text(c)).ToList();
                foreach (var row in values.OfType<JsonArray>())
                {
                    var map = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < names.Count && i < row.Count; i++) map[names[i]] = row[i];
                    list.Add(RowOf(map));
                }
            }
            else
            {
                var objects = report as JsonArray ?? obj?["values"] as JsonArray ?? obj?["value"] as JsonArray ?? obj?["Values"] as JsonArray;
                foreach (var o in objects?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    list.Add(RowOf(o.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase)));
            }
            total = int.TryParse(Text(obj?["TotalRowCount"]), out int count) ? count : list.Count;
            return list;
        }

        private static (string Key, DeviceInstallStatus Status) RowOf(IDictionary<string, JsonNode> map)
        {
            string Get(string column) => map.TryGetValue(column, out var node) ? Text(node) : "";
            string First(params string[] columns) => columns.Select(Get).FirstOrDefault(v => v.Length > 0) ?? "";
            string state = Get("InstallState"), appState = Get("AppInstallState");
            var status = new DeviceInstallStatus
            {
                Device = Get("DeviceName"),
                User = First("UserPrincipalName", "UserName"),
                State = First("InstallState_loc", "InstallState", "AppInstallState_loc", "AppInstallState"),
                Detail = First("InstallStateDetail_loc", "InstallStateDetail", "AppInstallStateDetails_loc", "AppInstallStateDetails"),
                ErrorCode = HexCode(First("HexErrorCode", "ErrorCode")),
                AppVersion = Get("AppVersion"),
                Changed = DateTime.TryParse(Get("LastModifiedDateTime"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var changed) ? changed.ToLocalTime() : null,
                Outcome = OutcomeOf(state, appState)
            };
            // "No details" is no detail.
            if (Regex.IsMatch(status.Detail, @"^(noAdditionalDetails|none|0)$", RegexOptions.IgnoreCase)) status.Detail = "";
            string key = First("DeviceId") is { Length: > 0 } device ? device + "|" + First("UserId", "UserPrincipalName") : status.Device + "|" + status.User + "|" + status.State;
            return (key, status);
        }

        /// <summary>
        /// The outcome of a row: the state as text (installed, failed, pendingInstall, notInstalled, notApplicable, in any
        /// form) or as the number of Intune (resultantAppState: 1 installed, 2 failed, 3 not installed, 4 uninstall failed,
        /// 5 pending install, -1 not applicable, 99 unknown).
        /// </summary>
        internal static InstallOutcome OutcomeOf(string state, string appState)
        {
            foreach (string raw in new[] { state, appState })
            {
                string trimmed = (raw ?? "").Trim();
                if (trimmed.Length == 0) continue;
                // A number first: its sign matters (-1 is "not applicable").
                if (int.TryParse(trimmed, out int n))
                {
                    switch (n)
                    {
                        case 1: return InstallOutcome.Installed;
                        case 2: case 4: return InstallOutcome.Failed;
                        case 3: return InstallOutcome.NotInstalled;
                        case 5: return InstallOutcome.Pending;
                        case -1: return InstallOutcome.NotApplicable;
                    }
                    continue;
                }
                string s = Regex.Replace(trimmed, @"[\s_\-]", "").ToLowerInvariant();
                if (s.Contains("fail") || s == "error") return InstallOutcome.Failed;
                if (s.Contains("pending") || s.Contains("inprogress") || s.Contains("downloading") || s.Contains("installing")) return InstallOutcome.Pending;
                if (s.Contains("notapplicable")) return InstallOutcome.NotApplicable;
                if (s.Contains("notinstalled") || s.Contains("notdetected") || s == "uninstalled") return InstallOutcome.NotInstalled;
                if (s.StartsWith("installed", StringComparison.Ordinal) || s is "success" or "succeeded") return InstallOutcome.Installed;
            }
            return InstallOutcome.Unknown;
        }

        /// <summary>An error code as Intune shows it ("0x87D1041C"), from its hexadecimal or decimal form; empty for none.</summary>
        internal static string HexCode(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length == 0) return "";
            if (code.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(code.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hex) ? (hex == 0 ? "" : "0x" + hex.ToString("X8")) : code;
            if (long.TryParse(code, out long number)) return number == 0 ? "" : "0x" + unchecked((uint)number).ToString("X8");
            return code;
        }

        /// <summary>What an error code usually means for a package of this application; empty when nothing is known about it.</summary>
        public static string ErrorHint(string hex)
        {
            if (string.IsNullOrEmpty(hex) || !uint.TryParse(hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex.Substring(2) : hex, System.Globalization.NumberStyles.HexNumber, null, out uint code)) return "";
            switch (code)
            {
                case 0x87D1041C: return T("AppStatus_Err87D1041C");
                case 0x80070642: return T("AppStatus_Err1602");
                case 0x80070652: return T("AppStatus_Err1618");
                case 0x80070643: return T("AppStatus_Err1603");
                // PSAppDeployToolkit: any error of the script (60001); the codes of this application since 3.0.6 (69001,
                // 69002), as Intune may show a code above 65535: masked like a Windows error, or as it is.
                case 0x8007EA61: return T("AppStatus_Err60001");
                case 0x80070D89: case 0x00010D89: case 0x80080D89: return T("AppStatus_Err69001");
                case 0x80070D8A: case 0x00010D8A: case 0x80080D8A: return T("AppStatus_Err69002");
                // "File not found" is also what Intune shows when a setup ends with the code 2.
                case 0x80070002: return T("AppStatus_ErrExit2");
            }
            // An error of Windows (0x8007xxxx): its text, in the language of Windows.
            if ((code & 0xFFFF0000) == 0x80070000)
            {
                int win32 = (int)(code & 0xFFFF);
                return T("AppStatus_ErrWindows", win32, new Win32Exception(win32).Message);
            }
            return "";
        }

        private static string Text(JsonNode node)
        {
            if (node is not JsonValue value) return "";
            if (value.TryGetValue<string>(out var s)) return s ?? "";
            return value.ToJsonString().Trim('"');
        }
    }
}
