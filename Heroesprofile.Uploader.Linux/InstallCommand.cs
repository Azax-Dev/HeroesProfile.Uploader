using System;
using System.IO;

namespace Heroesprofile.Uploader.Linux
{
    /// <summary>
    /// `install`/`uninstall`: adds/removes this app from the Linux app menu. Deliberately only ever
    /// touches ~/.local/{bin,share} (see <see cref="DesktopIntegration"/>) - no sudo, no system paths.
    /// </summary>
    internal static class InstallCommand
    {
        public static int Install()
        {
            var sourceExe = Environment.ProcessPath;
            // `dotnet run` launches the apphost directly (not the `dotnet` muxer) when UseAppHost is
            // on, which it is by default - so Environment.ProcessPath there is a real executable, not
            // something that merely *looks* like the published binary. What actually distinguishes a
            // framework-dependent build output (dotnet run/build) from the true self-contained
            // single-file publish is whether the managed .dll PublishSingleFile bundles away still
            // sits next to it - installing that apphost alone would be a broken copy (no runtime/DLLs
            // travel with it).
            var siblingDll = string.IsNullOrEmpty(sourceExe) ? null : Path.Combine(Path.GetDirectoryName(sourceExe), Path.GetFileName(sourceExe) + ".dll");
            if (string.IsNullOrEmpty(sourceExe) ||
                sourceExe.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(siblingDll)) {
                Console.Error.WriteLine(
                    "install needs to be run from the published single-file binary, not `dotnet run`/`dotnet build` output. " +
                    "Publish with `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true` and run that.");
                return 2;
            }

            DesktopIntegration.InstallAppMenuEntry(sourceExe);
            Console.WriteLine($"Installed to {DesktopIntegration.InstalledExePath} and added to the app menu.");
            Console.WriteLine("Run it from your app launcher, or enable \"Start on login\" from its Settings dialog.");
            return 0;
        }

        public static int Uninstall()
        {
            DesktopIntegration.UninstallAppMenuEntry();
            Console.WriteLine("Removed the app menu entry, icon, autostart entry, and the ~/.local/bin copy.");
            Console.WriteLine($"Your settings and replay data under {AppConfig.DataDir} were left alone.");
            return 0;
        }
    }
}
