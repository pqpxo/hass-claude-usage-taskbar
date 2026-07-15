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
