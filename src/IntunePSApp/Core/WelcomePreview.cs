using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using static J365.IntunePSApp.Localization.Loc;

namespace J365.IntunePSApp.Core
{
    /// <summary>The moment of a required installation the preview shows.</summary>
    public enum WelcomeMoment
    {
        /// <summary>An application of the list is open and deferrals are left.</summary>
        AppOpen,
        /// <summary>None of the applications is open.</summary>
        AppClosed,
        /// <summary>The deferrals are used up and an application is open: the last notice.</summary>
        LastNotice
    }

    /// <summary>What the welcome dialog of PSAppDeployToolkit 4.1.8 shows at one moment, or why it does not appear.</summary>
    public sealed class WelcomePreviewInfo
    {
        /// <summary>False: the dialog does not appear at this moment (<see cref="HiddenReason"/> says why).</summary>
        public bool Shown { get; init; } = true;
        public string HiddenReason { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string Message { get; init; } = "";
        public string Custom { get; init; } = "";
        /// <summary>The one row of the list (the toolkit groups the processes by description); null: no list.</summary>
        public string AppName { get; init; }
        /// <summary>The program under the name, as the toolkit writes it ("acrobat.exe").</summary>
        public string AppExe { get; init; } = "";
        public string DeferralsLabel { get; init; }
        public string DeferralsValue { get; init; }
        public string CountdownLabel { get; init; }
        public string CountdownValue { get; init; }
        /// <summary>Fluent: the accent button, on the left (alone, it takes the right half).</summary>
        public string MainButton { get; init; } = "";
        /// <summary>Fluent: the button on the right; null when the user cannot defer.</summary>
        public string DeferButton { get; init; }

        // Classic style
        public string ClassicWelcome { get; init; } = "";
        public string ClassicCloseApps { get; init; } = "";
        public string ClassicExpiry { get; init; } = "";
        public string ClassicDeferrals { get; init; }
        public string ClassicExpiryWarning { get; init; } = "";
        public string ClassicCountdownLabel { get; init; }
        public string ClassicCountdownValue { get; init; }
        public string ClassicButtonClose { get; init; }
        public string ClassicButtonDefer { get; init; } = "";
        public bool ClassicDeferEnabled { get; init; }
        public string ClassicButtonContinue { get; init; } = "";

        /// <summary>What happens at this moment, for the person who builds the package.</summary>
        public string Note { get; init; } = "";
    }

    /// <summary>The settings of the package the preview depends on.</summary>
    public sealed class WelcomePreviewInput
    {
        /// <summary>Auto, WhenNeeded, Silent or Interactive.</summary>
        public string Mode { get; init; } = "Auto";
        public string AppName { get; init; } = "";
        public string AppVersion { get; init; } = "";
        public string Company { get; init; } = "";
        public IList<string> Processes { get; init; } = new List<string>();
        public bool AllowDefer { get; init; }
        public int DeferTimes { get; init; } = 3;
        public int CountdownSeconds { get; init; } = 300;
        public string CustomMessage { get; init; } = "";
    }

    /// <summary>
    /// The welcome dialog the package shows during a required installation from Intune, moment by moment, the way
    /// Show-ADTInstallationWelcome of PSAppDeployToolkit 4.1.8 decides it (one user signed in): the texts come from the
    /// string table of the toolkit in the language of the dialogs.
    /// </summary>
    public static class WelcomePreview
    {
        /// <summary>The toolkit gives up waiting after DefaultTimeout (config.psd1: 3300 s).</summary>
        public const int TimeoutMinutes = 55;

        public static WelcomePreviewInfo Build(WelcomePreviewInput input, WelcomeMoment moment, PsadtStrings strings)
        {
            string Str(string fallbackKey, params string[] path) => Clean(strings?.Get(path)) is { Length: > 0 } s ? s : T(fallbackKey);
            bool apps = input.Processes.Count > 0;
            int deferrals = Math.Clamp(input.DeferTimes, 1, 99);
            var countdown = TimeSpan.FromSeconds(Math.Max(0, input.CountdownSeconds));
            string mode = input.Mode is "WhenNeeded" or "Silent" or "Interactive" ? input.Mode : "Auto";
            string close = Str("Prev_CloseAndInstall", "CloseAppsPrompt", "Fluent", "ButtonLeftText", "Install");
            string install = Str("Prev_Install", "CloseAppsPrompt", "Fluent", "ButtonLeftNoProcessesText", "Install");

            WelcomePreviewInfo Hidden(string reason) => new() { Shown = false, HiddenReason = reason, Note = reason };
            if (mode == "Silent") return Hidden(T("Prev_HiddenSilent"));
            if (!apps && !input.AllowDefer) return Hidden(T("Prev_HiddenNothing"));
            // Without applications to close, the moments "open" and "closed" are the same.
            if (!apps && moment == WelcomeMoment.AppOpen) moment = WelcomeMoment.AppClosed;
            // Without deferral, the first notice is already the last one.
            if (!input.AllowDefer && moment == WelcomeMoment.LastNotice) moment = WelcomeMoment.AppOpen;
            if (moment == WelcomeMoment.AppClosed && apps && mode == "WhenNeeded") return Hidden(T("Prev_HiddenWhenNeeded"));
            if (moment == WelcomeMoment.AppClosed && !input.AllowDefer) return Hidden(T("Prev_HiddenNoDefer"));
            if (moment == WelcomeMoment.LastNotice && !apps) return Hidden(T("Prev_HiddenNothing"));

            bool listed = apps && moment != WelcomeMoment.AppClosed;
            bool canDefer = input.AllowDefer && moment != WelcomeMoment.LastNotice;
            bool counting = !canDefer && listed && countdown > TimeSpan.Zero;
            string title = (input.AppName ?? "").Trim();
            string company = string.IsNullOrWhiteSpace(input.Company) ? AppInfo.PsadtName : input.Company.Trim();
            string subtitle = Str("Prev_Subtitle", "CloseAppsPrompt", "Fluent", "Subtitle", "Install");
            subtitle = subtitle.Contains("{0}") ? string.Format(CultureInfo.InvariantCulture, subtitle, company)
                : Regex.Replace(subtitle, @"\{Toolkit\\CompanyName\}", company.Replace("$", "$$"));
            string exe = apps ? input.Processes[0].Trim().ToLowerInvariant() : "";
            if (exe.Length > 0 && !exe.EndsWith(".exe", StringComparison.Ordinal)) exe += ".exe";

            string note = moment switch
            {
                WelcomeMoment.AppOpen when canDefer => T("Prev_NoteOpen", deferrals, close),
                WelcomeMoment.AppOpen => counting ? T("Prev_NoteOpenCountdown", Duration(countdown), close) : T("Prev_NoteOpenWait", close),
                WelcomeMoment.AppClosed => T("Prev_NoteClosed", install),
                _ => counting ? T("Prev_NoteLast", deferrals, Duration(countdown)) : T("Prev_NoteLastWait", deferrals, close)
            };
            if (canDefer) note += " " + T("Prev_NoteTimeout", TimeoutMinutes);

            return new WelcomePreviewInfo
            {
                Title = title,
                Subtitle = subtitle,
                Message = listed ? Str("Prev_CloseBody", "CloseAppsPrompt", "Fluent", "DialogMessage", "Install")
                    : Str("Prev_ReadyBody", "CloseAppsPrompt", "Fluent", "DialogMessageNoProcesses", "Install"),
                Custom = (input.CustomMessage ?? "").Trim(),
                AppName = listed ? (string.IsNullOrWhiteSpace(input.AppName) ? T("Prev_AppLabel") : input.AppName.Trim()) : null,
                AppExe = listed ? exe : "",
                DeferralsLabel = canDefer ? Str("Prev_Deferrals", "CloseAppsPrompt", "Fluent", "DeferralsRemaining") : null,
                // The toolkit counts the deferral being offered: 3, then 2, then 1, then the last notice.
                DeferralsValue = canDefer ? deferrals.ToString(CultureInfo.InvariantCulture) : null,
                CountdownLabel = counting ? Str("Prev_Countdown", "CloseAppsPrompt", "Fluent", "AutomaticStartCountdown") : null,
                CountdownValue = counting ? $"{(int)countdown.TotalHours}h {countdown.Minutes}m {countdown.Seconds}s" : null,
                MainButton = listed ? close : install,
                DeferButton = canDefer ? Str("Prev_Defer", "CloseAppsPrompt", "Fluent", "ButtonRightText") : null,

                ClassicWelcome = Str("Prev_ClassicWelcome", "CloseAppsPrompt", "Classic", "WelcomeMessage", "Install"),
                ClassicCloseApps = listed ? Str("Prev_ClassicCloseApps", "CloseAppsPrompt", "Classic", "CloseAppsMessage", "Install") : "",
                ClassicExpiry = canDefer ? Str("Prev_ClassicExpiry", "CloseAppsPrompt", "Classic", "ExpiryMessage", "Install") : "",
                ClassicDeferrals = canDefer ? Str("Prev_ClassicDeferrals", "CloseAppsPrompt", "Classic", "DeferralsRemaining") + " " + deferrals.ToString(CultureInfo.InvariantCulture) : null,
                ClassicExpiryWarning = canDefer ? Str("Prev_ClassicExpiryWarning", "CloseAppsPrompt", "Classic", "ExpiryWarning") : "",
                ClassicCountdownLabel = counting ? Str("Prev_ClassicCountdown", "CloseAppsPrompt", "Classic", "CountdownClose", "Install") : null,
                ClassicCountdownValue = counting ? $"{(int)countdown.TotalHours}:{countdown.Minutes:00}:{countdown.Seconds:00}" : null,
                ClassicButtonClose = listed ? Mnemonic(Str("Prev_ClassicClose", "CloseAppsPrompt", "Classic", "ButtonClose")) : null,
                ClassicButtonDefer = Mnemonic(Str("Prev_Defer", "CloseAppsPrompt", "Classic", "ButtonDefer")),
                ClassicDeferEnabled = canDefer,
                ClassicButtonContinue = Mnemonic(Str("Prev_ClassicContinue", "CloseAppsPrompt", "Classic", "ButtonContinue")),
                Note = note
            };
        }

        /// <summary>The moments worth showing for these settings (the others look the same as one of them).</summary>
        public static IReadOnlyList<WelcomeMoment> Moments(WelcomePreviewInput input)
        {
            var list = new List<WelcomeMoment>();
            if (input.Processes.Count > 0) list.Add(WelcomeMoment.AppOpen);
            list.Add(WelcomeMoment.AppClosed);
            if (input.Processes.Count > 0 && input.AllowDefer) list.Add(WelcomeMoment.LastNotice);
            return list;
        }

        /// <summary>The texts of the toolkit may carry formatting tags ([bold], [accent], [url]...): the preview shows the words.</summary>
        private static string Clean(string text) => text == null ? null : Regex.Replace(text, @"\[/?(bold|italic|accent|url)(=[^\]]*)?\]", "").Trim();

        /// <summary>"Close &amp;Programs" is shown "Close Programs" ("&amp;&amp;" is an ampersand).</summary>
        private static string Mnemonic(string text) => (text ?? "").Replace("&&", "\u0001").Replace("&", "").Replace('\u0001', '&');

        private static string Duration(TimeSpan t) => BuildProgress.Duration(t);
    }
}
