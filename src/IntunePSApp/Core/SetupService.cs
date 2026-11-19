using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using J365.IntunePSApp.Localization;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    public enum SetupState { Pending, Working, Ok, Warning, Failed }

    /// <summary>One dependency shown on the first-run screen and on Settings &gt; Dependencies.</summary>
    public class SetupItem : INotifyPropertyChanged
    {
        private SetupState _state = SetupState.Pending;
        private string _detail = "";
        private double _progress = -1;

        public SetupItem(string key, bool required) { Key = key; Required = required; }

        public string Key { get; }
        public bool Required { get; }
        public string Title => T("Setup_Item_" + Key);
        public string Description => T("Setup_ItemD_" + Key);

        public SetupState State
        {
            get => _state;
            set { _state = value; Raise(nameof(State)); Raise(nameof(Glyph)); Raise(nameof(Color)); Raise(nameof(IsWorking)); Raise(nameof(StateLabel)); }
        }

        /// <summary>Version, item count or the reason of a failure.</summary>
        public string Detail { get => _detail; set { _detail = value ?? ""; Raise(nameof(Detail)); } }

        /// <summary>0-100, or -1 when the size is unknown.</summary>
        public double Progress { get => _progress; set { _progress = value; Raise(nameof(Progress)); Raise(nameof(IsIndeterminate)); } }

        public bool IsWorking => State == SetupState.Working;
        public bool IsIndeterminate => Progress < 0;
        public string StateLabel => T("Setup_State_" + State);
        public string Glyph => State switch
        {
            SetupState.Ok => "",
            SetupState.Warning => "",
            SetupState.Failed => "",
            SetupState.Working => "",
            _ => ""
        };
        public string Color => State switch
        {
            SetupState.Ok => "#16A34A",
            SetupState.Warning => "#D97706",
            SetupState.Failed => "#DC2626",
            SetupState.Working => "#2563EB",
            _ => "#94A3B8"
        };

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public void RefreshTexts() { Raise(nameof(Title)); Raise(nameof(Description)); Raise(nameof(StateLabel)); }
    }

    /// <summary>
    /// First-run automation: downloads and validates everything the application needs (application database,
    /// PSAppDeployToolkit template and Microsoft's IntuneWinAppUtil) so the user never installs anything by hand.
    /// </summary>
    public static class SetupService
    {
        public const string Catalog = "catalog";
        public const string Psadt = "psadt";
        public const string Iwau = "iwau";

        public static List<SetupItem> CreateItems() => new List<SetupItem>
        {
            new SetupItem(Catalog, false),
            new SetupItem(Psadt, true),
            new SetupItem(Iwau, false),
        };

        public static bool IsReady(string key) => key switch
        {
            Catalog => CatalogService.Instance.IsLoaded,
            Psadt => ToolsService.LocalPsadt() != null,
            Iwau => ToolsService.HasIntuneWinAppUtil,
            _ => true
        };

        /// <summary>True on the first run, or when a required dependency disappeared (for example the tools folder was deleted).</summary>
        public static bool NeedsSetup => !ConfigStore.Current.SetupCompleted || !IsReady(Psadt);

        /// <summary>Fills the items with what is already on disk, without downloading anything.</summary>
        public static void Inspect(IEnumerable<SetupItem> items)
        {
            foreach (var item in items)
            {
                bool ready = IsReady(item.Key);
                item.Progress = -1;
                item.State = ready ? SetupState.Ok : item.Required ? SetupState.Failed : SetupState.Warning;
                item.Detail = ready ? ReadyDetail(item.Key) : T("Setup_Missing");
            }
        }

        private static string ReadyDetail(string key) => key switch
        {
            Catalog => T("Setup_CatalogDetail", CatalogService.Instance.Apps.Count.ToString("N0", Loc.Instance.Culture),
                CatalogService.Instance.Generated?.ToLocalTime().ToString("d", Loc.Instance.Culture) ?? "?"),
            Psadt => T("Setup_VersionDetail", ToolsService.PsadtVersion),
            Iwau => T("Setup_IwauDetail"),
            _ => ""
        };

        /// <summary>
        /// Downloads whatever is missing. Returns true when every required dependency is ready.
        /// With <paramref name="refresh"/> the database and the toolkit are also checked for newer versions.
        /// </summary>
        public static async Task<bool> RunAsync(IList<SetupItem> items, bool refresh, CancellationToken ct)
        {
            foreach (var item in items) { item.State = SetupState.Pending; item.Detail = ""; item.Progress = -1; }
            foreach (var item in items)
            {
                item.State = SetupState.Working;
                var progress = new Progress<(long read, long? total)>(p =>
                {
                    if (p.total.HasValue && p.total.Value > 0) item.Progress = Math.Min(100, p.read * 100d / p.total.Value);
                    item.Detail = p.total.HasValue ? $"{p.read / 1048576d:N1} / {p.total.Value / 1048576d:N1} MB" : $"{p.read / 1048576d:N1} MB";
                });
                try
                {
                    switch (item.Key)
                    {
                        case Catalog:
                            if (!CatalogService.Instance.IsLoaded) await CatalogService.Instance.LoadAsync();
                            if (!CatalogService.Instance.IsLoaded || refresh)
                                await CatalogService.Instance.UpdateAsync(ConfigStore.Current.CatalogUrl, progress, ct, force: !CatalogService.Instance.IsLoaded);
                            break;
                        case Psadt:
                            if (refresh) await ToolsService.UpdatePsadtAsync(ct, progress);
                            else await ToolsService.EnsurePsadtAsync(ct, progress);
                            break;
                        case Iwau:
                            await ToolsService.EnsureIntuneWinAppUtilAsync(ct, progress);
                            break;
                    }
                    bool ready = IsReady(item.Key);
                    item.State = ready ? SetupState.Ok : item.Required ? SetupState.Failed : SetupState.Warning;
                    item.Detail = ready ? ReadyDetail(item.Key) : T("Setup_Fallback_" + item.Key);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { item.State = SetupState.Pending; item.Detail = ""; throw; }
                catch (Exception ex)
                {
                    bool ready = IsReady(item.Key); // an update failed but the previous copy still works
                    item.State = ready ? SetupState.Ok : item.Required ? SetupState.Failed : SetupState.Warning;
                    item.Detail = ready ? ReadyDetail(item.Key) : ex.Message;
                    Log.Warn(T("Log_SetupItemFailed", item.Title, ex.Message));
                }
                item.Progress = -1;
            }
            bool ok = items.Where(i => i.Required).All(i => i.State == SetupState.Ok);
            if (ok && !ConfigStore.Current.SetupCompleted)
            {
                ConfigStore.Current.SetupCompleted = true;
                ConfigStore.Save();
            }
            return ok;
        }
    }

    /// <summary>Looks for new versions of this application and of the PSAppDeployToolkit (at most once a day).</summary>
    public static class UpdateService
    {
        public static event Action Changed;

        /// <summary>Newer application version available on GitHub, or null.</summary>
        public static string NewAppVersion
        {
            get
            {
                string latest = ConfigStore.Current.LatestAppVersion;
                return !string.IsNullOrEmpty(latest) && VersionUtil.Compare(latest, AppInfo.Version) > 0 ? latest : null;
            }
        }

        /// <summary>Newer PSAppDeployToolkit 4.x available, or null.</summary>
        public static string NewPsadtVersion
        {
            get
            {
                string latest = ConfigStore.Current.LatestPsadtVersion, local = ToolsService.PsadtVersion;
                return !string.IsNullOrEmpty(latest) && local != null && VersionUtil.Compare(latest, local) > 0 ? latest : null;
            }
        }

        public static async Task CheckAsync(bool force, CancellationToken ct)
        {
            var cfg = ConfigStore.Current;
            if (!force)
            {
                if (!cfg.CheckForUpdates) return;
                if (cfg.LastUpdateCheck.HasValue && (DateTime.Now - cfg.LastUpdateCheck.Value).TotalHours < 20) return;
            }
            string json = await Http.TryGetStringAsync(AppUpdater.ApiUrl, 20, ct);
            if (json != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                    if (tag.Length > 0) cfg.LatestAppVersion = tag.TrimStart('v', 'V');
                }
                catch { }
            }
            var psadt = await ToolsService.GetLatestPsadtReleaseAsync(ct);
            if (psadt != null) cfg.LatestPsadtVersion = psadt.Value.Version;
            cfg.LastUpdateCheck = DateTime.Now;
            ConfigStore.Save();
            Changed?.Invoke();
        }
    }
}
