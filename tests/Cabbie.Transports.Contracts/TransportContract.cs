using Cabbie.Application.Transports;
using Cabbie.Domain.Envelope;
using Cabbie.Domain.Envelope.ValueObjects;
using Cabbie.Domain.Messaging;
using Cabbie.Domain.Messaging.ValueObjects;
using Cabbie.Domain.Sagas.ValueObjects;
using Cabbie.Infrastructure.Serialization;

namespace Cabbie.Transports.Contracts;

/// <summary>
/// The behaviour every transport must show. A transport plugs in by subclassing this and
/// supplying the fixture members; xUnit runs the inherited facts on the subclass. The suite
/// always uses the serialising path (an <see cref="EnvelopeCodec"/> built here), never a
/// by-reference shortcut.
/// <para>Fixture rules:</para>
/// <list type="bullet">
/// <item>each test gets fresh endpoints from <see cref="CreateEndpointAsync"/>;</item>
/// <item>the suite always starts a listener before the first send (local binds a queue to its
/// first receiver);</item>
/// <item>teardown stops listeners, disposes senders and routers, then calls
/// <see cref="DisposeTransportAsync"/>.</item>
/// </list>
/// </summary>
public abstract class TransportContract : IAsyncLifetime
{
    private readonly EnvelopeCodec _codec = CreateCodec();
    private readonly List<IListener> _running = [];
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly List<Task> _stopping = [];
    private ITransport? _transport;

    /// <summary>The documented behaviours this transport differs on.</summary>
    protected abstract TransportCapabilities Capabilities { get; }

    /// <summary>How long the suite waits for this transport.</summary>
    protected abstract TransportContractTimings Timings { get; }

    /// <summary>Sets envelope times and drives every wait.</summary>
    protected virtual TimeProvider Time => TimeProvider.System;

    /// <summary>The transport under test, built with the codec the suite decodes with.</summary>
    protected abstract Task<ITransport> CreateTransportAsync(EnvelopeCodec codec);

    /// <summary>A new, unique endpoint on the transport.</summary>
    protected abstract Task<Uri> CreateEndpointAsync();

    /// <summary>
    /// Puts a message on the endpoint as some other producer would, bypassing the sender and
    /// written in the transport's own wire packing.
    /// </summary>
    protected abstract Task SendRawAsync(Uri endpoint, TransportMessage message);

    /// <summary>
    /// Every message the endpoint's listener has dead-lettered so far. Cumulative: the suite calls
    /// it repeatedly, so a call must not hide or consume what an earlier call returned (a broker
    /// fixture receives and deletes from the dead-letter queue and keeps its own running list).
    /// </summary>
    protected abstract Task<IReadOnlyList<TransportMessage>> ReadDeadLettersAsync(Uri endpoint);

    /// <summary>Cleans up transport resources, e.g. deletes broker queues.</summary>
    protected virtual Task DisposeTransportAsync() => Task.CompletedTask;

    private ITransport Transport =>
        _transport ?? throw new InvalidOperationException("InitializeAsync has not run.");

    public async Task InitializeAsync() => _transport = await CreateTransportAsync(_codec);

    public async Task DisposeAsync()
    {
        try
        {
            foreach (IListener listener in _running.ToList())
            {
                await listener.StopAsync(CancellationToken.None).WaitAsync(Timings.DeliveryTimeout, Time);
            }

            _running.Clear();
            foreach (Task stopping in _stopping)
            {
                await stopping.WaitAsync(Timings.DeliveryTimeout, Time);
            }

            for (int index = _disposables.Count - 1; index >= 0; index--)
            {
                await _disposables[index].DisposeAsync();
            }
        }
        finally
        {
            await DisposeTransportAsync();
        }
    }

    [Fact]
    public async Task sent_envelope_arrives_with_ids_type_attempts_headers_and_body_intact()
    {
        Uri endpoint = await CreateEndpointAsync();
        var receiver = new ContractReceiver();
        await ListenAsync(endpoint, receiver);
        Envelope sent = NewEnvelope(
            endpoint,
            number: 7,
            headers: new Headers(new Dictionary<string, string> { ["tenant"] = "acme", ["trace-id"] = "abc-123" }),
            attempts: 3,
            causation: new CausationId(Guid.NewGuid()));

        await NewSender(endpoint).SendAsync(sent);
        await WaitUntilAsync(() => receiver.Received.Count == 1, "the envelope is delivered");

        Envelope received = Assert.Single(receiver.Received);
        Assert.NotSame(sent, received);
        Assert.Equal(sent.Id, received.Id);
        Assert.Equal(sent.MessageType, received.MessageType);
        Assert.Equal(sent.CorrelationId, received.CorrelationId);
        Assert.Equal(sent.ConversationId, received.ConversationId);
        Assert.Equal(sent.CausationId, received.CausationId);
        Assert.Equal(sent.Attempts, received.Attempts);
        Assert.Equal(sent.Headers, received.Headers);
        Assert.Equal(new ContractMessage(7, "message 7"), received.Message.Value);
        Assert.NotEqual(0, received.Data.Value.Length);
    }

    [Fact]
    public async Task acked_envelope_is_not_redelivered()
    {
        Uri endpoint = await CreateEndpointAsync();
        var receiver = new ContractReceiver();
        await ListenAsync(endpoint, receiver);
        Envelope sent = NewEnvelope(endpoint);

        await NewSender(endpoint).SendAsync(sent);
        await WaitUntilAsync(() => receiver.DeliveriesOf(sent.Id) == 1, "the envelope is delivered");

        await NeverWithinAsync(() => receiver.DeliveriesOf(sent.Id) > 1, "an acked envelope is delivered again");
    }

    [Fact]
    public async Task deferred_envelope_is_redelivered_until_it_is_acked()
    {
        Uri endpoint = await CreateEndpointAsync();
        var receiver = new ContractReceiver((_, count) => count == 1 ? ReceiveOutcome.Defer : ReceiveOutcome.Ack);
        await ListenAsync(endpoint, receiver);
        Envelope sent = NewEnvelope(endpoint);

        await NewSender(endpoint).SendAsync(sent);
        await WaitUntilAsync(() => receiver.DeliveriesOf(sent.Id) >= 2, "the deferred envelope is delivered again");

        await NeverWithinAsync(() => receiver.DeliveriesOf(sent.Id) > 2, "the envelope is delivered after it was acked");
    }

    [Fact]
    public async Task nacked_envelope_is_dropped_or_redelivered_as_the_transport_declares()
    {
        Uri endpoint = await CreateEndpointAsync();
        var receiver = new ContractReceiver((_, count) => count == 1 ? ReceiveOutcome.Nack : ReceiveOutcome.Ack);
        await ListenAsync(endpoint, receiver);
        Envelope sent = NewEnvelope(endpoint);

        await NewSender(endpoint).SendAsync(sent);
        if (Capabilities.Nack == NackBehaviour.Redeliver)
        {
            await WaitUntilAsync(() => receiver.DeliveriesOf(sent.Id) >= 2, "the nacked envelope is delivered again");
            await NeverWithinAsync(() => receiver.DeliveriesOf(sent.Id) > 2, "the envelope is delivered after it was acked");
        }
        else
        {
            await WaitUntilAsync(() => receiver.DeliveriesOf(sent.Id) == 1, "the envelope is delivered");
            await NeverWithinAsync(() => receiver.DeliveriesOf(sent.Id) > 1, "a nacked envelope is delivered again");
        }
    }

    [Fact]
    public async Task unreadable_payload_is_acked_and_dead_lettered_without_reaching_the_receiver_or_looping()
    {
        Uri endpoint = await CreateEndpointAsync();
        var receiver = new ContractReceiver();
        await ListenAsync(endpoint, receiver);
        Envelope poison = NewEnvelope(endpoint);
        TransportMessage raw = _codec.Encode(poison) with { Body = "not json"u8.ToArray() };

        await SendRawAsync(endpoint, raw);
        await WaitUntilAsync(
            async () => (await ReadDeadLettersAsync(endpoint)).Any(message => HasMessageId(message, poison.Id)),
            "the unreadable message is dead-lettered");

        Envelope next = NewEnvelope(endpoint, number: 2);
        await NewSender(endpoint).SendAsync(next);
        await WaitUntilAsync(() => receiver.DeliveriesOf(next.Id) == 1, "the listener still delivers readable messages");

        await NeverWithinAsync(
            async () => (await ReadDeadLettersAsync(endpoint)).Count(message => HasMessageId(message, poison.Id)) > 1,
            "the unreadable message is dead-lettered again");
        Assert.Equal(0, receiver.DeliveriesOf(poison.Id));
        TransportMessage deadLetter = Assert.Single(
            await ReadDeadLettersAsync(endpoint),
            message => HasMessageId(message, poison.Id));
        Assert.Equal("not json"u8.ToArray(), deadLetter.Body.ToArray());
    }

    [Fact]
    public async Task stopping_a_listener_waits_for_the_in_flight_envelope_and_then_takes_no_more()
    {
        Uri endpoint = await CreateEndpointAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new ContractReceiver { Gate = gate };
        IListener listener = await ListenAsync(endpoint, receiver);
        ISender sender = NewSender(endpoint);
        Envelope inFlight = NewEnvelope(endpoint);
        Envelope late = NewEnvelope(endpoint, number: 2);

        try
        {
            await sender.SendAsync(inFlight);
            await receiver.Entered.Task.WaitAsync(Timings.DeliveryTimeout, Time);
            Task stopping = StopListener(listener);
            await NeverWithinAsync(() => stopping.IsCompleted, "the listener stops while an envelope is still in flight");

            gate.SetResult();
            await stopping.WaitAsync(Timings.DeliveryTimeout, Time);
            await sender.SendAsync(late);

            await NeverWithinAsync(() => receiver.DeliveriesOf(late.Id) > 0, "a stopped listener delivers an envelope");
            Assert.Equal(1, receiver.DeliveriesOf(inFlight.Id));
        }
        finally
        {
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task two_listeners_on_one_address_behave_as_the_transport_declares()
    {
        Uri endpoint = await CreateEndpointAsync();
        var first = new ContractReceiver();
        var second = new ContractReceiver();
        IListener firstListener = await ListenAsync(endpoint, first);
        ISender sender = NewSender(endpoint);

        if (Capabilities.SharedAddress == SharedAddressBehaviour.Rejected)
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => ListenAsync(endpoint, second));
            Envelope survivor = NewEnvelope(endpoint);

            await sender.SendAsync(survivor);

            await WaitUntilAsync(() => first.DeliveriesOf(survivor.Id) == 1, "the first listener still delivers");
        }
        else
        {
            await ListenAsync(endpoint, second);
            List<Envelope> sent = [.. Enumerable.Range(1, 10).Select(number => NewEnvelope(endpoint, number))];

            foreach (Envelope envelope in sent)
            {
                await sender.SendAsync(envelope);
            }

            await WaitUntilAsync(
                () => sent.All(envelope => first.DeliveriesOf(envelope.Id) + second.DeliveriesOf(envelope.Id) >= 1),
                "every envelope is delivered to one of the listeners");
            await NeverWithinAsync(
                () => sent.Any(envelope => first.DeliveriesOf(envelope.Id) + second.DeliveriesOf(envelope.Id) > 1),
                "an envelope is delivered to both listeners");

            // With the first listener gone the second must still be consuming.
            await StopListener(firstListener).WaitAsync(Timings.DeliveryTimeout, Time);
            Envelope after = NewEnvelope(endpoint, number: 11);
            await sender.SendAsync(after);
            await WaitUntilAsync(() => second.DeliveriesOf(after.Id) == 1, "the second listener delivers once the first is stopped");
        }
    }

    [Fact]
    public async Task concurrent_sends_to_one_endpoint_share_one_sender_that_is_disposed_once()
    {
        Uri endpoint = await CreateEndpointAsync();
        var receiver = new ContractReceiver();
        await ListenAsync(endpoint, receiver);
        var counting = new CountingTransport(Transport);
        var router = new TransportRouter([counting]);
        _disposables.Add(router);
        List<Envelope> sent = [.. Enumerable.Range(1, 32).Select(number => NewEnvelope(endpoint, number))];
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task[] sends =
        [
            .. sent.Select(envelope => Task.Run(async () =>
            {
                await go.Task;
                await router.SendAsync(envelope);
            })),
        ];

        go.SetResult();
        await Task.WhenAll(sends).WaitAsync(Timings.DeliveryTimeout, Time);

        await WaitUntilAsync(
            () => sent.All(envelope => receiver.DeliveriesOf(envelope.Id) == 1),
            "every envelope is delivered once");
        Assert.Equal(1, counting.SendersCreated);
        await router.DisposeAsync();
        Assert.Equal(1, counting.SendersDisposed);
    }

    [Fact]
    public async Task many_headers_and_every_optional_field_survive_the_transports_packing()
    {
        // Exists for SQS's limit of 10 message attributes: 25 custom plus 11 cabbie-* headers
        // must come back intact however the transport packs them (see EnvelopeHeaders).
        Uri endpoint = await CreateEndpointAsync();
        var receiver = new ContractReceiver();
        await ListenAsync(endpoint, receiver);
        var custom = new Dictionary<string, string>();
        foreach (int index in Enumerable.Range(1, 20))
        {
            custom[$"x-custom-{index:00}"] = $"value {index}";
        }

        custom["Mixed-Case-Name"] = "kept";
        custom["empty"] = "";
        custom["unicode"] = "ünïcödé ✓ 東京";
        custom["separators"] = "a=b; c,d: \"quoted\"";
        custom["long"] = new string('x', 1024);
        Envelope sent = NewEnvelope(
            endpoint,
            headers: new Headers(custom),
            attempts: 2,
            causation: new CausationId(Guid.NewGuid()),
            sagaId: new SagaId("order-7"),
            deliverBy: Time.GetUtcNow().AddMinutes(5));

        await NewSender(endpoint).SendAsync(sent);
        await WaitUntilAsync(() => receiver.Received.Count == 1, "the envelope is delivered");

        Envelope received = Assert.Single(receiver.Received);
        Assert.Equal(sent.Headers, received.Headers);
        Assert.Equal(sent.Id, received.Id);
        Assert.Equal(sent.CausationId, received.CausationId);
        Assert.Equal(sent.SagaId, received.SagaId);
        Assert.Equal(sent.SentAt, received.SentAt);
        Assert.Equal(sent.DeliverBy, received.DeliverBy);
        Assert.Equal(sent.Attempts, received.Attempts);
    }

    private static EnvelopeCodec CreateCodec()
    {
        var messageTypes = new MessageTypeCatalog();
        messageTypes.Register(typeof(ContractMessage));
        return new EnvelopeCodec(new JsonMessageSerializer(), messageTypes);
    }

    private static bool HasMessageId(TransportMessage message, EnvelopeId id) =>
        message.Headers.Any(header =>
            string.Equals(header.Key, EnvelopeHeaders.MessageId, StringComparison.OrdinalIgnoreCase)
            && header.Value == id.Value.ToString("D"));

    private async Task<IListener> ListenAsync(Uri endpoint, ContractReceiver receiver)
    {
        IListener listener = Transport.CreateListener(endpoint, receiver);
        await listener.StartAsync(CancellationToken.None);
        _running.Add(listener);
        return listener;
    }

    private Task StopListener(IListener listener)
    {
        _running.Remove(listener);
        Task stopping = listener.StopAsync(CancellationToken.None);
        _stopping.Add(stopping);
        return stopping;
    }

    private ISender NewSender(Uri endpoint)
    {
        ISender sender = Transport.CreateSender(endpoint);
        _disposables.Add(sender);
        return sender;
    }

    private Envelope NewEnvelope(
        Uri endpoint,
        int number = 1,
        Headers? headers = null,
        int attempts = 1,
        CausationId? causation = null,
        SagaId? sagaId = null,
        DateTimeOffset? deliverBy = null) =>
        new(
            new EnvelopeId(Guid.NewGuid()),
            new Message(new ContractMessage(number, $"message {number}")),
            MessageTypeNaming.For(typeof(ContractMessage)),
            new Destination(endpoint),
            new CorrelationId(Guid.NewGuid()),
            new ConversationId(Guid.NewGuid()),
            sagaId ?? new SagaId(""),
            new SentAt(Time.GetUtcNow()),
            new DeliverBy(deliverBy),
            headers ?? new Headers(),
            new ContentType(""),
            new Attempts(attempts),
            new EnvelopeData(),
            causation);

    private Task WaitUntilAsync(Func<bool> condition, string what) =>
        WaitUntilAsync(() => Task.FromResult(condition()), what);

    private Task WaitUntilAsync(Func<Task<bool>> condition, string what) =>
        Eventually.WaitUntilAsync(condition, Timings.DeliveryTimeout, Time, what);

    private Task NeverWithinAsync(Func<bool> condition, string what) =>
        NeverWithinAsync(() => Task.FromResult(condition()), what);

    private Task NeverWithinAsync(Func<Task<bool>> condition, string what) =>
        Eventually.NeverWithinAsync(condition, Timings.QuietWindow, Time, what);
}
