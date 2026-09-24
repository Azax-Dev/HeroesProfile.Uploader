using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Heroesprofile.Uploader.Common;
using Newtonsoft.Json.Linq;
using NLog;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Linux.Gui.ViewModels
{
    /// <summary>
    /// Drives the main window: loads/saves config.json, resolves the prefix (or shows the first-run
    /// empty state), owns the real Manager once a prefix is known, and exposes everything the view
    /// binds to. Manager itself fires from background threads, so every handler here marshals back
    /// to the UI thread before touching bound state.
    /// </summary>
    public partial class MainWindowViewModel : ObservableObject
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private const string NoPrefixFoundMessage = "Couldn't find Heroes of the Storm in that prefix. Check the path and try again.";

        public AppConfig Config { get; }
        public Manager Manager { get; private set; }
        public ObservableCollection<StatChipViewModel> StatChips { get; }

        [ObservableProperty]
        private ObservableCollection<ReplayRowViewModel> rows = new ObservableCollection<ReplayRowViewModel>();

        [ObservableProperty]
        private bool isEmptyState = true;

        [ObservableProperty]
        private string emptyPrefixPath = "";

        [ObservableProperty]
        private string emptyStatusText = "";

        [ObservableProperty]
        private bool emptyStatusIsOk;

        [ObservableProperty]
        private string overallStatusText = "Idle";

        [ObservableProperty]
        private string overallStatusBrushKey = "AppTextSecondaryBrush";

        [ObservableProperty]
        private bool showErrorBanner;

        [ObservableProperty]
        private string errorBannerText = "";

        [ObservableProperty]
        private string listCaption = "";

        [ObservableProperty]
        private bool isPaused;

        [ObservableProperty]
        private string updateStatusText = "";

        private string _updateReleaseUrl;

        [ObservableProperty]
        private bool postMatchPage;

        [ObservableProperty]
        private bool preMatchPage;

        [ObservableProperty]
        private bool twitchExtension;

        [ObservableProperty]
        private bool minimizeToTray;

        [ObservableProperty]
        private bool startOnLogin;

        /// <summary>Not a config setting: mirrors whether the app-menu entry exists (also set by the `install` CLI command).</summary>
        [ObservableProperty]
        private bool showInAppMenu;

        public string PauseButtonGlyph => IsPaused ? "▶" : "⏸"; // ▶ / ⏸
        public string PauseButtonTooltip => IsPaused ? "Resume uploading" : "Pause uploading";

        private ReplayListBridge _bridge;

        public MainWindowViewModel()
        {
            Config = LoadConfigSafely();
            StatChips = new ObservableCollection<StatChipViewModel>(StatusPresentation.GridOrder.Select(s => new StatChipViewModel(s)));

            // Assign backing fields directly (not the generated properties) so this initial sync
            // doesn't itself trigger the OnXChanged handlers below, which persist to config.json.
            postMatchPage = Config.PostMatchPage;
            preMatchPage = Config.PreMatchPage;
            twitchExtension = Config.TwitchExtension;
            minimizeToTray = Config.MinimizeToTray;
            startOnLogin = Config.StartOnLogin;
            showInAppMenu = DesktopIntegration.IsAppMenuEntryInstalled;

            TryResolveAndStart(Config.Prefix, reportErrorIfSet: true);
        }

        private static AppConfig LoadConfigSafely()
        {
            try {
                return AppConfig.Load();
            }
            catch (ConfigError ex) {
                // Corrupt config.json - log it and start from defaults rather than refusing to launch;
                // the user can fix things up again from Settings (which will overwrite the bad file).
                _log.Error(ex.Message);
                return new AppConfig();
            }
        }

        private void TryResolveAndStart(string prefix, bool reportErrorIfSet)
        {
            if (string.IsNullOrWhiteSpace(prefix)) {
                IsEmptyState = true;
                return;
            }

            if (!PrefixSetup.TryApply(prefix, out _)) {
                IsEmptyState = true;
                EmptyPrefixPath = prefix;
                if (reportErrorIfSet) {
                    EmptyStatusIsOk = false;
                    EmptyStatusText = NoPrefixFoundMessage;
                }
                return;
            }

            IsEmptyState = false;
            StartManager();
        }

        /// <summary>Called by the view after the folder picker returns a path in the first-run empty state.</summary>
        public async Task UseNewPrefixAsync(string path)
        {
            EmptyPrefixPath = path;

            if (!PrefixSetup.TryApply(path, out _)) {
                EmptyStatusIsOk = false;
                EmptyStatusText = NoPrefixFoundMessage;
                return;
            }

            EmptyStatusIsOk = true;
            EmptyStatusText = "Found it — switching to your replay list…";
            Config.Prefix = path;
            SaveConfig();

            await Task.Delay(600);
            IsEmptyState = false;
            StartManager();
        }

        private void StartManager()
        {
            if (Manager != null) {
                return;
            }

            Directory.CreateDirectory(AppConfig.DataDir);
            Manager = new Manager(new ReplayStorage(Path.Combine(AppConfig.DataDir, "replays_v8.xml"))) {
                PreMatchPage = Config.PreMatchPage,
                PostMatchPage = Config.PostMatchPage,
            };
            WebhookNotifier.WebhookUrl = Config.WebhookUrl;
            Manager.Twitch.Key = Config.TwitchUploaderKey;
            Manager.Twitch.Enabled = Config.TwitchExtension;

            _bridge = new ReplayListBridge(Manager);
            Rows = _bridge.Rows;
            Rows.CollectionChanged += (_, __) => UpdateListCaption();

            Manager.PropertyChanged += (_, e) => Dispatcher.UIThread.Post(() => OnManagerPropertyChanged(e.PropertyName));

            _log.Info($"Starting: prefix={Config.Prefix}, accounts={ReplayLocation.Current}, " +
                $"preMatchPage={Config.PreMatchPage}, postMatchPage={Config.PostMatchPage}, " +
                $"twitchExtension={Config.TwitchExtension}, webhook={(string.IsNullOrWhiteSpace(Config.WebhookUrl) ? "off" : "on")}");

            // Common.Uploader, spelled out - see PrefixSetup/RunCommand for why the plain name resolves wrong here.
            Manager.Start(new SettledMonitor(), new LiveMonitor(), new Analyzer(), new Common.Uploader(), new LiveProcessor(Manager.PreMatchPage, Manager.Twitch));

            UpdateListCaption();
            RefreshAggregates();
        }

        private void OnManagerPropertyChanged(string propertyName)
        {
            if (propertyName == nameof(Manager.Paused)) {
                IsPaused = Manager.Paused;
            }
            RefreshAggregates();
        }

        private void RefreshAggregates()
        {
            var totals = Manager?.Aggregates;
            foreach (var chip in StatChips) {
                chip.Total = totals != null && totals.TryGetValue(chip.Status, out var n) ? n : 0;
            }
            UpdateOverallStatus();
        }

        private void UpdateOverallStatus()
        {
            var errorCount = StatChips.First(c => c.Status == UploadStatus.UploadError).Total;
            ShowErrorBanner = errorCount > 0;
            ErrorBannerText = errorCount == 1 ? "1 upload failed — see log" : $"{errorCount} uploads failed — see log";

            if (Manager == null) {
                OverallStatusText = "Idle";
                OverallStatusBrushKey = "AppTextSecondaryBrush";
            } else if (IsPaused) {
                OverallStatusText = "Paused";
                OverallStatusBrushKey = "AppTextSecondaryBrush";
            } else if (Manager.Status == "Uploading...") {
                OverallStatusText = "Uploading…";
                OverallStatusBrushKey = "StatusProgressBrush";
            } else if (errorCount > 0) {
                OverallStatusText = "Idle — last upload failed";
                OverallStatusBrushKey = "StatusErrorBrush";
            } else {
                OverallStatusText = "Idle";
                OverallStatusBrushKey = "AppTextSecondaryBrush";
            }
        }

        private void UpdateListCaption()
        {
            var count = Rows.Count;
            ListCaption = count == 1 ? "1 replay" : $"{count:N0} replays";
        }

        partial void OnIsPausedChanged(bool value)
        {
            OnPropertyChanged(nameof(PauseButtonGlyph));
            OnPropertyChanged(nameof(PauseButtonTooltip));
        }

        [RelayCommand]
        private void TogglePause()
        {
            if (Manager != null) {
                Manager.Paused = !Manager.Paused;
            }
        }

        partial void OnPostMatchPageChanged(bool value)
        {
            Config.PostMatchPage = value;
            if (Manager != null) {
                Manager.PostMatchPage = value;
            }
            SaveConfig();
        }

        partial void OnPreMatchPageChanged(bool value)
        {
            Config.PreMatchPage = value;
            if (Manager != null) {
                Manager.PreMatchPage = value;
            }
            SaveConfig();
        }

        partial void OnTwitchExtensionChanged(bool value)
        {
            Config.TwitchExtension = value;
            Manager?.SetTwitchEnabled(value);
            SaveConfig();
        }

        partial void OnMinimizeToTrayChanged(bool value)
        {
            Config.MinimizeToTray = value;
            SaveConfig();
        }

        partial void OnStartOnLoginChanged(bool value)
        {
            Config.StartOnLogin = value;
            try {
                DesktopIntegration.SetStartOnLogin(value);
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not update the autostart entry");
            }
            SaveConfig();
        }

        partial void OnShowInAppMenuChanged(bool value)
        {
            try {
                if (value) {
                    var whyNot = DesktopIntegration.WhyNotInstallable();
                    if (whyNot != null) {
                        _log.Warn(whyNot);
                    }
                    else {
                        DesktopIntegration.InstallAppMenuEntry();
                    }
                }
                else {
                    DesktopIntegration.RemoveAppMenuEntry();
                }
                // The autostart entry points at the ~/.local/bin copy when there is one, so rewrite
                // it to follow that copy appearing or going away.
                if (StartOnLogin) {
                    DesktopIntegration.SetStartOnLogin(true);
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not update the app menu entry");
            }

            if (DesktopIntegration.IsAppMenuEntryInstalled != value) {
                // Didn't take; put the checkbox back without re-running this handler.
                showInAppMenu = !value;
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(ShowInAppMenu)));
            }
        }

        private void SaveConfig()
        {
            try {
                Config.Save();
            }
            catch (Exception ex) {
                _log.Error(ex, "Could not save settings");
            }
        }

        /// <summary>Applied by the view once the Settings dialog closes with Save.</summary>
        public void ApplySettings(SettingsWindowViewModel settings)
        {
            var prefixChanged = Config.Prefix != settings.PrefixPath;

            Config.Prefix = settings.PrefixPath;
            Config.WebhookUrl = settings.WebhookUrl;
            Config.Theme = settings.SelectedTheme;
            Config.TwitchUploaderKey = settings.TwitchUploaderKey;
            Config.LogLevel = settings.SelectedLogLevel;
            SaveConfig();

            Logging.Configure(Logging.ParseLevel(Config.LogLevel));
            WebhookNotifier.WebhookUrl = Config.WebhookUrl;
            if (Manager != null) {
                Manager.Twitch.Key = Config.TwitchUploaderKey;
            }

            if (Application.Current != null) {
                Application.Current.RequestedThemeVariant = App.ThemeVariantFor(Config.Theme);
            }
            RefreshThemeDependentVisuals();

            if (prefixChanged && !string.IsNullOrWhiteSpace(Config.Prefix)) {
                // ReplayLocation.Changed (subscribed inside Manager.Start) reloads the folder for us
                // if a Manager is already running; if we were in the empty state, start one now.
                if (PrefixSetup.TryApply(Config.Prefix, out _)) {
                    if (IsEmptyState) {
                        IsEmptyState = false;
                        StartManager();
                    }
                }
            }
        }

        private void RefreshThemeDependentVisuals()
        {
            foreach (var chip in StatChips) {
                chip.TouchForThemeRefresh();
            }
            foreach (var row in Rows) {
                row.TouchForThemeRefresh();
            }
        }

        [RelayCommand]
        private void ShowLog()
        {
            try {
                var dir = Path.GetDirectoryName(Logging.LogFilePath);
                Directory.CreateDirectory(dir);
                if (!File.Exists(Logging.LogFilePath)) {
                    File.WriteAllText(Logging.LogFilePath, "");
                }
                Process.Start(new ProcessStartInfo(Logging.LogFilePath) { UseShellExecute = true });
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not open the log file");
            }
        }

        [RelayCommand]
        private async Task CheckForUpdateAsync()
        {
            UpdateStatusText = "Checking…";
            _updateReleaseUrl = null;
            try {
                var repo = string.IsNullOrWhiteSpace(Config.UpdateRepository) ? new AppConfig().UpdateRepository : Config.UpdateRepository.Trim();
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("heroesprofile-uploader-linux");
                var json = await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=30");

                // Windows-only releases don't count - only one that ships a Linux build is an update for us
                var release = JArray.Parse(json)
                    .Where(r => !(bool)r["draft"] && !(bool)r["prerelease"])
                    .FirstOrDefault(r => (r["assets"] as JArray)?.Any(a => ((string)a["name"] ?? "").IndexOf("linux", StringComparison.OrdinalIgnoreCase) >= 0) == true);
                if (release == null) {
                    UpdateStatusText = "No Linux release published yet.";
                    return;
                }

                var tag = (string)release["tag_name"] ?? "";
                var latest = ParseVersion(tag);
                var current = typeof(MainWindowViewModel).Assembly.GetName().Version;

                if (latest != null && current != null && latest > current) {
                    UpdateStatusText = $"Update available: {tag} (you have v{current.ToString(3)}) — click to open the release page.";
                    _updateReleaseUrl = (string)release["html_url"];
                } else {
                    UpdateStatusText = "You're up to date.";
                }
            }
            catch (Exception ex) {
                _log.Warn(ex, "Update check failed");
                UpdateStatusText = "Couldn't check for updates - see log.";
            }
        }

        [RelayCommand]
        private void OpenUpdate()
        {
            if (string.IsNullOrEmpty(_updateReleaseUrl)) {
                return;
            }
            try {
                Process.Start(new ProcessStartInfo(_updateReleaseUrl) { UseShellExecute = true });
            }
            catch (Exception ex) {
                _log.Warn(ex, "Could not open the release page");
            }
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) {
                return null;
            }
            var trimmed = tag.TrimStart('v', 'V');
            return Version.TryParse(trimmed, out var version) ? version : null;
        }
    }
}
