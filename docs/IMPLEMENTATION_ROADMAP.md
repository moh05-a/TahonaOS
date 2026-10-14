# Implementation roadmap

Last updated: 12 October 2026. This tracks the work that follows [PRODUCT_AUDIT.md](PRODUCT_AUDIT.md): what is done, how it was verified, and what is still open.

## Where things stand

| Phase | State |
|---|---|
| 0. Audit | Done |
| 1. Launch blockers | Done, except one item listed under "Still open" |
| 2. Switching and onboarding | Done, except payment-history import |
| 3. Daily usability and differentiation | Front desk, follow-up evidence, outcomes and the owner report done |
| 4. SaaS operations | Subscription states, account page, operator commands, export, e-mail and per-clinic WhatsApp done. No payment provider, no web admin area |
| 5. Production readiness | Docker stack verified in Production mode, S3 storage, structured logs, backup and restore done. No hosted deployment |

**The software side of a supervised pilot is in place. Three things outside the code still block a real clinic:** a legal review of patient-data handling, a hosted deployment with TLS, and one trial of each external service with real credentials. See [Before the first real clinic](#before-the-first-real-clinic).

## Checks at this point

| Check | Result |
|---|---|
| Backend tests | 174 pass. Four run against real PostgreSQL, one against an S3-compatible server, one against an SMTP test server |
| Frontend type check and production build | Pass |
| Frontend lint | 0 errors, 45 warnings (44 at the baseline; the new one is the same set-state-in-effect pattern, in the import wizard) |
| Docker stack, Production mode | Sign-up from an e-mailed invitation, three imports, uploads to S3 storage and forgot-password by e-mail, all behind nginx with the security policy active |
| Browser run on the demo data | Existing and new pages in English and Arabic, no page errors and no failed API calls |
| Backup and restore | Database dump restored into a separate database with matching row counts. Uploaded files archived from the container |

The service-dependent tests are reported as skipped unless their environment variable is set: `TAHONA_TEST_POSTGRES`, `TAHONA_TEST_S3`, `TAHONA_TEST_SMTP`.

### What running it in Docker found

Three defects that the tests could not have shown, all fixed:

- **Security headers were not being sent.** In nginx an `add_header` inside a `location` cancels the ones declared for the server, so the content security policy and frame protection never reached the browser.
- **Uploads failed.** The document volume was owned by root while the API runs as an unprivileged user. A volume created by an older image needs a one-off `chown`; see [OPERATIONS.md](OPERATIONS.md).
- **A missing system library** logged an error at every start.

The first Docker run was also not a Production run: the local `.env` sets `ASPNETCORE_ENVIRONMENT=Development` and Compose passes it through. The checks above were repeated with Production forced.

## Phase 1: launch blockers

| Item | What changed | Verified by |
|---|---|---|
| Patient identifiers sent to the AI provider | `Ai:PatientData`, default `minimized`: file numbers instead of names and phone numbers. `off` sends totals only. `full` is an explicit choice | Tests on what the provider receives, and a live Gemini answer that cited `P-00041` instead of a name |
| Double-booking under concurrency | Create, edit and move run in one transaction under an advisory lock per dentist and per patient | PostgreSQL tests with 12 simultaneous bookings; they fail when the lock is removed |
| Demo accounts in production | The API refuses to start with demo data outside Development. The login page lists demo accounts only in development builds | A Production container started with demo data refused to start |
| Client address behind a proxy | Forwarded headers are honoured from `Proxy:TrustedNetworks` only | Audit entries in the Docker stack recorded the client address, not the nginx container's |
| Automated messages | Consent (not recorded, agreed, opted out), quiet hours, three attempts for a reminder, one for an automatic follow-up, one API instance per clinic at a time | Tests for each rule |
| Channels that did nothing | SMS and e-mail to patients are refused instead of logging a fake pending row | Test and a live API call |
| Webhook | Signature over the raw bytes, replies stored, duplicate events ignored, "STOP" and Arabic equivalents withdraw consent, statuses only move forward | Tests. Never exercised with events from Meta |
| WhatsApp service window | Through the Cloud API, free text needs a patient message in the last 24 hours; otherwise an approved template | Test |
| Recovered revenue overstated | A payment is counted only when it settles an invoice for the matter the clinic followed up on. Labels say "collected after follow-up" | Tests for each issue type and refund reversal |
| Unchecked references | Follow-up, appointment and dentist identifiers are checked against the patient and the clinic | Tests, including another clinic's records |
| Upload validation | File contents are inspected; the browser's content type is ignored | Tests, and an HTML file renamed `.png` rejected by the Docker stack |
| Session cookie | HttpOnly, Secure, SameSite=Strict, limited to `/api/auth` | Read from a Production response |
| Health checks and security headers | `/health`, `/health/ready`; content security policy with no inline script | Live calls; the browser flow passes under the policy |
| Logs | JSON, one object per line outside Development. One line per request with route template, status, duration, clinic and user. No query strings or bodies | Read from the Production container |

Still open: staff creation reveals whether an e-mail address already has an account in another clinic. Closing it means changing how accounts are identified at sign-in.

## Phase 2: switching and onboarding

| Item | What changed | Verified by |
|---|---|---|
| Creating a clinic | An operator issues an invitation; the owner names the clinic and chooses a password. That creates the clinic, roles, first administrator, starter price list, templates and follow-up rules | Tests, and browser runs on an empty database |
| Invitation safety | Random token stored as a hash, valid 14 days, single use, cancelled by a newer one | Tests |
| Password recovery | Single-use link valid 24 hours that ends other sessions. By e-mail when a mail server is configured; otherwise issued by an operator | Tests, and end to end through a test mail server |
| Onboarding checklist | Ten steps, some ticking themselves from clinic data, with figures to compare against the previous system | Tests and browser runs |
| Import: files | CSV (comma, semicolon or tab; UTF-8 or Windows-1256) and Excel. Up to 10 MB and 20,000 rows | Tests, including a generated workbook |
| Import: mapping, dry run, commit | Columns matched from English and Arabic headers. Preview writes nothing. Commit is one transaction and cannot run twice | Tests, including simultaneous commits and a crash half-way on PostgreSQL |
| Import: duplicates | Already imported, exact, possible, and repeated in the file. A person decides per row. "Fill empty fields" never overwrites | Tests |
| Import: patients, appointments, opening balances | Nothing is invented: a past appointment with no status is rejected unless the person chooses otherwise, and a balance becomes one marked invoice with no payment history | Tests and browser runs |
| Import: treatment plans | One row per procedure, grouped into plans. A plan is imported whole or not at all. A plan with no status becomes a draft. No treatment history or invoices are created | Tests, and a browser run through the wizard on the demo data: two plans imported, a plan with one bad row held back whole, an unknown patient rejected, then the import undone |
| Import: report and undo | Every source row with its result as CSV. Undo removes records nobody has used since | Tests |
| Closed days | The clinic lists dates it is closed. The front desk and the booking dialog warn; booking stays possible | Tests; the settings editor was checked in a browser |

Still open:

- **Payment history** is not imported. Opening balances carry what patients owe.
- **Dentists and staff** are added on the Staff page, not imported.
- **Import row messages are English only**, and the Arabic interface says so.
- **Excel files with merged or multi-row headers** are not handled.
- **No single set-up form.** The checklist links to the existing settings pages.

### Assisted migrations

1. Ask the clinic for exports and convert them to the templates from **Import data → Download a template**.
2. Sign in with an account the clinic administrator created for you. Operators have no access to clinic records by default.
3. Upload each file and download the report from the check step. Fix the listed rows in the file, not in the database.
4. Have the administrator compare the checklist figures with the previous system before pressing Import.
5. The administrator deactivates your account afterwards. Every import and its author stay on record.

## Phase 3: daily usability and differentiation

| Item | What changed | Verified by |
|---|---|---|
| Front desk | Today's list in time order with one-click confirm, arrived, start, complete and no-show. Late patients and balances flagged. Find, register, book, take payment and message without leaving the page. Keys: `/`, `N`, `B` | Tests, and a browser run including a real check-in |
| Evidence behind a follow-up | For each item: the record that raised it, the date and age of the event, the contacts made, the recorded outcome, any booking, and what was collected afterwards | Tests and a browser run |
| Payments | A payment can be taken for something not invoiced yet: the invoice is created with it. Staff who cannot manage invoices must take such a charge in full. Method, date, reference and notes of an entry can be corrected. An entry made by mistake is cancelled with a reason: the invoice and any follow-up credit go back, the entry stays on record with its receipt number, and it is in the export | Tests, and a browser run as administrator and receptionist in English and Arabic |
| Outcomes | No response, call back later, declined, already handled, unreachable, not applicable. Only real attempts are counted | Tests |
| Owner report | The last 30 days beside the 30 before: follow-ups made, bookings and payments that followed, no-show rate, returning patients, collections. States that it shows recorded events, not cause | Tests and a browser run |

Still open:

- Follow-up tasks cannot be assigned to a named staff member in advance.
- A payment cannot be held as credit before any charge exists, and a payment entry on the invoice page can be refunded but not corrected or cancelled from there (use Payments or the patient page).
- The dashboard still has charts nobody may act on. Decide after pilot feedback.
- The front desk has no drag-to-reschedule; that stays on the calendar page.

## Phase 4: SaaS operations

| Item | What changed | Verified by |
|---|---|---|
| Subscription states and policy | Trial, active, past due, cancelled, suspended. Cancelled and suspended are read-only with export. Archived clinics are signed out | Tests |
| Account page | Plan, status, dates, usage, how to pay and whom to contact, under **Settings → Subscription** | Test, and read in Production |
| Operator commands | Invite, list, change status, archive, restore, reset link, and business metrics. Changes are written to the clinic's audit log. No patient data | Tests; run against live databases |
| Data export | One ZIP of 19 CSV files with a manifest, optionally with uploaded files. Available when read-only | Tests and a live export |
| E-mail | Invitations and reset links by SMTP when configured. A failed send never loses the link | Tests, and a real delivery to a test mail server with Arabic text intact |
| Per-clinic WhatsApp | Each clinic connects its own number. Credentials are checked with Meta, the token is encrypted at rest and never returned, a number belongs to one clinic, and replies are routed by the receiving number | Tests. A live check reached Meta, which rejected a fake token. Sending and receiving with a real number have not been tried |

Still open:

- **No payment provider.** An operator records payments with `set-status --paid-until`. No provider available to a Jordanian business has been investigated.
- **Nothing changes status automatically.** A trial that ends stays a trial until an operator changes it.
- **No web admin area.** Operator commands need server access.
- **No account deletion command**, and no written retention policy.

## Phase 5: production readiness

| Item | What changed | Verified by |
|---|---|---|
| Docker stack | API and web images build and run in Production mode behind nginx | Full browser flow, see above |
| Durable file storage | S3-compatible storage selectable by configuration. Files stay private and are read only through the API | Test against an S3-compatible mock server, and uploads through the Docker stack. Not tried against AWS or another real provider |
| Backups | `scripts/backup.sh` and `scripts/restore.sh` | Database restore with matching counts; the file archive taken from the container contained the uploaded file. Restoring the file archive was not tried |
| Logs | Structured, with per-request lines | Read from the container |

Still open:

- **No hosted deployment**, so no TLS, domain or firewall has been set up or tested.
- **No alerting.** The logs are ready for a collector; none is configured.
- **With S3 storage, file backup is the bucket's job** (versioning or replication). The script only archives the local volume.
- **No written incident-response or retention policy.**

## Before the first real clinic

In order:

1. **Legal review** of patient-data handling under Jordan's Personal Data Protection Law: hosting location, the AI provider, WhatsApp, retention and deletion. Until then keep `AI__PATIENTDATA` at `minimized` or `off` and do not use a free-tier AI key.
2. **Host it**: a server with TLS in front, `ASPNETCORE_ENVIRONMENT=Production`, `PROXY__TRUSTEDNETWORKS`, `SECRETS__ENCRYPTIONKEY`, a real bucket and a real mail provider. Then run one restore drill there.
3. **Try each external service once with real credentials**: connect one WhatsApp Business number and send, receive and opt out; send an invitation through the real mail provider; upload to the real bucket.
4. **Write down the payment procedure** and put it in `APP__PAYMENTINSTRUCTIONS`.
5. **Run one full onboarding with a friendly clinic's real export**, with you present, and time it. That number decides whether the pricing works.

## Manual set-up required

| What | Where |
|---|---|
| Web address used in links | `APP__PUBLICURL` |
| Support contact and payment instructions shown to clinics | `APP__SUPPORTCONTACT`, `APP__PAYMENTINSTRUCTIONS` |
| Key that encrypts stored WhatsApp tokens | `SECRETS__ENCRYPTIONKEY` |
| Mail server | `EMAIL__HOST`, `EMAIL__PORT`, `EMAIL__USERNAME`, `EMAIL__PASSWORD`, `EMAIL__FROM`, `EMAIL__SECURITY` |
| File storage bucket | `STORAGE__PROVIDER=s3` and `STORAGE__S3__*` |
| Trusted proxy ranges | `PROXY__TRUSTEDNETWORKS` |
| AI data level | `AI__PATIENTDATA` (default `minimized`) |
| A paid AI key under a data-processing agreement, if AI is used with real patients | `AI__APIKEY` |
| WhatsApp webhook | `WHATSAPP__APPSECRET`, `WHATSAPP__WEBHOOKVERIFYTOKEN`, and the webhook address in Meta |
| Each clinic's WhatsApp number and approved template names | **Settings → Integrations** and **Messages → Templates** |
| Services for the optional tests | `TAHONA_TEST_POSTGRES`, `TAHONA_TEST_S3`, `TAHONA_TEST_SMTP` |
| Daily backup job | cron running `scripts/backup.sh` |
