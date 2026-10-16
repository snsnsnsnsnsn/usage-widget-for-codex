using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.Services;

public sealed class CodexUsageService : IUsageProvider
{
    private readonly AppServerUsageProvider _appServer;
    private readonly LocalSessionUsageProvider _localSession;

    public CodexUsageService(AppServerUsageProvider appServer, LocalSessionUsageProvider localSession)
    {
        _appServer = appServer;
        _localSession = localSession;
    }

    public async Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _appServer.GetAsync(cancellationToken)
                       ?? await _localSession.GetAsync(cancellationToken);
        return snapshot is null
            ? UsageFetchResult.Fail("Codexを一度実行してください")
            : UsageFetchResult.Ok(snapshot);
    }

    public void Dispose() => _appServer.Dispose();
}
