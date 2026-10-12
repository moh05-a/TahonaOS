# Revenue Recovery Engine — how it works

The Revenue Recovery Engine answers one question every morning:

> **Which patients should we contact today, and how much revenue do they represent?**

It has four parts:

1. **Detection.** It finds revenue leaks in clinic data.
2. **Follow-up automation.** It turns leaks into tasks, using configurable rules.
3. **Priority scoring.** It ranks patients from 0 to 100.
4. **Attribution.** It measures how much revenue was actually recovered.

Code: `backend/src/Tahona.Application/Features/Recovery/`
(`RecoveryDetector.cs`, `RecoveryScorer.cs`, `RecoveryService.cs`, `RecoverySyncService.cs`, `RecoveryAttributionService.cs`).

---

## 1. Detection

Detection is driven by the clinic's **follow-up rules** (Settings → Follow-up rules). Each rule says *"when X happens, act after N days"*. A disabled rule switches its detector off.

| Issue | Rule trigger (default delay) | Condition | Value counted |
|---|---|---|---|
| **Treatment not started** | Plan not started (3 days) | Plan is *Presented* or *Accepted*, no item completed, not booked, older than the delay | Remaining plan value (after plan discount) |
| **Treatment unfinished** | Treatment unfinished (14 days) | Plan is *In progress* or *Partially completed*, items still planned, no activity for the delay | Value of the remaining items |
| **Missed appointment** | Missed appointment (1 day) | No-show within the look-back window and not rebooked since | Fixed estimate (default 40 JOD). Zero if the visit belongs to a plan already counted, so nothing is counted twice |
| **Outstanding balance** | Overdue balance (0 days) | Issued or partially paid invoices past their due date (one issue per patient) | Total overdue balance |
| **Overdue follow-up** | Procedure completed, per category (Endodontic 14 days, Oral surgery 7 days) | A procedure that needs a review, and the patient has not been back | Review-visit value (default 25 JOD) |
| **Recall due** | Routine recall (180 days) | Active patient, last visit older than the delay, and no other open issue | Recall value (default 35 JOD) |

**Global guards:**
- Patients with an upcoming or in-progress appointment are never flagged. They'll be handled at the chair.
- Archived patients are excluded.
- Issues older than **Maximum look-back** (default 540 days) are treated as lost and not surfaced.

The patient's **potential revenue** is the sum of their issue values. The values are de-duplicated by construction: plan values cover only items not yet invoiced or completed, and missed visits tied to a counted plan are worth zero.

## 2. Follow-up workflow

Each detected issue maps to a `FollowUp` task, keyed by issue type and source record. It moves through these stages:

```
New ──contact──▶ Contacted ──(re-contact after N days)──▶ Re-contact due
  │                 │
  │                 ├── patient books ─────────▶ Booked
  │                 ├── patient pays ──────────▶ Recovered (amount attributed)
  │                 └── "not interested" ──────▶ Dismissed (never resurfaces)
  └── issue clears without outreach ──────────▶ Resolved (not counted as recovered)
```

- **Re-contact cadence.** Plan issues use the *"Plan still inactive"* rule (default 7 days). Other issues use **Re-contact after** (default 5 days).
- **Booking reopens on failure.** If a booking falls through (cancelled, no-show), the task re-opens.
- **The background worker** (`RecoveryAutomationWorker`) runs every 15 minutes per clinic. It creates tasks, auto-resolves cleared ones, and notifies staff:
  - **Urgent** for high-value inactive treatment
  - **High** for missed appointments
  - Billing staff for overdue balances
- **Optional auto-send.** If a WhatsApp Cloud API provider is configured and the rule has *Auto-send*, the template is sent automatically.

## 3. Priority score (0–100)

The score is a weighted average of four factors, each normalised to 0–1:

```
score = 100 × (wV·Value + wU·Urgency + wL·Likelihood + wS·Severity) / (wV + wU + wL + wS)
```

| Factor | Formula | Intuition |
|---|---|---|
| **Value** | `min(1, totalValue / HighValueThreshold) ^ 0.6` | Bigger opportunities first, with diminishing returns. The threshold defaults to 400 JOD |
| **Urgency** | `min(1, daysPastDue / 21)` | How long the follow-up has been overdue |
| **Likelihood** | `0.5·recency + 0.3·loyalty + 0.2·reliability (+0.1 if a plan was accepted)` | Probability the patient comes back |
| | `recency = e^(−daysSinceLastVisit / 120)` | Recently seen patients are far easier to bring back |
| | `loyalty = min(1, completedVisits / 5)` | Established relationship |
| | `reliability = 1 − min(1, noShows₍₁₂ₘ₎ × 0.25)` | Penalises habitual no-shows |
| **Severity** | `max(issue severity) + 0.1 × (distinct issues − 1)`, capped at 1 | Clinical and financial importance |

**Issue severities:**

| Issue | Severity |
|---|---|
| Unfinished treatment | 1.0 |
| Not started | 0.8 |
| Missed appointment | 0.75 |
| Balance | 0.7 |
| Clinical follow-up | 0.65 |
| Recall | 0.4 |

**Priority bands:**

| Score | Priority |
|---|---|
| ≥ 70 | **HIGH** |
| 45–69 | **MEDIUM** |
| < 45 | **LOW** |

Likelihood labels are **High** at ≥ 0.65, **Medium** at ≥ 0.40, and **Low** below that.

### Worked example (from the product brief)

Ahmad Khalil has a 450 JOD plan (root canal + crown + X-ray) that was presented 32 days ago and never booked. He has 3 completed visits and no no-shows, and the rule delay is 3 days, so the follow-up is 29 days overdue.

| Factor | Value | Weight | Points |
|---|---|---|---|
| Value | (450/400 → 1)^0.6 = **1.00** | 35 | 35.0 |
| Urgency | 29/21 → **1.00** | 20 | 20.0 |
| Likelihood | 0.5·e^(−32/120) + 0.3·0.6 + 0.2·1 = **0.76** | 25 | 19.1 |
| Severity | Not started = **0.80** | 20 | 16.0 |
| **Score** | | | **≈ 90 → HIGH** |

The app shows this breakdown when you click a patient on the Revenue Recovery page ("Why this score?").

### Configuration

All values live in **Settings → Recovery engine** and are stored per clinic in `ClinicSettings.Recovery`:

- **Weights:** `WeightValue`, `WeightUrgency`, `WeightLikelihood`, `WeightSeverity`
- **Priority thresholds:** `HighPriorityThreshold`, `MediumPriorityThreshold`
- **Valuation:** `HighValueThreshold`, `RecallEstimatedValue`, `MissedAppointmentDefaultValue`, `FollowUpVisitValue`
- **Windows:** `AttributionWindowDays`, `RecontactAfterDays`, `MissedAppointmentLookbackDays`, `MaxLookbackDays`

The scorer is a pure function covered by unit tests (`backend/tests/Tahona.Tests/RecoveryScorerTests.cs`).

## 4. Recovered revenue (attribution)

A payment counts as **recovered** only when the clinic actually contacted the patient about a recovery issue, by WhatsApp or a logged phone call, within the **attribution window** (default 45 days) before the payment.

- Patients who return on their own are **not** credited to the engine. This keeps the "Recovered" number honest.
- Refunds of recovered payments reduce the recovered total.
- The **recovery rate** is the share of follow-ups contacted in the last 90 days that ended *Booked* or *Recovered*.
