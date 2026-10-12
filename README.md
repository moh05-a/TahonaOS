# Tahona — Dental Clinic OS

Tahona is a dental clinic management system for clinics in Jordan and the wider MENA region. It handles patients, appointments, the dental chart, treatment plans, billing, and reports. Its main purpose is to help clinics **recover lost revenue**.

> Every morning the clinic opens **Revenue Recovery** and sees which patients to contact today and how much revenue they represent. Typical cases are plans that were presented but never started, treatment left unfinished, no-shows, unpaid balances, overdue clinical reviews, and recalls.

It supports English and Arabic with full RTL, uses JOD and the Asia/Amman time zone, and offers light and dark themes.

---

## Contents

- [Features](#features)
- [Quick start (local development)](#quick-start-local-development)
- [Demo accounts](#demo-accounts)
- [Run everything in Docker](#run-everything-in-docker)
- [Configuration](#configuration)
- [Architecture](#architecture)
- [Roles and permissions](#roles-and-permissions)
- [Testing](#testing)
- [Known limitations](#known-limitations)

## Features

| Area | What it does |
|---|---|
| **Revenue Recovery** | Detects six leak types, ranks patients with an explainable 0–100 score, and supports one-click WhatsApp or call outreach, booking, and outcome logging. Shows potential vs recovered revenue, a recovery trend, and recent wins. See [docs/RECOVERY_ALGORITHM.md](docs/RECOVERY_ALGORITHM.md). |
| **Follow-up automation** | Configurable rules (missed visit → 1 day, plan not started → 3 days, still inactive → 7 days, unfinished → 14 days, root-canal review, 6-month recall, overdue balance). A background worker creates tasks, notifies staff, and auto-resolves tasks when patients book or pay. |
| **Dashboard** | Today's appointments, revenue, outstanding payments, treatment potential, the recovery hero card, today's schedule, and charts: revenue vs expenses, weekly appointment outcomes, treatment conversion funnel, retention, and recovered revenue. |
| **Patients** | Full profile (Arabic name, medical alerts, insurance, emergency contact) with a journey timeline and tabs for appointments, chart, plans, history, invoices, payments, notes, documents, and communications. |
| **Dental chart** | Interactive odontogram with FDI numbering, adult and pediatric. Twelve condition types, each shown with both a colour and a glyph. Surfaces, per-tooth history and notes, and "create plan from tooth". Completed procedures update the chart automatically. |
| **Treatment plans** | Draft → Presented → Accepted → In progress → Partially completed / Completed / Rejected. Shows paid and remaining amounts, completion %, next appointment, item completion (which writes treatment history), and invoicing from the plan. |
| **Appointments** | Day, week, and month calendar in clinic time. Drag to move, resize to change duration, select a slot to create. Dentist filter and colours, a status workflow (confirm → check-in → in treatment → complete / no-show / cancel), double-booking protection, and reminders. |
| **Billing** | Invoices (draft or issued, tax, discounts, void), partial payments with automatic oldest-first allocation, refunds, cash / card / CliQ / bank transfer, outstanding balances, expenses, and a printable invoice. |
| **Reports** | Acceptance and completion rates, return rate, no-show rate, average treatment value, recovery metrics, P&L, production by dentist and category, top procedures, and payment methods. |
| **AI Revenue Assistant** | Answers questions about clinic data using read-only, tenant-scoped query tools. It never invents data: every answer shows the source rows. Uses Google Gemini (free-tier key supported) or Claude when an API key is set, otherwise a built-in rule-based mode. |
| **AI clinical notes** | Turns a dentist's dictation into Chief complaint / Findings / Assessment / Recommendation / Follow-up. It only reorganises what the dentist wrote, and a guard flags any clinical term or tooth number that wasn't in the original note. |
| **WhatsApp** | Templates with `{{patient_name}}`, `{{doctor}}`, `{{date}}`, `{{time}}`, `{{clinic_name}}`, `{{amount}}`, and `{{treatment}}`, in English and Arabic. Uses the Meta Cloud API when credentials are set; otherwise it opens wa.me links. Includes a message log and a signed webhook for delivery status. |
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
| `reception@tahona.demo` | Receptionist | Patients, appointments, payments, recovery outreach, messages |
| `dentist@tahona.demo` | Dentist | Patients, chart, treatment plans, clinical notes |
| `accountant@tahona.demo` | Accountant | Invoices, payments, expenses, reports |

Three more dentists are seeded: `dr.layla@`, `dr.khaled@`, and `dr.noor@tahona.demo`.

The seed includes the brief's example patient: **Ahmad Khalil**, phone `0791234567`, with a 450 JOD root canal + crown plan presented 32 days ago. Press **Ctrl+K** and type the number.

## Run everything in Docker

```bash
docker compose --profile full up -d --build      # → http://localhost:8080
```

This builds the API (`backend/Dockerfile`) and the web app (`frontend/Dockerfile`, nginx with an SPA fallback and an `/api` proxy). The API runs in Production mode, so set `SEED__DEMODATA=true` in `.env` if you want demo data there.

## Configuration

All secrets come from environment variables, either via `.env` or your host's secret store. Nothing sensitive is committed.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection |
| `JWT__SECRET` | HMAC key for access tokens (required, ≥ 32 chars) |
| `Jwt__AccessTokenMinutes` / `Jwt__RefreshTokenDays` | Token lifetimes (default 15 minutes / 14 days) |
| `Cors__Origins` | Allowed web origins |
| `SEED__DEMODATA`, `SEED__DEMOPASSWORD`, `SEED__RESETDEMO` | Demo data controls |
| `AI__PROVIDER` | `gemini` (default) or `claude` |
| `AI__APIKEY`, `AI__MODEL` | API key and model for that provider. Model defaults to `gemini-3.8-flash` (or `claude-opus-5-5`). Without a key, AI features run in built-in mode |
| `WHATSAPP__ACCESSTOKEN`, `WHATSAPP__PHONENUMBERID` | Meta WhatsApp Cloud API. Without them, messages use wa.me links and are logged |
| `WHATSAPP__WEBHOOKVERIFYTOKEN`, `WHATSAPP__APPSECRET` | Webhook verification and signature check (`/api/webhooks/whatsapp`) |
| `AUTOMATION__ENABLED`, `AUTOMATION__INTERVALMINUTES` | Background follow-up engine (default every 15 minutes) |
| `Storage__Root` | Folder for uploaded documents |

**Using the free Gemini tier.** Create a key at <https://aistudio.google.com/apikey>, put it in `AI__APIKEY`, and restart the API. The free tier is rate-limited, and Google states that free-tier content may be used to improve its products. The assistant sends patient names, phone numbers, and balances to the model, so use a paid (billing-enabled) key before putting real patient data through it.

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
├── docs/                            RECOVERY_ALGORITHM.md, ARCHITECTURE.md
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

| | Admin | Dentist | Receptionist | Accountant |
|---|:-:|:-:|:-:|:-:|
| Patients (view / create / edit) | ✓ | view, edit | ✓ | view |
| Appointments | ✓ | ✓ | ✓ | – |
| Dental chart, treatment plans, clinical notes | ✓ | ✓ | view | view plans |
| Invoices / refunds / expenses / reports | ✓ | view | payments | ✓ |
| Revenue Recovery (view / act) | ✓ | view | ✓ | view |
| Messages & templates | ✓ | view | send | send |
| Staff, settings, audit log | ✓ | – | – | – |

The API enforces these permissions. The UI also hides actions the user can't take.

## Testing

```bash
cd backend && dotnet test          # 40 tests: scorer, detector, tenant isolation, Gemini provider, helpers, note guard
cd frontend && npx tsc -b          # type-check
```

API documentation (OpenAPI/Swagger) is available at `/swagger` in Development.

## Known limitations

- **SMS and e-mail** channels are modelled but have no gateway yet. Those messages are logged as *awaiting manual send*. WhatsApp is the supported channel.
- **Inbound WhatsApp messages.** The webhook handles delivery and read status only. Replies from patients aren't threaded into the conversation view yet.
- **File storage** is local disk behind an authorised download endpoint. Swap `IFileStorage` for S3 or Azure Blob in production.
- **Multi-clinic.** The data model and tenancy are multi-clinic ready, but self-service clinic sign-up and a super-admin console are not built. New clinics are created by seeding.
- **Live AI calls.** The Gemini provider is covered by tests against a scripted HTTP handler, and the Claude provider was built against the official Anthropic SDK, but neither has been exercised against the live API in this environment. Without `AI__APIKEY` the built-in modes are used.
