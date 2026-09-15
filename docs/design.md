# Design and self-review

In short: three projects behind a Minimal API store one bid/ask row per directed pair in PostgreSQL, fetch a missing pair from Alpha Vantage and store it, and publish a creation event to RabbitMQ after the commit. The decisions I would defend are letting the database settle the insert race, rejecting rather than rounding excess price digits, and failing closed on any provider problem. The gaps I know about are no authentication, no quote expiry, and no outbox. The table at the end lists each with its consequence and the story that would close it.

## Request flow

Minimal API endpoints bind HTTP input into request records, call `RateService`, and map the result to a response record. Application logic validates the pair and prices, accesses storage through `IRateStore`, and requests missing quotes through `IExchangeRateProvider`. Infrastructure implements those contracts with EF Core, PostgreSQL, an HTTP client, and a RabbitMQ publisher.

The solution keeps API, application, and infrastructure code in separate projects. I chose an ASP.NET Core Minimal API host over an Azure Function App because the brief's fetch-on-miss endpoint, health probes, rate limiting, and OpenAPI document are all first-class in the web host, and because the target deployment is a container on a host I control rather than a consumption plan. The application project has no dependency on either host, so a Functions front end could be added later without touching the use cases. A mediator and generic repository would add indirection without useful behavior for five operations. `IRateStore` exposes the specific operations the use cases need and returns booleans for the expected "already there" and "not there" outcomes, so the service decides which error to raise. EF Core owns database mapping and migrations.

The API project has its own request and response records. The application's `Rate` and `RateInput` types never appear on the wire, so a field can be renamed in one layer without breaking the other. Strict price parsing is attached per property on the request records, not registered for every `decimal`.

A single composite key identifies the directed currency pair. Decimal arithmetic and PostgreSQL `numeric(18,8)` preserve the accepted price values. The digit limits live in one place, `PricePrecision`, and the parser, the validation rules, and the EF column mapping all read from it. The list query returns at most 56 rows, so pagination, an external cache, and a separate read database are unnecessary for this scope. Performance has not been benchmarked.

## Stored quotes

A lookup first queries PostgreSQL. If a record exists, it returns immediately regardless of age. Otherwise it calls the selected provider, validates the result, stores it, and returns it. The provider must return the requested pair with valid bid and ask values. Alpha Vantage responses with quota or information messages can use HTTP 200, so the adapter checks the JSON content as well as the HTTP status.

Provider calls have a bounded timeout and no automatic retry. A failure produces an error and no stored quote. Fake mode is an explicit configuration choice for local demos and tests. It is never a fallback for a failed real request.

### Freshness

There is no maximum age and no refresh on read. The brief says a rate found in the database is returned directly, so this follows the brief as written rather than adding a policy the brief did not ask for. I also did not want to invent a number. The right maximum age depends on facts I did not have: whether callers price trades or produce end-of-day reports, how much provider quota exists (a free key allows 25 requests a day, which makes any age under a day pointless across 56 pairs), and whether a provider refresh may overwrite a value a person entered by hand. A guessed TTL would look like a decision when it is not one.

If freshness became a requirement, the rule I would implement is FX-15 in the backlog: a configurable maximum age on provider-sourced rates only, a refresh when a lookup finds a stale row, the stale value with a warning header when the provider fails, and an explicit refresh endpoint for callers who need a fresh quote now. Manual rates stay exempt until someone says otherwise.

Manual updates replace current prices and source metadata. There is no quote history or automatic expiry. Deleting a quote is not a blocklist operation; a later lookup can recreate it.

## Concurrency

PostgreSQL prevents duplicate pairs. Concurrent cold lookups can both call Alpha Vantage, but only one inserts the pair. A lookup that loses the insert race reads the stored winner. Only a successful insert raises the creation event. If the winner is deleted between the failed insert and the follow-up read, the lookup returns 409 with a distinct "changed concurrently" title rather than pretending the caller tried to create a duplicate.

Updates use last-write-wins. There is no ETag or version check, so an overlapping update can overwrite another caller's values. I kept last-write-wins for the POC; editing workflows that need conflict detection would require a version check.

## Events

`IEventPublisher` separates application logic from transport. `RateCreated` includes an event ID, versioned event type, occurrence time, and the stored rate. Successful manual creation and provider insertion invoke the publisher; reads, updates, and deletes do not raise creation events.

`LoggingEventPublisher` writes the event to the log and is the default, so the application runs with no broker dependency, including under Compose. `RabbitMqEventPublisher` connects lazily on the first publish. It declares the durable topic exchange `fx-rates.events` and the durable queue `fx-rates.rate-created`, configurable through `Messaging__Queue`, then binds the queue with `rate.created.#`. It publishes with persistent delivery and publisher confirms, so a successful publish means the broker has accepted the message into the exchange and routed it to the declared queue. The routing key is the event type. Consumers can still bind their own queues, for example with `rate.created.*` to receive future versions. In a real system consumers own their queues; I included this queue so demo events are visible without manual binding. RabbitMQ connection fields are separate configuration values rather than an AMQP URI so passwords need no URL encoding.

Publishing happens after persistence and outside the database transaction. A broker failure loses the event; a process crash between the commit and the publish does the same. The HTTP caller still gets a success because the rate is stored, and the failure is logged with its stack trace. An outbox row written in the same transaction, drained by a background relay, would close that gap. Consumers would still need deduplication on `eventId` and an explicit retry policy. The interface itself promises no durable or exactly-once delivery.

## Error handling and logging

Expected failures are `FxException` values with an `ErrorKind`. The API's exception handler maps each kind to an HTTP status and returns the exception's message, which is written to be safe for callers. Anything else becomes a generic 500. Every 5xx logs the exception and stack trace to stdout with the same trace ID the caller received; the response body never includes exception text. Unexpected failures log at Error. Expected provider failures, such as a quota notice or a timeout, log at Warning so the Error stream only carries application faults. The exception handler middleware's own error log is disabled so each failure is written once.

HTTP client request logging is removed for the Alpha Vantage client because the provider puts the API key in the query string. Provider failures are still logged, without the URL.

## Testing approach

Unit tests exercise pair and price rules, the parser, application decisions, and provider parsing through controlled dependencies. A provider request test checks the Alpha Vantage request URI. Integration tests send HTTP requests to the API and use a real disposable PostgreSQL container. A rate-limiter integration test checks that requests exceeding the allowance return `429`. A separate integration test publishes through the RabbitMQ adapter to a disposable broker and consumes the message back. This checks status codes, persistence, migrations, serialization, and broker delivery without depending on external services.

Tests establish that stored quotes avoid provider calls, malformed or failed responses never persist, strict CRUD returns the documented errors, manual updates clear provider timestamps, an unexpected exception produces a generic 500 with a trace ID, and events reach the broker with the documented properties. Database uniqueness and concurrent insertion have database-backed tests. Direct database check-constraint tests are absent.

The real Alpha Vantage service and production deployment require separate smoke checks with configured credentials and infrastructure. A green local suite does not verify either.

## Assumptions I would revisit

Several decisions rest on assumptions about how the API would be used. They were reasonable for this brief, and a different product would change them.

- Eight supported currencies. A fixed allowlist keeps validation simple and bounds the table at 56 rows. A product with more markets would load the list from configuration or ISO 4217 and add pagination.
- One row per directed pair with no inversion. USD/EUR and EUR/USD are separate because a desk quotes them separately and the spreads differ. A reporting tool might accept a derived inverse.
- A manual update replaces the provider quote and clears its timestamp. This assumes a person's correction should win. A system that treats manual entries as temporary overrides would keep the provider quote and give the override an expiry.
- Delete is not a blocklist. A later lookup recreates the pair. Some products need "never quote this pair", which is a different operation.
- One shared request budget for every caller. Acceptable while the API is anonymous. Wrong as soon as there are tenants, and it would follow authentication.
- Ten integer and eight fractional digits, rejected rather than rounded. Right for fiat pairs. Crypto pairs need more fractional digits, and some callers would rather have rounding than a 400.

## Limits and improvements

| Current decision or limit | Consequence | Possible next step |
|---|---|---|
| Anonymous public writes | Any visitor can change or delete demo data | FX-12 authentication and authorization |
| Rates never expire | Stored values can become arbitrarily old | FX-15 freshness policy |
| One record per pair | Prior prices and authorship history are unavailable | Record quote history and authorship for rate changes |
| Last-write-wins | Concurrent edits can lose another edit | FX-17 optimistic concurrency |
| Concurrent cold fetches | Provider quota can be consumed by duplicate requests | Coalesce cold lookups for the same pair across replicas |
| Shared in-process request limit | Callers share one budget, and replicas have separate budgets | Share the request budget across replicas and apply caller-specific limits after authentication |
| Publish after commit, no outbox | A broker outage or a crash after commit loses the event | FX-10 outbox and relay worker |
| One channel per publish | Fine for tens of events; wasteful at thousands per second | Measure publish throughput before pooling channels or batching confirms |
| Provider format and eight-currency allowlist | Unexpected formats or unsupported currencies are rejected | Add a fallback provider and configure the supported currencies |
| One Compose deployment | Replacement can cause short downtime | Use a deployment platform with rolling updates |
| Database migrations | An older image may not understand a newer schema | FX-20 backup and expand-and-contract policy |
| No retry or circuit breaker on provider calls | One transient failure fails a cold lookup; a dead provider costs the full timeout each time | FX-11 resilience pipeline |
| No traces or metrics | Incidents are diagnosed from stdout | Trace API, database, provider, and publish calls; add latency and failure metrics |

The API still needs access control, a freshness policy, durable event delivery, and deployment recovery before production use. [The backlog](backlog.md) rates each follow-up story by value and effort and suggests an order.

Prices use ordinary decimal notation, without exponents. Raw JSON numbers and provider strings are checked before decimal parsing so excess digits cannot silently round into a valid value. Trailing fractional zeros are accepted. Responses remove trailing fractional zeros so a create of `0.91` and a later read both show `0.91`, while a provider value such as `0.91238513` retains all its digits. Timestamps are normalized to PostgreSQL microsecond precision so write responses match later reads.
