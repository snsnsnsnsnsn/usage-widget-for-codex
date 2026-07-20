using System.Diagnostics;
using System.Text.Json;
using System.IO;
using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.Services;

public sealed class AppServerUsageProvider : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private DateTimeOffset _retryAfter = DateTimeOffset.MinValue;
    private int _requestId = 10;

    public async Task<UsageSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _retryAfter) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync(cancellationToken);
            if (_process is null) return null;

            var id = Interlocked.Increment(ref _requestId);
            await WriteAsync($"{{\"method\":\"account/rateLimits/read\",\"id\":{id}}}", cancellationToken);
            using var response = await ReadResponseAsync(id, cancellationToken);
            if (!response.RootElement.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("rateLimits", out var limits)) return null;

            var primary = LocalSessionUsageProvider.ParseWindow(limits, "primary");
            var secondary = LocalSessionUsageProvider.ParseWindow(limits, "secondary");
            if (primary is null && secondary is null) return null;
            return new UsageSnapshot(primary, secondary, DateTimeOffset.UtcNow, UsageSource.AppServer, "Codex公式app-server");
        }
        catch
        {
            StopProcess();
            _retryAfter = DateTimeOffset.UtcNow.AddMinutes(5);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return;
        StopProcess();

        foreach (var executable in FindCodexExecutables())
        {
            try
            {
                var process = Process.Start(CreateStartInfo(executable));
                if (process is null) continue;
                _process = process;

                await WriteAsync("{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex_usage_widget\",\"title\":\"Codex Usage Widget\",\"version\":\"1.0.0\"}}}", cancellationToken);
                using var init = await ReadResponseAsync(1, cancellationToken);
                if (init.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("app-server initialization failed");
                await WriteAsync("{\"method\":\"initialized\",\"params\":{}}", cancellationToken);
                return;
            }
            catch
            {
                StopProcess();
            }
        }
        throw new FileNotFoundException("A runnable Codex CLI was not found.");
    }

    private static ProcessStartInfo CreateStartInfo(string executable)
    {
        ProcessStartInfo info;
        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            info = new ProcessStartInfo("cmd.exe");
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/s");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add($"\"{executable}\" app-server --listen stdio://");
        }
        else
        {
            info = new ProcessStartInfo(executable);
            info.ArgumentList.Add("app-server");
            info.ArgumentList.Add("--listen");
            info.ArgumentList.Add("stdio://");
        }
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WindowStyle = ProcessWindowStyle.Hidden;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = false;
        return info;
    }

    private static IEnumerable<string> FindCodexExecutables()
    {
        var candidates = new List<string>();
        var overridePath = Environment.GetEnvironmentVariable("CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath)) candidates.Add(overridePath);

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "codex.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "codex.exe"));

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(directory, "codex.exe"));
            candidates.Add(Path.Combine(directory, "codex.cmd"));
        }

        try
        {
            foreach (var process in Process.GetProcessesByName("codex"))
            {
                try
                {
                    if (process.MainModule?.FileName is string processPath) candidates.Add(processPath);
                }
                catch { }
                finally { process.Dispose(); }
            }
        }
        catch { }

        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        if (_process is null) throw new InvalidOperationException("app-server is not running");
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
    }

    private async Task<JsonDocument> ReadResponseAsync(int id, CancellationToken cancellationToken)
    {
        if (_process is null) throw new InvalidOperationException("app-server is not running");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));

        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null) throw new EndOfStreamException("app-server stopped");
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt32() == id)
                {
                    return document;
                }
            }
            catch (JsonException) { }
            document?.Dispose();
        }
    }

    private void StopProcess()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch { }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        StopProcess();
        _gate.Dispose();
    }
}
