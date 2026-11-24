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
        /// <summary>An application of the list is open: the dialog warns that it is about to be closed.</summary>
        AppOpen,
        /// <summary>None of the applications is open: the dialog does not appear.</summary>
        AppClosed
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

        public string CountdownLabel { get; init; }
        public string CountdownValue { get; init; }
        /// <summary>Fluent: the accent button (alone, on the right half: nothing is deferred).</summary>
        public string MainButton { get; init; } = "";

        // Classic style
        public string ClassicWelcome { get; init; } = "";
        public string ClassicCloseApps { get; init; } = "";

        public string ClassicCountdownLabel { get; init; }
        public string ClassicCountdownValue { get; init; }
        public string ClassicButtonClose { get; init; }
        /// <summary>Classic: the Defer button stays in the dialog, greyed (the installation is never deferred).</summary>
        public string ClassicButtonDefer { get; init; } = "";
        public string ClassicButtonContinue { get; init; } = "";

        /// <summary>What happens at this moment, for the person who builds the package.</summary>
        public string Note { get; init; } = "";
    }

    /// <summary>The settings of the package the preview depends on.</summary>
    public sealed class WelcomePreviewInput
    {
        /// <summary>Silent or Notify (<see cref="DeployModes"/>).</summary>
        public string Mode { get; init; } = DeployModes.Notify;
        public string AppName { get; init; } = "";
        public string AppVersion { get; init; } = "";
        public string Company { get; init; } = "";
        public IList<string> Processes { get; init; } = new List<string>();
        public int CountdownSeconds { get; init; } = 300;
        public string CustomMessage { get; init; } = "";
    }

    /// <summary>
    /// The welcome dialog the package shows during a required installation from Intune, moment by moment, the way
    /// Show-ADTInstallationWelcome of PSAppDeployToolkit 4.1.8 decides it (one user signed in): the texts come from the
    /// string table of the toolkit in the language of the dialogs. The installation is never deferred, so the dialog has
    /// one purpose: to warn that an open application is about to be closed.
    /// </summary>
    public static class WelcomePreview
    {
        /// <summary>The toolkit gives up waiting after DefaultTimeout (config.psd1: 3300 s).</summary>
        public const int TimeoutMinutes = 55;

        public static WelcomePreviewInfo Build(WelcomePreviewInput input, WelcomeMoment moment, PsadtStrings strings)
        {
            string Str(string fallbackKey, params string[] path) => Clean(strings?.Get(path)) is { Length: > 0 } s ? s : T(fallbackKey);
            bool apps = input.Processes.Count > 0;
            var countdown = TimeSpan.FromSeconds(Math.Max(0, input.CountdownSeconds));
            string mode = DeployModes.Normalize(input.Mode);
            string close = Str("Prev_CloseAndInstall", "CloseAppsPrompt", "Fluent", "ButtonLeftText", "Install");

            WelcomePreviewInfo Hidden(string reason) => new() { Shown = false, HiddenReason = reason, Note = reason };
            if (mode == DeployModes.Silent) return Hidden(T("Prev_HiddenSilent"));
            // Nothing to close: there is nothing to tell, and nothing to decide (the installation is never deferred).
            if (!apps) return Hidden(T("Prev_HiddenNothing"));
            if (moment == WelcomeMoment.AppClosed) return Hidden(T("Prev_HiddenClosed"));

            // From here on an application of the list is open: the dialog lists it and, with a countdown, closes it by itself.
            bool counting = countdown > TimeSpan.Zero;
            string title = (input.AppName ?? "").Trim();
            string company = string.IsNullOrWhiteSpace(input.Company) ? AppInfo.PsadtName : input.Company.Trim();
            string subtitle = Str("Prev_Subtitle", "CloseAppsPrompt", "Fluent", "Subtitle", "Install");
            subtitle = subtitle.Contains("{0}") ? string.Format(CultureInfo.InvariantCulture, subtitle, company)
                : Regex.Replace(subtitle, @"\{Toolkit\\CompanyName\}", company.Replace("$", "$$"));
            string exe = input.Processes[0].Trim().ToLowerInvariant();
            if (exe.Length > 0 && !exe.EndsWith(".exe", StringComparison.Ordinal)) exe += ".exe";

            string note = counting ? T("Prev_NoteOpenCountdown", Duration(countdown), close) : T("Prev_NoteOpenWait", close, TimeoutMinutes);

            return new WelcomePreviewInfo
            {
                Title = title,
                Subtitle = subtitle,
                Message = Str("Prev_CloseBody", "CloseAppsPrompt", "Fluent", "DialogMessage", "Install"),
                Custom = (input.CustomMessage ?? "").Trim(),
                AppName = string.IsNullOrWhiteSpace(input.AppName) ? T("Prev_AppLabel") : input.AppName.Trim(),
                AppExe = exe,

                CountdownLabel = counting ? Str("Prev_Countdown", "CloseAppsPrompt", "Fluent", "AutomaticStartCountdown") : null,
                CountdownValue = counting ? $"{(int)countdown.TotalHours}h {countdown.Minutes}m {countdown.Seconds}s" : null,
                MainButton = close,


                ClassicWelcome = Str("Prev_ClassicWelcome", "CloseAppsPrompt", "Classic", "WelcomeMessage", "Install"),
                ClassicCloseApps = Str("Prev_ClassicCloseApps", "CloseAppsPrompt", "Classic", "CloseAppsMessage", "Install"),

                ClassicCountdownLabel = counting ? Str("Prev_ClassicCountdown", "CloseAppsPrompt", "Classic", "CountdownClose", "Install") : null,
                ClassicCountdownValue = counting ? $"{(int)countdown.TotalHours}:{countdown.Minutes:00}:{countdown.Seconds:00}" : null,
                ClassicButtonClose = Mnemonic(Str("Prev_ClassicClose", "CloseAppsPrompt", "Classic", "ButtonClose")),
                ClassicButtonDefer = Mnemonic(Str("Prev_Defer", "CloseAppsPrompt", "Classic", "ButtonDefer")),

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
            return list;
        }

        /// <summary>The texts of the toolkit may carry formatting tags ([bold], [accent], [url]...): the preview shows the words.</summary>
        private static string Clean(string text) => text == null ? null : Regex.Replace(text, @"\[/?(bold|italic|accent|url)(=[^\]]*)?\]", "").Trim();

        /// <summary>"Close &amp;Programs" is shown "Close Programs" ("&amp;&amp;" is an ampersand).</summary>
        private static string Mnemonic(string text) => (text ?? "").Replace("&&", "\u0001").Replace("&", "").Replace('\u0001', '&');

        private static string Duration(TimeSpan t) => BuildProgress.Duration(t);
    }
}
