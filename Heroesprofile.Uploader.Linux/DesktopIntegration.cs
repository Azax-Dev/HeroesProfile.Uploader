using NLog;
using System;
using System.IO;
using System.Reflection;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// Writes/removes the Linux desktop-integration files: the ~/.local/bin copy and app-menu
    /// .desktop entry (the `install`/`uninstall` CLI commands), and the autostart .desktop entry
    /// (Settings' "Start on login" toggle). Shared so both paths agree on where everything lives.
    /// </summary>
    internal static class DesktopIntegration
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        private const string AppId = "heroesprofile-uploader";
        private const string IconResourceName = "heroesprofile-uploader-icon.png";

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static string XdgHome(string envVar, string fallbackLeaf)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            return !string.IsNullOrWhiteSpace(value) ? value : Path.Combine(Home, fallbackLeaf);
        }

        // ~/.local/bin has no XDG variable of its own - it's the de facto convention every major
        // distro's default $PATH (and tools like `pip install --user`) already agree on.
        private static string BinDir => Path.Combine(Home, ".local", "bin");
        private static string DataHome => XdgHome("XDG_DATA_HOME", Path.Combine(".local", "share"));
        private static string ConfigHome => XdgHome("XDG_CONFIG_HOME", ".config");

        public static string InstalledExePath => Path.Combine(BinDir, AppId);

        /// <summary>
        /// Where this app can be launched from again later. Inside an AppImage, ProcessPath is the
        /// binary in a temporary mount that disappears on exit, so the .AppImage file itself is used.
        /// </summary>
        private static string LaunchablePath => Environment.GetEnvironmentVariable("APPIMAGE") is string appImage && appImage != ""
            ? appImage
            : Environment.ProcessPath;

        public static bool IsAppMenuEntryInstalled => File.Exists(DesktopEntryPath);
        private static string DesktopEntryPath => Path.Combine(DataHome, "applications", $"{AppId}.desktop");
        private static string AutostartEntryPath => Path.Combine(ConfigHome, "autostart", $"{AppId}.desktop");
        // Actual asset is 486x432; this is the closest standard hicolor bucket, and desktop
        // environments scale it down for the menu/taskbar without visible loss.
        private static string IconPath => Path.Combine(DataHome, "icons", "hicolor", "512x512", "apps", $"{AppId}.png");

        /// <summary>
        /// Null if the running executable can be installed, otherwise why not. `dotnet run`/`dotnet build`
        /// output is an apphost with the managed .dll (and the rest of the app) next to it, so copying
        /// the apphost alone would be broken. The single-file publish has no such .dll. Inside an
        /// AppImage the running executable is that same single-file binary, so it installs as a plain
        /// executable that doesn't need the AppImage (or FUSE) afterwards.
        /// </summary>
        public static string WhyNotInstallable()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || exe.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(Path.GetDirectoryName(exe), Path.GetFileName(exe) + ".dll"))) {
                return "install needs to be run from the published single-file binary, not `dotnet run`/`dotnet build` output. " +
                    "Publish with `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true` and run that.";
            }
            return null;
        }

        /// <summary>Copies the running executable to ~/.local/bin and adds the app-menu entry + icon.</summary>
        public static void InstallAppMenuEntry()
        {
            var sourceExePath = Environment.ProcessPath;
            Directory.CreateDirectory(BinDir);
            // Already running the installed copy: just (re)write the menu entry and icon.
            if (Path.GetFullPath(sourceExePath) != Path.GetFullPath(InstalledExePath)) {
                File.Copy(sourceExePath, InstalledExePath, overwrite: true);
                MakeExecutable(InstalledExePath);
                _log.Info($"Installed binary to {InstalledExePath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResourceName))
            using (var dest = File.Create(IconPath)) {
                resource.CopyTo(dest);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(DesktopEntryPath));
            File.WriteAllText(DesktopEntryPath, DesktopEntryContents(InstalledExePath, minimized: false));
            _log.Info($"Wrote app menu entry {DesktopEntryPath}");
        }

        /// <summary>Removes everything <see cref="InstallAppMenuEntry"/> added. Leaves config and replay data alone.</summary>
        public static void UninstallAppMenuEntry()
        {
            RemoveAppMenuEntry();
            // Uninstalling should also turn off start-on-login - an autostart entry pointing at a
            // binary that no longer exists would just silently fail every login.
            SetStartOnLogin(false);
        }

        /// <summary>Removes the app-menu entry, icon and ~/.local/bin copy, but not the autostart entry.</summary>
        public static void RemoveAppMenuEntry()
        {
            DeleteIfExists(DesktopEntryPath);
            DeleteIfExists(IconPath);
            DeleteIfExists(InstalledExePath);
        }

        /// <summary>Writes or removes the XDG autostart entry for Settings' "Start on login" toggle.</summary>
        public static void SetStartOnLogin(bool enabled)
        {
            if (!enabled) {
                DeleteIfExists(AutostartEntryPath);
                return;
            }

            // Prefer the installed copy so autostart survives the source binary moving/disappearing;
            // fall back to wherever we're currently running from (e.g. testing before `install`).
            var exePath = File.Exists(InstalledExePath) ? InstalledExePath : LaunchablePath;
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartEntryPath));
            File.WriteAllText(AutostartEntryPath, DesktopEntryContents(exePath, minimized: true));
            _log.Info($"Wrote autostart entry {AutostartEntryPath}");
        }

        private static string DesktopEntryContents(string exePath, bool minimized)
        {
            var exec = minimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
            return
$@"[Desktop Entry]
Type=Application
Name=Heroes Profile Uploader
Comment=Upload Heroes of the Storm replays to heroesprofile.com
Exec={exec}
Icon={AppId}
Terminal=false
Categories=Game;Utility;
StartupWMClass={AppId}
";
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) {
                File.Delete(path);
                _log.Info($"Removed {path}");
            }
        }

        private static void MakeExecutable(string path)
        {
            // .NET has no chmod wrapper; File.Copy preserves the source's permission bits, which are
            // already +x for a published apphost, so this only matters if that ever stops being true.
            try {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (Exception ex) {
                _log.Debug($"Could not set the executable bit on {path}: {ex.Message}");
            }
        }
    }
}
