using Cabbie.Application.Routing;
using Cabbie.Application.Transports;
using Cabbie.Infrastructure.LocalQueues;
using Microsoft.Extensions.Hosting;

namespace Cabbie.Infrastructure.Hosting;

/// <summary>
/// IHostedService that checks every configured endpoint has a transport, starts the
/// <c>ListenTo</c> listeners and the local queue agents, and stops them on StopAsync
/// (listeners first, then the local queues are drained). Agents created after StartAsync
/// (lazy on first send) auto-start themselves.
/// </summary>
public sealed class CabbieHostedService : IHostedService
{
    private readonly LocalQueueCatalog _queues;
    private readonly TransportRouter _router;
    private readonly RoutingCatalog _routing;
    private readonly CabbieOptions _options;
    private readonly IReceiver _receiver;
    private readonly List<IListener> _listeners = [];

    public CabbieHostedService(
        LocalQueueCatalog queues,
        TransportRouter router,
        RoutingCatalog routing,
        CabbieOptions options,
        IReceiver receiver)
    {
        ArgumentNullException.ThrowIfNull(queues);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(receiver);
        _queues = queues;
        _router = router;
        _routing = routing;
        _options = options;
        _receiver = receiver;
    }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (Uri endpoint in _routing.Destinations.Select(d => d.Value).Concat(_options.ListenEndpoints))
        {
            _router.For(endpoint);
        }

        StartCount++;
        try
        {
            foreach (Uri uri in _options.ListenEndpoints)
            {
                IListener listener = _router.For(uri).CreateListener(uri, _receiver);
                _listeners.Add(listener);
                await listener.StartAsync(cancellationToken);
            }
        }
        catch
        {
            try
            {
                await StopListenersAsync(cancellationToken);
            }
            catch (Exception stopFailure) when (stopFailure is not OperationCanceledException)
            {
                // The start failure is the error to report; a listener that also fails to stop must not hide it.
            }

            throw;
        }

        foreach (LocalQueueAgent agent in _queues.All.ToList())
        {
            agent.Start();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopListenersAsync(cancellationToken);
        }
        finally
        {
            try
            {
                LocalQueueAgent[] agents = [.. _queues.All];
                await Task.WhenAll(agents.Select(agent => agent.DrainAsync(cancellationToken)));
            }
            finally
            {
                // After the drain: running handlers may still send through the cached senders.
                await _router.DisposeAsync();
                StopCount++;
            }
        }
    }

    private async Task StopListenersAsync(CancellationToken cancellationToken)
    {
        IListener[] started = [.. _listeners];
        _listeners.Clear();
        await Task.WhenAll(started.Select(listener => listener.StopAsync(cancellationToken)));
    }
}
