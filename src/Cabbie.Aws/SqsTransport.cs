using Amazon.SQS;
using Cabbie.Application.Transports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cabbie.Aws;

/// <summary>
/// The <c>sqs://</c> transport. An address names an existing queue (<c>sqs://orders/</c>) and its
/// dead-letter queue (<c>orders-dead-letter</c>); neither is created here. The caller owns the
/// <see cref="IAmazonSQS"/> client: the transport never disposes it.
/// </summary>
public sealed class SqsTransport : ITransport
{
    private readonly IAmazonSQS _client;
    private readonly EnvelopeCodec _codec;
    private readonly SqsTransportOptions _options;
    private readonly ILogger _logger;

    public SqsTransport(
        IAmazonSQS client,
        EnvelopeCodec codec,
        SqsTransportOptions? options = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(codec);
        _client = client;
        _codec = codec;
        _options = options ?? new SqsTransportOptions();
        _options.Validate();
        _logger = loggerFactory?.CreateLogger<SqsTransport>() ?? NullLogger<SqsTransport>.Instance;
    }

    public string Scheme => SqsEndpoint.Scheme;

    public ISender CreateSender(Uri address)
    {
        SqsEndpoint endpoint = SqsEndpoint.Parse(address);
        return new SqsSender(_client, _codec, endpoint);
    }

    public IListener CreateListener(Uri address, IReceiver receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        SqsEndpoint endpoint = SqsEndpoint.Parse(address);
        return new SqsListener(address, endpoint, _client, _codec, receiver, _options, _logger);
    }
}
