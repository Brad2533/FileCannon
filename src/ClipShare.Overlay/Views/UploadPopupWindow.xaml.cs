using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipShare.Overlay.Native;
using ClipShare.Overlay.Services;
using ClipShare.Shared;
using Microsoft.Win32;

namespace ClipShare.Overlay.Views;

/// <summary>
/// The always-on-top popup that appears when the global hotkey is pressed.
/// It is an ordinary top-level window (Topmost + ShowInTaskbar=False) — nothing
/// hooks into Discord or any other app's window, it just floats above everything.
///
/// While open, it docks to a corner of Discord's window and follows it if Discord
/// moves/resizes, purely by polling GetWindowRect — no injection, no hooks into
/// Discord's process. If Discord isn't running, it falls back to appearing near
/// the cursor. Tracking only runs while this window is visible.
/// </summary>
public partial class UploadPopupWindow : Window
{
    private const double DockMargin = 16;
    private static readonly TimeSpan TrackingInterval = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _trackingTimer;
    private readonly PiUploadClient _uploadClient = new();
    private IntPtr _trackedDiscordWindow = IntPtr.Zero;

    // Opening AddServerDialog (a separate owned top-level window) steals activation from
    // this window, which would otherwise fire OnDeactivated and Hide() this window right
    // out from under its own still-open modal dialog — breaking the dialog's keyboard
    // input entirely (WPF/Win32 routes input through the owner). Suppress the
    // deactivate-dismiss while a child dialog we opened is up.
    private bool _isChildDialogOpen;

    public UploadPopupWindow()
    {
        InitializeComponent();

        _trackingTimer = new DispatcherTimer { Interval = TrackingInterval };
        _trackingTimer.Tick += OnTrackingTick;
    }

    /// <summary>
    /// Docks to Discord's window if it can be found and isn't minimized; otherwise
    /// centers on the monitor under the cursor. Either way, shows and activates.
    /// </summary>
    public void ShowDockedOrNearCursor()
    {
        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle(); // creates the HWND without showing it, so DPI/position info is available

        LoadServerList();

        _trackedDiscordWindow = DiscordWindowLocator.FindMainWindow() ?? IntPtr.Zero;

        if (_trackedDiscordWindow != IntPtr.Zero && TryDockToDiscordWindow())
        {
            _trackingTimer.Start();
        }
        else
        {
            _trackedDiscordWindow = IntPtr.Zero;
            PositionOnCursorMonitor();
        }

        Opacity = 0;
        Show();
        Activate();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void LoadServerList()
    {
        var settings = SettingsService.Load();
        ServerComboBox.ItemsSource = settings.DiscordServers;

        if (settings.DiscordServers.Count == 0)
        {
            ServerComboBox.SelectedItem = null;
            return;
        }

        var lastSelected = settings.DiscordServers.FirstOrDefault(s => s.Name == settings.LastSelectedServerName);
        ServerComboBox.SelectedItem = lastSelected ?? settings.DiscordServers[0];
    }

    private void OnServerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerComboBox.SelectedItem is not DiscordServerEntry selected)
        {
            return;
        }

        var settings = SettingsService.Load();
        settings.LastSelectedServerName = selected.Name;
        SettingsService.Save(settings);
    }

    private void OnAddServerClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddServerDialog { Owner = this };

        _isChildDialogOpen = true;
        var accepted = dialog.ShowDialog() == true;
        _isChildDialogOpen = false;

        if (!accepted || dialog.Result is null)
        {
            return;
        }

        var settings = SettingsService.Load();
        settings.DiscordServers.Add(dialog.Result);
        settings.LastSelectedServerName = dialog.Result.Name;
        SettingsService.Save(settings);

        LoadServerList();
    }

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DismissPopup();
    }

    private void OnTrackingTick(object? sender, EventArgs e)
    {
        // If Discord closed or got minimized while the popup was open, stop following
        // and just leave the popup where it last was rather than yanking it elsewhere.
        if (_trackedDiscordWindow == IntPtr.Zero || !TryDockToDiscordWindow())
        {
            _trackingTimer.Stop();
        }
    }

    private bool TryDockToDiscordWindow()
    {
        if (!NativeMethods.IsWindow(_trackedDiscordWindow) || NativeMethods.IsIconic(_trackedDiscordWindow))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(_trackedDiscordWindow, out var rect))
        {
            return false;
        }

        double dpiScale = GetDpiScale();
        double margin = DockMargin; // treated as DIPs; good enough for a first pass

        Left = (rect.Right / dpiScale) - Width - margin;
        Top = (rect.Bottom / dpiScale) - Height - margin;
        return true;
    }

    private void PositionOnCursorMonitor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var hMonitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        NativeMethods.GetMonitorInfo(hMonitor, ref info);

        double dpiScale = GetDpiScale();

        double workLeft = info.rcWork.Left / dpiScale;
        double workTop = info.rcWork.Top / dpiScale;
        double workWidth = (info.rcWork.Right - info.rcWork.Left) / dpiScale;
        double workHeight = (info.rcWork.Bottom - info.rcWork.Top) / dpiScale;

        Left = workLeft + (workWidth - Width) / 2;
        Top = workTop + (workHeight - Height) / 2;
    }

    private double GetDpiScale()
    {
        // Monitor rects from Win32 are in physical pixels; WPF works in device-independent
        // units (96 DPI), so we need the current DPI scale to convert between them.
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget is not null ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a clip to upload",
            Filter = "All files|*.*|Video files|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.wmv;*.flv;*.ts;*.m4v"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await SelectFileAndUploadAsync(dialog.FileName);
        }
    }

    private bool CanAcceptDrop(DragEventArgs e) =>
        BrowseButton.IsEnabled && e.Data.GetDataPresent(DataFormats.FileDrop);

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = CanAcceptDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        RootBorder.BorderBrush = (Brush)FindResource(e.Effects == DragDropEffects.Copy ? "AccentBrush" : "BorderSubtleBrush");
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        RootBorder.BorderBrush = (Brush)FindResource("BorderSubtleBrush");
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        RootBorder.BorderBrush = (Brush)FindResource("BorderSubtleBrush");

        if (!CanAcceptDrop(e) || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return;
        }

        // Only a single clip is supported per upload — a multi-file drop (e.g. dragging a
        // whole clips folder) just takes the first entry rather than erroring out.
        var filePath = files[0];
        if (!File.Exists(filePath))
        {
            StatusText.Text = "That's not a file — try dragging the clip itself, not a folder.";
            return;
        }

        await SelectFileAndUploadAsync(filePath);
    }

    private async Task SelectFileAndUploadAsync(string filePath)
    {
        SelectedFileText.Text = filePath;
        await UploadAndPostAsync(filePath);
    }

    private async Task UploadAndPostAsync(string filePath)
    {
        var settings = SettingsService.Load();

        if (string.IsNullOrWhiteSpace(settings.PiUploadUrl) || string.IsNullOrWhiteSpace(settings.PiApiKey))
        {
            StatusText.Text = "Set piUploadUrl and piApiKey in %AppData%\\ClipShare\\settings.json first.";
            return;
        }

        BrowseButton.IsEnabled = false;
        UploadProgressBar.Visibility = Visibility.Visible;
        UploadProgressBar.Value = 0;
        StatusText.Text = "Uploading… 0%";

        UploadCompleteResponse result;
        try
        {
            var progress = new Progress<double>(fraction =>
            {
                UploadProgressBar.Value = fraction;
                StatusText.Text = $"Uploading… {fraction:P0}";
            });

            var selectedChannelId = (ServerComboBox.SelectedItem as DiscordServerEntry)?.ChannelId;

            result = await _uploadClient.UploadAsync(
                filePath, settings.PiUploadUrl, settings.PiApiKey, settings.ChunkSizeBytes, selectedChannelId, progress, CancellationToken.None);
        }
        catch (PiUploadException ex)
        {
            StatusText.Text = ex.Message;
            BrowseButton.IsEnabled = true;
            return;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Unexpected upload error: {ex.Message}";
            BrowseButton.IsEnabled = true;
            return;
        }

        // The link is already copied here regardless of the Discord outcome below — the Pi
        // server posts to Discord itself as part of /complete and echoes the result back, so
        // a Discord failure (bad token/channel/permission/outage) never loses the link, same
        // guarantee as before, just enforced server-side now instead of client-side.
        Clipboard.SetText(result.DownloadUrl);
        UploadProgressBar.Value = 1;
        StatusText.Text = result.DiscordPosted
            ? "Uploaded ✓ — link copied. Discord ✓"
            : $"Uploaded ✓ — link copied. {result.DiscordStatusMessage}";

        BrowseButton.IsEnabled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_isChildDialogOpen)
        {
            return;
        }

        // Clicking away dismisses the popup, same as most quick-access overlays (e.g. Win+Shift+S).
        DismissPopup();
    }

    private void DismissPopup()
    {
        _trackingTimer.Stop();
        Hide();
    }
}
