using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.Services;

/// <summary>
/// 既存のログイン情報（Claude Code CLI → Claudeデスクトップアプリの順）を読み取り専用で使い、
/// 公式の使用量APIから5時間枠・週間枠を取得する。
/// トークンの更新は各クライアント自身に任せ、資格情報へは書き込まない。
/// </summary>
public sealed class ClaudeUsageProvider : IUsageProvider
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _retryAfter) return UsageFetchResult.Fail("取得を一時停止中");

        var now = DateTimeOffset.UtcNow;
        var candidates = new[] { ReadCliCredential(), ClaudeDesktopCredentialReader.Read() };
        var credential = candidates.FirstOrDefault(c => c is not null && c.ExpiresAt > now);
        if (credential is null)
        {
            return UsageFetchResult.Fail(candidates.Any(c => c is not null)
                ? "ログイン期限切れ。Claudeアプリを開くと更新されます"
                : "Claudeアプリでログインしてください");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("usage-widget-for-codex/1.1");

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return UsageFetchResult.Fail("ログインを確認できません。Claudeアプリを開いてください");
            }
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _retryAfter = DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5));
                return UsageFetchResult.Fail("取得制限中。しばらく待ちます");
            }
            if (!response.IsSuccessStatusCode)
            {
                _retryAfter = DateTimeOffset.UtcNow.AddMinutes(2);
                return UsageFetchResult.Fail($"取得失敗 (HTTP {(int)response.StatusCode})");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = ParseUsage(json, DateTimeOffset.UtcNow);
            return snapshot is null
                ? UsageFetchResult.Fail("応答を解釈できません")
                : UsageFetchResult.Ok(snapshot);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UsageFetchResult.Fail("応答がタイムアウトしました");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            _retryAfter = DateTimeOffset.UtcNow.AddMinutes(1);
            return UsageFetchResult.Fail("Claudeへ接続できません");
        }
    }

    internal static UsageSnapshot? ParseUsage(string json, DateTimeOffset observedAt)
    {
        using var document = JsonDocument.Parse(json);
        var fiveHour = ParseWindow(document.RootElement, "five_hour", 300);
        var weekly = ParseWindow(document.RootElement, "seven_day", 10080);
        if (fiveHour is null && weekly is null) return null;
        return new UsageSnapshot(fiveHour, weekly, observedAt, UsageSource.ClaudeApi, "Claude公式API")
        {
            ModelLimit = ParseModelLimit(document.RootElement)
        };
    }

    private static RateLimitWindow? ParseWindow(JsonElement root, string name, int windowMinutes)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty("utilization", out var utilization) || !utilization.TryGetDouble(out var usedPercent)) return null;
        return new RateLimitWindow(usedPercent, windowMinutes, ParseResetsAt(element));
    }

    /// <summary>limits のうち、モデル名の付いた最初の週間枠（kind: weekly_scoped）。</summary>
    private static ModelLimit? ParseModelLimit(JsonElement root)
    {
        if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array) return null;
        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object ||
                !limit.TryGetProperty("kind", out var kind) || kind.GetString() != "weekly_scoped" ||
                !limit.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object ||
                !scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object ||
                !model.TryGetProperty("display_name", out var displayName) || displayName.GetString() is not { Length: > 0 } name ||
                !limit.TryGetProperty("percent", out var percent) || !percent.TryGetDouble(out var usedPercent)) continue;

            return new ModelLimit(name, new RateLimitWindow(usedPercent, 10080, ParseResetsAt(limit)));
        }
        return null;
    }

    private static DateTimeOffset? ParseResetsAt(JsonElement element)
    {
        if (element.TryGetProperty("resets_at", out var reset) && reset.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    internal static ClaudeCredential? ParseCredential(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object) return null;
        if (!oauth.TryGetProperty("accessToken", out var token) || token.GetString() is not { Length: > 0 } accessToken) return null;

        var expiresAt = DateTimeOffset.MaxValue;
        if (oauth.TryGetProperty("expiresAt", out var expires) && expires.TryGetInt64(out var unixMilliseconds) && unixMilliseconds > 0)
        {
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
        }
        return new ClaudeCredential(accessToken, expiresAt);
    }

    private static ClaudeCredential? ReadCliCredential()
    {
        var configDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        }

        try
        {
            using var stream = new FileStream(Path.Combine(configDirectory, ".credentials.json"),
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return ParseCredential(reader.ReadToEnd());
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record ClaudeCredential(string AccessToken, DateTimeOffset ExpiresAt);
