using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipShare.Overlay.Views;
using H.NotifyIcon;
using NHotkey;
using NHotkey.Wpf;

namespace ClipShare.Overlay;

/// <summary>
/// No visible main window on startup — the app lives in the tray and pops up
/// <see cref="UploadPopupWindow"/> on demand via a global hotkey.
/// </summary>
public partial class App : Application
{
    private const string HotkeyName = "ShowUploadPopup";

    private TaskbarIcon? _trayIcon;
    private UploadPopupWindow? _popup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SetupTrayIcon();
        RegisterHotkey();
    }

    private void RegisterHotkey()
    {
        try
        {
            HotkeyManager.Current.AddOrReplace(HotkeyName, Key.U, ModifierKeys.Control | ModifierKeys.Alt, OnHotkeyPressed);
        }
        catch (HotkeyAlreadyRegisteredException)
        {
            MessageBox.Show(
                "The ClipShare hotkey (Ctrl+Alt+U) is already registered by another application.\n\n" +
                "This will be user-configurable once Settings is built.",
                "ClipShare",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        _popup ??= new UploadPopupWindow();
        _popup.ShowDockedOrNearCursor();
        e.Handled = true;
    }

    private void SetupTrayIcon()
    {
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            // Placeholder — real tray icon art is tracked as later polish; without
            // some icon set, H.NotifyIcon renders nothing and the tray entry is invisible.
            Icon = System.Drawing.SystemIcons.Application,
            ToolTipText = "ClipShare — Ctrl+Alt+U to upload a clip",
            ContextMenu = contextMenu
        };

        // Created here in code-behind rather than XAML, so it's never added to a visual
        // tree — the icon otherwise defers creation until a Loaded event that will never
        // fire, leaving the process running with no visible tray entry. ForceCreate skips
        // that and registers the icon with the shell immediately.
        _trayIcon.ForceCreate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HotkeyManager.Current.Remove(HotkeyName);
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
