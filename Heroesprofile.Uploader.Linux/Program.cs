using System;
using System.Linq;
using Heroesprofile.Uploader.Linux.Gui;
using NLog;

namespace Heroesprofile.Uploader.Linux
{
    internal static class Program
    {
        // No async Main: Avalonia's classic desktop lifetime is itself a blocking call, so the GUI
        // path and the CLI paths (which just block on .GetAwaiter().GetResult()) both fit a plain
        // synchronous entry point - and Avalonia wants to own the startup thread directly.
        private static int Main(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h")) {
                PrintHelp();
                return 0;
            }

            if (args.Contains("--version")) {
                PrintVersion();
                return 0;
            }

            var prefixOverride = GetOptionValue(args, "--prefix");
            // The first non-flag argument is the subcommand; no args (or just "--minimized") means "launch the GUI".
            var command = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));

            try {
                switch (command) {
                    case null:
                        ConfigureLoggingForCommand();
                        return Gui.Gui.Run(minimized: args.Contains("--minimized"));

                    case "run":
                        ConfigureLoggingForCommand();
                        return RunCommand.Execute(prefixOverride).GetAwaiter().GetResult();

                    case "scan":
                        if (!args.Contains("--dry-run")) {
                            Console.Error.WriteLine("scan currently only supports --dry-run.\n");
                            PrintHelp();
                            return 1;
                        }
                        ConfigureLoggingForCommand();
                        return ScanCommand.Execute(prefixOverride).GetAwaiter().GetResult();

                    case "install":
                        return InstallCommand.Install();

                    case "uninstall":
                        return InstallCommand.Uninstall();

                    default:
                        Console.Error.WriteLine($"Unknown command '{command}'.\n");
                        PrintHelp();
                        return 1;
                }
            }
            catch (ConfigError ex) {
                // A setup problem the user can fix - the message says how, a stack trace would not help.
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"Fatal error: {ex}");
                return 1;
            }
        }

        /// <summary>
        /// Sets up NLog at the configured log level before a command (or the GUI) starts, so its
        /// own "no prefix configured"-style messages are logged too. Falls back to Info if the
        /// config can't be read yet - the command re-loads it right after and reports the real error.
        /// </summary>
        private static void ConfigureLoggingForCommand()
        {
            LogLevel level;
            try {
                level = Logging.ParseLevel(AppConfig.Load().LogLevel);
            }
            catch (ConfigError) {
                level = LogLevel.Info;
            }
            Logging.Configure(level);
        }

        private static string GetOptionValue(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static void PrintVersion()
        {
            // What Uploader.cs actually sends as ?version= - it reads Assembly.GetExecutingAssembly()
            // from inside Common.dll, so this is Common's AssemblyVersion, not this exe's.
            var version = typeof(Heroesprofile.Uploader.Common.ReplayLocation).Assembly.GetName().Version;
            var appVersion = typeof(Program).Assembly.GetName().Version;
            Console.WriteLine($"heroesprofile-uploader (Linux) {appVersion.ToString(3)} - Heroesprofile.Uploader.Common {version}");
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
$@"heroesprofile-uploader - native Linux Heroes Profile replay uploader

Usage:
  heroesprofile-uploader
      Launch the GUI. With no prefix configured yet, it asks you to browse to one.

  heroesprofile-uploader --minimized
      Launch the GUI already minimized to the tray (used by the start-on-login entry).

  heroesprofile-uploader run [--prefix <path>]
      Headless: watch the replay folder and upload new replays as they appear.
      Runs until SIGINT/SIGTERM (Ctrl+C, or `systemctl --user stop`).

  heroesprofile-uploader scan --dry-run [--prefix <path>]
      Analyze every replay under the replay folder and report what would be
      uploaded, without uploading or writing anything.

  heroesprofile-uploader install
      Copy this binary to ~/.local/bin and add it to the app menu (and, if
      ""Start on login"" is on in config.json, to XDG autostart).

  heroesprofile-uploader uninstall
      Remove what `install` added. Doesn't touch your config or replay data.

  heroesprofile-uploader --version
      Print the Heroesprofile.Uploader.Common version sent to the Heroes Profile API.

  heroesprofile-uploader --help
      Show this message.

Options:
  --prefix <path>   Wine/Proton prefix root, a Steam compatdata/<appid> folder, or the HotS
                     ""Accounts"" folder directly. Overrides ""prefix"" in the config file.

Config file ({AppConfig.ConfigPath}):
  {{ ""prefix"": ""/path/to/prefix"", ""preMatchPage"": false, ""postMatchPage"": false, ""webhookUrl"": """" }}

Replay storage and logs are kept under {AppConfig.DataDir}.");
        }
    }
}
