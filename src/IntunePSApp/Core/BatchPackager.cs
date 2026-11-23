using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>
    /// What every package of a batch shares (the company, how the installation is shown, the toasts of Intune, the
    /// publication). The rest of each package comes from the company defaults of Settings and from what the installer
    /// and the database say.
    /// </summary>
    public class BatchOptions
    {
        public string CompanyName { get; set; } = "";
        public string DeployMode { get; set; } = "Auto";
        /// <summary>"" or null (automatic: hidden for silent packages), showAll, showReboot or hideAll.</summary>
        public string IntuneNotifications { get; set; } = "";
        public bool AllowDefer { get; set; } = true;
        public int DeferTimes { get; set; } = 3;
        /// <summary>Owner shown in Intune. Empty: the company.</summary>
        public string Owner { get; set; } = "";
        /// <summary>Use the built-in .intunewin packager instead of IntuneWinAppUtil.</summary>
        public bool NativePackager { get; set; }
        /// <summary>Folder of the packages. Empty: the folder of Settings.</summary>
        public string OutputRoot { get; set; } = "";

        // Publication (the connection of this computer, as the editor publishes)
        /// <summary>Publishes each package to Intune as soon as it is generated.</summary>
        public bool Publish { get; set; }
        /// <summary>An app with the same name in Intune: Update (a new version of it; created when there is none), CreateNew or Fail.</summary>
        public string IfExists { get; set; } = "Update";
        /// <summary>none | available | required (only for apps the publication creates; an updated app keeps its assignments).</summary>
        public string AssignIntent { get; set; } = "none";
        /// <summary>AllUsers | AllDevices | Group</summary>
        public string AssignTo { get; set; } = "AllUsers";
        public string GroupId { get; set; } = "";

        public static BatchOptions FromConfig(AppConfig c) => new BatchOptions
        {
            CompanyName = c?.CompanyName ?? "",
            DeployMode = string.IsNullOrEmpty(c?.DeployMode) ? "Auto" : c.DeployMode,
            IntuneNotifications = c?.IntuneNotifications ?? "",
            AllowDefer = c?.AllowDefer ?? true,
            DeferTimes = c?.DeferTimes ?? 3,
        };

        /// <summary>The options of the publication of one package with the connection of this computer.</summary>
        public PublishOptions PublishOptions(bool certificate) => new PublishOptions
        {
            TenantId = IntuneConnection.TenantId,
            ClientId = IntuneConnection.ClientId,
            IfExists = IfExists is "CreateNew" or "Fail" ? IfExists : "Update",
            AssignIntent = AssignIntent is "available" or "required" ? AssignIntent : "none",
            AssignTo = AssignTo is "AllDevices" or "Group" ? AssignTo : "AllUsers",
            GroupId = (GroupId ?? "").Trim(),
            CertificateThumbprint = certificate ? IntuneConnection.CertificateThumbprint : ""
        };
    }

    public enum BatchState { Waiting, Downloading, Analyzing, Building, Done, Warning, Skipped, Failed, Cancelled, Publishing, Published, PublishFailed }

    /// <summary>One application of a batch: the installer chosen for it and how far its package got.</summary>
    public class BatchItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public BatchItem(CatalogApp app, CatalogInstaller installer)
        {
            App = app;
            Installer = installer;
        }

        public CatalogApp App { get; }
        public CatalogInstaller Installer { get; }
        public List<string> Warnings { get; } = new List<string>();

        public BatchState State { get; private set; } = BatchState.Waiting;
        public string Message { get; private set; } = "";
        public double Percent { get; private set; }
        /// <summary>The package generated for this application (null until it is built).</summary>
        public BuildResult Result { get; internal set; }
        /// <summary>Folder of the package generated for this application.</summary>
        public string PackageFolder => Result?.PackageFolder;
        /// <summary>The installer downloaded for this application: a skipped one can be opened in the editor without a new download.</summary>
        public string InstallerFile { get; internal set; }
        /// <summary>The outcome of the publication, when the batch published.</summary>
        public PublishResult Publication { get; internal set; }

        public string Title => $"{App?.Name} {App?.Version}".Trim();
        public string Initials => App?.Initials ?? "?";
        public string AvatarColor => App?.AvatarColor ?? "#0A5CA8";
        public string InstallerLabel => Installer == null || !Installer.IsSupported
            ? T("Batch_NoInstaller")
            : $"{Installer.TypeLabel}  ·  {Installer.ArchLabel}  ·  {Installer.ScopeLabel}";
        public string StateLabel => T("Batch_State_" + State);
        public bool IsFinished => State is BatchState.Done or BatchState.Warning or BatchState.Skipped or BatchState.Failed or BatchState.Cancelled
            or BatchState.Published or BatchState.PublishFailed;
        public bool IsRunning => State is BatchState.Downloading or BatchState.Analyzing or BatchState.Building or BatchState.Publishing;
        /// <summary>The package was generated (and published, when the batch publishes).</summary>
        public bool Succeeded => State is BatchState.Done or BatchState.Warning or BatchState.Published;
        /// <summary>A package exists for this application, whatever happened to its publication.</summary>
        public bool Built => Result != null;
        /// <summary>A package that was skipped or failed can be finished by hand in the editor, with the installer already here.</summary>
        public bool CanOpenInEditor => State is BatchState.Skipped or BatchState.Failed && !string.IsNullOrEmpty(InstallerFile) && File.Exists(InstallerFile);
        /// <summary>Theme key of the color of the state (the row follows a change of theme).</summary>
        public string StateColor => State switch
        {
            BatchState.Done or BatchState.Published => "Success",
            BatchState.Warning or BatchState.Skipped => "Warning",
            BatchState.Failed or BatchState.PublishFailed => "Danger",
            BatchState.Cancelled or BatchState.Waiting => "Muted",
            _ => "Brand"
        };

        internal void Update(BatchState state, string message, double percent)
        {
            State = state;
            Message = message ?? "";
            Percent = Math.Clamp(percent, 0, 100);
            Changed();
        }

        /// <summary>The text of the current step changes; the state and the bar stay.</summary>
        internal void Say(string message) => Update(State, message, Percent);

        internal void Finish(BatchState state, string message) => Update(state, message, state is BatchState.Done or BatchState.Warning or BatchState.Published ? 100 : Percent);

        /// <summary>Back to the start (the batch runs again, for example after a cancel).</summary>
        internal void Reset()
        {
            Warnings.Clear();
            Result = null;
            Publication = null;
            Update(BatchState.Waiting, "", 0);
        }

        /// <summary>Texts follow a change of language.</summary>
        public void Changed()
        {
            foreach (var p in new[] { nameof(State), nameof(Message), nameof(Percent), nameof(StateLabel), nameof(StateColor), nameof(IsFinished), nameof(IsRunning),
                nameof(Succeeded), nameof(Built), nameof(CanOpenInEditor), nameof(InstallerLabel) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    /// <summary>
    /// Packages several applications of the database one after the other with the same settings: each installer is
    /// downloaded from its official link and checked by its hash, analysed, completed with the knowledge of the database,
    /// built as in the editor and, when asked, published to Intune. A package that would need a decision (an .exe without
    /// known silent switches, a name that also matches other applications) is skipped with the reason, never guessed.
    /// </summary>
    public static class BatchPackager
    {
        public static readonly string[] DeployModes = { "Auto", "WhenNeeded", "Silent", "Interactive" };

        /// <summary>The items of a batch: each application with the installer the editor would choose for it.</summary>
        public static List<BatchItem> Plan(IEnumerable<CatalogApp> apps, AppConfig cfg)
            => (apps ?? Enumerable.Empty<CatalogApp>()).Where(a => a != null)
                .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .Select(a => new BatchItem(a, CatalogService.PickInstaller(a, cfg?.PreferredArch, cfg?.PreferredLocale)))
                .ToList();

        /// <summary>The settings of one package: company defaults, then the installer and the database, then the batch.</summary>
        public static PackageSettings Settings(InstallerInfo info, AppConfig cfg, BatchOptions o)
        {
            var s = (cfg ?? new AppConfig()).NewPackage();
            InstallerAnalyzer.ApplyTo(s, info);
            ApplyOptions(s, o);
            return s;
        }

        /// <summary>The shared choices of the batch over the settings of a package.</summary>
        public static void ApplyOptions(PackageSettings s, BatchOptions o)
        {
            if (s == null || o == null) return;
            s.CompanyName = (o.CompanyName ?? "").Trim();
            s.DeployMode = DeployModes.Contains(o.DeployMode) ? o.DeployMode : "Auto";
            s.IntuneNotifications = PackageBuilder.NotificationOptions.Contains(o.IntuneNotifications) ? o.IntuneNotifications : null;
            s.AllowDefer = o.AllowDefer;
            s.DeferTimes = Math.Clamp(o.DeferTimes, 1, 99);
            if (!string.IsNullOrWhiteSpace(o.Owner)) s.Owner = o.Owner.Trim();
            if (!string.IsNullOrWhiteSpace(o.OutputRoot)) s.OutputRoot = o.OutputRoot.Trim();
        }

        /// <summary>
        /// Why a package must not be built without someone looking at it: the errors the editor would show, an .exe whose
        /// silent switches are unknown (it would wait for clicks on every device), and a program name that also matches
        /// other applications of the database (they would be uninstalled, and they would satisfy the detection).
        /// </summary>
        public static List<string> Blockers(PackageSettings s, ValidationResult v, CatalogApp app)
        {
            var list = new List<string>(v?.Errors ?? new List<string>());
            if (s.InstallerType == "EXE" && string.IsNullOrWhiteSpace(s.InstallArgs)) list.Add(T("Batch_NoSilentArgs"));
            if (s.RemovePreviousVersions && app?.HasCuration != true && CatalogService.Instance.IsLoaded)
            {
                var others = CatalogService.Instance.OthersMatching(s.UninstallPattern, s.CatalogId);
                if (others.Count > 0) list.Add(T("Batch_OthersMatch", s.UninstallPattern, string.Join(", ", others)));
            }
            return list;
        }

        /// <summary>Notes about a package that was built, for the person who publishes it.</summary>
        public static List<string> Notes(PackageSettings s, ValidationResult v, CatalogApp app, CatalogInstaller inst)
        {
            // The editor asks about this one before generating; in a batch the decision is already taken (no switches).
            var notes = (v?.Warnings ?? new List<string>()).Where(w => w != T("Val_Warn_NoSilentArgs")).ToList();
            // The name of the detection was guessed from the database: an .exe whose manifest does not say what it registers.
            bool knowsName = app?.Extra?.NamePattern is { Length: > 0 } || s.InstallerType == "MSI"
                || (inst?.Arp ?? new List<ArpEntry>()).Any(e => !string.IsNullOrWhiteSpace(e.DisplayName));
            if (!knowsName && s.DetectionMethod == "Registry") notes.Add(T("Batch_GuessedName", s.DetectionNamePattern));
            var deps = CatalogService.Instance.DependenciesOf(app, inst);
            if (deps.Count > 0) notes.Add(T("Batch_Dependencies", string.Join(", ", deps.Select(d => d.Title))));
            return notes;
        }

        /// <summary>
        /// Builds (and publishes) the packages one after the other. Called from the thread of the interface, the items
        /// change there; from the command line they change on the thread that does the work.
        /// </summary>
        public static async Task RunAsync(IReadOnlyList<BatchItem> items, BatchOptions o, CancellationToken ct)
        {
            var cfg = ConfigStore.Current;
            // The lines of a publication arrive on threads of the process; they reach the items where the batch started.
            var context = SynchronizationContext.Current;
            void Post(Action a) { if (context == null) a(); else context.Post(_ => a(), null); }
            Log.Step(T("Log_BatchStart", items.Count));
            foreach (var item in items)
            {
                if (item.Succeeded) continue;   // a batch run again keeps what it already generated (and published)
                if (ct.IsCancellationRequested)
                {
                    if (!item.IsFinished) item.Finish(BatchState.Cancelled, T("Batch_NotStarted"));
                    continue;
                }
                // Generated before, only the publication failed: the publication alone is tried again (or, without
                // publication this time, the package is simply kept).
                if (item.State == BatchState.PublishFailed && item.Built)
                {
                    if (o?.Publish == true) await PublishOneAsync(item, o, Post, ct);
                    else item.Finish(item.Warnings.Count > 0 ? BatchState.Warning : BatchState.Done, T("Batch_Built", Path.GetFileName(item.Result.IntuneWinFile)));
                    continue;
                }
                item.Reset();
                await RunOneAsync(item, cfg, o, Post, ct);
            }
            Log.Info(T("Log_BatchDone", items.Count(i => i.Built), items.Count));
        }

        private static async Task RunOneAsync(BatchItem item, AppConfig cfg, BatchOptions o, Action<Action> post, CancellationToken ct)
        {
            if (item.Installer == null || !item.Installer.IsSupported) { item.Finish(BatchState.Skipped, T("Batch_NoInstaller")); return; }
            try
            {
                item.Update(BatchState.Downloading, T("Batch_Downloading"), 0);
                var download = new Progress<(long Done, long? Total)>(p =>
                {
                    if (item.State == BatchState.Downloading && p.Total is long total && total > 0) item.Update(BatchState.Downloading, T("Batch_Downloading"), p.Done * 100d / total);
                });
                string file = await CatalogService.DownloadInstallerAsync(item.App, item.Installer, download, ct);
                item.InstallerFile = file;

                item.Update(BatchState.Analyzing, T("Batch_Analyzing"), 0);
                var info = await Task.Run(() => InstallerAnalyzer.Analyze(file), ct);
                // As in the editor: the row of the database decides scope and architecture, a match by hash stays exact.
                bool byHash = info.CatalogMatch == "hash";
                CatalogService.Apply(info, item.App, item.Installer, "download");
                if (byHash) info.CatalogMatch = "hash";

                var s = Settings(info, cfg, o);
                var v = PackageBuilder.Validate(s);
                var blockers = Blockers(s, v, item.App);
                if (blockers.Count > 0)
                {
                    Log.Warn(T("Log_BatchSkipped", item.Title, string.Join(" ", blockers)));
                    item.Finish(BatchState.Skipped, string.Join(" ", blockers));
                    return;
                }
                item.Warnings.AddRange(Notes(s, v, item.App, item.Installer));

                item.Update(BatchState.Building, T("Log_CheckingPsadt"), 0);
                var build = new Progress<BuildProgressInfo>(p => { if (item.State == BatchState.Building) item.Update(BatchState.Building, p.Step, p.Percent); });
                item.Result = await Task.Run(() => PackageBuilder.BuildAsync(s, o?.NativePackager == true, ct, build), ct);
                if (o?.Publish == true) { await PublishOneAsync(item, o, post, ct); return; }
                if (item.Warnings.Count > 0) item.Finish(BatchState.Warning, string.Join(" ", item.Warnings));
                else item.Finish(BatchState.Done, T("Batch_Built", Path.GetFileName(item.Result.IntuneWinFile)));
            }
            catch (OperationCanceledException)
            {
                item.Finish(BatchState.Cancelled, T("Batch_CancelledItem"));
            }
            catch (Exception ex)
            {
                Log.Error(T("Log_BatchFailed", item.Title, ex.Message));
                item.Finish(BatchState.Failed, ex.Message);
            }
        }

        /// <summary>
        /// Publishes the package of an item with the connection of this computer, the way the editor publishes inside the
        /// application: Publish-ToIntune.ps1 without a window, the session handed over in its environment, no question.
        /// </summary>
        private static async Task PublishOneAsync(BatchItem item, BatchOptions o, Action<Action> post, CancellationToken ct)
        {
            item.Update(BatchState.Publishing, T("Publish_StepSignIn"), 0);
            string intune = item.Result?.IntuneFolder;
            try
            {
                if (string.IsNullOrEmpty(intune) || !Directory.Exists(intune)) throw new InvalidOperationException(T("Ed_OldPackage"));
                var session = await IntuneConnection.RenewAsync(ct);
                // The certificate connects by itself in the script; a user session hands over its refresh token.
                bool certificate = IntuneConnection.UsesCertificate;
                if (session == null || (!certificate && string.IsNullOrEmpty(session.RefreshToken))) { item.Finish(BatchState.PublishFailed, T("Auth_ErrNotSignedIn")); return; }
                var po = o.PublishOptions(certificate);
                if (IntunePublisher.Check(po) is string problem) { item.Finish(BatchState.PublishFailed, T(problem)); return; }
                string script = IntunePublisher.Prepare(intune);
                string cancelFile = IntunePublisher.CancelFile(script);
                var started = DateTime.UtcNow;
                using (var p = IntunePublisher.LaunchInApp(script, po, certificate ? null : session.RefreshToken, line => post(() => OnPublishLine(item, line))))
                {
                    // Cancel asks the script to stop at its next safe point (it then removes an app it created, and an
                    // existing app is never left half updated). Only a script that does not stop within 90 s is killed.
                    using (ct.Register(() =>
                    {
                        try { File.WriteAllText(cancelFile, "cancel"); } catch { }
                        _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => { try { if (!p.HasExited) p.Kill(true); } catch { } });
                    }))
                        await p.WaitForExitAsync();
                }
                try { if (File.Exists(cancelFile)) File.Delete(cancelFile); } catch { }
                // A result older than this run belongs to an earlier publication.
                var r = IntunePublisher.ReadResult(intune);
                if (r != null && File.GetLastWriteTimeUtc(IntunePublisher.ResultFile(intune)) < started.AddSeconds(-5)) r = null;
                item.Publication = r;
                PublishTexts.NoteAnswer(r);
                if (r?.IsPublished == true)
                {
                    string text = PublishTexts.Summary(r);
                    if (item.Warnings.Count > 0) text += " " + string.Join(" ", item.Warnings);
                    item.Finish(BatchState.Published, text);
                }
                else item.Finish(BatchState.PublishFailed, r != null ? PublishTexts.Summary(r) : ct.IsCancellationRequested ? T("Ed_PublishCancelled") : T("Batch_PublishNoResult"));
            }
            catch (OperationCanceledException)
            {
                item.Finish(BatchState.PublishFailed, T("Ed_PublishCancelled"));
            }
            catch (Exception ex)
            {
                Log.Error(T("Log_BatchFailed", item.Title, ex.Message));
                item.Finish(BatchState.PublishFailed, ex.Message);
            }
        }

        /// <summary>A line printed by the publishing script: a step, the percentage of the upload, or a message.</summary>
        internal static void OnPublishLine(BatchItem item, string line)
        {
            Log.Info(line);
            if (item.State != BatchState.Publishing) return;
            var pct = Regex.Match(line ?? "", @"^\s*(\d{1,3})\s*%\s*$");
            if (pct.Success) { item.Update(BatchState.Publishing, item.Message, int.Parse(pct.Groups[1].Value)); return; }
            string text = (line ?? "").Trim();
            if (text.Length == 0 || text.StartsWith("OK", StringComparison.Ordinal)) return;   // the step text stays until the next step
            item.Say(text.StartsWith("> ", StringComparison.Ordinal) ? PublishTexts.StepText(text.Substring(2)) : text);
        }

        /// <summary>The batch as tab-separated text (application, version, state, package, details), to paste in a ticket or a sheet.</summary>
        public static string Report(IEnumerable<BatchItem> items)
        {
            static string Cell(string text) => Regex.Replace(text ?? "", @"[\t\r\n]+", " ").Trim();
            var sb = new StringBuilder();
            sb.Append(T("Batch_ReportHeader")).Append("\r\n");
            foreach (var i in items ?? Enumerable.Empty<BatchItem>())
                sb.Append(Cell(i.App?.Name)).Append('\t').Append(Cell(i.App?.Version)).Append('\t').Append(Cell(i.StateLabel)).Append('\t')
                  .Append(Cell(i.PackageFolder)).Append('\t').Append(Cell(i.Message)).Append("\r\n");
            return sb.ToString();
        }
    }
}
