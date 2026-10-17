using Microsoft.Extensions.Hosting;
using Cabbie.Infrastructure.LocalQueues;

namespace Cabbie.Infrastructure.Hosting;

/// <summary>
/// IHostedService that starts local queue agents and drains them on StopAsync.
/// Agents created after StartAsync (lazy on first Enqueue) auto-start themselves.
/// </summary>
public sealed class CabbieHostedService : IHostedService
{
    private readonly LocalQueueCatalog _catalog;

    public CabbieHostedService(LocalQueueCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartCount++;
        foreach (LocalQueueAgent agent in _catalog.All.ToList())
        {
            agent.Start();
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LocalQueueAgent[] agents = [.. _catalog.All];
        await Task.WhenAll(agents.Select(agent => agent.DrainAsync(cancellationToken)));
        StopCount++;
    }
}
