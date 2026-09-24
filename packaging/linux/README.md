# Heroes Profile Uploader for Linux

A native Linux build of the Heroes Profile uploader. It uses the same upload logic as the Windows
app, so replays go up exactly as they would from Windows. Heroes of the Storm still runs under
Wine/Proton (Lutris, Steam, Bottles, plain Wine); only the uploader is native. You point it at the
Wine prefix that has HotS in it and it finds your replays.

## Install

1. Download `HeroesProfileUploader-linux-x86_64.AppImage` from the
   [latest release](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest).
   It is self-contained: no .NET or other libraries to install. It runs on any current
   x86_64 distro (Ubuntu 20.04+, Mint 20+, Debian 11+, Fedora, Arch, ...).

2. Make it executable and run it: right-click → Properties → Permissions → "Allow executing file as
   program", then double-click it. Or from a terminal:

   ```sh
   chmod +x HeroesProfileUploader-linux-x86_64.AppImage
   ./HeroesProfileUploader-linux-x86_64.AppImage
   ```

   Tick **Show in app menu** in the main window to add it to your app menu. This copies the app to
   `~/.local/bin/heroesprofile-uploader`, so you can delete the downloaded file afterwards. Untick it
   to remove it again.

   Prefer a plain binary? `HeroesProfileUploader-linux-x64.tar.gz` has the same program, unpacked.
   `./heroesprofile-uploader install` / `uninstall` does the same as the checkbox.

3. On first run it asks for your Wine/Proton prefix. Pick the prefix folder (the one containing `drive_c`). A Steam `compatdata/<appid>`
   folder or the HotS `Accounts` folder itself also work.

Common prefix locations:

| Launcher | Prefix |
|---|---|
| Lutris | whatever you chose at install, e.g. `~/Games/battlenet` |
| Steam (non-Steam game via Proton) | `~/.steam/steam/steamapps/compatdata/<appid>` |
| Bottles | `~/.local/share/bottles/bottles/<name>` |
| Plain Wine | `~/.wine` |

**Start on login** and **Minimize to tray** are toggles in the main window. The tray icon needs a
StatusNotifier host: KDE, Cinnamon, XFCE and most others have one; on GNOME install the
AppIndicator extension.

Settings (prefix, theme, webhook, Twitch key, log level) are saved in
`~/.config/heroesprofile/config.json`. Upload history and logs are under
`~/.local/share/heroesprofile/`.

## Headless (no GUI)

The same binary works without a desktop, for example on a machine you only reach over SSH:

```sh
heroesprofile-uploader scan --dry-run --prefix /path/to/prefix   # what would upload; uploads nothing
heroesprofile-uploader run --prefix /path/to/prefix              # watch and upload until stopped
```

`--prefix` can be left out once `"prefix"` is set in the config file.

To run it as a systemd user service:

```sh
mkdir -p ~/.config/systemd/user
cp heroesprofile-uploader.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now heroesprofile-uploader
journalctl --user -u heroesprofile-uploader -f
```

The service runs `~/.local/bin/heroesprofile-uploader run`. Use either the service or the GUI,
not both at once.

## Building from source

```sh
dotnet publish Heroesprofile.Uploader.Linux -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
packaging/linux/build-appimage.sh <path to the published heroesprofile-uploader> HeroesProfileUploader-linux-x86_64.AppImage
```

Needs the .NET 8 SDK. `Directory.Build.props` switches off the replay parser's GitVersionTask on
non-Windows builds, where its MSBuild task can't load.
