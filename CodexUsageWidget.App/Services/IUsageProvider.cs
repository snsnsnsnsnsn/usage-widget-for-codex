using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.Services;

public interface IUsageProvider : IDisposable
{
    Task<UsageFetchResult> FetchAsync(CancellationToken cancellationToken);
}
