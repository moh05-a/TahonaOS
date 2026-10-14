# TahonaOS product audit

Audit date: 12 October 2026. Scope: the whole repository at commit `8d0bb94` (backend, frontend, schema, background worker, integrations, tests, deployment files, docs).

This document describes the state **before** the pilot-readiness work. What has changed since is tracked in [IMPLEMENTATION_ROADMAP.md](IMPLEMENTATION_ROADMAP.md).

## 1. Summary

TahonaOS is a working single-clinic application with honest multi-tenant plumbing. It is not yet a SaaS product a clinic can adopt.

- **The daily-use core works end to end**: patients, calendar, chart, treatment plans, invoices, payments, refunds, Revenue Recovery, messaging log, reports, EN/AR.
- **Nothing exists for switching or selling**: no way to create a clinic except the demo seeder, no import, no export, no subscription state, no password recovery, no operator tooling.
- **Seven issues block a pilot with real patient data** (section 5). The most serious are patient names and phone numbers going to a free-tier AI model, a booking race that allows double-bookings, and demo credentials that can be seeded in production.

The recommendation is to stop adding clinical features and spend the next phases on trust, switching and provisioning, in that order.

## 2. Baseline checks

Run before any change:

| Check | Result |
|---|---|
| `dotnet test` (backend) | 42 passed, 0 failed |
| `npx tsc -b` (frontend) | No errors |
| `npm run lint` (frontend) | 0 errors, 44 warnings (mostly set-state-in-effect in dialogs, fast-refresh exports in `Odontogram.tsx`, and `Date` calls during render in `DashboardPage.tsx`) |
| Build warning | `MSB3277`: the test project resolves two versions of `Microsoft.EntityFrameworkCore.Relational` (10.0.4 and 10.0.12) |

Test coverage is narrow. The 42 tests cover the recovery scorer, the recovery detector (including one tenant-isolation test), phone and tooth helpers, and the Gemini provider. There are no tests for authentication, permissions, appointments, billing, messaging, the webhook, or any frontend behaviour.

## 3. What works end to end

Verified by reading the code path from controller to database, and by the earlier manual and Playwright runs against the demo data.

| Area | State |
|---|---|
| Login, refresh-token rotation, reuse detection, lockout | Works. Refresh tokens are hashed, rotated on use, and revoked on password change. |
| Permission checks | Enforced on the backend by `[HasPermission]` policies. The frontend only hides what the user cannot do. |
| Tenant isolation on reads | EF global query filters on every `ITenantEntity`, plus `Clinic`. Writes stamp `ClinicId` and refuse to save without a tenant. |
| Patients, notes, timeline, soft delete | Works. Search matches Arabic and English names and normalised Jordanian phone numbers. |
| Calendar | Day, week, month, drag and drop, status transitions, no-show rules. |
| Dental chart | Adult and primary dentition, conditions per surface, results recorded from completed plan items. |
| Treatment plans | Draft → Presented → Accepted → In progress → Completed or Rejected, with item-level completion creating treatment records. |
| Invoices, payments, refunds | Balances are recomputed on every payment and refund. Document numbers are allocated atomically. |
| Revenue Recovery | Six detectors driven by configurable rules, a documented 0–100 score, follow-up tasks, contact logging, outcome recording. |
| Audit log | Written in the same transaction as the change, for every `IAuditable` tenant entity. |
| EN/AR and RTL | Both dictionaries are complete and the layout mirrors. |
| AI assistant and note structuring | Work against Gemini (verified live on 12 October 2026) and against a built-in mode with no key. |

## 4. What is partial or simulated

| Area | What is actually there |
|---|---|
| **WhatsApp** | With no Meta credentials, "send" opens a `wa.me` link and logs the message as `ManualPending`. That is honest, but it is a link, not an integration. The Cloud API provider exists and has never been exercised against Meta. |
| **Webhook** | Verifies the signature and applies delivery statuses. It ignores inbound patient messages, has no duplicate-event protection, and computes the signature over a re-encoded string instead of the raw bytes. |
| **Automation** | The worker creates follow-up tasks and can send reminders. It has no quiet hours, no opt-out, no retry limit, and no protection against two API instances sending the same reminder. |
| **SMS and email channels** | Selectable in the UI but only log a `ManualPending` row. No gateway exists. |
| **"Subscription plan"** | A free-text column on `Clinic` with the value `Professional`. Nothing reads it. |
| **Multi-clinic** | Data isolation is real. Creating a second clinic is not possible without editing the seeder. |
| **Recovered revenue** | Any payment by a patient who was contacted in the previous 45 days counts as recovered, whatever the payment was for. See 5.6. |
| **File storage** | Local disk only. The class comment says to swap it for object storage in production. |
| **Health check** | `/health` returns 200 without touching the database. |

## 5. Launch blockers

Ranked by the harm they can do to a real clinic.

### 5.1 Patient identifiers are sent to a free-tier AI model

`AssistantTools` returns patient names and phone numbers to the model in every list tool. The default provider is Gemini on the free tier. Google's Gemini API terms for unpaid services say submitted content is used to improve Google products, may be read by human reviewers, and that sensitive or personal information should not be submitted ([Gemini API Additional Terms](https://ai.google.dev/terms)).

This is acceptable for demo data and not for a real clinic. Clinical-note structuring sends the dentist's free text, which can also contain identifiers.

### 5.2 Double-booking under concurrency

`AppointmentService.EnsureNoOverlapAsync` reads for a clash and then inserts. Two receptionists booking the same dentist and slot at the same moment both pass the check. There is no database constraint, lock or transaction behind it.

### 5.3 Demo accounts can be seeded in production

`Seed:DemoData` works in any environment, and `docker-compose.yml` passes it into a container that defaults to `Production`. That creates `admin@tahona.demo` with a password from an environment variable on a public server. The login page also lists the demo email addresses to everyone.

### 5.4 The API does not know the client's IP behind a proxy

There is no forwarded-headers handling. Behind nginx every request appears to come from the proxy, so the per-IP login limiter throttles all clinics together, and audit rows record the proxy address.

### 5.5 Automated messages can loop and cannot be stopped by the patient

- A reminder that fails is retried every 15 minutes indefinitely, creating a new failed message row each time.
- There is no patient opt-out, no consent record and no quiet hours. An automated reminder can go out at 3 a.m.
- Running two API instances would send every reminder twice.

Automatic sending only activates with Meta credentials, so no message has gone out this way yet. It must be fixed before credentials are added.

### 5.6 "Recovered revenue" overstates what follow-ups achieved

`RecoveryAttributionService` credits a payment to the most recent follow-up contact within 45 days, without checking that the payment relates to the issue the clinic called about. A patient reminded about a recall who then pays an old invoice counts as recovered recall revenue. The label "Recovered by follow-ups" also asserts causation the data cannot show.

### 5.7 Identifiers from the request body are not always checked against the tenant

Most services validate foreign keys through the tenant-filtered context. These do not:

| Location | Unchecked field |
|---|---|
| `MessagingService.SendAsync` | `FollowUpId`, `AppointmentId` stored on the message |
| `MessagingService.RenderAsync` | `AppointmentId` is not checked to belong to the patient |
| `ClinicalRecordsService` | `AppointmentId` on treatment records and clinical notes |
| `TreatmentPlanService.CompleteItemAsync` | `AppointmentId`, `DentistId` |

Reads stay filtered, so this does not leak another clinic's data. It allows a row to point at another clinic's record, which is an integrity defect in a system whose selling point is isolation.

### 5.8 Other security gaps

| Gap | Detail |
|---|---|
| Upload validation | The content type comes from the browser. File contents are not inspected. |
| Storage path check | `LocalFileStorage.Resolve` uses a prefix test without a trailing separator, so `storage2/…` passes a check for `storage`. Not reachable today, because paths are generated server-side. |
| No password recovery | A clinic admin who forgets their password cannot get back in. |
| No Content-Security-Policy | The SPA is served without one. |
| Email enumeration | Staff creation reports "email already in use" across all clinics. |
| No backups | Nothing documents or automates a database or file backup, and no restore has been tested. |
| No structured logging or alerts | Console logging only. |

## 6. Missing for a commercial pilot

None of this exists in the repository:

1. **Provisioning.** Creating a clinic, its first administrator, roles and default reference data.
2. **Onboarding.** A setup wizard, a checklist, a way to resume.
3. **Import.** Any path from a spreadsheet or another system into TahonaOS.
4. **Export.** Any way for a clinic to take its data out.
5. **Subscription state.** Trial, active, past due, cancelled, suspended, and what each one allows.
6. **Operator tooling.** Listing clinics, suspending one, helping with account recovery.
7. **Email delivery.** Needed for verification and password reset.
8. **Durable document storage and backups.**

## 7. Architectural risks

| Risk | Why it matters | Recommendation |
|---|---|---|
| **One WhatsApp number for every clinic** | Credentials are deployment-wide. All clinics would send from the same number and share one webhook. | Per-clinic credentials, stored encrypted, before a second clinic uses the Cloud API. Until then, only the manual mode is multi-tenant safe. |
| **Recovery dashboard is computed in memory on every request** | `RecoveryDetector` loads every open plan, missed appointment and balance each time. Fine for one clinic with a few thousand patients, slow beyond that. | Leave it. Cache per clinic for a minute if it becomes measurable. |
| **Trigram indexes are not scoped by clinic** | Name search scans a global index and then filters by tenant. | Leave it until total patients pass roughly 200,000. |
| **Single-process worker** | The automation loop runs inside the API. Two instances double the work. | Take a Postgres advisory lock per clinic per run. No separate worker service is justified yet. |
| **Auto-migration at startup** | Convenient for one instance, risky for several starting at once. | Keep it for the pilot and document the single-instance assumption. |
| **Frontend bundle** | FullCalendar and Recharts load on first use of their pages, which is acceptable. No action. | — |

No rewrite and no change of stack is warranted. The layering is sound and the code is small (about 11,000 lines of C# outside migrations).

## 8. Keep, improve, simplify, remove

| Feature | Decision | Reason |
|---|---|---|
| Patients, calendar, chart, plans, billing | **Keep** | The core a clinic uses every day. Polish from real workflow friction only. |
| Revenue Recovery | **Improve** | Fix attribution, show the evidence behind each item, stop implying causation. |
| Follow-up rules engine | **Keep** | Already configurable and deterministic. |
| WhatsApp manual mode | **Keep** | The only mode that works for every clinic today. Label it clearly. |
| WhatsApp Cloud API mode | **Improve before enabling** | Needs per-clinic credentials, opt-out, quiet hours, inbound messages. |
| SMS and email channels | **Remove from the UI** | They do nothing. Bring them back when a provider is chosen. |
| AI assistant | **Keep, restricted** | Useful to an owner. Must not receive identifiers by default. |
| AI note structuring | **Keep, restricted** | Same condition. |
| Claude provider | **Keep** | Small, isolated, and the paid alternative when a clinic needs a data-processing agreement. |
| `SubscriptionPlan` text column | **Replace** | With a real status and dates. |
| Demo seeder | **Keep, Development only** | Essential for sales demos, dangerous in production. |
| Decorative dashboard charts | **Review** | The owner view should lead with actions. Remove any chart nobody acts on after pilot feedback. |

## 9. Competitors

Public information on dental software sold in Jordan is thin. Two searches in English and Arabic returned one Jordanian vendor and several regional or global listings. Everything below comes from vendor announcements or software directories and should be confirmed with the vendors or with clinics that use them.

| Product | What the sources say | Source |
|---|---|---|
| ESKA Clinics (ESKADENIA, Jordan) | Used by a dental and skin clinic group in Jordan. Patient registration, appointments, case management, treatment plans, invoicing and e-claims. | [Intaj announcement](https://intaj.net/diva-care-clinics-successfully-launched-eska-carenet-powered-by-eskadenia-software) |
| DentaLore (Nebras Technology, Egypt) | Appointment management, charting, reminders. Priced per user, with a free trial. Listed availability is Egypt. | [Capterra](https://www.capterra.ae/software/169865/dentalore) |
| BestoSys (UAE listing) | EMR, scheduling, billing, WhatsApp integration, NABIDH connectivity. | [Capterra UAE](https://www.capterra.ae/directory/20027/dental/pricing/free/software) |
| Dentronix | Starting price listed at US$135 flat rate, WhatsApp integration, confirmations and reminders. | [GetApp](https://www.getapp.sg/software/2094581/dentronix) |
| Dentolize | Dental practice management. The vendor does not publish prices. 4.4/5 from five reviews. | [Capterra](https://www.capterra.com/p/10006711/Dentolize/) |

What this does and does not support:

- **Revenue recovery is not unique.** Follow-up of unscheduled treatment is a standard feature of US practice-analytics products, described by several vendors ([Jarvis Analytics](https://www.jarvisanalytics.com/blog/unscheduled-treatment), [Solutionreach](https://solutionreach.com/blog/what-makes-the-best-dental-practice-management-software-in-2026-a-feature-level-breakdown)). Their recovery figures are self-reported.
- **WhatsApp integration exists in regional products.** It is not a differentiator on its own.
- **Nothing found shows a competitor's weakness.** I found no evidence about how well any of these products handle Arabic, migration or support in Jordan. Claims that TahonaOS is better at these would be unsupported.
- **Pricing is unknown.** No per-month price in JOD was found for any product sold in Jordan. The 20, 29 and 39–49 JOD hypotheses cannot be compared with anything yet.

The honest position: TahonaOS can compete on easy switching, a fast Arabic-first daily workflow and a transparent follow-up record. Whether clinics value those enough to pay has to come from interviews and the pilot, not from this audit.

## 10. Regulatory and platform facts to review before launch

These are pointers for a qualified review, not conclusions.

- **Jordan Personal Data Protection Law No. 24 of 2023.** Published 17 September 2023, effective 17 March 2024, with a one-year alignment period. Sources describe explicit consent for sensitive data and conditions on cross-border transfers, and they disagree on the current state of the regulator ([Al Tamimi](https://www.tamimi.com/news/jordans-new-data-protection-law-safeguarding-personal-data), [DataGuidance](https://www.dataguidance.com/jurisdiction/jordan)). Health records are sensitive data under any reading. Hosting location, the AI provider and WhatsApp all involve transfers outside Jordan. This needs a lawyer.
- **WhatsApp Business Platform.** Meta bills per template message since 1 July 2025, with rates by country and category. Free-form messages are only allowed inside the customer service window, and business-initiated messages need approved templates ([Meta pricing documentation](https://developers.facebook.com/docs/whatsapp/pricing)). Message costs belong in the pricing model.
- **Tax and invoicing.** `TaxRatePercent` is configurable and defaults to zero. No Jordanian tax rule is hard-coded, and none should be added without verification.

## 11. Prioritised plan

Ranked by customer value, effort, risk and commercial impact. Effort is in working days for one developer.

| # | Work | Customer value | Effort | Risk if skipped | Commercial impact |
|---|---|---|---|---|---|
| 1 | AI data minimisation (5.1) | Trust | 1 | Legal and reputational | Required to sell |
| 2 | Production guard for demo data (5.3), proxy headers (5.4) | Trust | 0.5 | Account takeover, broken throttling | Required to host |
| 3 | Booking lock (5.2) | Daily reliability | 1 | Double-bookings | High: the first thing a receptionist notices |
| 4 | Messaging safety: opt-out, quiet hours, retry cap, webhook hardening (5.5) | Trust | 2 | Spam, platform ban | Required before Cloud API |
| 5 | Reference validation (5.7), upload checks (5.8) | Trust | 1 | Integrity defects | Medium |
| 6 | Attribution fix and honest labels (5.6) | Credible value claim | 1 | Owner distrust | High: this is the pitch |
| 7 | Clinic provisioning by invitation, password recovery | Can onboard a clinic | 2 | Cannot sell | Required to sell |
| 8 | Import centre: patients, appointments, opening balances | Switching | 5 | Clinics will not move | The main adoption lever |
| 9 | Onboarding wizard and checklist | Switching | 2 | Slow, support-heavy onboarding | High |
| 10 | Data export with manifest | Trust, no lock-in | 2 | Objection in every sale | High |
| 11 | Subscription state and billing page (manual payment confirmation) | — | 2 | Cannot track who pays | Required to charge |
| 12 | Operator tooling | — | 2 | Manual database work | Medium |
| 13 | Object storage, backups with a tested restore | Trust | 2 | Data loss | Required to host |
| 14 | Import: treatment plans, payment history | Switching | 4 | Partial migrations | Medium |
| 15 | WhatsApp Cloud API per clinic, inbound threads | Daily speed | 5 | Manual sending stays | Medium until validated |
| 16 | Receptionist fast paths, owner view, outcome report | Daily speed, value proof | 5 | Slower adoption | Medium |
| 17 | Email delivery | Self-service recovery | 1 | Operator-assisted resets | Low for a pilot |

Items 1 to 6 are Phase 1. Items 7 to 9 are Phase 2. Phase 3 is item 16. Phase 4 is items 10 to 12. Phase 5 is item 13. Items 14, 15 and 17 follow pilot feedback.

## 12. Assumptions worth challenging

- **"Revenue Recovery is the differentiator."** It is a good feature and a common one. Switching effort is more likely to decide whether a clinic adopts.
- **"Clinics want AI."** No evidence either way. It adds a data-protection burden. It should stay optional and off for patient-level data until a clinic asks for it and the provider terms are reviewed.
- **"WhatsApp must be automated."** Many small clinics already message patients from the receptionist's phone. The manual mode with a good log may be enough for the pilot and costs nothing per message.
- **"20–49 JOD per month."** Untested. At 20 JOD a clinic that needs two hours of support a month is unprofitable. Onboarding time per clinic is the number to measure first.
