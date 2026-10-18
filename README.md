# Cabbie

Cabbie is named for the driver of the hansom, the Victorian London cab — small, quiet, and built to slip through heavy traffic. It is a Wolverine-shaped in-process message bus for .NET. Most .NET apps that need messaging end up choosing between two extremes: a thin MediatR with no opinion about retries, scheduling, or persistence, and a full MassTransit / NServiceBus / Wolverine stack that ships every transport, codegen, and integration at once. Cabbie is the middle path. It borrows Wolverine's handler shape — catalog, in-process `Invoke`/`Publish`, cascading messages, envelope-based execution — and stops at the kernel. Broker adapters are separate packages. The onion architecture is enforced. Every slice ships with prove-with tests.

## Table of Contents

- [Features](#features)
- [Quickstart](#quickstart)
- [Dependency injection](#dependency-injection)
- [Error policies](#error-policies)
- [Endpoints and transports](#endpoints-and-transports)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Roadmap](#roadmap)
- [Inspiration](#inspiration)
- [Contributing](#contributing)
- [Author](#author)
- [License](#license)

## Features

- **In-process message dispatch** — `Invoke` for request/response, `Publish` for fire-and-forget.
- **Cascading messages** — handler return values become new messages after the handler succeeds. Throwing handlers publish nothing.
- **Instance sagas** — long-running workflow state keyed by aggregate id, advanced by messages it cares about. `NotFound` swallows harmless late arrivals.
- **Scheduled envelopes** — `[Timeout(Minutes = 1)]` on a message, parked until `PlayDue(asOf)`. Tests fast-forward without `Task.Delay`.
- **Local queues** — in-process worker queues keyed by destination URI, behind the `local://` transport. `Pause`/`Resume` per queue, clean drain on host stop.
- **In-memory inbox, outbox, dead-letter, and saga stores** — ports defined in Application, in-memory implementation in Infrastructure. The PostgreSQL adapter fills the durable version.
- **JSON serialization** — `IMessageSerializer` port + `System.Text.Json` default, used at the transport boundary, content-type aware.
- **Fan-out isolation** — handlers for one message fail, retry and dead-letter independently; `Requeue` and `ScheduleRetry` re-run only the failed handler.
- **Transport selection by URI scheme** — `ITransport`/`ISender`/`IListener`/`IReceiver` ports, a scheme-keyed registry, `ListenTo` endpoints.
- **Observability hooks** — `ActivitySource`, `Meter`, per-attempt observers. No exporter baked in.
- **Handler discovery by convention** — `HandlerConvention` walks opted-in assemblies. No source generation. Handlers may take constructor dependencies; extra method parameters after the message resolve from a per-message DI scope.
- **Cached invoker** — one delegate dispatch per call. No reflection on the hot path.
- **Onion architecture enforced by project rules** — the kernel never sees Npgsql, Rabbit, or HTTP.
- **Prove-with tests for every slice** — xUnit, snake_case behaviour sentences, small fakes.

## Quickstart

```bash
dotnet build src/Cabbie
dotnet test
```

## Dependency injection

```csharp
builder.Services.AddScoped<IReceiptSender, LoggingReceiptSender>();
builder.UseCabbie(options => options.HandlerAssemblies.Add(typeof(PlaceOrder).Assembly));
// or, without a host builder: services.AddCabbie(options => ...)
```

Handlers take constructor dependencies, and extra method parameters after the message resolve from the container:

```csharp
public sealed class SendOrderReceiptHandler(IReceiptSender sender)
{
    public void Handle(SendOrderReceipt message, ILogger<SendOrderReceiptHandler> logger, CancellationToken ct)
    {
        sender.Send(message.Id);
        logger.LogInformation("Receipt sent for {OrderId}", message.Id);
    }
}
```

- One `AsyncServiceScope` per handler invocation attempt: the handler and its method services come from that scope, which is disposed on success, failure, or cancellation. A retry gets a fresh scope and a new handler instance, so a poisoned scoped service (an EF `DbContext`, say) never leaks into the next attempt. Fan-out handlers and concurrent messages each get their own scope.
- `AddCabbie` scans `HandlerAssemblies` and registers instance handlers as scoped, unless you already registered the type. A registration you add after `AddCabbie` wins (the container resolves the last one), including a factory. Handlers that were never registered (scanned later, or built by tests) are created with `ActivatorUtilities`, with the factory cached per type. Keyed registrations are ignored.
- `Envelope` and `CancellationToken` parameters are filled by Cabbie; `out`, `ref`, and `params` parameters are rejected when Cabbie is added.
- Startup check: before queues start, every handler method parameter, and every constructor parameter of a handler Cabbie builds from the container, must be a service the container knows. A gap fails host start with `HandlerDependencyMissing` naming the handler, parameter and service type. Which handlers Cabbie builds is decided when the host starts, from the finished service collection, so handlers you registered yourself (for example through a factory), before or after `AddCabbie`, have their constructors left alone. The check sees one level: a service that is registered but whose own dependency is not is not caught. Turn on `ValidateOnBuild` (`builder.UseDefaultServiceProvider(o => o.ValidateOnBuild = true)`, or `ServiceProviderOptions` when you build the provider yourself) to have the container check every registered service's dependency chain when the provider is built.
- Sagas: saga state is created and loaded by Cabbie, never resolved from the container. A saga needs a public parameterless constructor (checked at scan); take services as handler method parameters.
- A missing service found at runtime, directly or further down the dependency chain, surfaces as `HandlerFault` wrapping `HandlerDependencyMissing`. It is not retried and the message is not dead-lettered: no attempt can succeed until the registration is fixed, so retrying only delays the error and a dead-letter entry would record a configuration mistake as a poisoned message. Any other constructor exception propagates unchanged and follows the normal error policy.

## Error policies

```csharp
builder.UseCabbie(options =>
{
    options.OnException<IOException>().RetryWithCooldown(TimeSpan.FromMilliseconds(200)).Then.MoveToErrorQueue();
    options.OnUnmatchedException().Retry().Then.MoveToErrorQueue();
    options.HandlerTimeout = TimeSpan.FromSeconds(30);
});
```

- Matching: the closest exception type in the thrown exception's inheritance chain wins, so `OnException<IOException>()` also handles `FileNotFoundException`, and a rule for `FileNotFoundException` beats one for `IOException`. At the same distance, a rule scoped to the message type beats the global rule; that form, `OnException<TException, TMessage>()`, is on `ErrorPolicyCatalog`, not on `CabbieOptions`. Distance wins over scope: a global rule for a closer type beats a message-scoped rule for a base type (a global `FileNotFoundException` rule beats an `IOException` rule scoped to `ChargePayment`). The chosen chain runs in registration order, one action per attempt: attempt 1 takes the first action, attempt 2 the second, and a failure past the end of the chain is a `HandlerFault`.
- `HandlerDependencyMissing` is never retried or dead-lettered, whatever rule or default would match it (see [Dependency injection](#dependency-injection)).
- Default policy: applies to queue deliveries only. `InvokeAsync` never retries or dead-letters an exception that has no explicit rule: it fails once, immediately, with `HandlerFault`; an explicit `OnException` rule still applies to it. On a queue delivery, an exception no rule matches gets one retry per entry of `options.DefaultRetryDelays` (default 100ms, 500ms, 2s), then the dead-letter; set `DefaultRetryDelays = []` to dead-letter on the first failure. `OnUnmatchedException()` replaces that chain: the first call discards the built-in default and starts an empty chain, and every later call appends to it in call order (`DefaultRetryDelays` is then ignored). A first call with no actions means fail fast: an unmatched exception becomes a `HandlerFault` on the first attempt. Without an `IErrorQueue` (a bare `Executor`), `MoveToErrorQueue` throws `HandlerFault`.
- Timeouts: `options.HandlerTimeout` sets a per-attempt timeout for every handler; `[HandlerTimeout(milliseconds)]` on a handler class or method overrides it (method, then class, then global). There is no per-handler opt-out of the global value. Each attempt gets its own clock, covering the handler and Inner middleware but not Outer middleware or the cooldowns between attempts. The timeout is cooperative: the handler's `CancellationToken` is cancelled, so a handler without a `CancellationToken` parameter cannot observe it, and a handler that ignores the token and returns still succeeds. An attempt that was cancelled by the timeout fails with `HandlerTimedOut`, which derives from `TimeoutException`, so `OnException<TimeoutException>()` matches it and `OnException<HandlerTimedOut>()` matches it more specifically. Only cancellation of the caller's token is a cancellation: it propagates as `OperationCanceledException` and no policy runs. Any other `OperationCanceledException` (an HTTP client timeout, say) is an ordinary fault and goes through the policy. Retry cooldowns and timeouts run on the container's `TimeProvider` (`TimeProvider.System` unless you register one).
- Dead-letter: the write is awaited before `HandlerFault` is thrown. If the write itself fails, Cabbie logs an error (event id 1) and throws `HandlerFault` wrapping `DeadLetterWriteFailed`: its `InnerException` is the write error and its `OriginalFault` property is the original handler exception. This includes cancellation exceptions the store raises on its own (an SDK timeout, say); only a write interrupted because the caller's token was cancelled propagates as `OperationCanceledException`.

## Endpoints and transports

An endpoint is an absolute URI; its scheme picks the transport.

```csharp
builder.UseCabbie(options =>
{
    options.PublishMessage<PlaceOrder>().To("local://orders");
    options.ListenTo("local://orders");
});
```

- Defaults: `[LocalQueue("x")]` routes to `local://x/`, otherwise `local://{lowercased type name}/`. Local queues listen implicitly; `ListenTo` on a `local://` endpoint only starts the queue at host start.
- Registering a transport: `services.AddSingleton<ITransport, MyTransport>()`. A transport owns one scheme and creates the two halves of an endpoint:

```csharp
public interface ITransport { string Scheme { get; } ISender CreateSender(Uri address); IListener CreateListener(Uri address, IReceiver receiver); }
public interface ISender { ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default); }
public interface IListener { Uri Address { get; } Task StartAsync(CancellationToken ct); Task StopAsync(CancellationToken ct); }
public interface IReceiver { Task<ReceiveOutcome> ReceiveAsync(Envelope envelope, CancellationToken cancellationToken); }
```

- Startup failures: an explicit route or `ListenTo` whose scheme has no transport fails host start with `UnknownTransportScheme` (scheme, endpoint and registered schemes in the message). Two transports for one scheme fail with `DuplicateTransportScheme`. Every outbound send, including policy requeues and cascades, goes through the scheme-keyed router.
- Serialization: `IMessageSerializer` (default `JsonMessageSerializer`, `application/json`) runs only at the transport boundary, through `EnvelopeCodec` (`cabbie-*` headers plus the body; custom headers may not use the `cabbie-` prefix). `local://` skips it. Replace it with `services.AddSingleton<IMessageSerializer, MySerializer>()`.
- Outcomes: the listener acts on what `IReceiver.ReceiveAsync` returns, only after the executor finished.

| Outcome | Meaning | Transport does |
|---|---|---|
| `Ack` | Handled, or the error policy resolved the fault (dead-lettered, discarded, requeued, parked for scheduled retry) | Delete the envelope |
| `Nack` | Fault the policy did not resolve | Make it available for redelivery |
| `Defer` | Cancelled before the executor finished (host stopping) | Release it without counting a failure |

- A policy `Requeue(delay...)` re-sends the envelope as the next attempt for the failed handler only (the `cabbie-handler` header names it), after the policy delay, and acks the original. The local transport has no redelivery: a `Nack` is logged and the envelope dropped, as before.
- Delivery is at-least-once, deduplicated by the inbox: `IInboxStore` records each (envelope id, handler) pair, so a redelivered envelope never re-runs a handler that already finished. A cascade send failure after handler success leaves the handler marked done; the redelivery retries only the cascades (held in process memory, lost on restart). A handler that faults, is cancelled, or hands off to a `Requeue`/`ScheduleRetry` attempt is released for the next attempt.
- Stays in-process: `InvokeAsync`, tracked sessions, the scheduled hold and outbox staging. These never write to the inbox; only envelopes delivered by a transport do. A handler running in an outbox transaction is marked processed only after that transaction commits. The in-memory inbox forgets processed entries after one hour or beyond 100,000 entries (oldest first).
- Envelope validation no longer restricts destination schemes (it was a closed `local`/`tcp`/`rabbitmq` list); the registered transports decide.

### SQS

`Cabbie.Aws` owns the `sqs` scheme: `new SqsTransport(IAmazonSQS client, EnvelopeCodec codec, SqsTransportOptions? options = null, ILoggerFactory? loggerFactory = null)`, registered like any `ITransport`.

- Address: `sqs://<queue-name>/`. The name is the URI host, so it is lowercase (1-70 letters, digits, hyphens). The queue and its dead-letter queue `<queue-name>-dead-letter` must already exist; the transport creates nothing and never disposes the client you give it. `StartAsync` on a missing queue throws the SDK's `QueueDoesNotExistException`.
- Headers: SQS allows 10 message attributes, so every header (the `cabbie-*` ones and custom ones) travels as one JSON object in the `cabbie-headers` attribute. The body is sent as text when it is valid SQS text, otherwise as base64 with `cabbie-body-encoding = base64`.

| Outcome | SQS call |
|---|---|
| `Ack` | `DeleteMessage` |
| `Nack` | `ChangeMessageVisibility` to `NackDelay` (default 5s): redelivered after that |
| `Defer` | `ChangeMessageVisibility` to 0: visible again at once |

- A failed receive (any exception except cancellation) is logged and retried after `PollErrorDelay` (default 5s), doubling per consecutive failure up to `MaxPollErrorDelay` (default 1 min); polling never stops until `StopAsync`. `SqsTransport` takes an optional `TimeProvider` for that pause.
- `VisibilityTimeout` (default 30s) is sent with every receive. A handler that runs longer than it is redelivered while still running; the inbox dedupes, and there is no visibility heartbeat yet. A listener that dies mid-flight has its messages redelivered after the same timeout.
- An unreadable message is never nacked: it is copied to the dead-letter queue with its body and attributes plus `cabbie-dead-letter-reason`, and the source message is deleted only after that write succeeds. If the dead-letter write fails, the source stays and is retried after the visibility timeout.
- `StopAsync` stops polling and waits for the messages in flight; cancelling its token abandons the wait and cancels the receiver (those messages are released). Several listeners on one queue compete: each message goes to one of them.
- LocalStack tests (`tests/Cabbie.Aws.Tests`) need Docker and are not part of `Cabbie.slnx`: `dotnet test tests/Cabbie.Aws.Tests`.

### SNS

`Cabbie.Aws` also owns the `sns` scheme: `new SnsTransport(IAmazonSimpleNotificationService client, EnvelopeCodec codec)`. It is publish-only: `CreateListener` throws `NotSupportedException`. Subscribers are SQS queues, listened to with `sqs://`.

- Address: `sns://<topic-name>/` (same host rules as `sqs://`). The topic must already exist: the sender looks it up by name (listing topics) and a missing one throws the SDK's `NotFoundException`; a failed lookup is retried by the next send. The transport creates no topics, subscriptions or queue policies and never disposes the client you give it.
- Fan-out: subscribe each queue with protocol `sqs` and `RawMessageDelivery=true`. Without raw delivery SNS wraps the body in its own JSON envelope and the listener dead-letters it. On AWS the queue policy must also allow `sns:SendMessage` from the topic.
- Headers: packed exactly as for SQS (one `cabbie-headers` attribute, base64 body when needed), so every subscribed listener sees the same headers and body. A topic with no subscriptions drops the publish without error.

## Architecture

Cabbie is onion. The kernel does not know about brokers, databases, or HTTP. Adapters reference the kernel; the kernel never references adapters.

```
src/Cabbie.Postgresql  src/Cabbie.RabbitMQ  src/Cabbie.Http  src/Cabbie.Aws
                   │
                   ▼
              src/Cabbie
              ├── Domain         value objects, named recovery, envelope model
              ├── Application    ports, executor, mediator, discovery
              └── Infrastructure in-memory stores, hosting, local transport
```

| Layer | Lives in | May know | Must not |
| --- | --- | --- | --- |
| Domain | `src/Cabbie/Domain/` | nothing outer | I/O, handlers, reflection catalogs, adapter packages |
| Application | `src/Cabbie/Application/` | Domain | Npgsql, Rabbit, HTTP, EF |
| Infrastructure | `src/Cabbie/Infrastructure/` | Application ports | adapter package refs |
| Adapters | `Cabbie.{Postgresql, RabbitMQ, Http, Aws}` | Cabbie core | leaking broker/DB types into core |

## Project Structure

```
Cabbie/
  src/Cabbie/
    Domain/                      Envelope, wire names, saga identity, named errors — no I/O
    Application/                 Invoke, cascades, routing, execution, sagas — ports only
    Infrastructure/              host, JSON, local queues, transport/persistence ports
  src/Cabbie.Postgresql/         persistence adapter (shipped)
  src/Cabbie.RabbitMQ/           broker adapter (scaffold)
  src/Cabbie.Http/               HTTP front door (scaffold)
  src/Cabbie.Aws/                AWS adapter: SQS and SNS transports (shipped)
  tests/Cabbie.Aws.Tests/        SQS and SNS against LocalStack (Docker): SQS runs the shared contract suite
  tests/Cabbie.Tests/            xUnit facts, small fakes
```

`src/Cabbie` does not `ProjectReference` the adapter projects. Npgsql stays in `Cabbie.Postgresql`; Rabbit in `Cabbie.RabbitMQ`; HTTP in `Cabbie.Http`; the AWS SDK in `Cabbie.Aws`. The dependency direction is enforced by project rules, not by convention.

## Roadmap

Goal: replace AsyncMonolith in StoreboostServices with Cabbie on AWS SQS/SNS. Done: Hansom → Cabbie rename.

A minimal getting-started sample comes with NuGet packaging (item 7).

1. **Handler DI** — constructor injection with a scope per message (fresh scope per retry); discover `Consume` inherited from generic base classes.
2. **Error policy fixes** — Done: base-type exception matching, a default retry/dead-letter policy, handler timeouts, an awaited dead-letter write. See [Error policies](#error-policies).
3. **Transport selection** — Done: endpoints are URIs, the scheme picks the transport through a registry, unknown schemes fail host start, and `ListenTo` listeners are started and stopped by the host. See [Endpoints and transports](#endpoints-and-transports).
4. **Transport contract suite** — Done: `tests/Cabbie.Transports.Contracts` holds one abstract xUnit suite (`TransportContract`) every transport subclasses with a small fixture: round trip, ack, defer, nack, unreadable payload dead-lettered, listener stop, shared addresses, sender lifecycle and header packing. Transports declare the behaviour they may differ on (`TransportCapabilities`) and their wait times (`TransportContractTimings`). Run today against `local://` through the serialising path; `Cabbie.Aws` reuses it. The duplicate-delivery check waits for the inbox (item 5).
5. **Handler isolation + inbox** — 5a Done: each handler in a fan-out runs, fails, retries and dead-letters on its own; a failure in one never skips or re-runs the others, and a policy `Requeue` or `ScheduleRetry` re-runs only the failed handler as the next attempt, after the policy delay. 5b Done: the inbox gives per-(envelope, handler) idempotency (in-memory and `Cabbie.Postgresql` stores), so a redelivery no longer re-runs handlers that already succeeded.
6. **`Cabbie.Aws`** — 6a Done: the `sqs://` transport (`Cabbie.Aws`), tested against LocalStack (Testcontainers) with the shared contract suite. See [SQS](#sqs). 6b Done: the `sns://` publish transport with fan-out to raw-delivery SQS subscriptions, tested on LocalStack. See [SNS](#sns). Topology provisioning (topics, subscriptions, queue policies) stays with the caller.
7. **Durable scheduler**, then an **EF-transactional outbox + relay**, then **packaging, versioning and CI** so Storeboost can consume Cabbie, then the **Storeboost cutover** behind an `IProducerService`/`IScheduleService`-shaped facade.

3 → 4 → 5 run in that order (they share `MessageDelivery` and host registration). 6 builds on 3–5.

### Later / maybe

- RabbitMQ adapter (broker transport + durable subscriptions).
- HTTP adapter (request/response across processes).
- First-party logging and validator middleware.
- Polymorphic serialization (discriminator-aware JSON).
- Observability polish (OTel exporters, in-flight gauges, latency histograms).

### Open decisions

- **Build vs adopt:** keep building Cabbie, or adopt Wolverine (which ships SQS/SNS, outbox and scheduling) for the Storeboost replacement.
- **Delays:** a Postgres-backed scheduler or EventBridge Scheduler (SQS caps delays at 15 minutes; SNS has none).
- **Outbox:** an EF-joined `Cabbie.EntityFrameworkCore`, or sharing the Npgsql connection and transaction with EF.
- **"Wolverine-shaped":** a design constraint, or only inspiration.

## Inspiration

Cabbie borrows the *shape* of [Wolverine](https://github.com/JasperFx/wolverine) by Jeremy Miller — handler catalog, envelope model, in-process `Invoke`/`Publish`, cascading messages. It does not clone Wolverine's catalog. There is no source generation, no Marten / EF integration, no in-core broker transports. The kernel stops at "Wolverine-shaped in-process" and refuses to grow beyond what the test suite proves.

## Contributing

One intent per PR, submitted as Graphite draft stacks (`gt submit --stack --draft`) with conventional-commit titles. Every slice ships with behaviour tests. `src/Cabbie` does not `ProjectReference` any adapter. The build treats analyzer warnings as errors. The slice is the unit of progress, not the commit.

See `learning.md` for a guided tour through event-driven architecture using Cabbie as the working example. See `AGENTS.md` and `.cursor/rules/` for the full set of project rules.

## Author

Charlie Martin — [@charliemartin0](https://github.com/charliemartin0) on GitHub.

## License

[MIT](LICENSE).
