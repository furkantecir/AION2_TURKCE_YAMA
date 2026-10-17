using System;
using System.IO;
using System.Text.Json;

namespace Aion2TR.App.Services;

public class AppSettings
{
    public string? CustomGamePath { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public bool LaunchViaSteam { get; set; } = true;
    public string? GitHubRepositoryUrl { get; set; } = "https://github.com/furkantecir";
    public bool ShowIntroAnimation { get; set; } = true;
}

public static class UserSettingsService
{
    private static readonly string _settingsPath;

    static UserSettingsService()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2TR");
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to load user settings", ex);
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to save user settings", ex);
        }
    }
}
