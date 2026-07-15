// version 7
using System.Text.Json.Serialization;

namespace ClaudeUsageTray;

internal sealed class AppSettings
{
    public int SettingsSchemaVersion { get; set; }

    public string HomeAssistantUrl { get; set; } = "http://homeassistant.local:8123";

    public string EncryptedAccessToken { get; set; } = string.Empty;

    [JsonIgnore]
    public string AccessToken { get; set; } = string.Empty;

    public string UsageEntityId { get; set; } = "sensor.claude_usage_sam_pro_session_usage";

    public string ResetEntityId { get; set; } = "sensor.claude_usage_sam_pro_session_reset_time";

    public int RefreshSeconds { get; set; } = 60;

    // Retains the existing property name so settings from earlier releases remain compatible.
    // This controls the height of the percentage drawn inside the tray icon.
    public int HoverValueFontSize { get; set; } = 54;

    public bool StartWithWindows { get; set; }

    public void Normalise()
    {
        HomeAssistantUrl = HomeAssistantUrl.Trim().TrimEnd('/');
        AccessToken = AccessToken.Trim();
        UsageEntityId = UsageEntityId.Trim();
        ResetEntityId = ResetEntityId.Trim();
        RefreshSeconds = Math.Clamp(RefreshSeconds, 30, 3600);
        HoverValueFontSize = Math.Clamp(HoverValueFontSize, 24, 62);
        SettingsSchemaVersion = 7;
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            SettingsSchemaVersion = SettingsSchemaVersion,
            HomeAssistantUrl = HomeAssistantUrl,
            EncryptedAccessToken = EncryptedAccessToken,
            AccessToken = AccessToken,
            UsageEntityId = UsageEntityId,
            ResetEntityId = ResetEntityId,
            RefreshSeconds = RefreshSeconds,
            HoverValueFontSize = HoverValueFontSize,
            StartWithWindows = StartWithWindows
        };
    }
}
