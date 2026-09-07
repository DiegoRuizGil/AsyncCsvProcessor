# AsyncCsvProcessor

Bulk product import system via CSV, with asynchronous background processing using queues and workers.

## TL;DR

You upload a CSV with potentially thousands of product rows, the API accepts it instantly and responds without blocking. A separate worker processes it row by row in the background: validates each row, saves what's valid to the database, and logs errors without stopping the rest of the file. If the worker dies mid-processing, a scheduled job detects the stuck job and retries it automatically; if everything fails, the job is marked as failed once retries are exhausted. The uploaded CSV is automatically deleted as soon as the job reaches a terminal state (completed or failed), covering all four points where that can happen.

Stack: .NET 10, RabbitMQ + MassTransit, PostgreSQL + EF Core, Quartz.NET, Docker Compose. Clean Architecture (`Domain` / `Application` / `Infrastructure` / `Api` / `Worker`), with integration tests using Testcontainers.

```bash
git clone https://github.com/DiegoRuizGil/AsyncCsvProcessor.git
cd AsyncCsvProcessor
cp .env.example .env
docker compose up --build
```

Swagger at `http://localhost:8080/swagger`.

## Table of Contents

- [Why this project](#why-this-project)
- [Architecture](#architecture)
- [Tech stack and why](#tech-stack-and-why)
- [Running it locally](#running-it-locally)
- [API usage](#api-usage)
- [Job lifecycle](#job-lifecycle)
- [Uploaded file cleanup](#uploaded-file-cleanup)
- [Stuck job recovery](#stuck-job-recovery)
- [Tests](#tests)
- [Design decisions](#design-decisions)
- [Known limitations](#known-limitations)

## Why this project

A portfolio project to practice a very common pattern in real systems: accepting a heavy task (here, importing a potentially large CSV) without blocking the client, delegating the actual work to a background process with queues, retries, and failure recovery. The chosen domain (product catalog) is deliberately simple so the focus stays on the async processing infrastructure rather than business logic.

It's the natural follow-up to [GameLoggr](https://github.com/DiegoRuizGil/GameLoggr) (ASP.NET Core + Clean Architecture + PostgreSQL), this time adding a second service (the worker), messaging, and a fully dockerized stack.

## Architecture

Clean Architecture with five projects:

```
src/
├── AsyncCsvProcessor.Domain/          # Entities: Job, Product, no external dependencies
├── AsyncCsvProcessor.Application/     # Interfaces (ports): IJobFileProcessor, IUploadedFileCleaner, IAsyncCsvProcessorDbContext
├── AsyncCsvProcessor.Infrastructure/  # EF Core DbContext (implementation of IAsyncCsvProcessorDbContext)
├── AsyncCsvProcessor.Api/             # Minimal API endpoints, receives the CSV and publishes the job
└── AsyncCsvProcessor.Worker/          # MassTransit consumers, actual CSV processing
```

High-level flow:

```
Client  → POST /jobs → Api saves the CSV to /app/uploads and creates the Job (Pending)
                      → Api publishes JobSubmitted to the queue matching its priority
Worker  → consumes JobSubmitted → marks the Job as Processing
                                → processes the CSV row by row (CsvJobFileProcessor)
                                → saves valid products, logs row errors
                                → marks the Job as Completed / CompletedWithErrors / Failed
                                → deletes the uploaded CSV
```

Api and Worker are two independent services (two containers) with isolated filesystems from each other — hence the shared `csv-uploads` volume (see [Running it locally](#running-it-locally)).

## Tech stack and why

| Piece | Choice | Why |
|---|---|---|
| Runtime | .NET 10 | Latest LTS/version available when the project started |
| Messaging | RabbitMQ + MassTransit v8.5.10 | MassTransit provides retries, scheduling, and fault handling out of the box; v8 pinned explicitly in the `.csproj` because v9+ became commercial in 2025, while v8 remains open source with support until end of 2026 |
| Persistence | PostgreSQL + EF Core | Job state, product catalog, and row errors all in the same database |
| Task scheduling | Quartz.NET (via `MassTransit.Quartz`), in-memory | Periodic reaper for stuck jobs, no extra infrastructure needed |
| CSV parsing | CsvHelper 33.1.0 | `Worker` only; lets processing keep going after a formatting error on a single row, without aborting the whole file |
| Containers | Docker Compose (Postgres, RabbitMQ, Api, Worker) | All 4 services come up together with a single command |
| Integration tests | xUnit v2 + Testcontainers + Respawn | Real Postgres in a container per test collection, with data reset between individual tests |
| Unit tests | xUnit v2, no infrastructure | Separate project (`AsyncCsvProcessor.UnitTests`), only references `Worker` (Domain comes in transitively) |

## Running it locally

Requirements: Docker and Docker Compose. You don't need the .NET SDK installed just to run it (you do to edit and rebuild).

```bash
git clone https://github.com/DiegoRuizGil/AsyncCsvProcessor.git
cd AsyncCsvProcessor
cp .env.example .env      # Postgres/RabbitMQ credentials for local development
docker compose up --build
```

This brings up 4 containers: `postgres`, `rabbitmq`, `api`, and `worker`, with healthchecks and conditional startup (`api`/`worker` won't start until `postgres`/`rabbitmq` are actually ready, not just "the container exists").

Once it's up:
- Swagger: `http://localhost:8080/swagger`
- RabbitMQ management UI: `http://localhost:15672` (credentials defined in `.env`)
- EF Core migrations are applied automatically when the Api starts

## API usage

**`POST /jobs`** — uploads a CSV and creates a job. `multipart/form-data` with:
- `File`: the CSV (required)
- `Priority`: `Low` / `Normal` / `High` (optional, defaults to `Normal`)

Returns `201 Created` with `{ id, status, priority }`.

**`GET /jobs/{id}`** — retrieves a job's status (includes `totalRows`, `processedRows`, `status`, etc.).

The expected CSV has columns `Sku`, `Name`, `Price`, `Category`, `Stock` (header matching ignores case and whitespace).

## Job lifecycle

```
Pending → Processing → Completed          (all rows valid)
                     → CompletedWithErrors (some invalid rows, the rest still processed)
                     → Failed              (no processor for the file, or retries exhausted)
```

Invalid rows don't stop the rest of the file: they're logged as `JobRowError` (row number + reason), and the job can still end up as `CompletedWithErrors` with the rest of the rows already inserted or updated in `Products` (upsert by `Sku`).

## Uploaded file cleanup

The CSV uploaded by the client is automatically deleted as soon as the job reaches a terminal state. There are four points where that can happen, all covered:

1. The job completes (with or without row errors) — `JobSubmittedConsumer.CompleteJob`
2. No processor can handle the file — `JobSubmittedConsumer`, "no processor" branch
3. MassTransit exhausts all configured retries after an exception during processing — `JobSubmittedFaultConsumer`
4. A job stuck in `Processing` (the worker died mid-processing) exhausts its recovery attempts — `StuckJobsCheckConsumer`

Deliberately, the file is **not** deleted if `ProcessAsync` throws inside `JobSubmittedConsumer`'s `Consume`: that's a point where MassTransit can still retry, so deleting there would lose the file of a job that might still recover.

The deletion logic lives behind `IUploadedFileCleaner` (interface in `Application`, implementation in `Worker`), injected into the three relevant consumers — this makes the deletion behavior testable without touching the real filesystem (see [Tests](#tests)).

## Stuck job recovery

`StuckJobsCheckConsumer` runs periodically (Quartz, configurable) and looks for jobs stuck in `Processing` whose `ProcessingStartedAt` exceeds a threshold — typically because the worker died mid-processing. Configuration (`appsettings.json`, `StuckJobRecovery` section):

```json
{
  "StuckJobRecovery": {
    "ThresholdMinutes": 5,
    "CheckIntervalMinutes": 2,
    "MaxAttempts": 3
  }
}
```

If a stuck job hasn't exhausted `MaxAttempts`, it's re-queued (`RegisterRecoveryAttempt` + republishing `JobSubmitted`). Once attempts are exhausted, it's marked `Failed` and the associated CSV is deleted.

## Tests

- **`AsyncCsvProcessor.UnitTests`**: pure logic with no infrastructure (CSV row validation, parsing).
- **`AsyncCsvProcessor.IntegrationTests`**: MassTransit consumers against a real Postgres in Testcontainers (`PostgresContainerFixture`, shared per collection via `ICollectionFixture`, with `Respawner.ResetAsync()` clearing data before each individual test), plus a MassTransit `ITestHarness` per test to publish messages and verify consumption/publishing.
- Consumers use a `FakeUploadedFileCleaner` in tests instead of touching the real filesystem, which lets tests verify exactly which paths were (or weren't) deleted in each scenario. Same approach for `IJobFileProcessor` (`FakeJobFileProcessor`): hand-written fakes rather than a mocking library, since these interfaces are small and don't need interaction verification — a plain fake is easier to read and avoids an extra dependency just for this.

```bash
dotnet test
```

## Design decisions

| Decision | Alternative considered | Why this one was chosen |
|---|---|---|
| CSV deletion only at genuinely terminal points (completed, no processor, fault after retries exhausted, stuck job exhausted) | Delete on any exception from `ProcessAsync` | A failure inside `Consume` can still be retried by MassTransit; deleting there would lose the file of a job that might still recover |
| MassTransit v8 pinned explicitly | Leave the version unconstrained / use v9+ | v9+ became commercial in 2025; v8 stays open source with support until end of 2026 |
| `Worker` as a service separate from `Api` (two containers) | Process the CSV within the same `Api` request | The whole point of the project is to demonstrate the queue/worker pattern: the Api responds instantly and the heavy work happens in the background |
| Shared Docker volume (`csv-uploads`) between `api` and `worker` | Base64-encode the CSV into the queue message, or upload it to an external bucket | Api and Worker run in containers with isolated filesystems; the volume is the simplest solution for a local CSV without adding external storage infrastructure |
| Invalid rows don't abort the whole CSV | Fail the entire job if any row is invalid | Data errors in a large CSV are common; the goal is to process what's valid and report what failed, not discard the whole file over one malformed row |
| Separate queue per priority (high/normal/low) | RabbitMQ's native priority queues (`x-max-priority`) | Easier to reason about, avoids the performance/ordering nuances of native priority queues |
| Retries via MassTransit's `UseMessageRetry` + built-in `_error` queue | Hand-rolled retry logic | MassTransit already solves this without extra infrastructure |
| CSV transported as a file path in the message, not inlined | Embedding the CSV content directly in the message | With thousands of rows, a message carrying the whole file is heavy and gets duplicated on every retry/error-queue hop; storage + reference is the standard pattern for this |
| Random filename on disk (`Guid`), original name kept separately in `Job.FileName` | Keeping the original uploaded filename on disk | Avoids path traversal (filenames containing `../`) and collisions between different jobs uploading a file with the same name |
| `IJobFileProcessor` as an abstraction over "parse a file, return valid rows + errors" | Coupling CSV parsing directly into the consumer | Keeps the consumer decoupled from CsvHelper specifics, leaving room for other file formats later without touching the orchestration logic |
| `IUploadedFileCleaner` as an injectable service in `Application`/`Worker` | Duplicate a private `TryDeleteUploadedFile` method in each consumer | It's used from several consumers; as a service it's mockable in integration tests without touching the real filesystem, and it follows the same pattern the project already uses (`IJobFileProcessor`, `IAsyncCsvProcessorDbContext`) |
| Real Postgres via Testcontainers for integration tests | In-memory provider / SQLite | Tests need to validate against real engine behavior (constraints, unique indexes, types), not a substitute that behaves differently |
| One shared Postgres container per test collection, reset with Respawn between tests | One container per test class | Starting a Docker container per test class would be slow; sharing one and resetting state with Respawn is the faster trade-off |

## Known limitations

- No authentication/authorization on the endpoints — out of scope for the project's goal (demonstrating the queue/worker pattern), but not production-ready as is.
- The CSV is stored on disk (local volume); there's no support for an external storage backend (S3, Azure Blob, etc.).
- `CsvJobFileProcessor` assumes CsvHelper's default encoding and delimiter; there's no automatic encoding or delimiter detection.
- No explicit file size limit configured on the upload endpoint.