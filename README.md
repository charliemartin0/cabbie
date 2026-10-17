# Hansom

Hansom takes its name from the Victorian London cab — small, quiet, and built to slip through heavy traffic. It is a Wolverine-shaped in-process message bus for .NET. Most .NET apps that need messaging end up choosing between two extremes: a thin MediatR with no opinion about retries, scheduling, or persistence, and a full MassTransit / NServiceBus / Wolverine stack that ships every transport, codegen, and integration at once. Hansom is the middle path. It borrows Wolverine's handler shape — catalog, in-process `Invoke`/`Publish`, cascading messages, envelope-based execution — and stops at the kernel. Broker adapters are separate packages. The onion architecture is enforced. Every slice ships with prove-with tests.

## Table of Contents

- [Features](#features)
- [Quickstart](#quickstart)
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
- **Handler discovery by convention** — `HandlerConvention` walks opted-in assemblies. No source generation.
- **Cached invoker** — one delegate dispatch per call. No reflection on the hot path.
- **Onion architecture enforced by project rules** — the kernel never sees Npgsql, Rabbit, or HTTP.
- **Prove-with tests for every slice** — xUnit, snake_case behaviour sentences, small fakes.

## Quickstart

```bash
dotnet build src/Hansom
dotnet test
dotnet run --project samples/Helpdesk/src/Helpdesk.Host
```

The Helpdesk sample runs against the in-memory kernel: instance saga, local queues, in-memory store. It is the smallest conversation Hansom can have. Everything in the roadmap is "more of the same, on real infrastructure."

## Architecture

Hansom is onion. The kernel does not know about brokers, databases, or HTTP. Adapters reference the kernel; the kernel never references adapters.

```
samples/Helpdesk ──┐
                   │
src/Hansom.Postgresql  src/Hansom.RabbitMQ  src/Hansom.Http
                   │
                   ▼
              src/Hansom
              ├── Domain         value objects, named recovery, envelope model
              ├── Application    ports, executor, mediator, discovery
              └── Infrastructure in-memory stores, hosting, local transport
```

| Layer | Lives in | May know | Must not |
| --- | --- | --- | --- |
| Domain | `src/Hansom/Domain/` | nothing outer | I/O, handlers, reflection catalogs, adapter packages |
| Application | `src/Hansom/Application/` | Domain | Npgsql, Rabbit, HTTP, EF |
| Infrastructure | `src/Hansom/Infrastructure/` | Application ports | adapter package refs |
| Adapters | `Hansom.{Postgresql, RabbitMQ, Http}` | Hansom core | leaking broker/DB types into core |

## Project Structure

```
Hansom/
  src/Hansom/
    Domain/                      Envelope, wire names, saga identity, named errors — no I/O
    Application/                 Invoke, cascades, routing, execution, sagas — ports only
    Infrastructure/              host, JSON, local queues, transport/persistence ports
  src/Hansom.Postgresql/         persistence adapter (shipped)
  src/Hansom.RabbitMQ/           broker adapter (scaffold)
  src/Hansom.Http/               HTTP front door (scaffold)
  samples/Helpdesk/src/
    Helpdesk.Domain/             entities / value objects — no Hansom, no infra
    Helpdesk.Application/        messages + handlers — Domain + Hansom
    Helpdesk.Infrastructure/     in-memory wiring — Application + Hansom
    Helpdesk.Host/               composition root
  tests/Hansom.Tests/            xUnit facts, small fakes
  tests/Helpdesk.Tests/          sample facts
```

`src/Hansom` does not `ProjectReference` the adapter projects. Npgsql stays in `Hansom.Postgresql`; Rabbit in `Hansom.RabbitMQ`; HTTP in `Hansom.Http`. The dependency direction is enforced by project rules, not by convention.

## Roadmap

The kernel ships. The broker adapters are the next milestones. Each is a slice: prove-with test first, then the smallest type that makes it pass.

1. **RabbitMQ adapter** — broker transport + durable subscriptions.
2. **HTTP adapter** — request/response across processes against the same execution pipeline.
3. **First-party logging middleware** — structured logs around handler invocation.
4. **Validator middleware** — pluggable handler-input validation.
5. **Outbox middleware** — kernel-level transactional outbox over the persistence ports.
6. **Polymorphic serialization** — discriminator-aware JSON for heterogeneous payloads.
7. **Observability polish** — OTel exporters, in-flight gauges, latency histograms beyond the static `ActivitySource`/`Meter`.
8. **1.0** — once the kernel is documented, one broker adapter is shipped, and the project reads clean to a newcomer.

## Inspiration

Hansom borrows the *shape* of [Wolverine](https://github.com/JasperFx/wolverine) by Jeremy Miller — handler catalog, envelope model, in-process `Invoke`/`Publish`, cascading messages. It does not clone Wolverine's catalog. There is no source generation, no Marten / EF integration, no in-core broker transports. The kernel stops at "Wolverine-shaped in-process" and refuses to grow beyond what the test suite proves.

## Contributing

One slice at a time. No stacked PRs. Every slice ships with prove-with tests. `src/Hansom` does not `ProjectReference` any adapter. Branch from `origin/main`, PR to `main`. The slice is the unit of progress, not the commit.

See `learning.md` for a guided tour through event-driven architecture using Hansom as the working example. See `AGENTS.md` and `.cursor/rules/` for the full set of project rules.

## Author

Charlie Martin — [@charliemartin0](https://github.com/charliemartin0) on GitHub.

## License

[MIT](LICENSE).
