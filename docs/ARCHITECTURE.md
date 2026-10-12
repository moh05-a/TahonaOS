# Architecture

## Layers

```
Tahona.Api  ──▶  Tahona.Application  ──▶  Tahona.Domain
     │                    ▲
     └──▶  Tahona.Infrastructure ──┘
```

| Layer | Responsibility | Depends on |
|---|---|---|
| **Domain** | Entities, enums, the permission catalogue and default roles, FDI tooth helpers, Jordanian phone normalisation | Nothing |
| **Application** | One feature folder per module (`Features/Patients`, `Features/Recovery`, …). Holds services (use cases), DTOs, and FluentValidation validators. Talks to persistence through `IAppDbContext` and to the outside world through **ports** (`ITokenService`, `IWhatsAppProvider`, `IAiProvider`, `IFileStorage`, `ISequenceGenerator`, `ISearchProvider`, `IClinicTime`) | Domain |
| **Infrastructure** | Implements those ports: EF Core with Npgsql, JWT, Meta WhatsApp and wa.me providers, Google Gemini (REST) and Claude (official Anthropic SDK) behind `IAiProvider`, local file storage, the trigram search provider, atomic sequences, the background worker, and the demo seeder | Application |
| **Api** | Thin controllers, the permission policy provider, the exception → ProblemDetails mapper, the automatic validation filter, rate limiting, and Swagger | Both |

Controllers contain no business logic. Each one calls a single application service method.

## Multi-tenancy

- **Tenant root.** The `Clinic` entity is the tenant root. Every clinic-owned entity derives from `TenantEntity`, which carries a `ClinicId`.
- **Query filters.** `AppDbContext` adds a global filter `ClinicId == CurrentClinicId` to every `ITenantEntity`, and to `Clinic` itself. Soft-deleted patients are also filtered out.
- **Where the tenant comes from.** `CurrentClinicId` comes from `ITenantContext`:
  - In HTTP requests, it's the `clinic_id` claim in the JWT.
  - The background worker pins a tenant explicitly with `tenant.Use(clinicId)`, one DI scope per clinic.
- **Writes.** `SaveChanges` stamps `ClinicId` on new tenant entities and refuses to save without a tenant.
- **Bypassing the filter.** Only login, refresh-token lookup, and the background worker's clinic enumeration call `IgnoreQueryFilters()`.
- **Test coverage.** `RecoveryDetectorTests.Other_clinics_data_is_never_visible` covers isolation.

## Authentication and authorisation

- **Login.** Login returns a 15-minute **access token** (JWT, HS256) and sets a 14-day **refresh token** in an `HttpOnly`, `SameSite=Strict` cookie scoped to `/api/auth`.
- **Refresh tokens.**
  - They are stored only as SHA-256 hashes.
  - They rotate on every use.
  - Reusing a revoked token revokes every active token for that user, which signals theft.
  - Changing a password or deactivating a user revokes all of their sessions.
- **Lockout and rate limits.** An account locks for 15 minutes after 5 failed logins. Login is rate-limited per IP; refresh has a looser limit, since a clinic's staff often share one IP.
- **Permissions.** The JWT carries `perm` claims. `[HasPermission("x")]` resolves a dynamic `perm:x` policy, and the fallback policy requires an authenticated user.
- **Frontend.** The access token lives only in memory. A 401 triggers one shared silent refresh and replays the original request.

## Persistence

- **Database.** PostgreSQL via EF Core 10 migrations, in `Infrastructure/Persistence/Migrations`. Migrations are applied at startup when `Database:AutoMigrate=true`.
- **Storage formats.**
  - Enums are stored as strings.
  - Money is `numeric(14,3)`.
  - Timestamps are UTC.
  - `ClinicSettings.Recovery` is a JSON (`jsonb`) owned type.
  - Role permissions are a `text[]` column.
- **Search.** `pg_trgm` GIN indexes on patient English name, Arabic name, and normalised phone back an `ILIKE` search. Phone input is normalised first, so `0791234567`, `+962 79 123 4567`, and `00962791234567` all match.
- **Required indexes.** Patient phone and name, appointment date (per clinic, dentist, and patient), treatment plan status, invoice status and due date, and follow-up status and due date.
- **Document numbers.** File, plan, invoice, and receipt numbers come from a single atomic `UPDATE … RETURNING` on `ClinicSettings`, so there are no duplicates under concurrency.

## Audit log

`AppDbContext.SaveChangesAsync` inspects the change tracker. For every entity marked `IAuditable` that is added, modified, or deleted, it writes an `AuditLog` row in the same transaction, containing:

- User and IP
- Action
- Entity type and id
- The changed fields as JSON (old and new values)

Secrets such as password hashes and sequence counters are excluded. Patient, appointment, treatment plan and item, clinical note, invoice, payment, follow-up, settings, and staff changes are all covered. Seeding suppresses auditing.

## Time and localisation

- **Clinic time.** All dates are stored in UTC. `IClinicTime` converts to the clinic's IANA zone (default `Asia/Amman`) for "today" and "this month" boundaries.
- **Frontend formatting.** Dates use `Intl` with the clinic time zone. FullCalendar runs in the clinic zone through the Luxon plugin.
- **Language and direction.** i18next supplies EN and AR dictionaries. `<html dir>` switches between `ltr` and `rtl`, and Tailwind logical properties (`ms-`, `pe-`, `start-`) mirror the layout. Arabic uses IBM Plex Sans Arabic.
- **Currency.** Currency is configured per clinic, default JOD with 3 decimals. In the Arabic UI it's shown as `د.أ`.

## AI safety

- **Assistant.** The model can only call read-only tools defined in `AssistantTools`. Each tool runs a tenant-scoped query and returns JSON for the model plus a data table for the UI. The system prompt forbids inventing data or giving clinical advice, and the UI always shows the source rows.
- **Clinical notes.** Structured output is constrained to a fixed schema. The prompt forbids adding information, and `GroundingWarnings` flags clinical terms and tooth numbers that aren't in the dentist's text. Notes are drafts until the dentist saves them, and they carry an *AI-assisted* flag.
- **Provider.** `Ai:Provider` selects Gemini (default, `GeminiAiProvider`) or Claude (`ClaudeAiProvider`). Both implement the same tool loop and structured-output call, so the safety rules above don't depend on the model. The Gemini loop replays each model turn verbatim so thought signatures and call ids survive.
- **Without an API key**, both features fall back to deterministic built-in modes.

## Extending

- **New AI provider.** Implement `IAiProvider` and add it to the provider switch in `Infrastructure/DependencyInjection.cs`.
- **New WhatsApp provider.** Implement `IWhatsAppProvider` and register it in `Infrastructure/DependencyInjection.cs`.
- **New recovery issue type.** Add it to `RecoveryIssueType`, add a detector block in `RecoveryDetector`, add its severity in `RecoveryScorer.IssueSeverity`, and add a matching follow-up trigger.
- **New permission.** Add it to `Permissions.All` and to the default roles. It then appears automatically in the role matrix.
