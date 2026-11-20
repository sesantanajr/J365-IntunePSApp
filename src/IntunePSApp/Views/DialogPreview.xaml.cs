using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using J365.IntunePSApp.Core;

namespace J365.IntunePSApp.Views
{
    /// <summary>How the preview draws the dialog: the style and the images of the package.</summary>
    public sealed class DialogPreviewLook
    {
        /// <summary>"Fluent" or "Classic" (config.psd1, UI.DialogStyle).</summary>
        public string Style { get; set; } = "Fluent";
        public Brush Accent { get; set; }
        /// <summary>Company logo, otherwise the icon of the application (Assets\AppIcon.png of the package).</summary>
        public ImageSource Logo { get; set; }
        /// <summary>The icon of the program in the list (the toolkit takes it from the running executable).</summary>
        public ImageSource AppIcon { get; set; }
        public string Company { get; set; } = "";
    }

    /// <summary>Preview of the PSAppDeployToolkit 4.1.8 welcome dialog, in the Fluent or the Classic style, at one moment.</summary>
    public partial class DialogPreview : UserControl
    {
        public DialogPreview() { InitializeComponent(); }

        public void Show(WelcomePreviewInfo m, DialogPreviewLook look)
        {
            bool classic = look.Style == "Classic";
            HiddenCard.Visibility = m.Shown ? Visibility.Collapsed : Visibility.Visible;
            HiddenText.Text = m.HiddenReason;
            FluentCard.Visibility = m.Shown && !classic ? Visibility.Visible : Visibility.Collapsed;
            ClassicCard.Visibility = m.Shown && classic ? Visibility.Visible : Visibility.Collapsed;
            if (!m.Shown) return;
            var accent = look.Accent ?? (Brush)FindResource("Brand");
            static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;
            bool listed = !string.IsNullOrEmpty(m.AppName);
            bool custom = !string.IsNullOrWhiteSpace(m.Custom);
            bool defer = m.DeferButton != null;

            // Fluent
            FSidebar.Background = accent;
            FLogo.Source = look.Logo;
            FTitle.Text = m.Title;
            FSubtitle.Text = m.Subtitle;
            FBody.Text = m.Message;
            FCustom.Text = m.Custom ?? "";
            FCustom.Visibility = Vis(custom);
            FAppRow.Visibility = Vis(listed);
            FAppIcon.Source = look.AppIcon ?? look.Logo;
            FAppName.Text = m.AppName ?? "";
            FAppExe.Text = m.AppExe ?? "";
            FAppExe.Foreground = accent;
            // The separator comes with the deferral rows only.
            FSeparator.Visibility = Vis(m.DeferralsLabel != null);
            FCountdownRow.Visibility = Vis(m.CountdownLabel != null);
            FCountdownLabel.Text = m.CountdownLabel ?? "";
            FCountdownValue.Text = m.CountdownValue ?? "";
            FDeferRow.Visibility = Vis(m.DeferralsLabel != null);
            FDeferLabel.Text = m.DeferralsLabel ?? "";
            FDeferValue.Text = m.DeferralsValue ?? "";
            FMainBtn.Background = accent;
            FMainText.Text = m.MainButton;
            FDeferBtn.Visibility = Vis(defer);
            FDeferText.Text = m.DeferButton ?? "";
            // Alone, the accent button takes the right half (like the toolkit).
            Grid.SetColumn(FMainBtn, defer ? 0 : 1);
            FMainBtn.Margin = new Thickness(defer ? 0 : 3, 0, defer ? 3 : 0, 0);

            // Classic: the accent colour only appears on the banner line, like the generated Banner.Classic.png.
            CIcon.Source = look.Logo;
            CWindowTitle.Text = m.Title;
            CBannerLogo.Source = look.Logo;
            CBannerText.Text = look.Company;
            CAccentBar.Background = accent;
            CWelcome.Text = m.ClassicWelcome;
            CTitle.Text = m.Title;
            CCustom.Text = m.Custom ?? "";
            CCustom.Visibility = Vis(custom);
            CCloseApps.Text = m.ClassicCloseApps;
            CCloseApps.Visibility = Vis(listed);
            CAppsBox.Visibility = Vis(listed);
            CApps.Text = m.AppName ?? "";
            CExpiry.Text = m.ClassicExpiry;
            CExpiry.Visibility = Vis(!string.IsNullOrEmpty(m.ClassicExpiry));
            CDeferrals.Text = m.ClassicDeferrals ?? "";
            CDeferrals.Visibility = Vis(m.ClassicDeferrals != null);
            CExpiryWarning.Text = m.ClassicExpiryWarning;
            CExpiryWarning.Visibility = Vis(!string.IsNullOrEmpty(m.ClassicExpiryWarning));
            CCountdownLabel.Text = m.ClassicCountdownLabel ?? "";
            CCountdownLabel.Visibility = Vis(m.ClassicCountdownLabel != null);
            CCountdownValue.Text = m.ClassicCountdownValue ?? "";
            CCountdownValue.Visibility = Vis(m.ClassicCountdownValue != null);
            CCloseBtn.Visibility = m.ClassicButtonClose != null ? Visibility.Visible : Visibility.Hidden;
            CCloseText.Text = m.ClassicButtonClose ?? "";
            CDeferText.Text = m.ClassicButtonDefer;
            // Without deferrals the button stays, greyed.
            CDeferBtn.Opacity = m.ClassicDeferEnabled ? 1 : 0.45;
            CContinueText.Text = m.ClassicButtonContinue;
        }
    }
}
