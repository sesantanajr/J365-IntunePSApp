using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using J365.IntunePSApp.Core;
using Microsoft.Win32;

namespace J365.IntunePSApp.Views
{
    /// <summary>
    /// Light or dark theme of the application: it follows Windows ("System") or a fixed choice made in Settings or on the
    /// sidebar. The application resources are, in this order: [0] the WPF Fluent control styles, [1] the colors of the
    /// theme (Themes/Light.xaml or Themes/Dark.xaml) and [2] the shared styles; a theme change replaces [0] and [1], and
    /// every view follows at once because colors are referenced with DynamicResource.
    /// </summary>
    public static class ThemeManager
    {
        public const string SystemTheme = "System";
        public const string LightTheme = "Light";
        public const string DarkTheme = "Dark";

        /// <summary>Raised after a theme change, for the few colors set in code.</summary>
        public static event Action Changed;

        public static bool IsDark { get; private set; }
        private static bool _applied;
        private static bool _listening;
        private static string _override;   // --theme on the command line (screenshots)

        /// <summary>The saved choice: System, Light or Dark.</summary>
        public static string Setting => Normalize(ConfigStore.Current.AppTheme);

        public static string Normalize(string value)
        {
            if (string.Equals(value, LightTheme, StringComparison.OrdinalIgnoreCase)) return LightTheme;
            if (string.Equals(value, DarkTheme, StringComparison.OrdinalIgnoreCase)) return DarkTheme;
            return SystemTheme;
        }

        public static void Initialize(string commandLineTheme)
        {
            _override = string.IsNullOrWhiteSpace(commandLineTheme) ? null : Normalize(commandLineTheme);
            Apply();
            if (_listening) return;
            _listening = true;
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category != UserPreferenceCategory.General) return;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => { if ((_override ?? Setting) == SystemTheme) Apply(); }));
            };
        }

        /// <summary>Saves and applies a choice (System, Light or Dark).</summary>
        public static void Set(string setting)
        {
            ConfigStore.Current.AppTheme = Normalize(setting);
            ConfigStore.Save();
            _override = null;
            Apply();
        }

        /// <summary>Light to dark and back (sidebar button). A fixed choice replaces "follow Windows".</summary>
        public static void Toggle() => Set(IsDark ? LightTheme : DarkTheme);

        /// <summary>True when Windows asks applications for the dark mode (Settings > Personalization > Colors).</summary>
        public static bool SystemPrefersDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch { return false; }
        }

        public static void Apply()
        {
            var app = Application.Current;
            if (app == null) return;
            string setting = _override ?? Setting;
            bool dark = setting == DarkTheme || (setting == SystemTheme && SystemPrefersDark());
            if (_applied && dark == IsDark) return;

            string mode = dark ? "Dark" : "Light";
            var merged = app.Resources.MergedDictionaries;
            var fluent = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{mode}.xaml") };
            var colors = new ResourceDictionary { Source = new Uri($"pack://application:,,,/J365.IntunePSApp;component/Themes/{mode}.xaml") };
            if (merged.Count >= 3)
            {
                merged[0] = fluent;
                merged[1] = colors;
            }
            IsDark = dark;
            _applied = true;
            foreach (Window w in app.Windows) ApplyWindowFrame(w);
            Changed?.Invoke();
        }

        /// <summary>
        /// Rounded corners (Windows 11) and a frame in the theme: the system menu and the resize border follow the
        /// light or dark mode of the application instead of the one of Windows.
        /// </summary>
        public static void ApplyWindowFrame(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int dark = IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
                int round = DwmCornerRound;
                DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
            }
            catch { }
        }

        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmCornerRound = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
