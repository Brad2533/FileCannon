using System.Diagnostics;

namespace ClipShare.Overlay.Services;

/// <summary>
/// Finds Discord's main desktop window (Stable/PTB/Canary/Development builds).
/// This only reads a window handle via public Win32 APIs — nothing is injected
/// into Discord's process.
/// </summary>
internal static class DiscordWindowLocator
{
    private static readonly string[] ProcessNames =
    [
        "Discord",
        "DiscordPTB",
        "DiscordCanary",
        "DiscordDevelopment"
    ];

    /// <summary>
    /// Electron apps like Discord spawn several processes sharing the same name
    /// (renderer, GPU, utility processes); only the main UI process has a non-zero
    /// MainWindowHandle, so we scan until we find it.
    /// </summary>
    public static IntPtr? FindMainWindow()
    {
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainWindowHandle;
                    }
                }
            }
        }

        return null;
    }
}
