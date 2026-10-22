using System.IO;
using System.Text.Json;

namespace ClipShare.Overlay.Services;

/// <summary>
/// Reads/writes <see cref="ClipShareSettings"/> from %AppData%\ClipShare\settings.json.
/// Kept outside the repo entirely (rather than alongside source) so secrets like the
/// Discord bot token and Pi API key can never end up in a git commit.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipShare",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static ClipShareSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new ClipShareSettings();
        }

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<ClipShareSettings>(json, JsonOptions) ?? new ClipShareSettings();
    }

    public static void Save(ClipShareSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
