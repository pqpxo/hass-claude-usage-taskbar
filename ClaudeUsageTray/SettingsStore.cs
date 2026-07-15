// version 7
using System.Text.Json;

namespace ClaudeUsageTray;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "SWAKES",
        "ClaudeUsageTray");

    private static string SettingsPath =>
        Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings
                {
                    SettingsSchemaVersion = 7
                };
            }

            string json = File.ReadAllText(SettingsPath);

            AppSettings settings =
                JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? new AppSettings();

            if (!string.IsNullOrWhiteSpace(
                    settings.EncryptedAccessToken))
            {
                settings.AccessToken =
                    SecretProtector.Unprotect(
                        settings.EncryptedAccessToken);
            }

            MigrateSettings(settings);
            settings.Normalise();
            return settings;
        }
        catch
        {
            return new AppSettings
            {
                SettingsSchemaVersion = 7
            };
        }
    }

    public static void Save(AppSettings settings)
    {
        settings.Normalise();

        settings.EncryptedAccessToken =
            string.IsNullOrWhiteSpace(settings.AccessToken)
                ? string.Empty
                : SecretProtector.Protect(settings.AccessToken);

        Directory.CreateDirectory(SettingsDirectory);

        string json =
            JsonSerializer.Serialize(settings, JsonOptions);

        File.WriteAllText(SettingsPath, json);
    }

    private static void MigrateSettings(AppSettings settings)
    {
        if (settings.SettingsSchemaVersion < 6)
        {
            // Version 5 used this value as the point size of a separate popup.
            // Version 6 and later draw the percentage directly into a 64 px
            // tray-icon canvas, so convert older values to a larger equivalent.
            settings.HoverValueFontSize = Math.Clamp(
                (int)Math.Round(
                    settings.HoverValueFontSize * 1.8d),
                40,
                62);

            settings.SettingsSchemaVersion = 6;
        }

        if (settings.SettingsSchemaVersion < 7)
        {
            settings.SettingsSchemaVersion = 7;
        }
    }
}
