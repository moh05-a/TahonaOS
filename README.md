<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="frontend/public/brand/tahona-logo-horizontal-dark.png" />
    <img src="frontend/public/brand/tahona-logo-horizontal.svg" alt="Tahona" width="320" />
  </picture>
</p>

# Tahona — Dental Clinic OS

Tahona is a dental clinic management system for independent clinics in Jordan and the wider MENA region. It handles patients, appointments, the dental chart, treatment plans, billing, and reports. It is built to be easy to switch to, quick to use every day, and honest about the follow-up work that brings patients and payments back.

> Every morning the clinic opens **Revenue Recovery** and sees which patients to contact today and why. Typical cases are plans that were presented but never started, treatment left unfinished, no-shows, unpaid balances, overdue clinical reviews, and recalls.

**Status:** the software for a supervised pilot is in place, but no real clinic should use it yet. A legal review of patient-data handling, a hosted deployment with TLS, and a trial of each external service with real credentials are still missing. [docs/IMPLEMENTATION_ROADMAP.md](docs/IMPLEMENTATION_ROADMAP.md) lists what is done, how it was verified, and what is open.

It supports English and Arabic with full RTL, uses JOD and the Asia/Amman time zone, and offers light and dark themes.

---

## Contents

- [Features](#features)
- [Quick start (local development)](#quick-start-local-development)
- [Demo accounts](#demo-accounts)
- [Run everything in Docker](#run-everything-in-docker)
- [Creating a clinic](#creating-a-clinic)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Roles and permissions](#roles-and-permissions)
- [Testing](#testing)
- [Known limitations](#known-limitations)

## Features

| Area | What it does |
|---|---|
| **Front desk** | Today's list with one-click confirm, check-in, start, complete and no-show. Late patients and balances are flagged, and staff can find, register, book, take a payment and message without leaving the page. A visit booked with a price shows it, with one click to take it. |
| **Switching** | Sign-up by invitation, a getting-started checklist, and an import centre for patients, appointments, treatment plans and outstanding balances from CSV or Excel. Columns are matched from English or Arabic headers, every row is checked before anything is saved, duplicates are shown for a decision, and an import can be undone. |
| **Your data** | A full export at any time: one ZIP of CSV files with a manifest, optionally with uploaded files. It stays available if the subscription lapses. |
| **Revenue Recovery** | Detects six leak types, ranks patients with an explainable 0–100 score, and supports one-click WhatsApp or call outreach, booking, and outcome logging. Every item shows the record that raised it, the contacts made and the outcome. Potential value is shown as an estimate, and payments are counted as collected after follow-up only when they are for the matter the clinic followed up on. An owner report puts the last 30 days beside the 30 before. See [docs/RECOVERY_ALGORITHM.md](docs/RECOVERY_ALGORITHM.md). |
| **Follow-up automation** | Configurable rules (missed visit → 1 day, plan not started → 3 days, still inactive → 7 days, unfinished → 14 days, root-canal review, 6-month recall, overdue balance). A background worker creates tasks, notifies staff, and auto-resolves tasks when patients book or pay. |
| **Dashboard** | Today's appointments, revenue, outstanding payments, treatment potential, the recovery hero card, today's schedule, and charts: revenue vs expenses, weekly appointment outcomes, treatment conversion funnel, retention, and recovered revenue. |
| **Patients** | Full profile (Arabic name, medical alerts, insurance, emergency contact) with a journey timeline and tabs for appointments, chart, plans, history, invoices, payments, notes, documents, and communications. |
| **Dental chart** | Interactive odontogram with FDI numbering, adult and pediatric. Twelve condition types, each shown with both a colour and a glyph. Surfaces, per-tooth history and notes, and "create plan from tooth". Completed procedures update the chart automatically. |
| **Treatment plans** | Draft → Presented → Accepted → In progress → Partially completed / Completed / Rejected. Shows paid and remaining amounts, completion %, next appointment, item completion (which writes treatment history), and invoicing from the plan. |
| **Appointments** | Day, week, and month calendar in clinic time. Each booking can carry the price the patient will pay, filled in from the price list; nothing is charged until the payment is taken. Drag to move, resize to change duration, select a slot to create. Dentist filter and colours, a status workflow (confirm → check-in → in treatment → complete / no-show / cancel), double-booking protection, and reminders. |
| **Billing** | Invoices (draft or issued, tax, discounts, void), partial payments with automatic oldest-first allocation, payment for a visit that has no invoice yet (the invoice is created with it), correction of a payment entry, cancellation of an entry made by mistake (kept on record), refunds, cash / card / CliQ / bank transfer, outstanding balances, expenses, and a printable invoice. |
| **Reports** | Acceptance and completion rates, return rate, no-show rate, average treatment value, recovery metrics, P&L, production by dentist and category, top procedures, and payment methods. |
| **AI Revenue Assistant** | Answers questions about clinic data using read-only, tenant-scoped query tools. It never invents data: every answer shows the source rows. By default the AI provider receives file numbers, not patient names or phone numbers. Uses Google Gemini or Claude when an API key is set, otherwise a built-in rule-based mode. |
| **AI clinical notes** | Turns a dentist's dictation into Chief complaint / Findings / Assessment / Recommendation / Follow-up. It only reorganises what the dentist wrote, and a guard flags any clinical term or tooth number that wasn't in the original note. |
| **WhatsApp** | Templates with `{{patient_name}}`, `{{doctor}}`, `{{date}}`, `{{time}}`, `{{clinic_name}}`, `{{amount}}`, and `{{treatment}}`, in English and Arabic. Each clinic can connect its own WhatsApp Business number. Without one, each message opens in WhatsApp for staff to send and is logged as awaiting manual send. With one it uses the Cloud API: delivery and read status, patient replies on the conversation, consent and opt-out ("STOP"), quiet hours, and capped retries. |
| **Also** | Global search (Ctrl+K, phone-number aware), notifications with four priorities, staff and an editable role-permission matrix, a price list, document uploads (X-rays, scans, PDFs), an automatic audit log, CSV export, and an EN/AR toggle. |

## Quick start (local development)

**Prerequisites:** .NET 10 SDK, Node 22+, Docker (for PostgreSQL).

```bash
# 1. Secrets & config
cp .env.example .env
#    then edit .env: set POSTGRES_PASSWORD, the same password in ConnectionStrings__Default,
#    JWT__SECRET (≥32 random chars, e.g. `openssl rand -base64 48`) and SEED__DEMOPASSWORD.

# 2. Database
docker compose up -d db

# 3. API  → http://localhost:5080  (Swagger at http://localhost:5080/swagger)
cd backend/src/Tahona.Api
dotnet run --no-launch-profile
#    Migrations are applied automatically. With SEED__DEMODATA=true, a demo clinic is created
#    on the first start: ~150 patients, ~700 appointments, plans, invoices and payments.

# 4. Web app → http://localhost:5173
cd frontend
npm install
npm run dev
```

The API reads `.env` from the repository root. Vite proxies `/api` to the API, so the refresh-token cookie stays same-origin.

**Reset the demo.** Demo data is generated relative to today, so the recovery dashboard always looks current. To regenerate it in development:

```bash
SEED__RESETDEMO=true dotnet run --no-launch-profile   # drops and re-creates the database
```

**Port conflicts.** If a local PostgreSQL already uses port 5432, set `POSTGRES_PORT=5433` and update the connection string port to match.

## Demo accounts

All demo users share the password from `SEED__DEMOPASSWORD` in your `.env`.

| Email | Role | Sees |
|---|---|---|
| `admin@tahona.demo` | Admin (also a dentist) | Everything |
| `dentist@tahona.demo` | Dentist | Patients, chart, treatment plans, clinical notes, recording payments |

Three more dentists are seeded: `dr.layla@`, `dr.khaled@`, and `dr.noor@tahona.demo`.

The seed includes the brief's example patient: **Ahmad Khalil**, phone `0791234567`, with a 450 JOD root canal + crown plan presented 32 days ago. Press **Ctrl+K** and type the number.

## Run everything in Docker

```bash
docker compose --profile full up -d --build      # → http://localhost:8080
```

This builds the API (`backend/Dockerfile`) and the web app (`frontend/Dockerfile`, nginx with an SPA fallback and an `/api` proxy). The API runs in Production mode and starts with an empty database: create the first clinic with an invitation (next section). Demo data is refused outside Development unless you also set `SEED__ALLOWOUTSIDEDEVELOPMENT=true`.

## Creating a clinic

```bash
cd backend/src/Tahona.Api
dotnet run --no-launch-profile -- operator invite --email owner@clinic.example --clinic "Clinic name"
# In Docker: docker compose exec api dotnet Tahona.Api.dll operator invite --email …
```

The command prints a sign-up link. The owner opens it, names the clinic, chooses a password and lands on the getting-started checklist. Other operator commands (list clinics, change subscription status, issue a password reset link) are in [docs/OPERATIONS.md](docs/OPERATIONS.md).

## Configuration

All secrets come from environment variables, either via `.env` or your host's secret store. Nothing sensitive is committed.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection |
| `JWT__SECRET` | HMAC key for access tokens (required, ≥ 32 chars) |
| `Jwt__AccessTokenMinutes` / `Jwt__RefreshTokenDays` | Token lifetimes (default 15 minutes / 14 days) |
| `Cors__Origins` | Allowed web origins |
| `APP__PUBLICURL` | Address of the web app, used in invitation and password-reset links |
| `APP__SUPPORTCONTACT`, `APP__PAYMENTINSTRUCTIONS` | Shown to clinics under Settings → Subscription |
| `SECRETS__ENCRYPTIONKEY` | Encrypts the WhatsApp tokens clinics store (`openssl rand -base64 32`) |
| `EMAIL__HOST`, `EMAIL__PORT`, `EMAIL__USERNAME`, `EMAIL__PASSWORD`, `EMAIL__FROM` | Optional mail server for invitations and "forgot password" |
| `STORAGE__PROVIDER`, `STORAGE__S3__*` | `local` (default) or `s3` for an S3-compatible bucket |
| `PROXY__TRUSTEDNETWORKS` | CIDR ranges of reverse proxies allowed to set the client address |
| `SEED__DEMODATA`, `SEED__DEMOPASSWORD`, `SEED__RESETDEMO` | Demo data controls (Development only) |
| `AI__PATIENTDATA` | What the AI provider may receive: `minimized` (default, file numbers only), `off` (totals only) or `full` |
| `AI__PROVIDER` | `gemini` (default) or `claude` |
| `AI__APIKEY`, `AI__MODEL` | API key and model for that provider. Model defaults to `gemini-3.8-flash` (or `claude-opus-5-5`). Without a key, AI features run in built-in mode |
| `WHATSAPP__WEBHOOKVERIFYTOKEN`, `WHATSAPP__APPSECRET` | Webhook verification and signature check (`/api/webhooks/whatsapp`), shared by all clinics |
| `WHATSAPP__ACCESSTOKEN`, `WHATSAPP__PHONENUMBERID` | One number for the whole installation. Only for a single-clinic copy; clinics normally connect their own number in Settings |
| `AUTOMATION__ENABLED`, `AUTOMATION__INTERVALMINUTES` | Background follow-up engine (default every 15 minutes) |
| `Storage__Root` | Folder for uploaded documents when storage is `local` |

**Using the free Gemini tier.** Create a key at <https://aistudio.google.com/apikey>, put it in `AI__APIKEY`, and restart the API. It is fine for the demo data. Google's terms for the free tier say submitted content is used to improve its products, may be read by reviewers, and must not contain personal information, so do not use a free key for a real clinic. With the default `AI__PATIENTDATA=minimized` the provider sees file numbers instead of names and phone numbers, but a question typed by staff and a dictated clinical note are sent as written.

**WhatsApp approved templates.** Business-initiated messages outside WhatsApp's 24-hour window need an approved template. Put its name in the template's *Approved WhatsApp template name* field. Variables are sent as body parameters in the order they appear in the text.

## Architecture

```
TahonaOS/
├── backend/                         ASP.NET Core (.NET 10), clean architecture
│   ├── src/Tahona.Domain            Entities, enums, permission catalogue, FDI tooth + phone helpers
│   ├── src/Tahona.Application       Use cases, DTOs, FluentValidation, Recovery Engine, AI tools
│   ├── src/Tahona.Infrastructure    EF Core + PostgreSQL, tenancy filters, audit interceptor, JWT,
│   │                                WhatsApp providers, Gemini + Claude providers, storage, background worker, seeder
│   ├── src/Tahona.Api               REST controllers, auth, permission policies, ProblemDetails, Swagger
│   └── tests/Tahona.Tests           xUnit: scoring, detection, tenant isolation, helpers, note guard
├── frontend/                        React 19 + TypeScript + Vite + Tailwind CSS 4
│   └── src/
│       ├── components/              UI kit, layout (sidebar, Ctrl+K search, notifications), charts
│       ├── features/<module>/       One folder per module (pages, dialogs, module components)
│       ├── lib/                     API client (silent refresh), types, formatting (JOD, Asia/Amman)
│       └── i18n/                    English + Arabic dictionaries
├── docs/                            PRODUCT_AUDIT.md, IMPLEMENTATION_ROADMAP.md, OPERATIONS.md,
│                                    ARCHITECTURE.md, RECOVERY_ALGORITHM.md
├── scripts/                         backup.sh, restore.sh
└── docker-compose.yml               PostgreSQL (+ full-stack profile)
```

Key design decisions are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md):

- **Multi-tenant isolation.** Every clinic-owned entity has a `ClinicId`, and EF Core global query filters scope all queries to it.
- **Auth.** Short-lived JWTs held in memory, with rotating refresh tokens in an HttpOnly cookie. Reuse of an old refresh token revokes the whole session chain.
- **Permissions.** Permission-based policies rather than hard-coded roles.
- **Audit log.** Recorded automatically for every audited entity.
- **Time and money.** Timestamps are stored in UTC and shown in clinic time. Money uses 3 decimals (JOD fils).

## Roles and permissions

Endpoints require **permissions**, such as `patients.view`, `invoices.manage`, and `recovery.manage`. Roles are just sets of permissions, editable under **Staff → Roles & permissions**. Admin always has every permission.

Every clinic starts with two roles. A clinic that wants a front-desk or finance role can create its own.

| | Admin | Dentist |
|---|:-:|:-:|
| Patients (view / create / edit) | ✓ | view, edit |
| Appointments | ✓ | ✓ |
| Dental chart, treatment plans, clinical notes | ✓ | ✓ |
| Record a payment, correct its details | ✓ | ✓ |
| Cancel or refund a payment, invoices, expenses, reports | ✓ | view only |
| Revenue Recovery (view / act) | ✓ | view |
| Messages & templates | ✓ | view |
| Staff, settings, audit log | ✓ | – |
| Import data, export data | ✓ | – |

The API enforces these permissions. The UI also hides actions the user can't take.

## Testing

```bash
cd backend && dotnet test          # 178 tests
cd frontend && npx tsc -b          # type-check
```

The tests cover tenant isolation, sign-up and password reset, the subscription policy, booking rules, payments and refunds, attribution, messaging consent and retry limits, the WhatsApp webhook, AI data minimisation, uploads, import, and export.

Six tests need a real service and are skipped unless its variable is set:

| Variable | Tests |
|---|---|
| `TAHONA_TEST_POSTGRES` (a connection string) | Simultaneous bookings, simultaneous imports, a crash in the middle of an import |
| `TAHONA_TEST_S3` (`serviceUrl\|accessKey\|secretKey`) | File storage round trip against an S3-compatible endpoint |
| `TAHONA_TEST_SMTP` (`host:smtpPort:httpPort` of a Mailpit server) | Delivery of an invitation e-mail |

```bash
TAHONA_TEST_POSTGRES="Host=localhost;Port=5432;Username=tahona;Password=…" dotnet test
```

API documentation (OpenAPI/Swagger) is available at `/swagger` in Development.

## Known limitations

The full list, with what must be done before a real clinic uses the system, is in [docs/IMPLEMENTATION_ROADMAP.md](docs/IMPLEMENTATION_ROADMAP.md). The main ones:

- **No legal review.** Handling of patient data has not been assessed against Jordan's Personal Data Protection Law. Nothing here claims compliance.
- **No hosted deployment.** The Docker stack was verified in Production mode on one machine. TLS, a domain and a firewall have not been set up.
- **External services have not been tried with real accounts.** WhatsApp sending and receiving, the mail provider and the storage bucket were tested against test servers or with a deliberately invalid token.
- **Subscriptions are managed by an operator.** There is no payment provider and no web admin area; operator commands need server access.
- **Import** covers patients, appointments, treatment plans and outstanding balances. Payment history is not imported, and row messages are in English.
- **SMS** is not connected.
