using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.Services;

public sealed class UsageService : IDisposable
{
    private readonly AppServerUsageProvider _appServer;
    private readonly LocalSessionUsageProvider _localSession;

    public UsageService(AppServerUsageProvider appServer, LocalSessionUsageProvider localSession)
    {
        _appServer = appServer;
        _localSession = localSession;
    }

    public async Task<UsageSnapshot?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var live = await _appServer.GetAsync(cancellationToken);
        if (live is not null) return live;
        return await _localSession.GetAsync(cancellationToken);
    }

    public void Dispose() => _appServer.Dispose();
}
