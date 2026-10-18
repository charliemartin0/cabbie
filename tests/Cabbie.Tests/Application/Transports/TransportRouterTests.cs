using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Tests.Domain.Envelope;

namespace Cabbie.Tests.Application.Transports;

public sealed class TransportRouterTests
{
    [Fact]
    public async Task send_goes_only_to_the_transport_registered_for_the_destination_scheme()
    {
        var alpha = new RecordingTransport("alpha");
        var beta = new RecordingTransport("beta");
        var router = new TransportRouter([alpha, beta]);
        Envelope envelope = EnvelopeFactory.Create(destination: new Destination(new Uri("beta://orders/")));

        await router.SendAsync(envelope);

        Assert.Equal(envelope, Assert.Single(beta.Sent));
        Assert.Empty(alpha.Sent);
    }

    [Fact]
    public async Task send_to_an_unregistered_scheme_throws_unknown_transport_scheme_naming_scheme_and_endpoint()
    {
        var router = new TransportRouter([new RecordingTransport("alpha")]);
        Envelope envelope = EnvelopeFactory.Create(destination: new Destination(new Uri("sqs://orders/")));

        UnknownTransportScheme error = await Assert.ThrowsAsync<UnknownTransportScheme>(
            () => router.SendAsync(envelope).AsTask());

        Assert.Equal("sqs", error.Scheme);
        Assert.Equal(new Uri("sqs://orders/"), error.Endpoint);
        Assert.Contains("'sqs'", error.Message, StringComparison.Ordinal);
        Assert.Contains("sqs://orders/", error.Message, StringComparison.Ordinal);
        Assert.Contains("alpha", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void two_transports_for_one_scheme_fail_with_duplicate_transport_scheme()
    {
        DuplicateTransportScheme error = Assert.Throws<DuplicateTransportScheme>(
            () => new TransportRouter([new RecordingTransport("alpha"), new RecordingTransport("ALPHA")]));

        Assert.Equal("alpha", error.Scheme, ignoreCase: true);
    }

    [Fact]
    public async Task one_sender_is_created_per_endpoint_and_reused_for_later_sends()
    {
        var alpha = new RecordingTransport("alpha");
        var router = new TransportRouter([alpha]);
        var destination = new Destination(new Uri("alpha://orders/"));

        await router.SendAsync(EnvelopeFactory.Create(destination: destination));
        await router.SendAsync(EnvelopeFactory.Create(destination: destination));

        RecordingSender sender = Assert.Single(alpha.Senders.Values);
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task sender_is_created_once_under_concurrent_sends()
    {
        var transport = new SlowTransport();
        var router = new TransportRouter([transport]);
        var destination = new Destination(new Uri("slow://orders/"));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => router.SendAsync(EnvelopeFactory.Create(destination: destination)).AsTask())));

        Assert.Equal(1, transport.Creations);
        Assert.Equal(20, transport.Sender.Sends);
    }

    [Fact]
    public async Task failed_sender_creation_is_not_cached_and_is_retried_on_the_next_send()
    {
        var transport = new SlowTransport { FailFirst = true };
        var router = new TransportRouter([transport]);
        var destination = new Destination(new Uri("slow://orders/"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.SendAsync(EnvelopeFactory.Create(destination: destination)).AsTask());
        await router.SendAsync(EnvelopeFactory.Create(destination: destination));

        Assert.Equal(2, transport.Creations);
        Assert.Equal(1, transport.Sender.Sends);
    }

    [Fact]
    public async Task disposing_the_router_disposes_every_cached_sender()
    {
        var alpha = new RecordingTransport("alpha");
        var router = new TransportRouter([alpha]);
        await router.SendAsync(EnvelopeFactory.Create(destination: new Destination(new Uri("alpha://one/"))));
        await router.SendAsync(EnvelopeFactory.Create(destination: new Destination(new Uri("alpha://two/"))));

        await router.DisposeAsync();

        Assert.All(alpha.Senders.Values, sender => Assert.Equal(1, sender.Disposals));
        Assert.Equal(2, alpha.Senders.Count);
    }

    private sealed class SlowTransport : ITransport
    {
        private int _creations;

        public CountingSender Sender { get; } = new();

        public bool FailFirst { get; init; }

        public int Creations => Volatile.Read(ref _creations);

        public string Scheme => "slow";

        public ISender CreateSender(Uri address)
        {
            int number = Interlocked.Increment(ref _creations);
            Thread.Sleep(50);
            return FailFirst && number == 1 ? throw new InvalidOperationException("no connection") : Sender;
        }

        public IListener CreateListener(Uri address, IReceiver receiver) => throw new NotSupportedException();
    }

    private sealed class CountingSender : ISender
    {
        private int _sends;

        public int Sends => Volatile.Read(ref _sends);

        public ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _sends);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
