# Cabbie readiness for StoreBoost event-driven messaging

## 1. Purpose and inputs

What Cabbie needs before it can be StoreBoost's event-driven backbone on AWS SNS + SQS, and what StoreBoost needs to build to use it. Phase 1 runs integration events on SNS/SQS alongside AsyncMonolith. Phase 2 moves every AsyncMonolith consumer, cron and raw SQS poller to Cabbie and then deletes AsyncMonolith.

| Input | Value |
|---|---|
| Cabbie ref | `main` at `70867ca` (stack #87-#97 merged 2026-10-26; no tag, no release) |
| StoreboostServices ref | `main` at `0cea04c` |
| Date | 2026-10-26 |
| Decision record | [ADR-0003](https://github.com/Cloudandcompass/StoreboostServices/pull/7523) |
| Jira | Phase 1: [SP-2036](https://storeboost.atlassian.net/browse/SP-2036). Phase 2: [SP-2084](https://storeboost.atlassian.net/browse/SP-2084). |

Gap ids `G1`-`G19` are Cabbie work. `S1`-`S17` are StoreBoost work and are tracked in Jira and ADR-0003 only. Phase 1 means "required before the pilot". Phase 2 means "required to delete AsyncMonolith".

## 2. What StoreBoost runs today

### AsyncMonolith 10.0.0 (`AsyncMonolith` + `AsyncMonolith.PostgreSql`)

| Capability | How StoreBoost uses it | Where |
|---|---|---|
| Outbox | `IProducerService.Produce` inside handler-managed transactions on `ApplicationDbContext`, so the row commits with business data. About 38 `IConsumerPayload` types, 27+ consumers. | `Infrastructure/Scheduling/AsyncMonolithStartupExtensions.cs` |
| Retry and poison | Consumers derive from `MonitoringConsumerBase`, which reports to Sentry through `IExceptionNotifier` and rethrows. Failures end in `poisoned_messages`. | `Application/Common/Scheduling/MonitoringConsumerBase.cs` |
| Cron with tag and time zone | About 20 `IHostedService`s call `IScheduleService.Schedule(payload, cron6, tz, tag)`, idempotent by tag. Some are flag-gated and delete their rows when the flag is off. `DeleteByTag` is used by `MonitorSspApprovalConsumer` and the creative-approval gateways. | `Infrastructure/Hosting/*` |
| Delayed send | `Produce(payload, availableAfter, insertId)` | `Application/Features/PlacesOccupancy/Scheduling/PlacesOccupancyProviderTaskPoll.cs` |
| `insertId` dedup | Same call as above | same |
| Timeout and attempts attributes | Not used. No consumer uses `ConsumerExecutionMode` or `ConsumerRailId`. | |
| Consumer discovery | Re-implemented in `Application/Common/Scheduling/AsyncMonolithServiceCollectionExtensions.cs`. Wire names are coupled to CLR type names (SP-1609). | |
| Tables | `consumer_messages`, `poisoned_messages`, `scheduled_messages`, via `ApplicationDbContext.ConfigureAsyncMonolith()` | |
| Cross-instance guards | DB locks (`IAudienceDeliverySweepLock`, `IAudienceSegmentIngestionLock`) because cron ticks overlap | |

### Five hand-written SQS pollers

Same copy-pasted `BackgroundService` pattern: receive 10, wait 20 s, fixed 5 s backoff, `SqsBatchDelete`. No concurrency control and no visibility extension. None of the messages carries a Cabbie envelope.

| Service | Message source |
|---|---|
| `FootfallDeviceSimIngestBackgroundService` | IoT Rule |
| `FootfallDeviceSignalIngestBackgroundService` | IoT Rule, attribute `thing_name` |
| `FootfallDeviceSystemMetricsIngestBackgroundService` | IoT Rule |
| `FlightDcoPlayConfirmBackgroundService` | playout Lambda |
| `AudienceDeliveryIngestBackgroundService` | S3 ObjectCreated |

### Process model and surroundings

- One process runs HTTP, the AsyncMonolith processors and the pollers. It deploys to Elastic Beanstalk Docker from `Backend/Dockerfile` (includes ffmpeg and Chrome). Staging and production are separate AWS accounts. CDK apps: `iac/sample/src/Sample/BackendApps/StoreBoostApiStack.cs`, `iac/creative-editor-playout/`.
- No SNS in the API. `IAmazonSQS` is registered in `Infrastructure/Cloud/SqsStartupExtensions.cs`. LocalStack init (`Backend/local-dev/scripts/init-aws-new.sh`) creates SQS queues but no SNS.
- The custom mediator (`Infrastructure/Mediator/Mediator.cs`) has `Send` only. There are no domain events.
- Acting scope is `IActiveUserContext.Scope` (`TenantId`, `UserGroupId`), set from HTTP headers in `Api/Middleware/UserContextMiddleware.cs`. `UserContextScopeSeeder` seeds only `UserContextHolder`, not `ActiveUserContextHolder`. AsyncMonolith consumers carry ids in their payloads.
- Observability is Sentry. No OpenTelemetry, no health checks.
- Rules that constrain the integration: new Application code goes through ports; schema changes only via `dotnet ef migrations add`; `LayerDependencyTests` bans EF, Sentry, Refit and HTTP in Domain; `BannedSymbols.txt` bans `DateTime.UtcNow`.

## 3. What Cabbie already covers

| Feature | Status | Where |
|---|---|---|
| Handler DI with a scope per attempt | `main` | `src/Cabbie/Application/Execution/HandlerServices.cs`, `HandlerInvoker.cs` |
| Error policies, handler timeouts | `main` | `src/Cabbie/Application/Execution/ErrorPolicyCatalog.cs`, `Executor.cs` |
| Correlation, causation and conversation ids | `main` | `src/Cabbie/Domain/Envelope/Envelope.cs` |
| Per-handler fan-out isolation | `main` (#91) | `src/Cabbie/Application/Bus/FanOut.cs` |
| Inbox (`IInboxStore`) | `main` (#92) | `src/Cabbie/Application/Persistence/IInboxStore.cs`, `src/Cabbie.Postgresql/PostgresInboxStore.cs` |
| `sqs://` send and listen | `main` (#93) | `src/Cabbie.Aws/SqsTransport.cs`, `SqsListener.cs` |
| `sns://` publish (no listen) | `main` (#94) | `src/Cabbie.Aws/SnsTransport.cs` |
| Postgres durable delays (`DeliveryOptions.Delay/Until`) | `main` (#95) | `src/Cabbie.Postgresql/PostgresScheduledEnvelopeHold.cs` |
| EF outbox: `EfOutbox<TContext>.Publish`, `AddCabbieOutbox()`, `OutboxRelayService<TContext>` | `main` (#96) | `src/Cabbie.EntityFrameworkCore/` |
| NuGet packaging, release-please, getting-started sample | `main` (#97) | `.github/workflows/release-please.yml`, `src/*/*.csproj` |

The AWS and EF work reached `main` as the draft stack #87-#97, merged on 2026-10-26. No tag and no release exists. The NuGet ids `cabbie`, `cabbie.aws`, `cabbie.entityframeworkcore` and `cabbie.postgresql` return 404 on nuget.org, so they are free.

Verified on `main` and still missing: handler namespace scoping is not reachable from `CabbieOptions`; `HandlerConvention` matches `Handle`, `HandleAsync`, `Consume`, `ConsumeAsync`, `Start`, `StartAsync` by name; `CabbieOptions` exposes only `HandlerAssemblies`, `PublishMessage<T>()`, `ListenTo`, `OnException<T>`, `OnUnmatchedException`, `DefaultRetryDelays`, `HandlerTimeout` and `UseTransactionalOutbox`; wire names come from `[MessageIdentity]` or `FullName` only; SQS listeners have no visibility heartbeat, concurrency equals the receive batch (1-10) and one failing message backs off the whole listener; only unreadable messages go to `<queue>-dead-letter` and handler failures go to `IDeadLetterStore`; queues, topics and subscriptions must already exist; there is no trace propagation and no health checks; the scheduler has delays only, no cron; Postgres stores create schema in code (`PostgresSchema.EnsureAllAsync`), only the outbox has an EF mapping; the outbox relay has no `SKIP LOCKED` and no purge.

## 4. Gap register

| Id | Phase | Gap → required behaviour | StoreBoost evidence | Jira |
|---|---|---|---|---|
| G1 | 1 | Stack #87-#97 is merged to `main` (done 2026-10-26). Remaining: make `OutgoingDispatcher`, `MessageDelivery`, `Mediator` and the in-memory dead-letter write take `sentAt` from `TimeProvider` instead of `DateTimeOffset.UtcNow`. Confirm and fix the shared static `ActivityListener` flakes (`AttemptCorrelationTests`, `ObservabilityAttemptObserverTests`, `cabbie.failures` counter tests). | `DateTimeOffset.UtcNow` remains in `MessageDelivery.cs`, `Mediator.cs`, `OutgoingDispatcher.cs`, `InMemoryMessageStore.cs`. StoreBoost bans `DateTime.UtcNow` (`BannedSymbols.txt`). | [SP-2095](https://storeboost.atlassian.net/browse/SP-2095) |
| G2 | 1 | release-please tags `v0.1.0`. CI pushes `Cabbie`, `Cabbie.Aws`, `Cabbie.EntityFrameworkCore` and `Cabbie.Postgresql` to nuget.org with the secret `NUGET_API_KEY`. `Cabbie.RabbitMQ` and `Cabbie.Http` stay non-packable. | StoreBoost CI must restore from a public feed. | [SP-2096](https://storeboost.atlassian.net/browse/SP-2096) |
| G3 | 1 | `CabbieOptions.IncludeNamespace(string)` and `ExcludeNamespace(string)`, forwarding to `HandlerCatalog`. | The Application assembly holds hundreds of mediator `Handle` methods and AsyncMonolith `Consume` methods. | [SP-2097](https://storeboost.atlassian.net/browse/SP-2097) |
| G4 | 1 | `CabbieOptions.MessageIdentity<T>(string alias)`, equivalent to `[MessageIdentity]`. `CabbieOptions.RequireMessageIdentity()` fails host start when a type routed to a non-`local` destination has no alias. | Contracts stay free of Cabbie references. SP-1609 type-name coupling. | [SP-2098](https://storeboost.atlassian.net/browse/SP-2098) |
| G5 | 1 | `CabbieOptions.AddMiddleware<TMiddleware>(MiddlewareLayer layer)`, resolved from the per-attempt scope. | Sentry middleware and scope seeding (S3, S4). | [SP-2099](https://storeboost.atlassian.net/browse/SP-2099) |
| G6 | 1 | A scoped, multi-registration `IOutgoingEnvelopeEnricher.Enrich(Envelope)`, run for every envelope created by the bus, cascades, the scheduler and `EfOutbox.Publish`. `CabbieOptions.PropagateHeaders(params string[] names)` copies those headers and the correlation, conversation and causation ids from the incoming envelope to envelopes created inside a handler, including outbox ones. | Tenant and user-group propagation (S3). | [SP-2100](https://storeboost.atlassian.net/browse/SP-2100) |
| G7 | 1 | `IServiceCollection.AddCabbieAws(Action<CabbieAwsOptions>)` registers `SqsTransport` and `SnsTransport` as `ITransport` from the container's `IAmazonSQS` and `IAmazonSimpleNotificationService`. `CabbieOptions.Subscribe(string queueUri, string topicUri)` declares subscriptions. With `CabbieAwsOptions.AutoProvision = true` (default false), host start creates the queue, the DLQ with `RedrivePolicy` (`MaxReceiveCount`, default 5), the topic, the raw subscription and the queue policy. | Manual `new SqsTransport(...)` today; LocalStack dev. | [SP-2101](https://storeboost.atlassian.net/browse/SP-2101) |
| G8 | 1 | While a message is in flight, `ChangeMessageVisibility` to `VisibilityTimeout` every `VisibilityTimeout/2`, up to `MaxVisibilityExtension` (default 30 min). | PCA assembly and DSP calls exceed 30 s. | [SP-2102](https://storeboost.atlassian.net/browse/SP-2102) |
| G9 | 1 | `SqsTransportOptions.MaxConcurrency` (default 10) caps in-flight handlers independently of `MaxMessages`, and the listener receives as slots free. A per-message nack or fault no longer backs off the listener; only receive failures do. | Pollers today have no concurrency control. | [SP-2103](https://storeboost.atlassian.net/browse/SP-2103) |
| G10 | 1 | With `CabbieAwsOptions.DeadLetterToQueue = true`, a delivery from `sqs://q/` that exhausts its policy is written to `q-dead-letter` with `cabbie-dead-letter-reason` and the exception type and message, instead of to `IDeadLetterStore`. | One poison location, with a CloudWatch alarm and console redrive. Replaces `poisoned_messages`. | [SP-2104](https://storeboost.atlassian.net/browse/SP-2104) |
| G11 | 1 | Inject `traceparent` and `tracestate` into headers on send and extract them on receive. Producer span per send. Consumer span per receive, parenting `Cabbie.Handler.Attempt`. Tags `messaging.system` (`aws_sqs`/`aws_sns`), `messaging.destination.name`, `messaging.message.id` and `messaging.operation.type`. Meters `cabbie.messages.consumed` (counter), `cabbie.handler.duration` (histogram, seconds) and `cabbie.messages.in_flight` (up-down counter). | No trace or correlation today across async hops. | [SP-2105](https://storeboost.atlassian.net/browse/SP-2105) |
| G12 | 1 | Throw a typed `MessageTooLarge` before `SendMessage`/`Publish` when the body plus attributes exceed 262,144 bytes. The outbox relay dead-letters such rows. | SQS limit; StoreBoost payloads are ids. | [SP-2106](https://storeboost.atlassian.net/browse/SP-2106) |
| G13 | 1 | `ModelBuilder.AddCabbieInbox()` plus `AddCabbieEfInbox<TContext>()`, an EF-backed `IInboxStore`. | The repo rule says schema only via `dotnet ef`, so no `EnsureAllAsync`. | [SP-2107](https://storeboost.atlassian.net/browse/SP-2107) |
| G14 | 1 | The outbox relay claims rows with `FOR UPDATE SKIP LOCKED` and a lease, and `EfOutboxRelayOptions.SentRetention` (default 7 days) purges sent rows. | Multiple EB instances; unbounded table. | [SP-2108](https://storeboost.atlassian.net/browse/SP-2108) |
| G15 | 2 | `IRecurringSchedules`: `UpsertAsync(string tag, object message, string cron, TimeZoneInfo timeZone, CancellationToken)`, `DeleteAsync(string tag, CancellationToken)` and `ExistsAsync(string tag, CancellationToken)`. EF-mapped table `cabbie_recurring` (`AddCabbieRecurring()`), claims with `SKIP LOCKED`, 6-field cron with seconds via Cronos. | About 20 cron registrations; `DeleteByTag`. | [SP-2109](https://storeboost.atlassian.net/browse/SP-2109) |
| G16 | 2 | `ModelBuilder.AddCabbieScheduled()` plus an EF-backed `IScheduledEnvelopeHold`, and `DeliveryOptions.Tag` with `IMessageScheduler.CancelAsync(string tag)`. | `Produce(availableAfter)`; dynamic one-shots. | [SP-2110](https://storeboost.atlassian.net/browse/SP-2110) |
| G17 | 2 | `DeliveryOptions.DeduplicationKey`. A unique index on pending outbox and scheduled rows means a second publish with the same key is a no-op. | `insertId` in `PlacesOccupancyProviderTaskPoll`. | [SP-2111](https://storeboost.atlassian.net/browse/SP-2111) |
| G18 | 2 | `CabbieOptions.ListenToRaw<TMessage, TMapper>(string uri)` with `IRawMessageMapper<TMessage>.Map(RawSqsMessage)`. `RawSqsMessage` carries the body, message attributes and system attributes. `Map` returns the message, or null to dead-letter it as unreadable. | Five pollers fed by IoT Rule, S3 and Lambda. | [SP-2112](https://storeboost.atlassian.net/browse/SP-2112) |
| G19 | 2 | `IHealthChecksBuilder.AddCabbie()`. It reports Unhealthy when a listener has been in poll-error backoff longer than `UnhealthyAfter` (default 2 min), or when the oldest unsent outbox row is older than `OutboxLagThreshold` (default 5 min). | Worker environment health (S9). | [SP-2113](https://storeboost.atlassian.net/browse/SP-2113) |

## 5. AsyncMonolith parity matrix

| AsyncMonolith | Cabbie equivalent | Gap |
|---|---|---|
| `Produce` (transactional) | `EfOutbox<TContext>.Publish` | covered (#96); G14 for multi-instance safety |
| `Produce(availableAfter)` | `DeliveryOptions.Delay` + EF-mapped hold | G16 |
| `insertId` | `DeliveryOptions.DeduplicationKey` | G17 |
| `Schedule(cron, tz, tag)` / `DeleteByTag` | `IRecurringSchedules` | G15 |
| Consumer attempts / timeout attributes | Error policies + `[HandlerTimeout]` | covered |
| `poisoned_messages` | `<queue>-dead-letter` | G10 |
| `MonitoringConsumerBase` | Outer-layer middleware | G5 + S4 |
| Consumer discovery by payload type | Convention discovery scoped to `*.EventHandlers` | G3 |
| Wire name = CLR type name | `[MessageIdentity]` / fluent alias | G4 |

## 6. Not required

- **FIFO topics and queues:** no consumer relies on ordering, and the rail and execution-mode attributes are unused.
- **S3 claim check:** G12 guards; payloads are ids.
- **`SendMessageBatch` / `DeleteMessageBatch`:** one outbox row per transaction; revisit with G11 metrics.
- **Lambda consumer adapter:** rejected in ADR-0003.
- **Sagas, RabbitMQ and HTTP adapters:** no StoreBoost use.
- **EventBridge Scheduler and SQS `DelaySeconds`:** the Postgres scheduler covers delays.
- **Cross-instance exclusive execution:** StoreBoost keeps its DB locks.
- **Polymorphic serialization:** one topic per event type.
- **Postgres dead-letter store:** replaced by G10.

## 7. Topology contract

- One SNS topic per integration event: `sb-{identity}-{stage}`.
- One SQS queue per consuming endpoint: `sb-{endpoint}-{stage}`.
- A DLQ named `<queue>-dead-letter`, `maxReceiveCount` 5, 14-day retention, CloudWatch alarm on more than 0 visible messages.
- Subscriptions use `RawMessageDelivery=true`.
- Every name is lowercase and matches `^[a-z0-9-]{1,70}$`.
- IAM for the API/worker role. SQS: `ReceiveMessage`, `DeleteMessage`, `ChangeMessageVisibility`, `GetQueueUrl`, `GetQueueAttributes`, `SendMessage`. SNS: `Publish`, `ListTopics`.
- CDK owns AWS environments (`iac/messaging/`). G7 `AutoProvision` is for LocalStack only.

### Versioning

- Additive JSON changes only; System.Text.Json ignores unknown members.
- A breaking change gets a new identity (`campaign-published-v2`) and a new topic. Both are published until the consumers move.

## 8. Hosting summary

Recommended: a worker host from the same image, selected by `Messaging:Role` (`All|Api|Worker`). Phase 1 runs `Role=All` in the existing API environment, which is today's process model. Phase 2 adds a worker Elastic Beanstalk environment (needs G19). A Lambda SQS event source was rejected: it needs a new Cabbie Lambda adapter, RDS Proxy for connection fan-out, and cold starts of the ffmpeg/Chrome image with the full Application DI graph, which is a poor fit for Postgres-transactional handlers. See [ADR-0003](https://github.com/Cloudandcompass/StoreboostServices/pull/7523).
