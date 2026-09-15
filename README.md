# FX rates API

[![Build, test, and deploy](https://github.com/adamtrip/fx-rates-api/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/adamtrip/fx-rates-api/actions/workflows/ci-cd.yml)

A .NET 10 POC for managing one bid/ask quote per currency pair. The API stores rates in PostgreSQL, fetches missing pairs from Alpha Vantage, and publishes a `rate.created.v1` event to RabbitMQ whenever a new pair is stored. Existing records stay valid until someone updates or deletes them.

If you have ten minutes, start here:

- [RateService.cs](src/FxRates.Application/Rates/RateService.cs): the lookup reads PostgreSQL first, calls the provider on a miss, and lets the primary key settle concurrent inserts.
- [AlphaVantageProvider.cs](src/FxRates.Infrastructure/Providers/AlphaVantageProvider.cs): quota notices arrive as HTTP 200 and become 503; the API key never reaches logs or error messages.
- [RatesApiTests.cs](tests/FxRates.IntegrationTests/Api/RatesApiTests.cs): CRUD, price boundaries, and eight concurrent cold lookups against a real PostgreSQL container.
- [ci-cd.yml](.github/workflows/ci-cd.yml): test, publish images, and deploy only after both succeed.
- [docs/design.md](docs/design.md): decisions, assumptions, limits, and what I would do next.

Full documentation:

- [Design and self-review](docs/design.md): architecture, tradeoffs, assumptions, limitations, and what I would do next.
- [Backlog](docs/backlog.md): user stories, tasks, priorities, and estimates.
- [Verification](docs/verification.md): checks run on the local build and what remains unverified.
- [Deployment](docs/deployment.md): CI/CD pipeline, GHCR images, self-hosted runner, and tunnel.
- [Request examples](requests.http): every operation and the common error cases.

The application has no authentication. Anyone who can reach it can create, replace, or delete rates. I chose to leave authentication out of this demo.

## Run with Docker

The Compose setup requires Docker with Docker Compose but no local .NET SDK.

```sh
cp .env.example .env
```

If `.env` already exists, edit it rather than copying over it. Changing `POSTGRES_PASSWORD` or `RABBITMQ_PASSWORD` does not change the password inside an existing volume.

Edit `.env`. Set `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD` to local passwords without semicolons, and set `EXCHANGE_PROVIDER_MODE=Fake` for a demo without provider credentials. Then run:

```sh
docker compose up --build -d --wait
docker compose ps
```

Compose starts PostgreSQL and RabbitMQ and applies EF Core migrations with a one-shot migration container. The API waits for PostgreSQL and successful migrations, but does not wait for RabbitMQ to be healthy. `--wait` returns only when every container reports healthy, so the first lookup's event has a broker to reach. Without it, the API can come up seconds before RabbitMQ and that first event is logged as lost. Open the [interactive documentation](http://localhost:5080/docs). The fake provider produces synthetic values; they are not market data.

For real quotes, set `EXCHANGE_PROVIDER_MODE=AlphaVantage` and `ALPHA_VANTAGE_API_KEY` in `.env`, then run `docker compose up -d` again. Never commit `.env` or an API key. A free Alpha Vantage key allows 25 requests per day. Each cold lookup of a new pair spends one request, and two concurrent cold lookups of the same pair spend two. When the quota is gone, missing pairs return `503` until the next day; stored pairs keep working. Changing providers does not refresh existing database records. Delete a stored pair before requesting it if you want a quote from the newly selected provider.

```sh
curl http://localhost:5080/api/rates/USD/EUR
curl http://localhost:5080/api/rates
```

The first command fetches and stores a missing pair and publishes an event. Repeating it returns the stored record. [requests.http](requests.http) contains examples for all operations and common errors.

The RabbitMQ management UI is at [localhost:15672](http://localhost:15672) with the user `fxrates` and the password from `.env`. The publisher automatically declares the durable queue `fx-rates.rate-created` on its first connection and binds it to `fx-rates.events` with routing key `rate.created.#`, so events appear there without manual binding. Set `MESSAGING_MODE=Logging` to write events only to the API log. This also works under Compose: the broker container still starts, but the API does not depend on it.

```sh
docker compose logs --tail=100 api migrate
docker compose down
```

`down` preserves database and broker data; adding `--volumes` deletes both. RabbitMQ has a fixed hostname so a recreated container finds its queues in the volume. The API binds to `127.0.0.1:5080` by default. PostgreSQL and AMQP have no published host port.

## Develop and test

Install the .NET 10 SDK specified by [global.json](global.json). Docker must be running for integration tests, which start PostgreSQL and RabbitMQ containers.

```sh
scripts/check.sh
```

CI runs restore, format check, build, and tests. `scripts/check.sh` also checks for pending migrations, validates Compose and deployment shell syntax, and lints the workflow when `actionlint` is installed. The CI checks can be run individually:

```sh
dotnet restore FxRates.slnx
dotnet format FxRates.slnx --no-restore --verify-no-changes
dotnet build FxRates.slnx --no-restore
dotnet test FxRates.slnx --no-build
```

Unit tests cover application rules, price parsing, and provider response handling. Integration tests send HTTP requests to the API with a disposable PostgreSQL container and controlled provider responses, and publish to a disposable RabbitMQ container. They need no Alpha Vantage key and never call the real provider. Docker failure is a test failure, not a silently skipped suite.

`dotnet format` fixes whitespace and style differences in place; run it without `--verify-no-changes` before committing.

### Run from an IDE or `dotnet run`

The `Development` environment is preconfigured in `src/FxRates.Api/appsettings.Development.json`: fake provider, logging publisher, plain-text logs, and a connection string that expects the PostgreSQL password `fxrates-dev` on `localhost:5432`. The launch profile in `Properties/launchSettings.json` selects that environment and opens `/docs` on port 5080.

Start PostgreSQL with its port published, then run the API:

```sh
# Use POSTGRES_PASSWORD=fxrates-dev in .env for this example.
cat > compose.override.yml <<'YAML'
services:
  postgres:
    ports:
      - "127.0.0.1:5432:5432"
YAML
docker compose up -d postgres
dotnet tool restore
dotnet ef database update --project src/FxRates.Infrastructure
dotnet run --project src/FxRates.Api
```

`compose.override.yml` is ignored by Git. The EF migration command uses the local PostgreSQL default above without environment variables; `ConnectionStrings__Rates` overrides it. The API does not migrate the database on startup. Stop the Compose API container first if it already occupies port 5080.

Any setting can be overridden with an environment variable, using `__` as the section separator:

| Setting | Example or default |
|---|---|
| `ConnectionStrings__Rates` | `Host=localhost;Port=5432;Database=fxrates;Username=fxrates;Password=<local-password>` |
| `ExchangeProvider__Mode` | `Fake` or `AlphaVantage` (case-insensitive) |
| `ExchangeProvider__ApiKey` | Required for `AlphaVantage` |
| `ExchangeProvider__TimeoutSeconds` | `10` |
| `Messaging__Mode` | `Logging` or `RabbitMq` |
| `Messaging__Host`, `__Port`, `__UserName`, `__Password`, `__VirtualHost` | Required for `RabbitMq`; port defaults to `5672`, virtual host to `/` |
| `Messaging__Exchange` | `fx-rates.events` |
| `Messaging__Queue` | `fx-rates.rate-created` |
| `RateLimiting__Enabled` | `true` |
| `RateLimiting__PermitLimit` | `60` |
| `RateLimiting__WindowSeconds` | `60` |
| `ASPNETCORE_URLS` | `http://localhost:5080` |

## Project layout

```text
src/FxRates.Application    Use cases and rules. No framework or I/O dependencies.
  Rates/                   Rate records, price rules and parsing, RateService
  Events/                  RateCreated and IEventPublisher
  Providers/               IExchangeRateProvider
  Storage/                 IRateStore
  Errors/                  ErrorKind and FxException
src/FxRates.Infrastructure Adapters for the Application contracts.
  Persistence/             EF Core context, entity, store, and Migrations/
  Providers/               Alpha Vantage and fake providers with their options
  Messaging/               RabbitMQ and logging publishers with their options
src/FxRates.Api            HTTP hosting.
  Contracts/               Request and response records, strict price converter
  Endpoints/               Minimal API route group
  ErrorHandling/           Problem Details exception handler
  RateLimiting/            Fixed-window limiter settings and setup
tests/FxRates.UnitTests    Application/ and Infrastructure/ tests with in-memory doubles
tests/FxRates.IntegrationTests
  Api/                     Hosted API against a PostgreSQL container
  Messaging/               RabbitMQ publisher against a broker container
```

## HTTP contract

Supported currencies are EUR, USD, GBP, JPY, CHF, CAD, AUD, and NZD. Codes are trimmed and normalized to uppercase. Base and quote must differ. USD/EUR and EUR/USD are separate records; the API never derives one by inversion.

Prices are JSON numbers in ordinary decimal notation, without exponents, positive, with `bid <= ask`. PostgreSQL stores `numeric(18,8)`, allowing up to ten integer digits and eight fractional digits. Unsupported precision is rejected, not rounded. There are at most 56 directed pairs, so the list has no pagination.

| Method and path | Success | Behavior |
|---|---|---|
| `GET /api/rates` | `200` | Sorted stored records only; never calls the provider |
| `GET /api/rates/{base}/{quote}` | `200` | Returns stored record or fetches and persists the missing pair |
| `POST /api/rates` | `201` | Creates a manual record; returns its location |
| `PUT /api/rates/{base}/{quote}` | `200` | Replaces prices on an existing record |
| `DELETE /api/rates/{base}/{quote}` | `204` | Removes an existing record; the next lookup can recreate it |

Create body:

```json
{"baseCurrency":"USD","quoteCurrency":"EUR","bid":0.91,"ask":0.92}
```

Update body:

```json
{"bid":0.92,"ask":0.93}
```

Responses include `baseCurrency`, `quoteCurrency`, `bid`, `ask`, `source`, `updatedAt`, and `providerQuotedAt`. `updatedAt` is when the application stored the current values. `providerQuotedAt` is the provider's quote time when available. Manual writes set `source` to `Manual` and clear `providerQuotedAt`. Neither timestamp implies freshness. The OpenAPI document describes every field.

Errors use Problem Details with a `traceId`. Validation returns `400`, duplicate creation `409` with the title "Rate already exists", and updates or deletes of missing pairs `404`. A lookup that loses an insert race and then finds the row deleted returns `409` with the title "Rate changed concurrently"; retry it. Provider HTTP `429` and `5xx`, connection failures, and Alpha Vantage quota or information notices, including the free-tier daily limit, return `503`. Other non-success provider statuses, including `401` for a rejected key, and unparseable or wrong-pair payloads return `502`. Invalid provider prices also return `502`. Provider timeouts return `504`. Unexpected failures return `500` with a generic body. Failed provider requests store nothing and never fall back to synthetic prices.

The API limits all rate endpoints to 60 requests per 60-second window per application instance by default. This is a shared allowance across callers, not a per-user or distributed quota. Excess requests receive `429`. Rate responses carry `Cache-Control: no-store`.

## Events

Each newly stored pair, whether created manually or fetched from the provider, raises one `rate.created.v1` event after the database commit. Updates, deletes, and lookups that lose an insert race raise nothing.

In `RabbitMq` mode the API publishes to the durable topic exchange `fx-rates.events` with the event type as routing key, persistent delivery, and publisher confirms. The publisher connects lazily on the first publish. On that connection it automatically declares the durable queue `fx-rates.rate-created`, configurable through `Messaging__Queue`, and binds it with `rate.created.#`. Consumers can bind their own queues. In a real system consumers own their queues; this queue makes the demo events visible in RabbitMQ. The body is JSON:

```json
{
  "eventId": "6b6b3b1e-3c0d-4a6a-9a5b-1c2d3e4f5a6b",
  "occurredAt": "2026-09-11T12:00:00.000000+00:00",
  "rate": {"baseCurrency":"USD","quoteCurrency":"EUR","bid":0.91,"ask":0.92,"source":"AlphaVantage","updatedAt":"...","providerQuotedAt":"..."},
  "eventType": "rate.created.v1"
}
```

The message properties carry the same `eventId` as `message_id` and the event type as `type`. Consumers should treat `eventId` as the deduplication key.

Publishing happens after the commit, with no outbox. If the broker is down, the rate is still stored, the HTTP call still succeeds, and the failure is logged with its stack trace. That event is lost. See [design and self-review](docs/design.md) for why and what would close the gap.

## Operations and delivery

- `/docs` provides Scalar interactive documentation, and `/openapi/v1.json` provides the contract.
- `/health/live` reports process liveness. `/health/ready` checks that PostgreSQL answers a query. Broker availability is not part of readiness because event delivery is best effort.
- Logs go to stdout as JSON outside the `Development` environment. Every `5xx` response logs the exception with the same `traceId` the caller received, at Error for unexpected failures and at Warning for expected provider failures. Provider credentials never appear in logs because HTTP client request logging is disabled.
- [Deployment instructions](docs/deployment.md) describe GHCR, the self-hosted runner, Compose, and Cloudflare Tunnel.

The repository is [adamtrip/fx-rates-api](https://github.com/adamtrip/fx-rates-api) on GitHub. The API runs at [fin-demo.adamtrip.pt](https://fin-demo.adamtrip.pt/docs), deployed by the pipeline in [the deployment doc](docs/deployment.md).

Read [design and self-review](docs/design.md) for tradeoffs and limitations, [the backlog](docs/backlog.md) for stories, acceptance criteria, priorities, and estimates, and [verification results](docs/verification.md) for checks performed on the local build.

## License

[MIT](LICENSE).
