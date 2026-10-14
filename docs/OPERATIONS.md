# Operations guide

For whoever runs a TahonaOS deployment: creating clinics, managing subscriptions, backups, and the settings that affect patient data.

## Operator commands

Operator commands run on the server with the same configuration as the API. There is no web address for them: whoever can run them already controls the deployment. They work on account data and never print patient records.

```bash
# From source
cd backend/src/Tahona.Api && dotnet run --no-launch-profile -- operator <command> [options]

# In Docker
docker compose exec api dotnet Tahona.Api.dll operator <command> [options]
```

Add `--operator "<your name>"` to any command, or set `OPERATOR_NAME`. The name is recorded with the action.

| Command | What it does |
|---|---|
| `invite --email <address> [--clinic <name>] [--contact <name>] [--plan Founding\|Standard] [--trial-days 30] [--valid-days 14] [--note <text>]` | Issues a sign-up invitation and prints the link |
| `invitations` | Lists invitations and their state |
| `revoke-invitation --id <id>` | Cancels an open invitation |
| `clinics` | Lists clinics with status, plan, users, patients, appointments in 30 days, last login and onboarding date |
| `metrics` | Business figures across real clinics: clinics by status, how many finished onboarding and the median days it took, clinics and staff active in the last 7 days, bookings, follow-up contacts, imports, and churn |
| `set-status --clinic <slug or id> --status Trial\|Active\|PastDue\|Cancelled\|Suspended [--reason <text>] [--paid-until yyyy-MM-dd]` | Changes the subscription state |
| `archive --clinic <slug or id> [--reason <text>]` | Signs the clinic out. Data is kept |
| `restore --clinic <slug or id>` | Reverses an archive |
| `reset-link --email <address>` | Issues a single-use password reset link, valid 24 hours |

### Creating a clinic

1. Run `operator invite --email owner@clinic.example --clinic "Clinic name"`.
2. With a mail server configured, the link is e-mailed to the owner. Otherwise send the printed link yourself. It is shown once and cannot be recovered; issue a new invitation if it is lost.
3. The owner opens the link, names the clinic, chooses a password, and lands on the **Getting started** checklist.

The link uses `APP__PUBLICURL` as the address of the web app. An invitation works once, for 14 days by default. Issuing a new one for the same address cancels the earlier link.

### Resetting a password

- **With a mail server configured:** anyone can use **Forgot your password?** on the sign-in page. The answer is the same whether or not the address has an account, and a second request within two minutes is ignored.
- **Staff member:** the clinic administrator resets it under **Staff**.
- **Clinic administrator:** confirm who is asking, by phone on a number you already hold, then run `operator reset-link --email <address>` and send the link to that person only. Using it signs out every other session of that user.

## Subscription policy

| Status | What the clinic can do |
|---|---|
| Trial, Active, PastDue | Everything |
| Cancelled, Suspended | Sign in, view every record, export. Nothing can be changed |
| Archived | Nobody can sign in. Data is kept |

A clinic is never locked out of its own patient records because of billing. A status change takes effect within 30 seconds.

**Nothing changes status automatically.** A trial that ends stays a trial until you change it; the clinic sees a banner. Suspend only after contacting the clinic.

Clinic administrators see their plan, status, dates and how to pay under **Settings → Subscription**. Set `APP__PAYMENTINSTRUCTIONS` and `APP__SUPPORTCONTACT` so that page tells them what to do.

**Payment is manual for now.** When a clinic pays, record it:

```bash
operator set-status --clinic <slug> --status Active --paid-until 2026-11-30 --reason "October invoice paid by CliQ"
```

Each change is written to the clinic's audit log with your name.

**Before you archive a clinic**, tell the administrator to download an export from **Settings → Your data**. There is no deletion command yet; deleting a clinic's data is a manual database operation that needs its own written procedure.

## Backups

`scripts/backup.sh` dumps the database from the `tahona-db` container, checks that the dump can be read, also archives uploaded files when the API container is running, writes checksums, and removes backups older than `RETENTION_DAYS` (default 30).

```bash
scripts/backup.sh /var/backups/tahona
```

Schedule it daily and copy the output directory to another machine. A backup on the same disk does not protect against losing that disk.

`scripts/restore.sh` restores a dump into a **new** database and prints row counts. It refuses to touch a database that already exists.

```bash
scripts/restore.sh /var/backups/tahona/tahona-db-20261012-165225.dump tahona_restore_check --compare tahona
docker exec tahona-db dropdb -U tahona tahona_restore_check
```

To put a restored copy into service: stop the API, point `ConnectionStrings__Default` at the restored database, start the API.

**What has been tested:** on 12 October 2026 a dump of the development database was restored into a separate database and the row counts matched for 11 tables. The uploaded-files archive was taken from a running container and contained the uploaded file.

**What has not:** restoring the files archive, and any restore on a hosted system. Run a restore drill after the first deployment and every few months after.

**With S3 storage** the script has no local files to archive. Turn on versioning or replication for the bucket instead; the database backup is still needed.

## File storage

By default uploaded files go to a folder (`Storage__Root`), which in Docker is the `tahona-files` volume. For a real deployment use a private S3-compatible bucket:

```
STORAGE__PROVIDER=s3
STORAGE__S3__BUCKET=...
STORAGE__S3__REGION=...
STORAGE__S3__ACCESSKEY=...
STORAGE__S3__SECRETKEY=...
# Only for S3-compatible services (R2, Spaces, MinIO):
STORAGE__S3__SERVICEURL=https://...
STORAGE__S3__FORCEPATHSTYLE=true
```

Files are stored under `{clinic}/{patient}/` and are only ever read through the API, which checks the user and the clinic. The bucket must not be public. Switching provider does not move existing files; copy them across first, keeping the same paths.

**A volume created by an older image** is owned by root and uploads fail with a server error. Fix it once:

```bash
docker compose exec -u root api sh -c 'chown -R $APP_UID /data/storage'
```

This was tested against an S3-compatible mock server, not yet against a real provider.

## E-mail

Set `EMAIL__HOST`, `EMAIL__PORT`, `EMAIL__FROM` and, if the server needs them, `EMAIL__USERNAME` and `EMAIL__PASSWORD`. `EMAIL__SECURITY` is `starttls` (default), `ssl`, or `none` for a local test server.

With it configured, invitations and password-reset links are e-mailed in Arabic and English, and the sign-in page offers self-service reset. If a send fails, the operator command still prints the link. Recipient addresses are not written to the logs.

This was tested against a local SMTP test server without TLS or a password. Send yourself an invitation through the real provider before relying on it.

## Patient data and external services

### AI provider

`AI__PATIENTDATA` decides what leaves the server:

| Value | The provider receives |
|---|---|
| `minimized` (default) | Clinic figures and patient rows identified by file number. No names, no phone numbers |
| `off` | Clinic-wide totals only. Clinical-note structuring runs on the server without the provider |
| `full` | Names and phone numbers as well |

Limits of `minimized`:

- A staff member's own question is sent as typed. If they type a patient's name, the provider sees it.
- The text a dentist dictates for note structuring is sent as written.

Google's terms for the free Gemini tier say submitted content is used to improve its products, may be read by reviewers, and must not contain personal information. Do not use a free key for a real clinic. Use `full` only under a data-processing agreement that covers patient data. Each clinic can also switch the assistant off in **Settings**.

### WhatsApp

Each clinic connects **its own** WhatsApp Business number under **Settings → Integrations**, with the phone number ID and a permanent access token from its Meta business account. Before anything is saved the details are checked with Meta. The token is encrypted with `SECRETS__ENCRYPTIONKEY` and never shown again. A number can belong to one clinic only.

A clinic that has not connected a number is in **manual mode**: each message opens in WhatsApp for a staff member to send and is recorded as "awaiting manual send" until they confirm it. Nothing is sent automatically, and nothing is reported as sent that was not.

For a clinic with a connected number:

- Automatic reminders and follow-ups go only to patients whose consent is recorded as agreed, unless the clinic turns that requirement off, and never during the clinic's quiet hours.
- A reminder is attempted at most three times. An automatic follow-up is attempted once.
- A reply of "STOP", "إيقاف" or similar withdraws the patient's consent, and staff are notified.
- Free text is only accepted within 24 hours of the patient's last message. Otherwise the message template needs an approved WhatsApp template name.
- A patient's message is filed under the clinic whose number received it.

Deployment setup, once:

1. Set `SECRETS__ENCRYPTIONKEY` (`openssl rand -base64 32`). Without it, clinics cannot connect a number. If the key is lost or changed, stored tokens become unreadable and clinics reconnect; their messages fall back to manual sending in the meantime.
2. In Meta, point the app's webhook at `https://<host>/api/webhooks/whatsapp` and set `WHATSAPP__WEBHOOKVERIFYTOKEN` and `WHATSAPP__APPSECRET`. Requests without a valid signature are rejected.

`WHATSAPP__ACCESSTOKEN` and `WHATSAPP__PHONENUMBERID` set one number for the whole installation. Use them only when a single clinic runs its own copy.

Meta charges per template message. Check its current rates for Jordan before a clinic turns on automatic sending.

The credential check has reached Meta and rejected a fake token. Sending, delivery status, replies and opt-out have tests but have not been tried with a real number.

### What operators can see

The operator commands show account state and counts. They cannot read patient names, clinical notes or invoices. To help a clinic inside its data, ask its administrator to create a user for you with the role you need, and to deactivate it afterwards. That user's actions appear in the clinic's audit log like anyone else's.

## Deployment settings

| Setting | Purpose |
|---|---|
| `APP__PUBLICURL` | Address of the web app, used in invitation and reset links |
| `APP__SUPPORTCONTACT`, `APP__PAYMENTINSTRUCTIONS` | Shown to clinic administrators under Settings → Subscription |
| `SECRETS__ENCRYPTIONKEY` | Encrypts stored WhatsApp tokens |
| `ASPNETCORE_ENVIRONMENT` | Must be `Production` on a server. Docker Compose passes this through from `.env`, so a `.env` copied from a development machine runs the container in Development |
| `PROXY__TRUSTEDNETWORKS` | CIDR ranges of proxies allowed to set the client address. Needed for per-address login limits and correct audit entries |
| `CORS__ORIGINS` | Web origins allowed to call the API with credentials |
| `SEED__DEMODATA` | Development only. The API will not start with it elsewhere unless `SEED__ALLOWOUTSIDEDEVELOPMENT=true` |
| `Database__AutoMigrate` | Applies migrations at startup. Run a single API instance while migrating |
| `AUTOMATION__ENABLED` | The follow-up engine. Each clinic is processed by one instance at a time |

Health checks: `/health` answers when the process is up, `/health/ready` when the database also answers.

Logs: outside Development every log entry is one JSON object per line. Each API request produces one line with the method, the route template, the status, the duration, and the clinic and user ids. Query strings and request bodies are never logged. Server errors are logged at warning level, which is the line to alert on once a log collector is in place.

## If something goes wrong

- **A clinic reports wrong data after an import.** Open **Import data**, download the report for that import, and use **Undo import**. Records already in use are kept and listed.
- **A clinic cannot sign in.** Check `operator clinics`: an archived clinic cannot sign in; a suspended one can.
- **Suspected account compromise.** Issue a reset link for the user, which ends all of their sessions, and review the clinic's audit log with the administrator.
- **Suspected data breach.** There is no written incident procedure yet. At a minimum: preserve the logs and a database backup, rotate `JWT__SECRET` (this signs everyone out) and every provider key, and take legal advice on notification duties before contacting clinics.
