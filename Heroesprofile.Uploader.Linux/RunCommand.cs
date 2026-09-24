using Heroesprofile.Uploader.Common;
using NLog;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// `run`: watches the resolved Accounts folder and uploads new replays as they appear, exactly
    /// like the Windows app's background service - same Manager, same Monitor/LiveMonitor/Analyzer/
    /// Uploader/LiveProcessor. Blocks until SIGINT/SIGTERM (Ctrl+C, or `systemctl --user stop`).
    /// </summary>
    internal static class RunCommand
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public static async Task<int> Execute(string prefixOverride)
        {
            var config = AppConfig.Load();
            var prefix = PrefixSetup.ResolvePrefix(prefixOverride, config);
            PrefixSetup.Apply(prefix);

            WebhookNotifier.WebhookUrl = config.WebhookUrl;

            Directory.CreateDirectory(AppConfig.DataDir);
            var manager = new Manager(new ReplayStorage(Path.Combine(AppConfig.DataDir, "replays_v8.xml"))) {
                PreMatchPage = config.PreMatchPage,
                PostMatchPage = config.PostMatchPage,
            };

            _log.Info($"Starting: prefix={prefix}, accounts={ReplayLocation.Current}, " +
                $"preMatchPage={config.PreMatchPage}, postMatchPage={config.PostMatchPage}, " +
                $"webhook={(string.IsNullOrWhiteSpace(config.WebhookUrl) ? "off" : "on")}");

            // SIGINT/SIGTERM both mean "shut down cleanly" - systemd sends SIGTERM on `stop`,
            // Ctrl+C sends SIGINT. Cancel = true stops the runtime from also terminating the process,
            // so Stop() and the log line below get to run first.
            var stopRequested = new TaskCompletionSource<bool>();
            using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => {
                ctx.Cancel = true;
                stopRequested.TrySetResult(true);
            });
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => {
                ctx.Cancel = true;
                stopRequested.TrySetResult(true);
            });

            // Common.Uploader, spelled out - unqualified "Uploader" resolves to the enclosing
            // Heroesprofile.Uploader namespace segment instead of the type (same reason the Windows
            // app spells it "Common.Uploader" in App.xaml.cs).
            manager.Start(new SettledMonitor(), new LiveMonitor(), new Analyzer(), new Common.Uploader(), new LiveProcessor(config.PreMatchPage, manager.Twitch));

            _log.Info("Running - watching for new replays. Press Ctrl+C to stop.");
            await stopRequested.Task;

            _log.Info("Stopping...");
            manager.Stop();
            _log.Info("Stopped.");

            return 0;
        }
    }
}
