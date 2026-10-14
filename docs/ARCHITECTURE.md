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
- **Lockout and rate limits.** An account locks for 15 minutes after 5 failed logins. Login is rate-limited per IP; refresh has a looser limit, since a clinic's staff often share one IP. Behind a proxy the client address comes from `X-Forwarded-For`, accepted only from `Proxy:TrustedNetworks`.
- **Sign-up and recovery.** There is no open registration. An operator issues an invitation; accepting it creates the clinic and its first administrator. Password resets use a single-use link. Both tokens are random, stored only as SHA-256 hashes, and expire.
- **Admin permissions.** The system Admin role always resolves to every permission (`Role.EffectivePermissions`), so a permission added later reaches administrators without a data fix.
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
- **Check-then-write under a lock.** `IAppDbContext.WithLocksAsync` runs an action in one transaction holding PostgreSQL advisory locks. Booking uses a lock per dentist and per patient, so two simultaneous bookings cannot both pass the overlap check. Imports and invitation acceptance use the same mechanism.

## File storage

`IFileStorage` has two implementations chosen by `Storage:Provider`: a local folder, and an S3-compatible bucket. Paths are `{clinic}/{patient}/{random}_{name}`. Files are never served directly: downloads go through an endpoint that checks the permission and the tenant, and uploads are accepted only when the file's bytes match its extension.

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

## Clinic lifecycle

- **Provisioning.** `ClinicProvisioningService` creates the clinic, settings, the four system roles, the first administrator, and the starter catalogue from `ClinicDefaults`. The demo seeder uses the same defaults.
- **Subscription.** A clinic is `Trial`, `Active`, `PastDue`, `Cancelled` or `Suspended`. `ClinicAccessMiddleware` makes cancelled and suspended clinics read-only on the server: every request that would change data is refused, except sign-in, sign-out and export. Archived clinics (`IsActive = false`) cannot sign in. Status is changed by an operator; no payment provider is integrated.
- **Operator commands.** `Tahona.Api.dll operator …` runs without the web server. It works across tenants on account data only, and writes each change to the affected clinic's audit log. See [OPERATIONS.md](OPERATIONS.md).
- **Account e-mail.** `IEmailSender` (SMTP via MailKit) sends invitation and reset links when configured. It is optional: every flow works with an operator passing the link on.
- **Onboarding.** `OnboardingService` derives the checklist from clinic data and from confirmations stored in `ClinicSettings.Onboarding` (JSON).

## Import and export

- **Import** (`Features/Import`). An upload is parsed by `ITabularFileReader` (CSV or Excel) and stored as an `ImportBatch` with one `ImportRow` per source row, kept verbatim.
  - **Preview** evaluates every row with the kind's `IImportHandler` and writes no clinic record.
  - **Commit** evaluates again under a per-clinic lock and applies everything in one transaction. A committed batch cannot be committed again.
  - **Provenance.** Imported patients keep `ExternalId`, `SourceSystem` and `ImportBatchId`; each `ImportRow` records the entity it produced.
  - **Undo** removes what a batch created unless it has been used since.
  - **Nothing invented.** Opening balances become invoices flagged `IsOpeningBalance`, excluded from invoiced totals. No payments, visits or treatment are created to fill gaps.
  - **Kinds.** Patients, appointments, opening balances and treatment plans. A treatment plan is grouped from several rows and is imported whole or not at all.
- **Export** (`Features/Export`). One ZIP of CSV files with a manifest, limited to the caller's clinic by the tenant filter. Each export is recorded in the audit log.

## Messaging safety

- **Honest statuses.** Manual mode records `ManualPending` until staff confirm. `Sent`, `Delivered`, `Read` and `Failed` come only from the provider. Channels with no gateway are refused.
- **Consent.** `Patient.MessagingConsent` is `Unknown`, `Granted` or `OptedOut`, with who recorded it and when. An opted-out patient cannot be messaged; a phone call can still be logged.
- **Automation rules.** Deterministic only: a configured provider, the clinic's settings, the patient's consent, outside quiet hours. Each attempt is counted before sending, with a fixed cap, so a failure cannot loop. The worker takes a per-clinic advisory lock so two instances never process one clinic together.
- **One number per clinic.** `TenantWhatsAppProvider` resolves, once per unit of work, the current clinic's own connected number (`ClinicWhatsApp`, token encrypted by `ISecretProtector` with AES-256-GCM), then deployment-wide credentials, then manual sending. A clinic whose token cannot be decrypted falls back to manual, never to another clinic's number.
- **Webhook.** The signature is checked over the raw body. The receiving number in the event identifies the clinic. Processing is idempotent: provider message ids are unique, and statuses only move forward. An inbound message is attached to the clinic and patient that most recently wrote to that number, or dropped.

## AI safety

- **Assistant.** The model can only call read-only tools defined in `AssistantTools`. Each tool runs a tenant-scoped query and returns JSON for the model plus a data table for the UI. The system prompt forbids inventing data or giving clinical advice, and the UI always shows the source rows.
- **Clinical notes.** Structured output is constrained to a fixed schema. The prompt forbids adding information, and `GroundingWarnings` flags clinical terms and tooth numbers that aren't in the dentist's text. Notes are drafts until the dentist saves them, and they carry an *AI-assisted* flag.
- **Data minimisation.** `Ai:PatientData` (`IAiDataPolicy`) is `minimized` by default: tool results sent to the provider identify patients by file number, with no names or phone numbers. The evidence tables shown to staff are built by the server and keep the names. `off` offers only aggregate tools and keeps note structuring local. Model output is never executed and never triggers a message or a write.
- **Provider.** `Ai:Provider` selects Gemini (default, `GeminiAiProvider`) or Claude (`ClaudeAiProvider`). Both implement the same tool loop and structured-output call, so the safety rules above don't depend on the model. The Gemini loop replays each model turn verbatim so thought signatures and call ids survive.
- **Without an API key**, both features fall back to deterministic built-in modes.

## Extending

- **New import kind.** Add it to `ImportKind`, describe its fields in `ImportDefinitions`, implement `IImportHandler`, and register it in `Application/DependencyInjection.cs`.
- **New AI provider.** Implement `IAiProvider` and add it to the provider switch in `Infrastructure/DependencyInjection.cs`.
- **New WhatsApp provider.** Implement `IWhatsAppProvider` and register it in `Infrastructure/DependencyInjection.cs`.
- **New recovery issue type.** Add it to `RecoveryIssueType`, add a detector block in `RecoveryDetector`, add its severity in `RecoveryScorer.IssueSeverity`, and add a matching follow-up trigger.
- **New permission.** Add it to `Permissions.All` and to the default roles. It then appears automatically in the role matrix.
