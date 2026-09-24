# Heroes Profile Uploader for Linux

A native Linux build of the Heroes Profile uploader. It uses the same upload logic as the Windows
app, so replays go up exactly as they would from Windows. Heroes of the Storm still runs under
Wine/Proton (Lutris, Steam, Bottles, plain Wine); only the uploader is native. You point it at the
Wine prefix that has HotS in it and it finds your replays.

## Install

1. Download `HeroesProfileUploader-linux-x64.tar.gz` from the
   [latest release](https://github.com/Heroes-Profile/HeroesProfile.Uploader/releases/latest) and unpack it.
   The `heroesprofile-uploader` binary is self-contained: no .NET install required.

2. Add it to your app menu (copies the binary to `~/.local/bin` and installs a launcher and icon):

   ```sh
   ./heroesprofile-uploader install
   ```

   `heroesprofile-uploader uninstall` removes them again. You can also just run the binary from
   wherever you unpacked it.

3. Launch **Heroes Profile Uploader** from your app menu. On first run it asks for your Wine/Proton
   prefix. Pick the prefix folder (the one containing `drive_c`). A Steam `compatdata/<appid>`
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
```

Needs the .NET 8 SDK. `Directory.Build.props` switches off the replay parser's GitVersionTask on
non-Windows builds, where its MSBuild task can't load.
