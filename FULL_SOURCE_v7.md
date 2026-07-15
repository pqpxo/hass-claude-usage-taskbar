# Claude Usage Tray — Complete Source (Version 7)

This file contains the complete Version 7 project source.

## `ClaudeUsageTray/ClaudeUsageTray.csproj`

````xml
<!-- version 7 -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ApplicationIcon>Assets\claude.ico</ApplicationIcon>
    <AssemblyName>ClaudeUsageTray</AssemblyName>
    <RootNamespace>ClaudeUsageTray</RootNamespace>
    <Version>1.5.0</Version>
    <FileVersion>1.5.0.0</FileVersion>
    <AssemblyVersion>1.5.0.0</AssemblyVersion>
    <Authors>SWAKES</Authors>
    <Product>Claude Usage Tray</Product>
    <Description>Windows system tray application showing Claude usage from Home Assistant.</Description>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="Assets\claude.ico" LogicalName="ClaudeUsageTray.Assets.claude.ico" />
  </ItemGroup>
</Project>
````

## `ClaudeUsageTray/AppSettings.cs`

````csharp
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
````

## `ClaudeUsageTray/HomeAssistantClient.cs`

````csharp
// version 7
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeUsageTray;

internal sealed class HomeAssistantClient : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public async Task<UsageSnapshot> GetUsageAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);

        string usageState = await GetEntityStateAsync(
            settings.HomeAssistantUrl,
            settings.AccessToken,
            settings.UsageEntityId,
            cancellationToken);

        decimal usagePercentage = ParseUsagePercentage(usageState);
        string? resetState = null;

        if (!string.IsNullOrWhiteSpace(settings.ResetEntityId))
        {
            try
            {
                resetState = await GetEntityStateAsync(
                    settings.HomeAssistantUrl,
                    settings.AccessToken,
                    settings.ResetEntityId,
                    cancellationToken);
            }
            catch
            {
                resetState = null;
            }
        }

        DateTimeOffset retrievedAt = DateTimeOffset.Now;
        DateTimeOffset? resetAt = ResolveResetTime(resetState, retrievedAt);

        return new UsageSnapshot(
            UsagePercentage: Math.Clamp(usagePercentage, 0m, 100m),
            ResetState: resetState,
            ResetAt: resetAt,
            RetrievedAt: retrievedAt);
    }

    private async Task<string> GetEntityStateAsync(
        string baseUrl,
        string accessToken,
        string entityId,
        CancellationToken cancellationToken)
    {
        string requestUrl =
            $"{baseUrl.TrimEnd('/')}/api/states/{Uri.EscapeDataString(entityId)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response =
            await _httpClient.SendAsync(request, cancellationToken);

        string responseBody =
            await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Home Assistant returned {(int)response.StatusCode} "
                + $"({response.ReasonPhrase}).");
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);

        if (!document.RootElement.TryGetProperty(
                "state",
                out JsonElement stateElement))
        {
            throw new InvalidOperationException(
                $"Entity '{entityId}' did not return a state value.");
        }

        return stateElement.GetString()?.Trim()
            ?? throw new InvalidOperationException(
                $"Entity '{entityId}' returned an empty state.");
    }

    private static decimal ParseUsagePercentage(string state)
    {
        if (string.Equals(
                state,
                "unknown",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                state,
                "unavailable",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The usage entity is currently {state}.");
        }

        string cleaned = state
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (decimal.TryParse(
                cleaned,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out decimal invariantValue))
        {
            return invariantValue;
        }

        if (decimal.TryParse(
                cleaned,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out decimal localValue))
        {
            return localValue;
        }

        throw new InvalidOperationException(
            $"Unable to interpret '{state}' as a usage percentage.");
    }

    private static DateTimeOffset? ResolveResetTime(
        string? resetState,
        DateTimeOffset retrievedAt)
    {
        if (string.IsNullOrWhiteSpace(resetState)
            || string.Equals(
                resetState,
                "unknown",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                resetState,
                "unavailable",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string cleaned = resetState.Trim();

        if (TryParseUnixTimestamp(cleaned, out DateTimeOffset unixTime))
        {
            return unixTime;
        }

        if (TryParseTimeOfDay(cleaned, retrievedAt, out DateTimeOffset timeOfDay))
        {
            return timeOfDay;
        }

        DateTimeStyles dateStyles =
            DateTimeStyles.AllowWhiteSpaces
            | DateTimeStyles.AssumeLocal;

        if (DateTimeOffset.TryParse(
                cleaned,
                CultureInfo.InvariantCulture,
                dateStyles,
                out DateTimeOffset invariantDate))
        {
            return invariantDate;
        }

        if (DateTimeOffset.TryParse(
                cleaned,
                CultureInfo.CurrentCulture,
                dateStyles,
                out DateTimeOffset localDate))
        {
            return localDate;
        }

        if (TryParseDuration(cleaned, out TimeSpan duration))
        {
            return retrievedAt.Add(duration);
        }

        return null;
    }

    private static bool TryParseUnixTimestamp(
        string value,
        out DateTimeOffset result)
    {
        result = default;

        if (!long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long timestamp))
        {
            return false;
        }

        try
        {
            result = Math.Abs(timestamp) >= 10_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
                : DateTimeOffset.FromUnixTimeSeconds(timestamp);

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryParseTimeOfDay(
        string value,
        DateTimeOffset retrievedAt,
        out DateTimeOffset result)
    {
        result = default;

        bool resemblesTimeOnly = Regex.IsMatch(
            value,
            @"^\s*\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!resemblesTimeOnly)
        {
            return false;
        }

        if (!TimeOnly.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out TimeOnly parsedTime)
            && !TimeOnly.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsedTime))
        {
            return false;
        }

        DateTime localNow = retrievedAt.LocalDateTime;
        DateTime candidate = localNow.Date.Add(parsedTime.ToTimeSpan());

        if (candidate <= localNow)
        {
            candidate = candidate.AddDays(1);
        }

        result = new DateTimeOffset(candidate);
        return true;
    }

    private static bool TryParseDuration(
        string value,
        out TimeSpan duration)
    {
        duration = default;

        if (TimeSpan.TryParse(
                value,
                CultureInfo.InvariantCulture,
                out TimeSpan invariantDuration)
            && invariantDuration > TimeSpan.Zero)
        {
            duration = invariantDuration;
            return true;
        }

        Match match = Regex.Match(
            value,
            @"^\s*(?:(?<hours>\d+(?:\.\d+)?)\s*"
            + @"(?:h|hr|hrs|hour|hours))?\s*"
            + @"(?:(?<minutes>\d+(?:\.\d+)?)\s*"
            + @"(?:m|min|mins|minute|minutes))?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (!match.Success
            || (!match.Groups["hours"].Success
                && !match.Groups["minutes"].Success))
        {
            return false;
        }

        double hours = ParseDurationPart(match.Groups["hours"].Value);
        double minutes = ParseDurationPart(match.Groups["minutes"].Value);
        duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);

        return duration > TimeSpan.Zero;
    }

    private static double ParseDurationPart(string value)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : 0d;
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if (!Uri.TryCreate(
                settings.HomeAssistantUrl,
                UriKind.Absolute,
                out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Enter a valid Home Assistant URL beginning with "
                + "http:// or https://.");
        }

        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            throw new InvalidOperationException(
                "Enter a Home Assistant long-lived access token.");
        }

        if (string.IsNullOrWhiteSpace(settings.UsageEntityId))
        {
            throw new InvalidOperationException(
                "Enter the Claude usage entity ID.");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

internal sealed record UsageSnapshot(
    decimal UsagePercentage,
    string? ResetState,
    DateTimeOffset? ResetAt,
    DateTimeOffset RetrievedAt);
````

## `ClaudeUsageTray/HoverDetailsForm.cs`

````csharp
// version 7
using System.Drawing.Drawing2D;

namespace ClaudeUsageTray;

internal sealed class HoverDetailsForm : Form
{
    private const int HorizontalPadding = 14;
    private const int VerticalPadding = 8;
    private const int CornerRadius = 10;
    private const int ScreenGap = 8;

    private static readonly Color ClaudeOrange =
        Color.FromArgb(217, 119, 87);

    private static readonly Color PopupBackground =
        Color.FromArgb(35, 37, 42);

    private static readonly Color SecondaryText =
        Color.FromArgb(238, 238, 238);

    private static readonly Color SeparatorText =
        Color.FromArgb(145, 149, 158);

    private readonly Font _usageFont = new(
        "Segoe UI",
        11f,
        FontStyle.Bold,
        GraphicsUnit.Point);

    private readonly Font _countdownFont = new(
        "Segoe UI",
        10f,
        FontStyle.Regular,
        GraphicsUnit.Point);

    private readonly System.Windows.Forms.Timer _countdownTimer;

    private UsageSnapshot? _snapshot;
    private string _usageText = string.Empty;
    private string _countdownText = string.Empty;

    public HoverDetailsForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PopupBackground;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        Opacity = 0.97d;

        _countdownTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000
        };

        _countdownTimer.Tick += (_, _) => UpdateDisplayText();

        UpdateDisplayText();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExTransparent = 0x00000020;
            const int WsExNoActivate = 0x08000000;

            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |=
                WsExToolWindow
                | WsExTransparent
                | WsExNoActivate;

            return parameters;
        }
    }

    public void SetSnapshot(UsageSnapshot? snapshot)
    {
        _snapshot = snapshot;
        UpdateDisplayText();
    }

    public void ShowNear(Point anchor)
    {
        UpdateDisplayText();
        PositionNearTaskbar(anchor);

        if (!Visible)
        {
            Show();
        }

        _countdownTimer.Start();
    }

    public void HideDetails()
    {
        _countdownTimer.Stop();

        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        const TextFormatFlags flags =
            TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter;

        Size usageSize = TextRenderer.MeasureText(
            e.Graphics,
            _usageText,
            _usageFont,
            Size.Empty,
            flags);

        Size separatorSize = TextRenderer.MeasureText(
            e.Graphics,
            " | ",
            _countdownFont,
            Size.Empty,
            flags);

        Size countdownSize = TextRenderer.MeasureText(
            e.Graphics,
            _countdownText,
            _countdownFont,
            Size.Empty,
            flags);

        int x = HorizontalPadding;

        TextRenderer.DrawText(
            e.Graphics,
            _usageText,
            _usageFont,
            new Rectangle(
                x,
                0,
                usageSize.Width,
                ClientSize.Height),
            ClaudeOrange,
            flags);

        x += usageSize.Width;

        TextRenderer.DrawText(
            e.Graphics,
            " | ",
            _countdownFont,
            new Rectangle(
                x,
                0,
                separatorSize.Width,
                ClientSize.Height),
            SeparatorText,
            flags);

        x += separatorSize.Width;

        TextRenderer.DrawText(
            e.Graphics,
            _countdownText,
            _countdownFont,
            new Rectangle(
                x,
                0,
                countdownSize.Width,
                ClientSize.Height),
            SecondaryText,
            flags);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer.Dispose();
            _usageFont.Dispose();
            _countdownFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateDisplayText()
    {
        string usageText = _snapshot is null
            ? "--%"
            : $"{_snapshot.UsagePercentage:0.#}%";

        string countdownText = FormatResetCountdown(_snapshot?.ResetAt);

        if (string.Equals(_usageText, usageText, StringComparison.Ordinal)
            && string.Equals(
                _countdownText,
                countdownText,
                StringComparison.Ordinal))
        {
            return;
        }

        _usageText = usageText;
        _countdownText = countdownText;

        RecalculateSize();
        Invalidate();
    }

    private static string FormatResetCountdown(DateTimeOffset? resetAt)
    {
        if (resetAt is null)
        {
            return "Reset unavailable";
        }

        TimeSpan remaining = resetAt.Value.ToLocalTime() - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero)
        {
            return "Reset due";
        }

        int totalMinutes = Math.Max(
            1,
            (int)Math.Ceiling(remaining.TotalMinutes));

        int totalHours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        return totalHours > 0
            ? $"Reset in {totalHours}h {minutes:D2}m"
            : $"Reset in {minutes}m";
    }

    private void RecalculateSize()
    {
        const TextFormatFlags flags =
            TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine;

        Size usageSize = TextRenderer.MeasureText(
            _usageText,
            _usageFont,
            Size.Empty,
            flags);

        Size separatorSize = TextRenderer.MeasureText(
            " | ",
            _countdownFont,
            Size.Empty,
            flags);

        Size countdownSize = TextRenderer.MeasureText(
            _countdownText,
            _countdownFont,
            Size.Empty,
            flags);

        int contentWidth =
            usageSize.Width
            + separatorSize.Width
            + countdownSize.Width;

        int contentHeight = Math.Max(
            usageSize.Height,
            Math.Max(separatorSize.Height, countdownSize.Height));

        Size = new Size(
            Math.Max(150, contentWidth + (HorizontalPadding * 2)),
            Math.Max(38, contentHeight + (VerticalPadding * 2)));
    }

    private void PositionNearTaskbar(Point anchor)
    {
        Screen screen = Screen.FromPoint(anchor);
        Rectangle bounds = screen.Bounds;
        Rectangle working = screen.WorkingArea;

        int x;
        int y;

        bool taskbarAtBottom =
            working.Bottom < bounds.Bottom
            && anchor.Y >= working.Bottom;

        bool taskbarAtTop =
            working.Top > bounds.Top
            && anchor.Y <= working.Top;

        bool taskbarAtLeft =
            working.Left > bounds.Left
            && anchor.X <= working.Left;

        bool taskbarAtRight =
            working.Right < bounds.Right
            && anchor.X >= working.Right;

        if (taskbarAtBottom)
        {
            x = anchor.X - (Width / 2);
            y = working.Bottom - Height - ScreenGap;
        }
        else if (taskbarAtTop)
        {
            x = anchor.X - (Width / 2);
            y = working.Top + ScreenGap;
        }
        else if (taskbarAtLeft)
        {
            x = working.Left + ScreenGap;
            y = anchor.Y - (Height / 2);
        }
        else if (taskbarAtRight)
        {
            x = working.Right - Width - ScreenGap;
            y = anchor.Y - (Height / 2);
        }
        else
        {
            x = anchor.X - (Width / 2);
            y = anchor.Y - Height - 14;
        }

        x = Math.Clamp(
            x,
            working.Left + ScreenGap,
            Math.Max(
                working.Left + ScreenGap,
                working.Right - Width - ScreenGap));

        y = Math.Clamp(
            y,
            working.Top + ScreenGap,
            Math.Max(
                working.Top + ScreenGap,
                working.Bottom - Height - ScreenGap));

        Location = new Point(x, y);
    }

    private void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        int diameter = CornerRadius * 2;

        using var path = new GraphicsPath();

        path.AddArc(
            0,
            0,
            diameter,
            diameter,
            180,
            90);

        path.AddArc(
            Width - diameter,
            0,
            diameter,
            diameter,
            270,
            90);

        path.AddArc(
            Width - diameter,
            Height - diameter,
            diameter,
            diameter,
            0,
            90);

        path.AddArc(
            0,
            Height - diameter,
            diameter,
            diameter,
            90,
            90);

        path.CloseFigure();

        Region? previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }
}
````

## `ClaudeUsageTray/IconFactory.cs`

````csharp
// version 7
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

internal static class IconFactory
{
    private const int IconSize = 64;
    private const string ClaudeIconResourceName = "ClaudeUsageTray.Assets.claude.ico";

    private static readonly Color ClaudeOrange = Color.FromArgb(217, 119, 87);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon CreateClaudeLogoIcon()
    {
        Assembly assembly = typeof(IconFactory).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(ClaudeIconResourceName);
        if (stream is not null)
        {
            using var sourceIcon = new Icon(stream, new Size(IconSize, IconSize));
            return (Icon)sourceIcon.Clone();
        }

        return CreateFallbackClaudeIcon();
    }

    public static Icon CreatePercentageIcon(decimal percentage, int fontSize)
    {
        int rounded = Math.Clamp(
            (int)Math.Round(percentage, MidpointRounding.AwayFromZero),
            0,
            999);

        string value = string.Concat(
            rounded.ToString(CultureInfo.InvariantCulture),
            "%");

        return CreateFittedTextIcon(value, fontSize, ClaudeOrange);
    }

    public static Icon CreateUnavailablePercentageIcon(int fontSize)
    {
        return CreateFittedTextIcon("--%", fontSize, ClaudeOrange);
    }

    public static Icon CreateSpinnerIcon(int frame)
    {
        using var bitmap = CreateTransparentBitmap(out Graphics graphics);

        using (graphics)
        {
            const int spokeCount = 12;
            const float centre = IconSize / 2f;
            const float innerRadius = 13f;
            const float outerRadius = 30f;

            for (int spoke = 0; spoke < spokeCount; spoke++)
            {
                int distanceFromLead = (spoke - frame + spokeCount) % spokeCount;
                int alpha = Math.Clamp(255 - (distanceFromLead * 18), 55, 255);
                double angle = (Math.PI * 2d * spoke / spokeCount) - (Math.PI / 2d);

                float startX = centre + (float)Math.Cos(angle) * innerRadius;
                float startY = centre + (float)Math.Sin(angle) * innerRadius;
                float endX = centre + (float)Math.Cos(angle) * outerRadius;
                float endY = centre + (float)Math.Sin(angle) * outerRadius;

                using var pen = new Pen(Color.FromArgb(alpha, ClaudeOrange), 7f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

                graphics.DrawLine(pen, startX, startY, endX, endY);
            }
        }

        return ConvertBitmapToIcon(bitmap);
    }

    private static Icon CreateFittedTextIcon(string text, int fontSize, Color textColor)
    {
        using var bitmap = CreateTransparentBitmap(out Graphics graphics);

        using (graphics)
        using (var fontFamily = new FontFamily("Segoe UI"))
        using (var textPath = new GraphicsPath())
        using (var textBrush = new SolidBrush(textColor))
        using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
        {
            format.FormatFlags |= StringFormatFlags.NoWrap;

            textPath.AddString(
                text,
                fontFamily,
                (int)FontStyle.Bold,
                100f,
                PointF.Empty,
                format);

            RectangleF sourceBounds = textPath.GetBounds();
            if (sourceBounds.Width <= 0f || sourceBounds.Height <= 0f)
            {
                return ConvertBitmapToIcon(bitmap);
            }

            float targetHeight = Math.Clamp(fontSize, 24, 62);
            float targetWidth = IconSize - 1f;
            float targetLeft = (IconSize - targetWidth) / 2f;
            float targetTop = (IconSize - targetHeight) / 2f;

            // Independent scaling deliberately condenses the percentage horizontally.
            // This keeps all digits and the % symbol readable in Windows' square icon slot.
            float scaleX = targetWidth / sourceBounds.Width;
            float scaleY = targetHeight / sourceBounds.Height;

            using var transform = new Matrix(
                scaleX,
                0f,
                0f,
                scaleY,
                targetLeft - (sourceBounds.Left * scaleX),
                targetTop - (sourceBounds.Top * scaleY));

            textPath.Transform(transform);
            graphics.FillPath(textBrush, textPath);
        }

        return ConvertBitmapToIcon(bitmap);
    }

    private static Icon CreateFallbackClaudeIcon()
    {
        using var bitmap = CreateTransparentBitmap(out Graphics graphics);

        using (graphics)
        {
            const int spokeCount = 14;
            const float centre = IconSize / 2f;
            const float innerRadius = 3f;
            const float outerRadius = 28f;

            for (int spoke = 0; spoke < spokeCount; spoke++)
            {
                double angle = Math.PI * 2d * spoke / spokeCount;

                float startX = centre + (float)Math.Cos(angle) * innerRadius;
                float startY = centre + (float)Math.Sin(angle) * innerRadius;
                float endX = centre + (float)Math.Cos(angle) * outerRadius;
                float endY = centre + (float)Math.Sin(angle) * outerRadius;

                using var pen = new Pen(ClaudeOrange, 7f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };

                graphics.DrawLine(pen, startX, startY, endX, endY);
            }
        }

        return ConvertBitmapToIcon(bitmap);
    }

    private static Bitmap CreateTransparentBitmap(out Graphics graphics)
    {
        var bitmap = new Bitmap(
            IconSize,
            IconSize,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        return bitmap;
    }

    private static Icon ConvertBitmapToIcon(Bitmap bitmap)
    {
        IntPtr iconHandle = bitmap.GetHicon();

        try
        {
            using Icon icon = Icon.FromHandle(iconHandle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }
}
````

## `ClaudeUsageTray/Program.cs`

````csharp
// version 7
using System.Threading;

namespace ClaudeUsageTray;

internal static class Program
{
    private const string MutexName = @"Local\SWAKES.ClaudeUsageTray";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var appMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Claude Usage Tray is already running.",
                "Claude Usage Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new TrayApplicationContext());
    }
}
````

## `ClaudeUsageTray/SecretProtector.cs`

````csharp
// version 7
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeUsageTray;

internal static class SecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataLength;
        public IntPtr DataPointer;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memoryHandle);

    public static string Protect(string plaintext)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(plaintext);
        DataBlob inputBlob = CreateBlob(inputBytes);

        try
        {
            if (!CryptProtectData(
                    ref inputBlob,
                    "Claude Usage Tray Home Assistant token",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out DataBlob outputBlob))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                byte[] outputBytes = CopyFromBlob(outputBlob);
                return Convert.ToBase64String(outputBytes);
            }
            finally
            {
                if (outputBlob.DataPointer != IntPtr.Zero)
                {
                    LocalFree(outputBlob.DataPointer);
                }
            }
        }
        finally
        {
            FreeBlob(inputBlob);
        }
    }

    public static string Unprotect(string protectedText)
    {
        byte[] inputBytes = Convert.FromBase64String(protectedText);
        DataBlob inputBlob = CreateBlob(inputBytes);

        try
        {
            if (!CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out DataBlob outputBlob))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                byte[] outputBytes = CopyFromBlob(outputBlob);
                return Encoding.UTF8.GetString(outputBytes);
            }
            finally
            {
                if (outputBlob.DataPointer != IntPtr.Zero)
                {
                    LocalFree(outputBlob.DataPointer);
                }
            }
        }
        finally
        {
            FreeBlob(inputBlob);
        }
    }

    private static DataBlob CreateBlob(byte[] data)
    {
        var blob = new DataBlob
        {
            DataLength = data.Length,
            DataPointer = Marshal.AllocHGlobal(data.Length)
        };

        Marshal.Copy(data, 0, blob.DataPointer, data.Length);
        return blob;
    }

    private static byte[] CopyFromBlob(DataBlob blob)
    {
        var data = new byte[blob.DataLength];
        Marshal.Copy(blob.DataPointer, data, 0, blob.DataLength);
        return data;
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.DataPointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.DataPointer);
        }
    }
}
````

## `ClaudeUsageTray/SettingsForm.cs`

````csharp
// version 7
namespace ClaudeUsageTray;

internal sealed class SettingsForm : Form
{
    private static readonly Color PreviewBackground = Color.FromArgb(35, 37, 42);

    private readonly TextBox _urlTextBox = new();
    private readonly TextBox _tokenTextBox = new();
    private readonly TextBox _usageEntityTextBox = new();
    private readonly TextBox _resetEntityTextBox = new();
    private readonly NumericUpDown _refreshSecondsInput = new();
    private readonly NumericUpDown _hoverValueFontSizeInput = new();
    private readonly PictureBox _fontSizePreviewPictureBox = new();
    private readonly CheckBox _startWithWindowsCheckBox = new();
    private readonly Button _testButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();
    private readonly Label _statusLabel = new();
    private readonly HomeAssistantClient _client;

    public AppSettings EditedSettings { get; }

    public SettingsForm(AppSettings currentSettings, HomeAssistantClient client)
    {
        _client = client;
        EditedSettings = currentSettings.Clone();

        Text = "Claude Usage Tray Settings";
        Icon = IconFactory.CreateClaudeLogoIcon();
        ClientSize = new Size(640, 610);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildInterface();
        LoadValues();
    }

    private void BuildInterface()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 11
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, "Home Assistant URL", _urlTextBox, 38);

        _tokenTextBox.UseSystemPasswordChar = true;
        AddRow(layout, 1, "Long-lived token", _tokenTextBox, 38);

        AddRow(layout, 2, "Usage entity", _usageEntityTextBox, 38);
        AddRow(layout, 3, "Reset-time entity", _resetEntityTextBox, 38);

        _refreshSecondsInput.Minimum = 30;
        _refreshSecondsInput.Maximum = 3600;
        _refreshSecondsInput.Increment = 30;
        _refreshSecondsInput.Width = 110;
        AddRow(layout, 4, "Refresh interval", _refreshSecondsInput, 38);

        _hoverValueFontSizeInput.Minimum = 24;
        _hoverValueFontSizeInput.Maximum = 62;
        _hoverValueFontSizeInput.Increment = 1;
        _hoverValueFontSizeInput.Width = 110;
        _hoverValueFontSizeInput.ValueChanged += (_, _) => UpdateFontSizePreview();
        AddRow(layout, 5, "Hover percentage font size", _hoverValueFontSizeInput, 38);

        _fontSizePreviewPictureBox.BackColor = PreviewBackground;
        _fontSizePreviewPictureBox.Size = new Size(88, 78);
        _fontSizePreviewPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
        _fontSizePreviewPictureBox.Margin = new Padding(3, 4, 3, 4);

        layout.Controls.Add(new Label
        {
            Text = "Tray icon preview",
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        }, 0, 6);
        layout.Controls.Add(_fontSizePreviewPictureBox, 1, 6);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));

        _startWithWindowsCheckBox.Text = "Start Claude Usage Tray when I sign in to Windows";
        _startWithWindowsCheckBox.AutoSize = true;
        layout.Controls.Add(new Label(), 0, 7);
        layout.Controls.Add(_startWithWindowsCheckBox, 1, 7);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var helpLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Hover over the Claude tray icon to replace it with the current percentage and show a compact usage window containing the value and live reset countdown. Move the pointer away to restore the Claude logo."
        };
        layout.Controls.Add(new Label(), 0, 8);
        layout.Controls.Add(helpLabel, 1, 8);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

        _statusLabel.AutoSize = true;
        _statusLabel.MaximumSize = new Size(390, 0);
        _statusLabel.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(new Label(), 0, 9);
        layout.Controls.Add(_statusLabel, 1, 9);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _testButton.Text = "Test Connection";
        _testButton.AutoSize = true;
        _testButton.Click += async (_, _) => await TestConnectionAsync();

        _saveButton.Text = "Save";
        _saveButton.AutoSize = true;
        _saveButton.Click += (_, _) => SaveAndClose();

        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_testButton);

        layout.Controls.Add(new Label(), 0, 10);
        layout.Controls.Add(buttons, 1, 10);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        Controls.Add(layout);
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private static void AddRow(
        TableLayoutPanel layout,
        int row,
        string labelText,
        Control inputControl,
        int rowHeight)
    {
        var label = new Label
        {
            Text = labelText,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        };

        inputControl.Dock = inputControl is NumericUpDown ? DockStyle.Left : DockStyle.Fill;
        inputControl.Margin = new Padding(3, 6, 3, 6);

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(inputControl, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
    }

    private void LoadValues()
    {
        _urlTextBox.Text = EditedSettings.HomeAssistantUrl;
        _tokenTextBox.Text = EditedSettings.AccessToken;
        _usageEntityTextBox.Text = EditedSettings.UsageEntityId;
        _resetEntityTextBox.Text = EditedSettings.ResetEntityId;
        _refreshSecondsInput.Value = Math.Clamp(EditedSettings.RefreshSeconds, 30, 3600);
        _hoverValueFontSizeInput.Value = Math.Clamp(EditedSettings.HoverValueFontSize, 24, 62);
        _startWithWindowsCheckBox.Checked = EditedSettings.StartWithWindows;
        UpdateFontSizePreview();
    }

    private void UpdateFontSizePreview()
    {
        using Icon previewIcon = IconFactory.CreatePercentageIcon(
            76m,
            (int)_hoverValueFontSizeInput.Value);

        Bitmap replacement = previewIcon.ToBitmap();
        Image? previous = _fontSizePreviewPictureBox.Image;
        _fontSizePreviewPictureBox.Image = replacement;
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fontSizePreviewPictureBox.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private AppSettings ReadValues()
    {
        return new AppSettings
        {
            SettingsSchemaVersion = 7,
            HomeAssistantUrl = _urlTextBox.Text,
            AccessToken = _tokenTextBox.Text,
            EncryptedAccessToken = EditedSettings.EncryptedAccessToken,
            UsageEntityId = _usageEntityTextBox.Text,
            ResetEntityId = _resetEntityTextBox.Text,
            RefreshSeconds = (int)_refreshSecondsInput.Value,
            HoverValueFontSize = (int)_hoverValueFontSizeInput.Value,
            StartWithWindows = _startWithWindowsCheckBox.Checked
        };
    }

    private async Task TestConnectionAsync()
    {
        SetBusy(true, "Testing Home Assistant connection...");

        try
        {
            AppSettings candidate = ReadValues();
            candidate.Normalise();
            UsageSnapshot snapshot = await _client.GetUsageAsync(candidate);
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text =
                $"Connected successfully. Current Claude usage: {snapshot.UsagePercentage:0.#}%";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Firebrick;
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _testButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        UseWaitCursor = busy;
        _statusLabel.Text = status;
    }

    private void SaveAndClose()
    {
        AppSettings candidate = ReadValues();
        candidate.Normalise();

        if (!Uri.TryCreate(candidate.HomeAssistantUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show(
                this,
                "Enter a valid Home Assistant URL beginning with http:// or https://.",
                "Invalid URL",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.AccessToken))
        {
            MessageBox.Show(
                this,
                "Enter a Home Assistant long-lived access token.",
                "Token Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(candidate.UsageEntityId))
        {
            MessageBox.Show(
                this,
                "Enter the Claude usage entity ID.",
                "Entity Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        EditedSettings.SettingsSchemaVersion = 7;
        EditedSettings.HomeAssistantUrl = candidate.HomeAssistantUrl;
        EditedSettings.AccessToken = candidate.AccessToken;
        EditedSettings.EncryptedAccessToken = candidate.EncryptedAccessToken;
        EditedSettings.UsageEntityId = candidate.UsageEntityId;
        EditedSettings.ResetEntityId = candidate.ResetEntityId;
        EditedSettings.RefreshSeconds = candidate.RefreshSeconds;
        EditedSettings.HoverValueFontSize = candidate.HoverValueFontSize;
        EditedSettings.StartWithWindows = candidate.StartWithWindows;

        DialogResult = DialogResult.OK;
        Close();
    }
}
````

## `ClaudeUsageTray/SettingsStore.cs`

````csharp
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
````

## `ClaudeUsageTray/StartupManager.cs`

````csharp
// version 7
using Microsoft.Win32;

namespace ClaudeUsageTray;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeUsageTray";

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open the Windows startup registry key.");

        if (enabled)
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to determine the application path.");

            key.SetValue(ValueName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
````

## `ClaudeUsageTray/TrayApplicationContext.cs`

````csharp
// version 7
using System.Diagnostics;

namespace ClaudeUsageTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int MinimumManualRefreshAnimationMilliseconds = 2000;
    private const int SpinnerFrameCount = 12;
    private const int HoverAnchorRadius = 20;

    private readonly HomeAssistantClient _homeAssistantClient = new();
    private readonly NotifyIcon _usageNotifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _startWithWindowsMenuItem;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _spinnerTimer;
    private readonly System.Windows.Forms.Timer _hoverMonitorTimer;
    private readonly HoverDetailsForm _hoverDetailsForm;

    private AppSettings _settings;
    private Icon? _usageIcon;
    private UsageSnapshot? _latestSnapshot;
    private bool _refreshInProgress;
    private bool _isExiting;
    private bool _showingClaudeLogo;
    private bool _showingHoverValue;
    private bool _contextMenuOpen;
    private int _spinnerFrame;
    private string? _lastError;
    private Point _trayHoverAnchor;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();
        _hoverDetailsForm = new HoverDetailsForm();

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Opening += (_, _) =>
        {
            _contextMenuOpen = true;
            RestoreClaudeLogoIcon();
        };

        _contextMenu.Closed += (_, _) => _contextMenuOpen = false;

        _contextMenu.Items.Add(
            "Refresh Now",
            null,
            async (_, _) => await RefreshUsageAsync(
                showErrorBalloon: true,
                showRefreshAnimation: true));

        _contextMenu.Items.Add(
            "Settings",
            null,
            (_, _) => ShowSettings());

        _contextMenu.Items.Add(new ToolStripSeparator());

        _contextMenu.Items.Add(
            "Open Home Assistant",
            null,
            (_, _) => OpenUrl(_settings.HomeAssistantUrl));

        _contextMenu.Items.Add(
            "Open Claude",
            null,
            (_, _) => OpenUrl("https://claude.ai/"));

        _contextMenu.Items.Add(new ToolStripSeparator());

        _startWithWindowsMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = true
        };

        _startWithWindowsMenuItem.CheckedChanged += StartWithWindowsChanged;
        _contextMenu.Items.Add(_startWithWindowsMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _usageIcon = IconFactory.CreateClaudeLogoIcon();
        _showingClaudeLogo = true;

        _usageNotifyIcon = new NotifyIcon
        {
            Icon = _usageIcon,
            Text = string.Empty,
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _usageNotifyIcon.MouseClick += UsageNotifyIconMouseClick;
        _usageNotifyIcon.MouseDown += UsageNotifyIconMouseDown;
        _usageNotifyIcon.MouseMove += UsageNotifyIconMouseMove;

        _spinnerTimer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };

        _spinnerTimer.Tick += SpinnerTimerTick;

        _hoverMonitorTimer = new System.Windows.Forms.Timer
        {
            Interval = 75
        };

        _hoverMonitorTimer.Tick += HoverMonitorTimerTick;

        _refreshTimer = new System.Windows.Forms.Timer();
        _refreshTimer.Tick += async (_, _) =>
            await RefreshUsageAsync(
                showErrorBalloon: false,
                showRefreshAnimation: false);

        ApplyRefreshInterval();
        _refreshTimer.Start();

        if (SettingsAreComplete())
        {
            _ = RefreshUsageAsync(
                showErrorBalloon: false,
                showRefreshAnimation: false);
        }
        else
        {
            Application.Idle += ShowInitialSettingsOnIdle;
        }
    }

    private bool SettingsAreComplete()
    {
        return !string.IsNullOrWhiteSpace(_settings.HomeAssistantUrl)
            && !string.IsNullOrWhiteSpace(_settings.AccessToken)
            && !string.IsNullOrWhiteSpace(_settings.UsageEntityId);
    }

    private async void UsageNotifyIconMouseClick(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        RestoreClaudeLogoIcon();

        if (!SettingsAreComplete())
        {
            ShowSettings();
            return;
        }

        await RefreshUsageAsync(
            showErrorBalloon: true,
            showRefreshAnimation: true);
    }

    private void UsageNotifyIconMouseDown(
        object? sender,
        MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            RestoreClaudeLogoIcon();
        }
    }

    private void UsageNotifyIconMouseMove(
        object? sender,
        MouseEventArgs e)
    {
        if (_isExiting || _refreshInProgress || _contextMenuOpen)
        {
            return;
        }

        _trayHoverAnchor = Cursor.Position;
        ShowHoverPresentation();
        _hoverMonitorTimer.Start();
    }

    private async Task RefreshUsageAsync(
        bool showErrorBalloon,
        bool showRefreshAnimation)
    {
        if (_refreshInProgress || _isExiting || !SettingsAreComplete())
        {
            return;
        }

        _refreshInProgress = true;
        Stopwatch animationStopwatch = Stopwatch.StartNew();
        UsageSnapshot? snapshot = null;
        Exception? refreshError = null;

        if (showRefreshAnimation)
        {
            StartRefreshAnimation();
        }

        try
        {
            snapshot = await _homeAssistantClient.GetUsageAsync(_settings);
        }
        catch (Exception ex)
        {
            refreshError = ex;
        }

        try
        {
            if (showRefreshAnimation)
            {
                int remainingMilliseconds =
                    MinimumManualRefreshAnimationMilliseconds
                    - (int)animationStopwatch.ElapsedMilliseconds;

                if (remainingMilliseconds > 0)
                {
                    await Task.Delay(remainingMilliseconds);
                }

                StopRefreshAnimation();
            }

            if (_isExiting)
            {
                return;
            }

            if (refreshError is null && snapshot is not null)
            {
                UpdateUsageDisplay(snapshot);
                _lastError = null;
                return;
            }

            string errorMessage =
                refreshError?.Message
                ?? "Unable to retrieve Claude usage.";

            if (_latestSnapshot is null)
            {
                UpdateUnavailableDisplay();
            }
            else if (_showingHoverValue)
            {
                ShowHoverPresentation(forceIconRefresh: true);
            }
            else
            {
                RestoreClaudeLogoIcon();
            }

            if (showErrorBalloon
                || !string.Equals(
                    _lastError,
                    errorMessage,
                    StringComparison.Ordinal))
            {
                _usageNotifyIcon.BalloonTipTitle = "Claude Usage Tray";
                _usageNotifyIcon.BalloonTipText = errorMessage;
                _usageNotifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                _usageNotifyIcon.ShowBalloonTip(5000);
            }

            _lastError = errorMessage;
        }
        finally
        {
            if (showRefreshAnimation)
            {
                StopRefreshAnimation();
            }

            _refreshInProgress = false;
        }
    }

    private void StartRefreshAnimation()
    {
        _hoverMonitorTimer.Stop();
        _hoverDetailsForm.HideDetails();
        _showingHoverValue = false;
        _spinnerFrame = 0;

        ReplaceUsageIcon(
            IconFactory.CreateSpinnerIcon(_spinnerFrame));

        _showingClaudeLogo = false;
        _usageNotifyIcon.Text = "Refreshing Claude usage…";
        _spinnerTimer.Start();
    }

    private void StopRefreshAnimation()
    {
        _spinnerTimer.Stop();
    }

    private void SpinnerTimerTick(object? sender, EventArgs e)
    {
        if (!_refreshInProgress || _isExiting)
        {
            _spinnerTimer.Stop();
            return;
        }

        _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrameCount;

        ReplaceUsageIcon(
            IconFactory.CreateSpinnerIcon(_spinnerFrame));

        _showingClaudeLogo = false;
        _showingHoverValue = false;
    }

    private void HoverMonitorTimerTick(object? sender, EventArgs e)
    {
        if (!_showingHoverValue || _refreshInProgress || _contextMenuOpen)
        {
            _hoverMonitorTimer.Stop();
            _hoverDetailsForm.HideDetails();
            return;
        }

        Point cursor = Cursor.Position;

        var hoverZone = new Rectangle(
            _trayHoverAnchor.X - HoverAnchorRadius,
            _trayHoverAnchor.Y - HoverAnchorRadius,
            HoverAnchorRadius * 2,
            HoverAnchorRadius * 2);

        if (hoverZone.Contains(cursor))
        {
            return;
        }

        RestoreClaudeLogoIcon();
    }

    private void UpdateUsageDisplay(UsageSnapshot snapshot)
    {
        _latestSnapshot = snapshot;
        _hoverDetailsForm.SetSnapshot(snapshot);

        if (_showingHoverValue)
        {
            ShowHoverPresentation(forceIconRefresh: true);
        }
        else
        {
            RestoreClaudeLogoIcon();
        }

        _usageNotifyIcon.Text = string.Empty;
    }

    private void UpdateUnavailableDisplay()
    {
        _latestSnapshot = null;
        _hoverDetailsForm.SetSnapshot(null);

        if (_showingHoverValue)
        {
            ShowHoverPresentation(forceIconRefresh: true);
        }
        else
        {
            RestoreClaudeLogoIcon();
        }

        _usageNotifyIcon.Text = string.Empty;
    }

    private void ShowHoverPresentation(bool forceIconRefresh = false)
    {
        if (_isExiting || _refreshInProgress || _contextMenuOpen)
        {
            return;
        }

        if (forceIconRefresh || !_showingHoverValue)
        {
            ShowHoverValueIcon();
        }

        _hoverDetailsForm.SetSnapshot(_latestSnapshot);
        _hoverDetailsForm.ShowNear(_trayHoverAnchor);
    }

    private void ShowHoverValueIcon()
    {
        Icon percentageIcon = _latestSnapshot is null
            ? IconFactory.CreateUnavailablePercentageIcon(
                _settings.HoverValueFontSize)
            : IconFactory.CreatePercentageIcon(
                _latestSnapshot.UsagePercentage,
                _settings.HoverValueFontSize);

        ReplaceUsageIcon(percentageIcon);
        _showingClaudeLogo = false;
        _showingHoverValue = true;
        _usageNotifyIcon.Text = string.Empty;
    }

    private void RestoreClaudeLogoIcon()
    {
        _hoverMonitorTimer.Stop();
        _hoverDetailsForm.HideDetails();
        _showingHoverValue = false;

        if (_showingClaudeLogo)
        {
            return;
        }

        ReplaceUsageIcon(IconFactory.CreateClaudeLogoIcon());
        _showingClaudeLogo = true;
        _usageNotifyIcon.Text = string.Empty;
    }

    private void ReplaceUsageIcon(Icon replacement)
    {
        Icon? previous = _usageIcon;
        _usageIcon = replacement;
        _usageNotifyIcon.Icon = replacement;
        previous?.Dispose();
    }

    private void ShowSettings()
    {
        RestoreClaudeLogoIcon();

        using var form = new SettingsForm(
            _settings,
            _homeAssistantClient);

        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings = form.EditedSettings;

        try
        {
            SettingsStore.Save(_settings);
            StartupManager.SetEnabled(_settings.StartWithWindows);

            _startWithWindowsMenuItem.CheckedChanged -=
                StartWithWindowsChanged;

            _startWithWindowsMenuItem.Checked =
                _settings.StartWithWindows;

            _startWithWindowsMenuItem.CheckedChanged +=
                StartWithWindowsChanged;

            ApplyRefreshInterval();

            _ = RefreshUsageAsync(
                showErrorBalloon: true,
                showRefreshAnimation: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Unable to Save Settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void StartWithWindowsChanged(object? sender, EventArgs e)
    {
        try
        {
            _settings.StartWithWindows =
                _startWithWindowsMenuItem.Checked;

            StartupManager.SetEnabled(_settings.StartWithWindows);
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            _startWithWindowsMenuItem.CheckedChanged -=
                StartWithWindowsChanged;

            _startWithWindowsMenuItem.Checked =
                !_startWithWindowsMenuItem.Checked;

            _startWithWindowsMenuItem.CheckedChanged +=
                StartWithWindowsChanged;

            MessageBox.Show(
                ex.Message,
                "Startup Setting Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ApplyRefreshInterval()
    {
        _refreshTimer.Interval =
            Math.Clamp(_settings.RefreshSeconds, 30, 3600) * 1000;
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Unable to Open Link",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ShowInitialSettingsOnIdle(object? sender, EventArgs e)
    {
        Application.Idle -= ShowInitialSettingsOnIdle;
        ShowSettings();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _refreshTimer.Stop();
        _spinnerTimer.Stop();
        _hoverMonitorTimer.Stop();
        _hoverDetailsForm.HideDetails();
        _usageNotifyIcon.Visible = false;

        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _spinnerTimer.Dispose();
            _hoverMonitorTimer.Dispose();
            _hoverDetailsForm.Dispose();
            _usageNotifyIcon.Dispose();
            _contextMenu.Dispose();
            _homeAssistantClient.Dispose();
            _usageIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}
````

## `publish-win-x64.ps1`

````powershell
﻿# version 7
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$project = Join-Path $PSScriptRoot "ClaudeUsageTray\ClaudeUsageTray.csproj"
$nugetConfig = Join-Path $PSScriptRoot "NuGet.Config"
$publishRoot = Join-Path $PSScriptRoot "publish"
$output = Join-Path $publishRoot "win-x64"
$executable = Join-Path $output "ClaudeUsageTray.exe"
$processName = "ClaudeUsageTray"

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (dotnet exit code $LASTEXITCODE)."
    }
}

function Stop-ClaudeUsageTray {
    $runningProcesses = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)

    if ($runningProcesses.Count -eq 0) {
        return
    }

    foreach ($process in $runningProcesses) {
        $displayPath = $null

        try {
            $displayPath = $process.Path
        }
        catch {
            $displayPath = $null
        }

        if ([string]::IsNullOrWhiteSpace($displayPath)) {
            Write-Host "Closing running Claude Usage Tray process (PID $($process.Id))..."
        }
        else {
            Write-Host "Closing running Claude Usage Tray process: $displayPath"
        }

        Stop-Process -Id $process.Id -Force -ErrorAction Stop

        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "ClaudeUsageTray.exe did not close within 10 seconds. Close it from Task Manager and run the script again."
            }

            Start-Sleep -Milliseconds 200
        }
    }

    Start-Sleep -Milliseconds 500
}

function Remove-DirectoryWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [int]$Attempts = 8
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Could not remove '$Path'. Another program may still be using a file in that folder. Close Claude Usage Tray, File Explorer preview windows, and any antivirus scan using the file, then try again. Original error: $($_.Exception.Message)"
            }

            Start-Sleep -Milliseconds (500 * $attempt)
        }
    }
}

function Publish-SingleFileWithRetry {
    param(
        [int]$Attempts = 3
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $stage = Join-Path $publishRoot (".stage-win-x64-" + [Guid]::NewGuid().ToString("N"))
        $stageExecutable = Join-Path $stage "ClaudeUsageTray.exe"

        Write-Host "Single-file publish attempt $attempt of $Attempts..."

        & dotnet @(
            "publish",
            $project,
            "--configuration", "Release",
            "--runtime", "win-x64",
            "--self-contained", "true",
            "--output", $stage,
            "--no-restore",
            "/p:PublishSingleFile=true",
            "/p:IncludeNativeLibrariesForSelfExtract=true",
            "/p:EnableCompressionInSingleFile=true"
        ) | Out-Host

        $publishExitCode = $LASTEXITCODE

        if (($publishExitCode -eq 0) -and (Test-Path -LiteralPath $stageExecutable -PathType Leaf)) {
            return $stage
        }

        Write-Host "Single-file attempt $attempt failed. Waiting before retrying..." -ForegroundColor Yellow
        Remove-DirectoryWithRetry -Path $stage
        Start-Sleep -Seconds (2 * $attempt)
    }

    return $null
}

function Publish-FolderFallback {
    $stage = Join-Path $publishRoot (".stage-folder-win-x64-" + [Guid]::NewGuid().ToString("N"))
    $stageExecutable = Join-Path $stage "ClaudeUsageTray.exe"

    Write-Host "Publishing a self-contained folder build instead..." -ForegroundColor Yellow

    Invoke-DotNet `
        -Arguments @(
            "publish",
            $project,
            "--configuration", "Release",
            "--runtime", "win-x64",
            "--self-contained", "true",
            "--output", $stage,
            "--no-restore",
            "/p:PublishSingleFile=false"
        ) `
        -FailureMessage "Folder-based application publish failed"

    if (-not (Test-Path -LiteralPath $stageExecutable -PathType Leaf)) {
        throw "The folder-based publish completed without creating '$stageExecutable'."
    }

    return $stage
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw "The .NET SDK was not found. Install a compatible .NET 8 SDK, then run this script again."
    }

    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Project file not found: $project"
    }

    if (-not (Test-Path -LiteralPath $nugetConfig -PathType Leaf)) {
        throw "NuGet configuration not found: $nugetConfig"
    }

    $sdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0) {
        throw "The installed .NET SDK could not be selected. Check global.json and the output of 'dotnet --list-sdks'."
    }

    Write-Host "Claude Usage Tray - Windows x64 publisher" -ForegroundColor Cyan
    Write-Host "Using .NET SDK: $sdkVersion"
    Write-Host "NuGet source: https://api.nuget.org/v3/index.json"
    Write-Host ""

    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

    Stop-ClaudeUsageTray

    Write-Host "Restoring Microsoft runtime packs..."
    Invoke-DotNet `
        -Arguments @(
            "restore",
            $project,
            "--runtime", "win-x64",
            "--configfile", $nugetConfig
        ) `
        -FailureMessage "Package restore failed"

    Write-Host ""
    Write-Host "Publishing the self-contained executable..."

    $stageOutput = Publish-SingleFileWithRetry -Attempts 3
    $usedFolderFallback = $false

    if ([string]::IsNullOrWhiteSpace($stageOutput)) {
        Write-Host ""
        Write-Host "The single-file bundler remained locked after three attempts." -ForegroundColor Yellow
        $stageOutput = Publish-FolderFallback
        $usedFolderFallback = $true
    }

    Stop-ClaudeUsageTray
    Remove-DirectoryWithRetry -Path $output

    Move-Item -LiteralPath $stageOutput -Destination $output -Force

    Get-ChildItem -LiteralPath $publishRoot -Directory -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like ".stage-*" } |
        ForEach-Object {
            Remove-DirectoryWithRetry -Path $_.FullName
        }

    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Publishing completed without creating '$executable'."
    }

    Write-Host ""
    Write-Host "Publish succeeded:" -ForegroundColor Green
    Write-Host $executable

    if ($usedFolderFallback) {
        Write-Host ""
        Write-Host "Note: Windows locked the single-file bundle, so a folder-based self-contained build was created." -ForegroundColor Yellow
        Write-Host "Keep every file in '$output' together when moving or running the application."
    }
}
catch {
    Write-Host ""
    Write-Host "Publish failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
````

## `build.bat`

````batch
@REM version 7
@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found.
    echo Install the .NET 8 SDK or a newer SDK and try again.
    exit /b 1
)

echo Restoring dependencies from NuGet.org...
dotnet restore ClaudeUsageTray.sln --configfile NuGet.Config
if errorlevel 1 goto :error

echo Building Release configuration...
dotnet build ClaudeUsageTray.sln --configuration Release --no-restore
if errorlevel 1 goto :error

echo.
echo Build complete.
echo Output: ClaudeUsageTray\bin\Release\net8.0-windows\win-x64\ClaudeUsageTray.exe
exit /b 0

:error
echo.
echo Build failed. No successful output has been reported.
exit /b 1
````

## `NuGet.Config`

````xml
<?xml version="1.0" encoding="utf-8"?>
<!-- version 7 -->
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
````

## `global.json`

````json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
````

## `.github/workflows/build.yml`

````yaml
# version 7
name: Build Windows Application

on:
  workflow_dispatch:
  push:
    branches:
      - main
    tags:
      - "v*"

jobs:
  build:
    runs-on: windows-latest

    steps:
      - name: Check out repository
        uses: actions/checkout@v4

      - name: Set up .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "8.0.x"

      - name: Restore Microsoft runtime packs
        shell: pwsh
        run: >-
          dotnet restore .\ClaudeUsageTray\ClaudeUsageTray.csproj
          --runtime win-x64
          --configfile .\NuGet.Config

      - name: Publish Windows x64 executable
        shell: pwsh
        run: |
          dotnet publish .\ClaudeUsageTray\ClaudeUsageTray.csproj `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --output .\publish\win-x64 `
            --no-restore `
            /p:PublishSingleFile=true `
            /p:IncludeNativeLibrariesForSelfExtract=true `
            /p:EnableCompressionInSingleFile=true

      - name: Upload executable
        uses: actions/upload-artifact@v4
        with:
          name: ClaudeUsageTray-win-x64
          path: publish\win-x64\ClaudeUsageTray.exe
          if-no-files-found: error
````

## `.gitignore`

````text
# version 7
.vs/
**/bin/
**/obj/
publish/
*.user
*.suo
````

## `README.md`

````markdown
# Claude Usage Tray — Version 7

A lightweight Windows notification-area application that reads Claude usage from Home Assistant.

## Version 7 changes

- Keeps the Claude logo as the normal notification-area icon.
- Replaces the Claude logo with the current percentage while the pointer is over the icon.
- Shows a compact hover details window at the same time.
- Displays the hover details as `76% | Reset in 3h 24m`.
- Updates the reset countdown live while the hover details window is visible.
- Supports reset entities containing an ISO date/time, Unix timestamp, time of day, `TimeSpan`, or text such as `3h 24m`.
- Restores the Claude logo and closes the hover details window when the pointer moves away.
- Retains the configurable tray percentage font size.
- Keeps single-click manual refresh and the minimum two-second spinner animation.
- Migrates existing Version 6 settings automatically.

## Requirements

- Windows 10 or Windows 11, 64-bit
- Home Assistant accessible from the Windows PC
- A Claude usage entity, defaulting to:
  - `sensor.claude_usage_sam_pro_session_usage`
- A reset-time entity, defaulting to:
  - `sensor.claude_usage_sam_pro_session_reset_time`
- A Home Assistant long-lived access token
- Internet access to NuGet.org while publishing the source
- A compatible .NET 8 SDK when compiling the source

The published self-contained executable does not require .NET to be installed separately.

## Publish the application

Close any running Claude Usage Tray instance, then open PowerShell in the extracted project folder and run:

```powershell
# version 7
Set-ExecutionPolicy -Scope Process Bypass
.\publish-win-x64.ps1
```

The completed application is written to:

```text
publish\win-x64\ClaudeUsageTray.exe
```

## Tray controls

- **Hover:** replaces the Claude logo with the current percentage and shows the percentage plus live reset countdown above the taskbar.
- **Move away:** closes the hover details window and restores the Claude logo.
- **Single left-click:** manually refreshes usage and shows the spinner for at least two seconds.
- **Right-click:** opens the context menu.
- **Automatic refresh:** runs at the configured interval without displaying the spinner.

## Hover countdown examples

```text
76% | Reset in 3h 24m
76% | Reset in 42m
76% | Reset due
76% | Reset unavailable
```
````

## `THIRD_PARTY_NOTICES.md`

````markdown
# Third-Party Notices

## Claude logo

The application includes the Claude symbol supplied for the project by the user. Claude and the Claude logo are trademarks of Anthropic PBC. Their inclusion does not imply endorsement or affiliation.
````

## `LICENSE`

````text
MIT License

Copyright (c) 2026 SWAKES

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
````
