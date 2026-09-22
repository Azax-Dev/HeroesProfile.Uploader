using Heroesprofile.Uploader.Common;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Heroesprofile.Uploader.Windows
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            if (App.Settings.AllowPreReleases) {
                PreReleasePanel.Visibility = Visibility.Visible;
            }
            RefreshReplayPathStatus();
            TwitchKeyBox.Password = App.TwitchKey;
        }

        private void TwitchKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            App.TwitchKey = TwitchKeyBox.Password;
            TwitchKeyStatus.Text = "";
        }

        private async void TwitchValidate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TwitchKeyBox.Password)) {
                TwitchKeyStatus.Text = "Paste your uploader key first.";
                return;
            }

            TwitchValidateButton.IsEnabled = false;
            TwitchKeyStatus.Text = "Checking...";
            try {
                TwitchKeyStatus.Text = await TwitchLiveSession.Validate(TwitchKeyBox.Password);
            }
            finally {
                TwitchValidateButton.IsEnabled = true;
            }
        }

        private void TwitchPortal_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.heroesprofile.com/Api/Account#twitch");
        }

        /// <summary>
        /// Empty disables the webhook, anything else has to be an http(s) url
        /// </summary>
        private static bool IsValidWebhookUrl(string url)
        {
            return url == "" ||
                (Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps));
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) {
                PreReleasePanel.Visibility = Visibility.Visible;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // don't let a broken webhook url get saved, the user has to fix or clear it
            if (!IsValidWebhookUrl(WebhookUrlBox.Text.Trim())) {
                WebhookError.Visibility = Visibility.Visible;
                WebhookUrlBox.Focus();
                e.Cancel = true;
                return;
            }
            WebhookUrlBox.Text = WebhookUrlBox.Text.Trim();

            // commit values typed but never tabbed out of, then persist them
            ReplayPathBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            WebhookUrlBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            App.Settings.Save();
        }

        private void WebhookUrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (WebhookError.Visibility == Visibility.Visible && IsValidWebhookUrl(WebhookUrlBox.Text.Trim())) {
                WebhookError.Visibility = Visibility.Collapsed;
            }
        }

        private void BrowseReplayPath_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.Description = "Select the Heroes of the Storm 'Accounts' folder";
                var current = ReplayPathBox.Text;
                if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current)) {
                    dialog.SelectedPath = current;
                } else if (Directory.Exists(ReplayLocation.DefaultPath)) {
                    dialog.SelectedPath = ReplayLocation.DefaultPath;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    ReplayPathBox.Text = dialog.SelectedPath;
                    ReplayPathBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                }
            }
        }

        private void ResetReplayPath_Click(object sender, RoutedEventArgs e)
        {
            ReplayPathBox.Text = "";
            ReplayPathBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        private void ReplayPathBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshReplayPathStatus();
        }

        /// <summary>
        /// Tell the user right away whether the folder they picked actually exists
        /// </summary>
        private void RefreshReplayPathStatus()
        {
            var path = string.IsNullOrWhiteSpace(ReplayPathBox.Text) ? ReplayLocation.DefaultPath : ReplayPathBox.Text.Trim();
            var found = Directory.Exists(path);
            ReplayPathStatus.Text = found ? $"Found: {path}" : $"Folder not found: {path}";
            if (found) {
                ReplayPathStatus.ClearValue(ForegroundProperty); // keep the current theme's color
            } else {
                ReplayPathStatus.Foreground = Brushes.OrangeRed;
            }
        }
    }
}
