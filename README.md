# Cabbie

Cabbie is named for the driver of the hansom, the Victorian London cab — small, quiet, and built to slip through heavy traffic. It is a Wolverine-shaped in-process message bus for .NET. Most .NET apps that need messaging end up choosing between two extremes: a thin MediatR with no opinion about retries, scheduling, or persistence, and a full MassTransit / NServiceBus / Wolverine stack that ships every transport, codegen, and integration at once. Cabbie is the middle path. It borrows Wolverine's handler shape — catalog, in-process `Invoke`/`Publish`, cascading messages, envelope-based execution — and stops at the kernel. Broker adapters are separate packages. The onion architecture is enforced. Every slice ships with prove-with tests.

## Table of Contents

- [Features](#features)
- [Quickstart](#quickstart)
- [Dependency injection](#dependency-injection)
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
- **Local queues** — in-process worker queues keyed by destination URI. `Pause`/`Resume` per queue, clean drain on host stop.
- **In-memory inbox, outbox, dead-letter, and saga stores** — ports defined in Application, in-memory implementation in Infrastructure. The PostgreSQL adapter fills the durable version.
- **JSON serialization** — `ISerializer` port + `System.Text.Json` implementation, content-type aware.
- **Observability hooks** — `ActivitySource`, `Meter`, per-attempt observers. No exporter baked in.
- **Handler discovery by convention** — `HandlerConvention` walks opted-in assemblies. No source generation. Handlers may take constructor dependencies; extra method parameters after the message resolve from a per-message DI scope.
- **Cached invoker** — one delegate dispatch per call. No reflection on the hot path.
- **Onion architecture enforced by project rules** — the kernel never sees Npgsql, Rabbit, or HTTP.
- **Prove-with tests for every slice** — xUnit, snake_case behaviour sentences, small fakes.

## Quickstart

```bash
dotnet build src/Cabbie
dotnet test
dotnet run --project samples/Helpdesk/src/Helpdesk.Host
```

The Helpdesk sample runs against the in-memory kernel: instance saga, local queues, in-memory store. It is the smallest conversation Cabbie can have. Everything in the roadmap is "more of the same, on real infrastructure."

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
- `AddCabbie` scans `HandlerAssemblies` and registers instance handlers as scoped. Handlers that were never registered (scanned later, or built by tests) are created with `ActivatorUtilities`, with the factory cached per type.
- `Envelope` and `CancellationToken` parameters are filled by Cabbie; `out`, `ref`, and `params` parameters are rejected when Cabbie is added.
- Startup check: before queues start, every handler method parameter, and every constructor parameter of a handler Cabbie builds from the container, must be a service the container knows. A gap fails host start with `HandlerDependencyMissing` naming the handler, parameter and service type. Handlers you registered yourself (for example through a factory) have their constructors left alone.
- Sagas: saga state is created and loaded by Cabbie, never resolved from the container. A saga needs a public parameterless constructor (checked at scan); take services as handler method parameters.
- A missing service found at runtime surfaces as `HandlerFault` wrapping `HandlerDependencyMissing`. It is not retried, since another attempt cannot succeed. Any other constructor exception propagates unchanged.

## Architecture

Cabbie is onion. The kernel does not know about brokers, databases, or HTTP. Adapters reference the kernel; the kernel never references adapters.

```
samples/Helpdesk ──┐
                   │
src/Cabbie.Postgresql  src/Cabbie.RabbitMQ  src/Cabbie.Http
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
| Adapters | `Cabbie.{Postgresql, RabbitMQ, Http}` | Cabbie core | leaking broker/DB types into core |

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
  samples/Helpdesk/src/
    Helpdesk.Domain/             entities / value objects — no Cabbie, no infra
    Helpdesk.Application/        messages + handlers — Domain + Cabbie
    Helpdesk.Infrastructure/     in-memory wiring — Application + Cabbie
    Helpdesk.Host/               composition root
  tests/Cabbie.Tests/            xUnit facts, small fakes
  tests/Helpdesk.Tests/          sample facts
```

`src/Cabbie` does not `ProjectReference` the adapter projects. Npgsql stays in `Cabbie.Postgresql`; Rabbit in `Cabbie.RabbitMQ`; HTTP in `Cabbie.Http`. The dependency direction is enforced by project rules, not by convention.

## Roadmap

Goal: replace AsyncMonolith in StoreboostServices with Cabbie on AWS SQS/SNS. Done: Hansom → Cabbie rename.

1. **Handler DI** — constructor injection with a scope per message (fresh scope per retry); discover `Consume` inherited from generic base classes.
2. **Error policy fixes** — base-type exception matching, a default retry/poison policy, a per-handler timeout, an awaited dead-letter write. Can run in parallel with 1.
3. **Transport selection** — route by URI scheme (`sqs://`, `sns://`) through a transport registry; unknown schemes fail loudly; a listener port the host starts and stops.
4. **Transport contract suite** — one shared test suite every transport must pass (round trip, duplicates, redelivery, dead-letter), proven first against a serialising loopback transport.
5. **Handler isolation + inbox** — one failing handler no longer skips the others; redelivery re-runs only the failed handler; the inbox gives per-(envelope, handler) idempotency.
6. **`Cabbie.Aws`** — SQS send/receive, then SNS fan-out and topology. Tested against LocalStack (Testcontainers).
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

One slice at a time. No stacked PRs. Every slice ships with prove-with tests. `src/Cabbie` does not `ProjectReference` any adapter. Branch from `origin/main`, PR to `main`. The slice is the unit of progress, not the commit.

See `learning.md` for a guided tour through event-driven architecture using Cabbie as the working example. See `AGENTS.md` and `.cursor/rules/` for the full set of project rules.

## Author

Charlie Martin — [@charliemartin0](https://github.com/charliemartin0) on GitHub.

## License

[MIT](LICENSE).
