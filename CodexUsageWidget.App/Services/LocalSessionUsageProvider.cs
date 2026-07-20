using System.Text;
using System.Text.Json;
using System.IO;
using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.Services;

public sealed class LocalSessionUsageProvider
{
    private const int MaxFiles = 40;
    private const int TailBytes = 2 * 1024 * 1024;

    public Task<UsageSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() => ReadLatest(cancellationToken), cancellationToken);
    }

    private static UsageSnapshot? ReadLatest(CancellationToken cancellationToken)
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        var roots = new[] { Path.Combine(codexHome, "sessions"), Path.Combine(codexHome, "archived_sessions") };
        var files = roots.Where(Directory.Exists)
            .SelectMany(SafeEnumerateFiles)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaxFiles)
            .ToArray();

        UsageSnapshot? latest = null;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = ReadFileTail(file.FullName);
            if (candidate is not null && (latest is null || candidate.ObservedAt > latest.ObservedAt))
            {
                latest = candidate;
            }
        }
        return latest;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        try { return Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories); }
        catch { return []; }
    }

    private static UsageSnapshot? ReadFileTail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[stream.Length - start];
            var read = stream.Read(buffer, 0, buffer.Length);
            var text = Encoding.UTF8.GetString(buffer, 0, read);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var snapshot = TryParseSessionLine(lines[i]);
                if (snapshot is not null) return snapshot;
            }
        }
        catch { }
        return null;
    }

    internal static UsageSnapshot? TryParseSessionLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload)) return null;
            if (!payload.TryGetProperty("rate_limits", out var limits)) return null;

            var primary = ParseWindow(limits, "primary");
            var secondary = ParseWindow(limits, "secondary");
            if (primary is null && secondary is null) return null;

            var observedAt = DateTimeOffset.UtcNow;
            if (root.TryGetProperty("timestamp", out var timestamp) &&
                DateTimeOffset.TryParse(timestamp.GetString(), out var parsedTimestamp))
            {
                observedAt = parsedTimestamp;
            }

            return new UsageSnapshot(primary, secondary, observedAt, UsageSource.LocalSession, "Codexセッション記録");
        }
        catch
        {
            return null;
        }
    }

    internal static RateLimitWindow? ParseWindow(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetDouble(element, "used_percent", "usedPercent", out var usedPercent)) return null;
        TryGetInt(element, "window_minutes", "windowDurationMins", out var windowMinutes);

        DateTimeOffset? resetsAt = null;
        if (TryGetLong(element, "resets_at", "resetsAt", out var unixSeconds) && unixSeconds > 0)
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        return new RateLimitWindow(usedPercent, windowMinutes, resetsAt);
    }

    private static bool TryGetDouble(JsonElement element, string snake, string camel, out double value)
    {
        value = 0;
        return (element.TryGetProperty(snake, out var p) || element.TryGetProperty(camel, out p)) && p.TryGetDouble(out value);
    }

    private static bool TryGetInt(JsonElement element, string snake, string camel, out int value)
    {
        value = 0;
        return (element.TryGetProperty(snake, out var p) || element.TryGetProperty(camel, out p)) && p.TryGetInt32(out value);
    }

    private static bool TryGetLong(JsonElement element, string snake, string camel, out long value)
    {
        value = 0;
        return (element.TryGetProperty(snake, out var p) || element.TryGetProperty(camel, out p)) && p.TryGetInt64(out value);
    }
}
