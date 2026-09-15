# Backlog

In short: ten delivered stories, 35 points and 41 hours, cover the brief, including repository publication and deployment. Six production-readiness stories, 20 points and 39 hours, are scoped and ordered but not started.

Hours estimate implementation and focused verification. P0 is required for the brief, P1 is supporting work, and P2 is production readiness.

## Delivered stories

| Story | Priority | Points | Hours | Depends on |
|---|---|---:|---:|---|
| FX-00 Set up the solution and tooling | P0 | 2 | 3 | None |
| FX-01 Store and manage manual rates | P0 | 5 | 6 | FX-00 |
| FX-02 Fetch a missing pair | P0 | 5 | 6 | FX-01 |
| FX-03 Explain failures and report health | P0 | 3 | 3 | FX-01, FX-02 |
| FX-04 Verify API behavior automatically | P0 | 5 | 6 | FX-01, FX-02, FX-03 |
| FX-05 Run and explore the demo locally | P0 | 3 | 4 | FX-01, FX-02 |
| FX-06 Build, publish, and deploy | P0 | 5 | 5 | FX-04, FX-05 |
| FX-07 Explain design and hand over the POC | P0 | 2 | 3 | FX-00 through FX-06 |
| FX-08 Expose a creation event contract | P1 | 2 | 2 | FX-01, FX-02 |
| FX-09 Deliver creation events through RabbitMQ | P1 | 3 | 3 | FX-08 |
| **Total** | | **35** | **41** | |

The repository and the deployment host were the last two tasks to finish; both were completed on 2026-09-15 and are listed under [setup completed last](#setup-completed-last).

### FX-00: Set up the solution and tooling

As a maintainer, I want a solution with enforced conventions and a one-command check so every later story builds on the same rules.

Acceptance criteria:

- Three source projects and two test projects reference each other in one direction: API to Infrastructure to Application.
- Warnings are errors, package versions are managed centrally, and `dotnet format --verify-no-changes` passes.
- The EF tool version is pinned in a manifest and the SDK version in `global.json`.
- Secrets and build output are excluded from Git and from the Docker build context; an example environment file is checked in.
- One script runs every check that CI runs plus the migration, Compose, and workflow checks.

| Task | Subtasks | Hours |
|---|---|---:|
| Create the solution | Three source projects and two test projects; layered references; `global.json` | 0.5 |
| Enforce conventions | `Directory.Build.props` with warnings as errors, central package versions, `.editorconfig`, format check | 1 |
| Add tooling and hygiene | EF tool manifest, `.gitignore`, `.dockerignore`, `.env.example`, license | 0.5 |
| Add the local check script | `scripts/check.sh` mirroring CI plus migration, Compose, and workflow checks | 0.5 |
| Create the repository | Initialise Git, first commit, push, confirm the ignore rules held | 0.5 |

### FX-01: Store and manage manual rates

As an API consumer, I want to create, list, update, and delete bid/ask rates so I can manage the current quote for each supported currency pair.

Acceptance criteria:

- A valid create returns `201`, the record, and a location. Creating that directed pair again returns `409`.
- Prices are positive, bid does not exceed ask, and values fit ten integer and eight fractional digits without rounding.
- Only the eight documented currencies are accepted. Codes normalize to uppercase, and identical base and quote currencies return `400`.
- Updating an existing pair returns `200` with new prices and manual source metadata. Updating a missing pair returns `404`.
- Deleting an existing pair returns `204`; deleting a missing pair returns `404`.
- Listing returns sorted stored rows without contacting the provider.
- PostgreSQL retains records across API restarts and prevents duplicate pairs.

| Task | Subtasks | Hours |
|---|---|---:|
| Model records and validation | Define pair, price, source, and timestamp rules; cover boundary values | 1.5 |
| Implement persistence | Map decimal columns and composite key; add initial migration; implement store operations | 2 |
| Implement application and HTTP operations | Add service methods and Minimal API group; return strict CRUD statuses | 2 |
| Verify storage behavior | Check normalization, persistence, duplicate conflicts, and missing records | 0.5 |

### FX-02: Fetch a missing pair

As an API consumer, I want a missing currency pair to be fetched and stored so subsequent lookups work from the database.

Acceptance criteria:

- A stored quote returns directly regardless of age and exposes its timestamps.
- A missing pair calls the configured provider, validates the response, saves it, and returns it.
- A deleted pair can be recreated by a later lookup.
- Alpha Vantage responses must identify the requested pair and contain valid bid, ask, and quote time values.
- Provider HTTP failures, quota messages, invalid JSON, wrong pairs, invalid prices, and timeout responses save nothing.
- Fake mode requires explicit configuration. A real provider failure never silently returns synthetic values.
- Concurrent insertion cannot create duplicate pairs. A losing lookup reads the stored winner.

| Task | Subtasks | Hours |
|---|---|---:|
| Define provider contract | Define quote result and configuration; validate required credentials | 0.5 |
| Implement Alpha Vantage adapter | Map fields; check JSON errors; validate quote; enforce timeout and cancellation | 2.5 |
| Add fake mode | Supply deterministic synthetic rates; identify their source | 0.5 |
| Implement lookup orchestration | Query first; fetch on miss; persist; resolve duplicate insertion | 1.5 |
| Verify lookup scenarios | Check stored hits, failures, recreated pairs, and insertion races | 1 |

### FX-03: Explain failures and report health

As a demo operator, I want clear errors and useful logs so I can diagnose invalid requests and unavailable dependencies.

Acceptance criteria:

- Expected failures map to documented Problem Details statuses, with a trace ID.
- Unexpected errors hide implementation details from callers and log the full exception with the same trace ID.
- Structured logs distinguish provider and application failures without logging the API key.
- Liveness works independently of PostgreSQL; readiness fails when PostgreSQL is unreachable.
- Rate endpoints enforce a configurable shared allowance and return `429` when exhausted.

| Task | Subtasks | Hours |
|---|---|---:|
| Handle errors | Map application errors; handle invalid HTTP bodies; attach trace IDs | 1 |
| Configure logs and health | JSON console output; database readiness and process liveness | 1 |
| Limit and verify requests | Add global fixed-window limit; test rejected requests and safe error content | 1 |

### FX-04: Verify API behavior automatically

As a maintainer, I want repeatable tests so changes do not break the documented API behavior.

Acceptance criteria:

- Unit tests cover validation, price parsing, database-first decisions, manual metadata, and provider parsing.
- API integration tests use a disposable PostgreSQL container and controlled provider responses.
- Tests cover success and failure HTTP contracts, persistence, and database uniqueness.
- Tests require no real provider credentials or outbound provider calls.
- A failed test or a formatting difference blocks image publication and deployment.

| Task | Subtasks | Hours |
|---|---|---:|
| Build unit suite | Test application rules; stub specific contracts; check provider error formats and timeouts | 2 |
| Build integration fixture | Start PostgreSQL; apply migrations; host API; inject provider responses | 1.5 |
| Exercise HTTP and concurrency | Test CRUD, cold reads, failed fetches, price boundaries, and duplicate insertion | 2 |
| Run complete suite | Resolve failures; record environment limits without claiming skipped checks passed | 0.5 |

### FX-05: Run and explore the demo locally

As an evaluator, I want a documented local startup and interactive API examples so I can assess the POC without a custom frontend.

Acceptance criteria:

- Docker Compose builds and starts PostgreSQL, RabbitMQ, migrations, and the API. API startup waits for PostgreSQL and successful migrations, independently of RabbitMQ health.
- A new environment can use fake mode without external credentials.
- The database survives container replacement, and its port is private by default.
- `/docs` exposes interactive documentation; `/openapi/v1.json` exposes the schema with field descriptions.
- Checked-in HTTP examples cover all operations and representative failures.
- Secrets are supplied externally and excluded from source and image build context.
- Running from an IDE needs no manual environment variables.

| Task | Subtasks | Hours |
|---|---|---:|
| Containerize application and migrations | Create runtime and migration images; configure Compose health and persistence | 2 |
| Expose exploration tools | Register OpenAPI and Scalar; add request examples; add Development settings | 1 |
| Document and exercise setup | Write environment examples; follow startup from clean state | 1 |

### FX-06: Build, publish, and deploy

As a maintainer, I want changes on `main` to deploy only after successful checks so the public demo runs a tested revision.

Acceptance criteria:

- Pull requests format-check, build, and test on hosted runners.
- Successful `main` builds publish matching application and migration images to GHCR, tagged by commit SHA.
- Only trusted deployment jobs use the repository's dedicated self-hosted runner.
- Until that runner exists, a placeholder job completes the pipeline and prints what it would deploy.
- Deployment keeps credentials outside the checkout and data in persistent volumes.
- Migration failure prevents the new API from starting.
- Operator documentation explains runner setup, tunnel routing, smoke checks, and rollback limits.

| Task | Subtasks | Hours |
|---|---|---:|
| Add CI and image publication | Restore, format, build, test; publish multi-platform images after success | 2 |
| Add deployment automation | Pull pinned images; run migration; start API; check health; placeholder job | 1.5 |
| Configure and verify infrastructure | Register the runner; route the tunnel; configure secrets; perform smoke checks | 1.5 |

### FX-07: Explain design and hand over the POC

As an evaluator, I want setup instructions, a backlog, and a self-review so I can understand the implementation and its limits.

Acceptance criteria:

- Documentation matches implemented routes, configuration, and startup commands.
- Stories include acceptance criteria, priorities, story points, and one Hours estimate column.
- Self-review explains architecture, precision, concurrency, provider handling, error handling, and event limitations.
- Pending infrastructure and unverified checks are distinguished from completed work.

| Task | Subtasks | Hours |
|---|---|---:|
| Write documentation | Setup, API contract, deployment, design, and backlog | 2 |
| Review and reconcile | Compare docs to code and verification evidence; fix mismatches | 1 |

### FX-08: Expose a creation event contract

As a developer, I want creation events behind a transport interface so a broker can be introduced without changing use-case logic.

Acceptance criteria:

- A successful new insertion invokes `IEventPublisher` with an event ID, type, occurrence time, and rate payload.
- Updates, reads, duplicate creates, and losing insert races do not emit creation events.
- The logging adapter is explicitly documented as having no queue or durable delivery guarantee.
- Publishing failure does not misrepresent the already committed database write as a failed insert.

| Task | Subtasks | Hours |
|---|---|---:|
| Define and wire events | Add event contract and publisher; invoke after successful insertion | 1 |
| Verify and document limits | Test event triggers and publisher failure; describe commit/publish gap | 1 |

### FX-09: Deliver creation events through RabbitMQ

As an event consumer, I want new rates delivered through a queue so downstream applications can react independently.

Acceptance criteria:

- The API publishes each `rate.created.v1` event to a durable topic exchange, routed by event type, with persistent delivery and publisher confirms.
- On the first publish, the publisher connects and declares the durable queue `fx-rates.rate-created`, configurable through `Messaging__Queue`, bound to `fx-rates.events` with `rate.created.#`.
- The message carries the event ID, event type, and JSON body documented in the README, so a consumer can deduplicate on the ID.
- Compose starts a broker, and the API selects it by configuration. Logging mode has no broker dependency under Compose, although the broker container still starts.
- A broker failure never turns a committed rate into an HTTP error; it is logged with the exception.
- An integration test publishes to a disposable broker and consumes the message back.

| Task | Subtasks | Hours |
|---|---|---:|
| Implement the publisher | Lazy connection reuse, exchange and queue declaration, binding, confirms, message properties | 1.5 |
| Configure and containerize | Messaging options with validation; RabbitMQ in Compose with a health check | 0.5 |
| Verify and document | Broker-backed test; unreachable-broker test; README event contract | 1 |

## Setup completed last

These two tasks belong to delivered stories but needed the repository and a host, so they ran after the code was ready to publish.

| Task | Story | Subtasks | Hours | Depends on |
|---|---|---|---:|---|
| Create the repository | FX-00 | Initialise Git, first commit, push, confirm the ignore rules held, watch one `main` workflow run | 0.5 | FX-07 |
| Configure and verify infrastructure | FX-06 | Register the runner; route the tunnel; configure secrets; enable `FX_DEPLOY_ENABLED`; smoke-check the deployment | 1.5 | Create the repository |

## Production readiness

None of these six stories has started. Effort uses the Hours estimate: low is under 6 hours and medium is 6 to 12 hours.

| Story | Value | Effort | Priority | Points | Hours | Depends on |
|---|---|---|---|---:|---:|---|
| FX-10 Durable events with an outbox | High | Medium | P2 | 5 | 8 | FX-09 |
| FX-11 Resilience for provider and broker calls | High | Low | P2 | 2 | 4 | FX-02, FX-09 |
| FX-12 Authentication and authorization | High | Medium | P2 | 5 | 12 | None |
| FX-15 Rate freshness policy | High | Low | P2 | 3 | 5 | FX-02 |
| FX-17 Optimistic concurrency for updates | Medium | Low | P2 | 2 | 4 | FX-01 |
| FX-20 Database backup and migration policy | High | Medium | P2 | 3 | 6 | FX-06 |
| **Total** | |  | | **20** | **39** | |

Suggested order: FX-12 restricts writes first, then FX-20 establishes backup and migration recovery. Follow with FX-11 for transient failures, FX-10 for durable creation events, FX-15 for stale quotes, and FX-17 for conflicting edits.

### FX-10: Durable events with an outbox

A RabbitMQ outage or a crash after the rate commit can lose a creation event. An outbox would let the API retain it for later delivery.

As an event consumer, I want every stored rate to eventually produce an event, even if the broker was down or the API crashed right after the commit.

Acceptance criteria:

- An outbox row is written in the same transaction as the rate.
- A background relay publishes pending rows in order, marks them sent, and retries with a limit.
- Rows that exhaust retries are marked dead and surfaced in a log and a metric.
- Integration tests cover broker outage during a create and recovery after the broker returns.

| Task | Subtasks | Hours |
|---|---|---:|
| Outbox table | Entity, migration, write inside the insert transaction | 2 |
| Relay worker | Hosted service, batch polling, publish with confirms, retry and dead marking | 3 |
| Failure tests | Outage during create, recovery, duplicate suppression on relay restart | 2 |
| Document delivery guarantee | Update README events section and design doc | 1 |

### FX-11: Resilience for provider and broker calls

A transient Alpha Vantage failure currently fails a cold lookup. Use `Microsoft.Extensions.Http.Resilience` for bounded retries and a circuit breaker on the typed client.

As an API consumer, I want transient provider failures retried and a dead provider to fail fast so cold lookups are reliable and never hang.

Acceptance criteria:

- Provider calls retry on 5xx, 429, and timeouts with exponential backoff and a total budget under the configured timeout.
- A circuit breaker opens after repeated failures and returns `503` immediately while open.
- Broker publishes retry a bounded number of times before the failure is logged.
- Tests prove the retry count, the breaker opening, and that 4xx responses are not retried.

| Task | Subtasks | Hours |
|---|---|---:|
| HTTP resilience pipeline | Add the package; configure retry, breaker, and per-attempt timeout on the typed client | 1.5 |
| Broker publish retry | Bounded retry around the publish; keep the caller-facing behavior | 1 |
| Tests | Stub handler counting attempts; breaker state test; no retry on 401 | 1.5 |

### FX-12: Authentication and authorization

Anyone who can reach the API can delete every rate. Authentication and write permissions would restrict those changes to trusted callers.

As an operator, I want callers identified and writes restricted so only trusted systems and people can change rates.

Acceptance criteria:

- Machine callers authenticate with an API key sent in a header. Keys are stored hashed and can be revoked.
- People authenticate with a JWT bearer token from an external identity provider; the API validates issuer, audience, and expiry.
- `POST`, `PUT`, and `DELETE` require a `rates:write` scope. `GET` is configurable as open or read-scoped.
- Unauthenticated requests return `401` and unauthorized ones `403`, both as Problem Details.
- Tests cover each scheme, expired tokens, revoked keys, and missing scope.

| Task | Subtasks | Hours |
|---|---|---:|
| API key scheme | Authentication handler, hashed key store, revocation, management script | 3 |
| JWT bearer | Identity provider configuration, scope claims, validation options | 3 |
| Authorize endpoints | Policies on the route group; read-open switch | 1 |
| Tests | Both schemes, failure cases, Problem Details shape | 3 |
| Documentation and rotation | Key issuance and rotation runbook; OpenAPI security schemes | 2 |

### FX-15: Rate freshness policy

The API returns stored quotes regardless of age. A maximum age and refresh path would let callers obtain newer provider prices.

As an API consumer, I want stale provider quotes refreshed so lookups return something close to current.

Acceptance criteria:

- A configurable maximum age applies to provider-sourced rates; manual rates are exempt.
- A lookup of a stale rate refreshes it from the provider and returns the new value, or returns the stale value with a warning header if the provider fails.
- An explicit `POST /api/rates/{base}/{quote}/refresh` forces a provider fetch.
- Tests cover fresh, stale with success, stale with provider failure, and manual exemption.

| Task | Subtasks | Hours |
|---|---|---:|
| Staleness rule | Max-age option; decision in the service; warning header | 1.5 |
| Refresh paths | Refresh on stale lookup; explicit refresh endpoint | 2 |
| Tests and docs | Scenarios above; README contract update | 1.5 |

### FX-17: Optimistic concurrency for updates

Concurrent updates to the same pair can overwrite each other. A version check would let callers detect that conflict.

As an API consumer, I want an update rejected if the rate changed since I read it so I never overwrite someone else's edit.

Acceptance criteria:

- Responses carry an `ETag` derived from a version column.
- `PUT` with `If-Match` succeeds only when the version matches, otherwise returns `412`.
- `PUT` without `If-Match` keeps today's last-write-wins behavior, documented as such.
- Tests cover match, mismatch, and absent header.

| Task | Subtasks | Hours |
|---|---|---:|
| Version column | Entity, migration, increment on every write | 1 |
| Headers | ETag on responses; If-Match check in the update path | 1.5 |
| Tests and docs | Three scenarios; README contract update | 1.5 |

### FX-20: Database backup and migration policy

There is no backup or migration policy. Off-host backups and a tested restore would provide recovery after a lost volume or a failed schema change.

As an operator, I want scheduled backups, a tested restore, and a migration rule so a schema change or a host failure cannot lose rates.

Acceptance criteria:

- A scheduled job takes a logical backup to off-host storage with retention.
- A restore has been performed into a fresh environment and the steps are written down with timings.
- Migrations follow expand-and-contract: no migration removes or renames a column the running version still reads.
- CI fails a pull request whose migration drops a column or table without a linked follow-up.

| Task | Subtasks | Hours |
|---|---|---:|
| Backup job and restore drill | Scheduled dump, retention, restore into a clean database, record timings | 3 |
| Migration policy | Written rule; review checklist; CI guard on destructive operations | 1.5 |
| Recovery options | Evaluate point-in-time recovery or a managed database; record the decision | 1.5 |
