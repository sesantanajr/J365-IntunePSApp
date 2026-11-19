using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace J365.IntunePSApp.Views
{
    /// <summary>What the welcome dialog of a package shows.</summary>
    public sealed class DialogPreviewModel
    {
        /// <summary>"Fluent" or "Classic" (config.psd1, UI.DialogStyle).</summary>
        public string Style { get; set; } = "Fluent";
        public Brush Accent { get; set; }
        /// <summary>Company logo, otherwise the icon of the application.</summary>
        public ImageSource Logo { get; set; }
        public string Company { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Body { get; set; } = "";
        public IList<string> Apps { get; set; } = new List<string>();
        public string Custom { get; set; } = "";
        /// <summary>Null: the deferral is not offered.</summary>
        public string DeferText { get; set; }
        public string MainButton { get; set; } = "";
    }

    /// <summary>Preview of the PSAppDeployToolkit welcome dialog in the Fluent or the Classic style.</summary>
    public partial class DialogPreview : UserControl
    {
        public DialogPreview() { InitializeComponent(); }

        public void Show(DialogPreviewModel m)
        {
            bool classic = m.Style == "Classic";
            FluentCard.Visibility = classic ? Visibility.Collapsed : Visibility.Visible;
            ClassicCard.Visibility = classic ? Visibility.Visible : Visibility.Collapsed;
            var accent = m.Accent ?? (Brush)FindResource("Brand");
            bool hasApps = m.Apps != null && m.Apps.Count > 0;
            string apps = hasApps ? string.Join("\n", m.Apps.Select(a => "•  " + a)) : "";
            var customVis = string.IsNullOrWhiteSpace(m.Custom) ? Visibility.Collapsed : Visibility.Visible;
            var deferVis = m.DeferText == null ? Visibility.Collapsed : Visibility.Visible;

            // Fluent
            FLogo.Source = m.Logo;
            FTitle.Text = m.Title;
            FSubtitle.Text = m.Subtitle;
            FBody.Text = m.Body;
            FAppsBox.Visibility = hasApps ? Visibility.Visible : Visibility.Collapsed;
            FApps.Text = apps;
            FCustom.Text = m.Custom ?? "";
            FCustom.Visibility = customVis;
            FDefer.Text = m.DeferText ?? "";
            FDefer.Visibility = deferVis;
            FDeferBtn.Visibility = deferVis;
            FMainBtn.Background = accent;
            FMainText.Text = m.MainButton;

            // Classic: the accent colour only appears on the banner line, like the generated Banner.Classic.png.
            CIcon.Source = m.Logo;
            CWindowTitle.Text = m.Title;
            CBannerLogo.Source = m.Logo;
            CBannerText.Text = m.Company;
            CAccentBar.Background = accent;
            CBody.Text = m.Body;
            CAppsBox.Visibility = hasApps ? Visibility.Visible : Visibility.Collapsed;
            CApps.Text = hasApps ? string.Join("\n", m.Apps) : "";
            CCustom.Text = m.Custom ?? "";
            CCustom.Visibility = customVis;
            CDefer.Text = m.DeferText ?? "";
            CDefer.Visibility = deferVis;
            CDeferBtn.Visibility = deferVis;
            CMainText.Text = m.MainButton;
        }
    }
}
