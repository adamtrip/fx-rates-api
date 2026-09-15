# Local verification

Verified on 2026-09-15 with .NET SDK 10.0.100, Docker 28.5.1, and Docker Desktop on Apple Silicon.

## Checks run

| Check | Result |
|---|---|
| `dotnet format FxRates.slnx --verify-no-changes` | Passed, no differences |
| `dotnet build FxRates.slnx -c Release` | Passed, no warnings or errors, warnings treated as errors |
| `dotnet test FxRates.slnx --no-build -c Release` | 70 unit tests and 36 integration tests passed; none skipped |
| `dotnet ef migrations has-pending-model-changes` | No pending model changes |
| `actionlint -config-file .github/actionlint.yaml .github/workflows/ci-cd.yml` | Passed |
| `bash -n deploy/deploy.sh scripts/check.sh` | Passed |
| `docker compose config --quiet` | Passed |
| `dotnet ef database update --project src/FxRates.Infrastructure` with no environment variables | Applied the initial migration against a local PostgreSQL using the Development password |
| `docker compose up --build -d --wait` | PostgreSQL and RabbitMQ healthy, migrator exited 0, API healthy |
| `docker buildx build --platform linux/amd64` for the `api` and `migrator` targets | Built on the arm64 host with the build stage running natively. The amd64 migration bundle printed its usage and the amd64 API reported its missing connection string when run under emulation |

The integration tests start two PostgreSQL 18 containers, one for the API tests and one for the rate-limiter test, and a RabbitMQ 4 container for the publisher tests. Docker failure fails the suite; nothing is skipped.

## Smoke checks against the Compose stack

Run with `EXCHANGE_PROVIDER_MODE=Fake` and `MESSAGING_MODE=RabbitMq`:

- `/health/ready` returned 200 after `--wait`.
- `GET /api/rates/CHF/CAD` on a missing pair returned 200 with a quote from the fake provider, `source` `Fake`, and prices without trailing zeros.
- The management API showed the durable queue `fx-rates.rate-created` bound to `fx-rates.events` with routing key `rate.created.#`, declared by the API on its first publish and without any manual setup.
- Peeking the queue returned one message with routing key and `type` `rate.created.v1` and a JSON body carrying `eventId`, `occurredAt`, and the stored rate.
- The API log recorded the RabbitMQ connection at the first publish, not at startup, and no publish failures.
- With `MESSAGING_MODE=Logging`, the API container restarted and reported ready while the RabbitMQ container was stopped.

Run with `ASPNETCORE_ENVIRONMENT=Development` against a throwaway PostgreSQL:

- `POST /api/rates` with bid `0.91` returned `"bid":0.91`, and the following `GET` returned the same text rather than `0.91000000`.
- 61 requests to `/api/rates` inside one minute produced 55 `200` responses and 6 `429` responses with `Retry-After: 60` and a Problem Details body.

An earlier run on 2026-09-11 fetched a live Alpha Vantage quote for USD/EUR through the real provider and stored it. That check has not been repeated since; it consumes provider quota.

## Remaining checks

- No Git repository has been initialized and no remote repository has been created. The `.gitignore` has not been exercised by a real commit.
- GitHub Actions has been linted locally but has not run on GitHub. The placeholder deployment job has not executed.
- The multi-platform publish has been exercised only in one direction, arm64 host building amd64 images. The amd64 hosted runner building arm64 images uses the same cross-compilation path but has not run.
- Public hosting, runner registration, GHCR publication, and tunnel routing have not been performed.
- Broker container recreation with a stable hostname is configured but was not exercised by deleting and recreating the container.
- Event delivery has no outbox. A broker outage during a create loses that event by design; see the backlog story FX-10.
- No performance benchmark or load target was defined or measured.
