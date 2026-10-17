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
}
