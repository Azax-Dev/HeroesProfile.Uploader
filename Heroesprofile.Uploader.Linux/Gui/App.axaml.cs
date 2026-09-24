using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Heroesprofile.Uploader.Linux.Gui.ViewModels;
using Heroesprofile.Uploader.Linux.Gui.Views;

namespace Heroesprofile.Uploader.Linux.Gui
{
    public partial class App : Application
    {
        /// <summary>Set by <see cref="Gui.Run"/> before the framework initialization event fires.</summary>
        public static bool StartMinimized { get; set; }

        private MainWindowViewModel _viewModel;
        private MainWindow _window;
        private TrayIcon _trayIcon;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                _viewModel = new MainWindowViewModel();
                RequestedThemeVariant = ThemeVariantFor(_viewModel.Config.Theme);

                _window = new MainWindow { DataContext = _viewModel };
                desktop.MainWindow = _window;

                // TrayIcon/NativeMenuItem, declared under <TrayIcon.Icons> in App.axaml, aren't part
                // of a regular control tree - x:Name doesn't generate a field for them the way it does
                // for a Window's children, so the tray icon is fetched via the attached-property
                // getter instead.
                _trayIcon = TrayIcon.GetIcons(this)[0];

                // Tray icon shows only while the window is hidden - mirrors the Windows app's own
                // NotifyIcon.Visible toggling in MainWindow.xaml.cs/App.xaml.cs.
                _window.PropertyChanged += (_, e) => {
                    if (e.Property == Visual.IsVisibleProperty) {
                        _trayIcon.IsVisible = !_window.IsVisible;
                    }
                };
                _viewModel.PropertyChanged += (_, e) => {
                    if (e.PropertyName == nameof(MainWindowViewModel.IsPaused)) {
                        SyncTrayPauseLabel();
                    }
                };

                desktop.Exit += (_, __) => _viewModel.Manager?.Stop();

                if (StartMinimized && _viewModel.Config.MinimizeToTray) {
                    // Never shown at all - straight to the tray, like the Windows app's --autorun.
                    _window.WindowState = WindowState.Minimized;
                    _window.ShowInTaskbar = false;
                }
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void TrayOpen_Click(object sender, System.EventArgs e)
        {
            _window.RestoreFromTray();
        }

        private void TrayPause_Click(object sender, System.EventArgs e)
        {
            _viewModel.TogglePauseCommand.Execute(null);
        }

        private void TrayOpenLog_Click(object sender, System.EventArgs e)
        {
            _viewModel.ShowLogCommand.Execute(null);
        }

        private void TrayQuit_Click(object sender, System.EventArgs e)
        {
            _window.QuitForReal();
        }

        /// <summary>
        /// NativeMenuItem, nested inside a NativeMenu, doesn't get an x:Name-generated field the way a
        /// regular visual-tree control does - so it's found by position instead. Order matches App.axaml:
        /// Open(0), Pause(1), Open log(2), separator(3), Quit(4).
        /// </summary>
        private void SyncTrayPauseLabel()
        {
            if (_trayIcon.Menu.Items[1] is NativeMenuItem pauseItem) {
                pauseItem.Header = _viewModel.IsPaused ? "Resume uploading" : "Pause uploading";
            }
        }

        public static ThemeVariant ThemeVariantFor(string theme)
        {
            return theme switch {
                "Dark" => ThemeVariant.Dark,
                "Light" => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };
        }
    }
}
