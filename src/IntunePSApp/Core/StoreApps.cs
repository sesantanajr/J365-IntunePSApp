using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>An application of the Microsoft Store, as the Store describes it.</summary>
    public sealed class StoreApp
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Description { get; set; } = "";
        public string PublisherUrl { get; set; } = "";
        public string PrivacyUrl { get; set; } = "";
        public string License { get; set; } = "";
        /// <summary>The official logo of the Store (https), or empty.</summary>
        public string LogoUrl { get; set; } = "";
        public List<string> Architectures { get; } = new List<string>();
        public List<string> Scopes { get; } = new List<string>();

        /// <summary>A Win32 application distributed by the Store (product ids XP…), as opposed to a UWP/MSIX app (9…).</summary>
        public bool IsWin32 => Id.StartsWith("XP", StringComparison.OrdinalIgnoreCase);

        /// <summary>Every installer of the Store installs per user.</summary>
        public bool UserOnly => Scopes.Count > 0 && Scopes.All(s => string.Equals(s, "user", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Install behavior in Intune. UWP/MSIX apps of the Store are provisioned for every user in the system context (what
        /// Microsoft recommends for the Company Portal, although their manifests say "user"); a Win32 app of the Store that
        /// only installs per user runs as the user.
        /// </summary>
        public string RunAsAccount => IsWin32 && UserOnly ? "user" : "system";

        public string StorePageUrl => "https://apps.microsoft.com/detail/" + Id;
    }

    /// <summary>
    /// The Microsoft Store as a source of applications: the public service winget uses for its "msstore" source (search and
    /// manifests) and the catalog of the Store (localized texts and the official logo). Apps of the Store are not packaged:
    /// Intune installs them, and keeps them current, as "Microsoft Store app (new)" (<see cref="StorePublisher"/>).
    /// </summary>
    public static class StoreCatalog
    {
        public const string InstallerType = "msstore";
        private const string DisplayCatalogUrl = "https://displaycatalog.mp.microsoft.com/v7.0/products";

        /// <summary>The service of the msstore source of winget. J365_STORE_API points it elsewhere (tests, a mirror).</summary>
        private static string ApiUrl => Environment.GetEnvironmentVariable("J365_STORE_API") is { Length: > 0 } url
            ? url.TrimEnd('/') : "https://storeedgefd.dsx.mp.microsoft.com/v9.0";

        private static readonly ConcurrentDictionary<string, List<CatalogApp>> Searches = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, StoreApp> Details = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>One object per product id: an application ticked for the batch stays ticked in every search that finds it.</summary>
        private static readonly ConcurrentDictionary<string, CatalogApp> Known = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, (byte[] Data, string Type)> Logos = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A product id of the Store: 9PM860492SZD (a UWP/MSIX app, 12 letters and digits) or XPDP273C0XHQH2 (a Win32 app of
        /// the Store, XP and 12 more).
        /// </summary>
        public static bool IsStoreId(string text) => Regex.IsMatch((text ?? "").Trim(), @"^(9[A-Z0-9]{11}|XP[A-Z0-9]{12})$", RegexOptions.IgnoreCase);

        /// <summary>Market of the Store: the region of the preferred locale ("pt-BR" → "BR"; "US" when there is none).</summary>
        public static string Market(string locale)
        {
            var parts = (locale ?? "").Split('-');
            return parts.Length > 1 && Regex.IsMatch(parts[^1], "^[A-Za-z]{2}$") ? parts[^1].ToUpperInvariant() : "US";
        }

        /// <summary>
        /// Applications of the Store for a text: an id is looked up by id, any other text matches names and publishers.
        /// Answers are kept for the session. Throws <see cref="IOException"/> when the Store does not answer.
        /// </summary>
        public static async Task<List<CatalogApp>> SearchAsync(string query, int max, CancellationToken ct)
        {
            string q = (query ?? "").Trim();
            if (q.Length < 2) return new List<CatalogApp>();
            string key = q.ToLowerInvariant() + "|" + max;
            if (Searches.TryGetValue(key, out var cached)) return cached;
            // The service matches a product id exactly in upper case only (a case-insensitive filter finds nothing).
            bool byId = IsStoreId(q);
            JsonObject body = byId
                ? new JsonObject { ["Query"] = new JsonObject { ["KeyWord"] = q.ToUpperInvariant(), ["MatchType"] = "Exact" } }
                : new JsonObject { ["Query"] = new JsonObject { ["KeyWord"] = q, ["MatchType"] = "Substring" }, ["MaximumResults"] = max };
            var list = ParseSearch(await PostAsync(ApiUrl + "/manifestSearch", body, ct), max);
            // An id the search does not return: its manifest decides (the authoritative answer of the Store).
            if (byId && list.Count == 0)
            {
                try { list.Add(await FindAsync(q, "en-US", ct)); }
                catch (IOException) { }
            }
            if (Searches.Count > 200) Searches.Clear();
            Searches[key] = list;
            return list;
        }

        /// <summary>The answer of manifestSearch: one application per product id of the Store.</summary>
        internal static List<CatalogApp> ParseSearch(string json, int max = 50)
        {
            var list = new List<CatalogApp>();
            JsonNode root;
            try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); } catch { return list; }
            foreach (var d in root?["Data"] as JsonArray ?? new JsonArray())
            {
                string id = Str(d, "PackageIdentifier");
                if (!IsStoreId(id) || list.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
                string name = Str(d, "PackageName"), publisher = Str(d, "Publisher");
                list.Add(Known.GetOrAdd(id.Trim().ToUpperInvariant(), _ => ToCatalogApp(id, name, publisher)));
                if (list.Count >= max) break;
            }
            return list;
        }

        /// <summary>The application of the Store with this product id, read from the Store when it was not found before (command line).</summary>
        public static async Task<CatalogApp> FindAsync(string id, string locale, CancellationToken ct)
        {
            id = (id ?? "").Trim().ToUpperInvariant();
            if (Known.TryGetValue(id, out var known)) return known;
            var app = await GetAsync(id, locale, ct);
            return Known.GetOrAdd(id, _ => ToCatalogApp(app.Id, app.Name, app.Publisher));
        }

        /// <summary>An application of the Store in the catalog (no installer to download: Intune installs it from the Store).</summary>
        public static CatalogApp ToCatalogApp(string id, string name, string publisher)
        {
            id = (id ?? "").Trim().ToUpperInvariant();
            return new CatalogApp
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
                Publisher = (publisher ?? "").Trim(),
                Version = "",
                Homepage = "https://apps.microsoft.com/detail/" + id,
                IsStore = true,
                Installers = { new CatalogInstaller { Type = InstallerType } }
            };
        }

        /// <summary>
        /// Everything the Store says about an application: its manifest (install scopes, links) and, when the catalog of the
        /// Store answers, the texts in the language of the interface and the official logo. Kept for the session.
        /// </summary>
        public static async Task<StoreApp> GetAsync(string id, string locale, CancellationToken ct)
        {
            id = (id ?? "").Trim().ToUpperInvariant();
            if (!IsStoreId(id)) throw new IOException(T("Store_ErrNotFound", id));
            if (Details.TryGetValue(id, out var known)) return known;
            string market = Market(locale);
            var app = ParseManifest(await GetStringAsync($"{ApiUrl}/packageManifests/{Uri.EscapeDataString(id)}?Market={market}", ct))
                      ?? throw new IOException(T("Store_ErrNotFound", id));
            try
            {
                string languages = Uri.EscapeDataString($"{(string.IsNullOrEmpty(locale) ? "en-US" : locale)},en-US,neutral");
                ApplyDisplay(app, await GetStringAsync($"{DisplayCatalogUrl}?bigIds={id}&market={market}&languages={languages}", ct));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { Log.Warn(T("Log_StoreDisplayFailed", id, ex.Message)); }
            Details[id] = app;
            return app;
        }

        /// <summary>The manifest of the msstore source (packageManifests/{id}), or null when it names no application.</summary>
        internal static StoreApp ParseManifest(string json)
        {
            JsonNode data;
            try { data = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json)?["Data"]; } catch { return null; }
            string id = Str(data, "PackageIdentifier");
            var version = (data?["Versions"] as JsonArray)?.FirstOrDefault();
            if (!IsStoreId(id) || version == null) return null;
            var locale = version["DefaultLocale"];
            var app = new StoreApp
            {
                Id = id.ToUpperInvariant(),
                Name = Str(locale, "PackageName") is { Length: > 0 } name ? name : id,
                Publisher = Str(locale, "Publisher"),
                Description = FirstText(Str(locale, "ShortDescription"), Str(locale, "Description")),
                PublisherUrl = Str(locale, "PublisherUrl"),
                PrivacyUrl = Str(locale, "PrivacyUrl"),
                License = Str(locale, "License"),
            };
            foreach (var i in version["Installers"] as JsonArray ?? new JsonArray())
            {
                string arch = Str(i, "Architecture"), scope = Str(i, "Scope");
                if (arch.Length > 0 && !app.Architectures.Contains(arch, StringComparer.OrdinalIgnoreCase)) app.Architectures.Add(arch);
                if (scope.Length > 0 && !app.Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase)) app.Scopes.Add(scope);
            }
            return app;
        }

        /// <summary>The catalog of the Store: localized description and the logo (square, the largest up to 300 px).</summary>
        internal static void ApplyDisplay(StoreApp app, string json)
        {
            JsonNode properties;
            try { properties = ((JsonNode.Parse(json ?? "{}")?["Products"] as JsonArray)?.FirstOrDefault()?["LocalizedProperties"] as JsonArray)?.FirstOrDefault(); }
            catch { return; }
            if (properties == null) return;
            string description = FirstText(Str(properties, "ShortDescription"), Str(properties, "Description"));
            if (description.Length > 0) app.Description = description;
            if (app.Publisher.Length == 0) app.Publisher = Str(properties, "PublisherName");
            var images = (properties["Images"] as JsonArray ?? new JsonArray())
                .Select(im => (Purpose: Str(im, "ImagePurpose"), Width: Int(im, "Width"), Height: Int(im, "Height"), Uri: Str(im, "Uri")))
                .Where(im => im.Uri.Length > 0 && im.Width > 0 && im.Width == im.Height && im.Width <= 300)
                .OrderBy(im => im.Purpose == "Logo" ? 0 : im.Purpose == "Tile" ? 1 : 2).ThenByDescending(im => im.Width)
                .ToList();
            if (images.Count > 0) app.LogoUrl = images[0].Uri.StartsWith("//", StringComparison.Ordinal) ? "https:" + images[0].Uri : images[0].Uri;
        }

        /// <summary>The official logo (PNG or JPEG bytes and their type), or (null, null).</summary>
        public static async Task<(byte[] Data, string Type)> LogoAsync(StoreApp app, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(app?.LogoUrl) || !app.LogoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return (null, null);
            if (Logos.TryGetValue(app.LogoUrl, out var cached)) return cached;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                using var resp = await Http.Client.GetAsync(app.LogoUrl, cts.Token);
                if (!resp.IsSuccessStatusCode) return (null, null);
                var data = await resp.Content.ReadAsByteArrayAsync(cts.Token);
                if (data.Length == 0 || data.Length > 4 * 1024 * 1024) return (null, null);
                string type = data.Length > 3 && data[0] == 0x89 && data[1] == 0x50 ? "image/png"
                    : data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8 ? "image/jpeg" : null;
                if (type == null) return (null, null);
                Logos[app.LogoUrl] = (data, type);
                return (data, type);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return (null, null); }
        }

        private static async Task<string> PostAsync(string url, JsonNode body, CancellationToken ct)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
                using var resp = await Http.Client.SendAsync(request, cts.Token);
                if (!resp.IsSuccessStatusCode) throw new IOException(T("Store_ErrUnavailable", $"HTTP {(int)resp.StatusCode}"));
                return await resp.Content.ReadAsStringAsync(cts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (IOException) { throw; }
            catch (Exception ex) { throw new IOException(T("Store_ErrUnavailable", ex is OperationCanceledException ? "timeout" : ex.GetBaseException().Message), ex); }
        }

        private static async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            var (body, error) = await Http.GetStringAsync(url, 30, ct);
            ct.ThrowIfCancellationRequested();
            return body ?? throw new IOException(error == "HTTP 404" ? T("Store_ErrNotFound", url.Split('?')[0].Split('/')[^1]) : T("Store_ErrUnavailable", error));
        }

        private static string FirstText(params string[] texts) => texts.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim() ?? "";

        private static string Str(JsonNode node, string name)
        {
            try { return node?[name] is JsonValue v && v.TryGetValue(out string s) ? s ?? "" : ""; }
            catch { return ""; }
        }

        private static int Int(JsonNode node, string name)
        {
            try { return node?[name] is JsonValue v && v.TryGetValue(out int n) ? n : 0; }
            catch { return 0; }
        }
    }

    /// <summary>
    /// Publishes an application of the Store to Intune as "Microsoft Store app (new)" (Graph: winGetApp): nothing is packaged
    /// or uploaded, Intune installs the application from the Store and keeps it current. Same connection, same choices
    /// (app of the same name, assignment, toasts) as the publication of packages.
    /// </summary>
    public static class StorePublisher
    {
        /// <summary>The app Intune creates: identity and links of the Store, the official logo, the install behavior.</summary>
        public static JsonObject AppBody(StoreApp app, string owner, byte[] icon, string iconType)
        {
            var body = new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.winGetApp",
                ["displayName"] = app.Name,
                ["description"] = string.IsNullOrWhiteSpace(app.Description) ? app.Name : app.Description,
                ["publisher"] = string.IsNullOrWhiteSpace(app.Publisher) ? app.Name : app.Publisher,
                ["developer"] = app.Publisher ?? "",
                ["owner"] = owner ?? "",
                ["notes"] = $"Published with {AppInfo.Name} {AppInfo.Version} (Microsoft Store {app.Id})",
                ["packageIdentifier"] = app.Id,
                ["installExperience"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.winGetAppInstallExperience", ["runAsAccount"] = app.RunAsAccount },
            };
            if (IsWebUrl(app.PublisherUrl)) body["informationUrl"] = app.PublisherUrl;
            if (IsWebUrl(app.PrivacyUrl)) body["privacyInformationUrl"] = app.PrivacyUrl;
            if (icon != null && icon.Length > 0)
                body["largeIcon"] = new JsonObject { ["@odata.type"] = "#microsoft.graph.mimeContent", ["type"] = iconType ?? "image/png", ["value"] = Convert.ToBase64String(icon) };
            return body;
        }

        /// <summary>The assignment of a Store app, with the toasts of the batch (hidden for silent deployments).</summary>
        public static JsonObject AssignmentBody(PublishOptions po, string notifications)
        {
            JsonObject target = po.AssignTo switch
            {
                "AllDevices" => new JsonObject { ["@odata.type"] = "#microsoft.graph.allDevicesAssignmentTarget" },
                "Group" => new JsonObject { ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget", ["groupId"] = po.GroupId },
                _ => new JsonObject { ["@odata.type"] = "#microsoft.graph.allLicensedUsersAssignmentTarget" }
            };
            return new JsonObject
            {
                ["mobileAppAssignments"] = new JsonArray(new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.mobileAppAssignment",
                    ["intent"] = po.AssignIntent,
                    ["target"] = target,
                    ["settings"] = new JsonObject
                    {
                        ["@odata.type"] = "#microsoft.graph.winGetAppAssignmentSettings",
                        ["notifications"] = notifications,
                        ["restartSettings"] = null,
                        ["installTimeSettings"] = null
                    }
                })
            };
        }

        /// <summary>
        /// Creates the app in Intune (or, with an app of the same product id there, keeps it, updates its texts and logo, or
        /// creates another, as <see cref="BatchOptions.IfExists"/> says), waits until Intune has read it from the Store and
        /// assigns it. Never throws for a refusal of Intune: the result says what happened, as the publishing script does.
        /// </summary>
        public static async Task<PublishResult> PublishAsync(StoreApp app, BatchOptions o, Action<string> step, CancellationToken ct)
        {
            var result = new PublishResult { Status = "failed", DisplayName = app.Name, TenantId = IntuneConnection.TenantLabel, Utc = DateTime.UtcNow.ToString("o") };
            try
            {
                step?.Invoke(T("Publish_StepSignIn"));
                var token = await IntuneConnection.RenewAsync(ct) ?? throw new AuthException(T("Auth_ErrNotSignedIn"));
                var g = new Graph(token.AccessToken);
                var po = o.PublishOptions(IntuneConnection.UsesCertificate);
                string notifications = PackageBuilder.IntuneNotifications(new PackageSettings { DeployMode = o.DeployMode, IntuneNotifications = o.IntuneNotifications });

                step?.Invoke(T("Publish_StepLooking"));
                string existing = await FindAsync(g, app.Id, ct);
                var (icon, iconType) = await StoreCatalog.LogoAsync(app, ct);
                string owner = string.IsNullOrWhiteSpace(o.Owner) ? o.CompanyName ?? "" : o.Owner;
                // Supersede, too, keeps the existing Store app: the Store updates it by itself, so there is no version to supersede.
                if (existing != null && po.IfExists != "CreateNew")
                {
                    result.AppId = existing;
                    result.PortalUrl = PortalUrl(existing);
                    if (po.IfExists == "Fail") { result.Status = "exists"; return result; }
                    // The Store keeps the app current by itself: an update refreshes its texts and logo, assignments stay.
                    step?.Invoke(T("Publish_StepUpdating"));
                    var update = AppBody(app, owner, icon, iconType);
                    foreach (var fixedOnce in new[] { "packageIdentifier", "installExperience" }) update.Remove(fixedOnce);
                    await g.SendAsync(new HttpMethod("PATCH"), "/beta/deviceAppManagement/mobileApps/" + existing, update, ct);
                    IntuneConnection.NoteIntune(refused: false);
                    result.Status = "published";
                    result.Action = "updated";
                    return result;
                }

                step?.Invoke(T("Publish_StepCreating"));
                string id;
                try
                {
                    // Never sent twice: a second POST after a lost answer made a duplicate app (the script does the same).
                    var created = await g.SendAsync(HttpMethod.Post, "/beta/deviceAppManagement/mobileApps", AppBody(app, owner, icon, iconType), ct, repeatable: false);
                    id = created?["id"]?.GetValue<string>() ?? throw new InvalidDataException(T("Batch_PublishNoResult"));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && (ex is HttpRequestException || (ex is GraphException ge && ge.Status >= 500)))
                {
                    // Intune may have created it before the answer was lost: the app of this product id is looked for.
                    id = await FindAsync(g, app.Id, ct);
                    if (id == null) throw;
                }
                IntuneConnection.NoteIntune(refused: false);
                result.AppId = id;
                result.PortalUrl = PortalUrl(id);
                result.Status = "published";
                result.Action = "created";

                if (po.AssignIntent is "available" or "required")
                {
                    step?.Invoke(T("Publish_StepAssigning"));
                    // Intune assigns an app once it has read it from the Store (publishingState "published").
                    for (int i = 0; i < 40; i++)
                    {
                        // The app exists already: a failed read (a 404 right after the creation, a timeout) only ends the
                        // wait, it does not turn a created app into a failure (the script waits the same way).
                        string state;
                        try { state = (await g.GetAsync($"/beta/deviceAppManagement/mobileApps/{id}?$select=publishingState", ct))?["publishingState"]?.GetValue<string>() ?? ""; }
                        catch (Exception ex) when (!ct.IsCancellationRequested && (ex is GraphException || ex is HttpRequestException)) { break; }
                        if (state.Length == 0 || state == "published") break;
                        await Task.Delay(TimeSpan.FromSeconds(3), ct);
                    }
                    string label = po.AssignIntent + " for " + po.AssignTo + (po.AssignTo == "Group" ? " " + po.GroupId : "");
                    try
                    {
                        await g.SendAsync(HttpMethod.Post, $"/beta/deviceAppManagement/mobileApps/{id}/assign", AssignmentBody(po, notifications), ct);
                        result.Assignment = label;
                    }
                    catch (GraphException ex)
                    {
                        // The app is in Intune: the assignment can be done in the admin center.
                        result.Assignment = "failed: " + ex.Message;
                    }
                }
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                if (result.Status != "published") result.Status = "cancelled";
                return result;
            }
            catch (Exception ex)
            {
                if (ex is GraphException g && IntuneDiagnosis.IsRefusal(g)) IntuneConnection.NoteIntune(refused: true);
                result.Status = "failed";
                result.Error = ex.Message;
                return result;
            }
        }

        /// <summary>The id of a Store app of Intune with this product id, or null.</summary>
        private static async Task<string> FindAsync(Graph g, string packageIdentifier, CancellationToken ct)
        {
            string path = "/beta/deviceAppManagement/mobileApps?$filter=" + Uri.EscapeDataString("isof('microsoft.graph.winGetApp')") + "&$top=999";
            for (int page = 0; path != null && page < 20; page++)
            {
                var data = await g.GetAsync(path, ct);
                foreach (var a in data?["value"] as JsonArray ?? new JsonArray())
                    if (string.Equals(a?["packageIdentifier"]?.GetValue<string>(), packageIdentifier, StringComparison.OrdinalIgnoreCase))
                        return a?["id"]?.GetValue<string>();
                string next = data?["@odata.nextLink"]?.GetValue<string>();
                path = next != null && next.StartsWith(EntraAuth.GraphUrl, StringComparison.OrdinalIgnoreCase) ? next.Substring(EntraAuth.GraphUrl.Length) : null;
            }
            return null;
        }

        public static string PortalUrl(string appId) => "https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/" + appId;

        private static bool IsWebUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
    }
}
