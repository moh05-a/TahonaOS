using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Billing;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Ai;

/// <summary>A table of real records returned by a tool; shown to the user beside the answer as evidence.</summary>
public record DataTableDto(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<Guid?>? RowPatientIds = null);

/// <summary>Result of a tool: compact JSON for the model + a table for the UI.</summary>
public record ToolOutput(object ModelPayload, DataTableDto? Table);

/// <summary>
/// The read-only, tenant-scoped query tools the assistant can use. Every number and name in an answer
/// must come from one of these. They never write data.
/// </summary>
public class AssistantTools(
    IAppDbContext db,
    RecoveryService recovery,
    InvoiceService invoices,
    ISearchProvider search,
    IClinicTime clinicTime,
    IClock clock,
    IAiProvider ai,
    IAiDataPolicy policy)
{
    private const int MaxRows = 25;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>Tools that return totals only; the only ones offered when Ai:PatientData is Off.</summary>
    public static readonly IReadOnlySet<string> AggregateTools =
        new HashSet<string> { "get_recovery_summary", "get_revenue_summary", "get_appointment_stats" };

    /// <summary>
    /// True when tool results may carry names and phone numbers. Without an AI provider nothing leaves the server,
    /// so the built-in mode always sees full rows.
    /// </summary>
    private bool Identifiable => !ai.IsConfigured || policy.PatientData == AiPatientDataMode.Full;

    /// <summary>What the model sees for a patient: the name, or only the file number when data is minimised.</summary>
    private string Who(string name, string fileNumber) => Identifiable ? name : fileNumber;

    private string? Phone(string? phone) => Identifiable ? phone : null;

    /// <summary>What staff see in the evidence table: the file number is added so answers that cite it can be matched.</summary>
    private string Cell(string name, string fileNumber) => Identifiable ? name : $"{name} ({fileNumber})";

    private async Task<Dictionary<Guid, string>> FileNumbersAsync(IEnumerable<Guid> patientIds, CancellationToken ct)
    {
        var ids = patientIds.Distinct().ToList();
        return await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FileNumber, ct);
    }

    public record Definition(string Name, string Description, string SchemaJson);

    public static readonly IReadOnlyList<Definition> Definitions =
    [
        new("get_recovery_summary",
            "Revenue Recovery overview: potential (at-risk) value, payments collected after follow-up this and last month, patients to contact, counts per issue type, and the share of contacted patients who booked or paid. Potential value is an estimate, not money owed.",
            """{"type":"object","properties":{},"required":[]}"""),
        new("list_recovery_patients",
            "Patients the clinic should contact, ranked by recovery score. Filter by issue type, minimum value or priority. Issue types: TreatmentNotStarted, TreatmentUnfinished, MissedAppointment, OutstandingBalance, OverdueFollowUp, RecallDue.",
            """{"type":"object","properties":{"issue_type":{"type":"string","enum":["TreatmentNotStarted","TreatmentUnfinished","MissedAppointment","OutstandingBalance","OverdueFollowUp","RecallDue"]},"min_value":{"type":"number","description":"Minimum total value in clinic currency"},"priority":{"type":"string","enum":["High","Medium","Low"]},"limit":{"type":"integer","minimum":1,"maximum":25}},"required":[]}"""),
        new("list_outstanding_balances",
            "Patients who owe money, largest balance first, with days overdue and last payment date.",
            """{"type":"object","properties":{"limit":{"type":"integer","minimum":1,"maximum":25}},"required":[]}"""),
        new("list_missed_appointments",
            "No-show appointments between two dates (inclusive, clinic local dates YYYY-MM-DD), and whether each patient has rebooked.",
            """{"type":"object","properties":{"from_date":{"type":"string","description":"YYYY-MM-DD"},"to_date":{"type":"string","description":"YYYY-MM-DD"}},"required":["from_date","to_date"]}"""),
        new("list_inactive_patients",
            "Active patients whose last completed visit was more than N months ago and who have no upcoming appointment.",
            """{"type":"object","properties":{"months":{"type":"integer","minimum":1,"maximum":60},"limit":{"type":"integer","minimum":1,"maximum":25}},"required":["months"]}"""),
        new("list_treatment_plans",
            "Treatment plans with remaining value. Filter by status (Draft, Presented, Accepted, InProgress, PartiallyCompleted, Completed, Rejected), only_not_started, only_unfinished, min_remaining_value.",
            """{"type":"object","properties":{"status":{"type":"string","enum":["Draft","Presented","Accepted","InProgress","PartiallyCompleted","Completed","Rejected"]},"only_not_started":{"type":"boolean"},"only_unfinished":{"type":"boolean"},"min_remaining_value":{"type":"number"},"limit":{"type":"integer","minimum":1,"maximum":25}},"required":[]}"""),
        new("get_revenue_summary",
            "Financial totals between two clinic-local dates: collected revenue, refunds, expenses, invoiced amount, production, and 'recovered' = payments collected after the clinic followed up with the patient about the same matter (a recorded sequence, not proof that the follow-up caused the payment).",
            """{"type":"object","properties":{"from_date":{"type":"string","description":"YYYY-MM-DD"},"to_date":{"type":"string","description":"YYYY-MM-DD"}},"required":["from_date","to_date"]}"""),
        new("get_appointment_stats",
            "Appointment counts by status and by dentist between two clinic-local dates.",
            """{"type":"object","properties":{"from_date":{"type":"string"},"to_date":{"type":"string"}},"required":["from_date","to_date"]}"""),
        new("find_patient",
            "Look up patients by name (English or Arabic), phone number or file number and return their key facts: visits, balance, open treatment, next appointment.",
            """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}""")
    ];

    public async Task<ToolOutput> ExecuteAsync(string name, JsonElement input, CancellationToken ct) => name switch
    {
        "get_recovery_summary" => await RecoverySummaryAsync(ct),
        "list_recovery_patients" => await RecoveryPatientsAsync(
            Enum<RecoveryIssueType>(input, "issue_type"), Dec(input, "min_value"), Enum<PriorityLevel>(input, "priority"), Limit(input), ct),
        "list_outstanding_balances" => await BalancesAsync(Limit(input), ct),
        "list_missed_appointments" => await MissedAsync(Date(input, "from_date"), Date(input, "to_date"), ct),
        "list_inactive_patients" => await InactiveAsync(Int(input, "months") ?? 6, Limit(input), ct),
        "list_treatment_plans" => await PlansAsync(Enum<TreatmentPlanStatus>(input, "status"), Bool(input, "only_not_started"),
            Bool(input, "only_unfinished"), Dec(input, "min_remaining_value"), Limit(input), ct),
        "get_revenue_summary" => await RevenueAsync(Date(input, "from_date"), Date(input, "to_date"), ct),
        "get_appointment_stats" => await AppointmentStatsAsync(Date(input, "from_date"), Date(input, "to_date"), ct),
        "find_patient" => await FindPatientAsync(Str(input, "query") ?? "", ct),
        _ => throw new BusinessRuleException($"Unknown tool {name}.")
    };

    public static string Serialize(object payload) => JsonSerializer.Serialize(payload, Json);
    public static JsonElement ToElement(object payload) => JsonSerializer.SerializeToElement(payload, Json);

    // ================================================================== tools

    public async Task<ToolOutput> RecoverySummaryAsync(CancellationToken ct)
    {
        var d = await recovery.GetDashboardAsync(new RecoveryQuery(null, null, null, null, 1), ct);
        var s = d.Summary;
        var table = new DataTableDto("Revenue Recovery summary", ["Metric", "Value"],
        [
            ["Potential revenue at risk", Money(s.PotentialRevenue, d.Currency)],
            ["Collected after follow-up, this month", Money(s.RecoveredThisMonth, d.Currency)],
            ["Collected after follow-up, last month", Money(s.RecoveredLastMonth, d.Currency)],
            ["Patients to contact now", s.PatientsToContact.ToString()],
            ["Unfinished treatments", s.UnfinishedTreatments.ToString()],
            ["Treatments not started", s.NotStartedTreatments.ToString()],
            ["Missed appointments (not rebooked)", s.MissedAppointments.ToString()],
            ["Outstanding balances", $"{s.OutstandingBalanceCount} · {Money(s.OutstandingBalanceTotal, d.Currency)}"],
            ["Overdue clinical follow-ups", s.OverdueFollowUps.ToString()],
            ["Recalls due", s.RecallsDue.ToString()],
            ["Contacted patients who booked or paid (90 days)", $"{s.RecoveryRate}%"]
        ]);
        return new ToolOutput(new { currency = d.Currency, summary = s }, table);
    }

    public async Task<ToolOutput> RecoveryPatientsAsync(RecoveryIssueType? issue, decimal? minValue, PriorityLevel? priority, int limit, CancellationToken ct)
    {
        var d = await recovery.GetDashboardAsync(new RecoveryQuery(priority, issue, null, null, 1000), ct);
        var rows = d.Patients.Where(p => minValue is null || p.TotalValue >= minValue).Take(limit).ToList();
        var files = await FileNumbersAsync(rows.Select(p => p.PatientId), ct);
        var payload = rows.Select(p => new
        {
            patient = Who(p.FullName, files[p.PatientId]), phone = Phone(p.Phone), score = p.Score.Score, priority = p.Score.Priority.ToString(),
            totalValue = p.TotalValue, daysSinceVisit = p.DaysSinceVisit, stage = p.Stage,
            issues = p.Issues.Select(i => new { type = i.IssueType.ToString(), i.Detail, value = i.Value }),
            recommendedAction = p.RecommendedAction
        });
        var table = new DataTableDto($"Recovery patients{(issue is null ? "" : $" — {issue}")}",
            ["Patient", "Issue", "Value", "Days since visit", "Priority", "Score"],
            rows.Select(p => (IReadOnlyList<string>)[Cell(p.FullName, files[p.PatientId]), Humanize(p.PrimaryIssue) + (p.Issues.Count > 1 ? $" +{p.Issues.Count - 1}" : ""),
                Money(p.TotalValue, d.Currency), p.DaysSinceVisit?.ToString() ?? "—", p.Score.Priority.ToString().ToUpperInvariant(), p.Score.Score.ToString()]).ToList(),
            rows.Select(p => (Guid?)p.PatientId).ToList());
        return new ToolOutput(new { currency = d.Currency, totalMatching = d.Patients.Count(p => minValue is null || p.TotalValue >= minValue), patients = payload }, table);
    }

    public async Task<ToolOutput> BalancesAsync(int limit, CancellationToken ct)
    {
        var currency = await CurrencyAsync(ct);
        var rows = await invoices.OutstandingBalancesAsync(limit, ct);
        var total = await db.Invoices.Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.Balance > 0)
            .SumAsync(i => (decimal?)i.Balance, ct) ?? 0;
        var files = await FileNumbersAsync(rows.Select(r => r.PatientId), ct);
        var table = new DataTableDto("Outstanding balances", ["Patient", "Phone", "Balance", "Invoices", "Days overdue", "Last payment"],
            rows.Select(r => (IReadOnlyList<string>)[Cell(r.PatientName, files[r.PatientId]), r.Phone, Money(r.Balance, currency), r.OpenInvoices.ToString(),
                r.DaysOverdue.ToString(), r.LastPaymentAt is null ? "—" : LocalDate(r.LastPaymentAt.Value)]).ToList(),
            rows.Select(r => (Guid?)r.PatientId).ToList());
        return new ToolOutput(new { currency, totalOutstanding = total, patients = rows.Select(r => new
        {
            patient = Who(r.PatientName, files[r.PatientId]), phone = Phone(r.Phone), r.Balance, r.OpenInvoices, r.DaysOverdue,
            lastPayment = r.LastPaymentAt is null ? null : LocalDate(r.LastPaymentAt.Value)
        }) }, table);
    }

    public async Task<ToolOutput> MissedAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = clinicTime.Today;
        var f = from ?? today.AddDays(-7);
        var t = to ?? today;
        var start = clinicTime.StartOfDayUtc(f);
        var end = clinicTime.StartOfDayUtc(t.AddDays(1));
        var rows = await db.Appointments.AsNoTracking()
            .Where(a => a.Status == AppointmentStatus.NoShow && a.StartTime >= start && a.StartTime < end)
            .OrderByDescending(a => a.StartTime).Take(100)
            .Select(a => new
            {
                a.PatientId, Patient = a.Patient.FullName, File = a.Patient.FileNumber, a.Patient.Phone, a.StartTime, a.Treatment,
                Dentist = a.Dentist.FullName,
                Rebooked = db.Appointments.Any(b => b.PatientId == a.PatientId && b.StartTime > a.StartTime
                                                    && b.Status != AppointmentStatus.Cancelled && b.Status != AppointmentStatus.NoShow)
            }).ToListAsync(ct);
        var table = new DataTableDto($"Missed appointments {f:yyyy-MM-dd} → {t:yyyy-MM-dd}", ["Patient", "Phone", "Date", "Treatment", "Dentist", "Rebooked"],
            rows.Select(r => (IReadOnlyList<string>)[Cell(r.Patient, r.File), r.Phone, LocalDateTime(r.StartTime), r.Treatment, r.Dentist, r.Rebooked ? "Yes" : "No"]).ToList(),
            rows.Select(r => (Guid?)r.PatientId).ToList());
        return new ToolOutput(new
        {
            from = f.ToString("yyyy-MM-dd"), to = t.ToString("yyyy-MM-dd"), count = rows.Count, notRebooked = rows.Count(r => !r.Rebooked),
            appointments = rows.Select(r => new { patient = Who(r.Patient, r.File), phone = Phone(r.Phone), date = LocalDateTime(r.StartTime), r.Treatment, dentist = r.Dentist, rebooked = r.Rebooked })
        }, table);
    }

    public async Task<ToolOutput> InactiveAsync(int months, int limit, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var cutoff = now.AddMonths(-months);
        var q = db.Patients.AsNoTracking()
            .Where(p => p.Status == PatientStatus.Active && p.LastVisitAt != null && p.LastVisitAt < cutoff
                        && !db.Appointments.Any(a => a.PatientId == p.Id && a.StartTime > now
                                                     && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)));
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.LastVisitAt).Take(limit)
            .Select(p => new { p.Id, p.FullName, p.FileNumber, p.Phone, p.LastVisitAt }).ToListAsync(ct);
        var table = new DataTableDto($"Patients not seen in {months}+ months", ["Patient", "Phone", "Last visit", "Months ago"],
            rows.Select(r => (IReadOnlyList<string>)[Cell(r.FullName, r.FileNumber), r.Phone, LocalDate(r.LastVisitAt!.Value),
                Math.Round((now - r.LastVisitAt!.Value).TotalDays / 30.4).ToString(CultureInfo.InvariantCulture)]).ToList(),
            rows.Select(r => (Guid?)r.Id).ToList());
        return new ToolOutput(new
        {
            months, totalMatching = total,
            patients = rows.Select(r => new { patient = Who(r.FullName, r.FileNumber), phone = Phone(r.Phone), lastVisit = LocalDate(r.LastVisitAt!.Value) })
        }, table);
    }

    public async Task<ToolOutput> PlansAsync(TreatmentPlanStatus? status, bool? notStarted, bool? unfinished, decimal? minRemaining, int limit, CancellationToken ct)
    {
        var currency = await CurrencyAsync(ct);
        var q = db.TreatmentPlans.AsNoTracking();
        if (status is not null) q = q.Where(t => t.Status == status);
        if (notStarted == true)
            q = q.Where(t => (t.Status == TreatmentPlanStatus.Presented || t.Status == TreatmentPlanStatus.Accepted)
                             && !t.Items.Any(i => i.Status == TreatmentItemStatus.Completed));
        if (unfinished == true)
            q = q.Where(t => (t.Status == TreatmentPlanStatus.InProgress || t.Status == TreatmentPlanStatus.PartiallyCompleted)
                             && t.Items.Any(i => i.Status == TreatmentItemStatus.Planned || i.Status == TreatmentItemStatus.Scheduled));

        var rows = await q.Select(t => new
            {
                t.PatientId, Patient = t.Patient.FullName, File = t.Patient.FileNumber, t.PlanNumber, t.Title, t.Status, t.TotalAmount, Dentist = t.Dentist.FullName,
                t.CreatedAt, t.LastActivityAt,
                Remaining = t.Items.Where(i => i.Status == TreatmentItemStatus.Planned || i.Status == TreatmentItemStatus.Scheduled)
                    .Sum(i => (decimal?)i.Total) ?? 0
            })
            .Where(r => minRemaining == null || r.Remaining >= minRemaining)
            .OrderByDescending(r => r.Remaining).Take(limit).ToListAsync(ct);

        var table = new DataTableDto("Treatment plans", ["Patient", "Plan", "Status", "Total", "Remaining", "Dentist", "Last activity"],
            rows.Select(r => (IReadOnlyList<string>)[Cell(r.Patient, r.File), $"{r.PlanNumber} · {r.Title}", r.Status.ToString(), Money(r.TotalAmount, currency),
                Money(r.Remaining, currency), r.Dentist, LocalDate(r.LastActivityAt ?? r.CreatedAt)]).ToList(),
            rows.Select(r => (Guid?)r.PatientId).ToList());
        return new ToolOutput(new
        {
            currency, count = rows.Count, remainingTotal = rows.Sum(r => r.Remaining),
            plans = rows.Select(r => new { patient = Who(r.Patient, r.File), plan = r.PlanNumber, r.Title, status = r.Status.ToString(), total = r.TotalAmount,
                remaining = r.Remaining, dentist = r.Dentist, lastActivity = LocalDate(r.LastActivityAt ?? r.CreatedAt) })
        }, table);
    }

    public async Task<ToolOutput> RevenueAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var currency = await CurrencyAsync(ct);
        var today = clinicTime.Today;
        var f = from ?? new DateOnly(today.Year, today.Month, 1);
        var t = to ?? today;
        var start = clinicTime.StartOfDayUtc(f);
        var end = clinicTime.StartOfDayUtc(t.AddDays(1));

        var payments = await db.Payments.AsNoTracking().Where(p => p.PaidAt >= start && p.PaidAt < end)
            .Select(p => new { p.Type, p.Amount, Recovered = p.RecoveredFollowUpId != null }).ToListAsync(ct);
        var collected = payments.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount);
        var refunds = payments.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);
        var recovered = payments.Where(p => p.Recovered).Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
        var expenses = await db.Expenses.Where(e => e.ExpenseDate >= start && e.ExpenseDate < end).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;
        var invoiced = await db.Invoices.Where(i => i.IssueDate >= start && i.IssueDate < end && i.Status != InvoiceStatus.Void && i.Status != InvoiceStatus.Draft && !i.IsOpeningBalance)
            .SumAsync(i => (decimal?)i.Total, ct) ?? 0;
        var production = await db.TreatmentRecords.Where(r => r.PerformedAt >= start && r.PerformedAt < end).SumAsync(r => (decimal?)r.Price, ct) ?? 0;

        var table = new DataTableDto($"Revenue {f:yyyy-MM-dd} → {t:yyyy-MM-dd}", ["Metric", "Amount"],
        [
            ["Collected (net of refunds)", Money(collected - refunds, currency)],
            ["Collected after follow-up", Money(recovered, currency)],
            ["Refunds", Money(refunds, currency)],
            ["Invoiced", Money(invoiced, currency)],
            ["Production (treatment performed)", Money(production, currency)],
            ["Expenses", Money(expenses, currency)],
            ["Net (collected − expenses)", Money(collected - refunds - expenses, currency)]
        ]);
        return new ToolOutput(new
        {
            currency, from = f.ToString("yyyy-MM-dd"), to = t.ToString("yyyy-MM-dd"), collectedNet = collected - refunds, recovered, refunds,
            invoiced, production, expenses, net = collected - refunds - expenses
        }, table);
    }

    public async Task<ToolOutput> AppointmentStatsAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var today = clinicTime.Today;
        var f = from ?? today;
        var t = to ?? today;
        var start = clinicTime.StartOfDayUtc(f);
        var end = clinicTime.StartOfDayUtc(t.AddDays(1));
        var rows = await db.Appointments.AsNoTracking().Where(a => a.StartTime >= start && a.StartTime < end)
            .Select(a => new { a.Status, Dentist = a.Dentist.FullName }).ToListAsync(ct);
        var byStatus = rows.GroupBy(r => r.Status).ToDictionary(g => g.Key.ToString(), g => g.Count());
        var byDentist = rows.GroupBy(r => r.Dentist).Select(g => new
        {
            dentist = g.Key, total = g.Count(), completed = g.Count(x => x.Status == AppointmentStatus.Completed),
            noShows = g.Count(x => x.Status == AppointmentStatus.NoShow)
        }).OrderByDescending(x => x.total).ToList();
        var table = new DataTableDto($"Appointments {f:yyyy-MM-dd} → {t:yyyy-MM-dd}", ["Dentist", "Total", "Completed", "No-shows"],
            byDentist.Select(d => (IReadOnlyList<string>)[d.dentist, d.total.ToString(), d.completed.ToString(), d.noShows.ToString()]).ToList());
        return new ToolOutput(new { from = f.ToString("yyyy-MM-dd"), to = t.ToString("yyyy-MM-dd"), total = rows.Count, byStatus, byDentist }, table);
    }

    public async Task<ToolOutput> FindPatientAsync(string query, CancellationToken ct)
    {
        if (query.Trim().Length < 2) return new ToolOutput(new { error = "Query too short." }, null);
        var currency = await CurrencyAsync(ct);
        var now = clock.UtcNow;
        var rows = await search.FilterPatients(db.Patients.AsNoTracking(), query.Trim()).Take(5)
            .Select(p => new
            {
                p.Id, p.FullName, p.FullNameAr, p.Phone, p.FileNumber, p.LastVisitAt, p.Status,
                Visits = db.Appointments.Count(a => a.PatientId == p.Id && a.Status == AppointmentStatus.Completed),
                Balance = db.Invoices.Where(i => i.PatientId == p.Id && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid))
                    .Sum(i => (decimal?)i.Balance) ?? 0,
                OpenTreatment = db.TreatmentPlanItems.Where(i => i.TreatmentPlan.PatientId == p.Id
                        && (i.TreatmentPlan.Status == TreatmentPlanStatus.Presented || i.TreatmentPlan.Status == TreatmentPlanStatus.Accepted
                            || i.TreatmentPlan.Status == TreatmentPlanStatus.InProgress || i.TreatmentPlan.Status == TreatmentPlanStatus.PartiallyCompleted)
                        && (i.Status == TreatmentItemStatus.Planned || i.Status == TreatmentItemStatus.Scheduled))
                    .Sum(i => (decimal?)i.Total) ?? 0,
                Next = db.Appointments.Where(a => a.PatientId == p.Id && a.StartTime > now
                        && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed))
                    .Min(a => (DateTime?)a.StartTime)
            }).ToListAsync(ct);

        var table = new DataTableDto("Patients found", ["Patient", "File", "Phone", "Visits", "Balance", "Open treatment", "Last visit", "Next appointment"],
            rows.Select(r => (IReadOnlyList<string>)[r.FullName, r.FileNumber, r.Phone, r.Visits.ToString(), Money(r.Balance, currency),
                Money(r.OpenTreatment, currency), r.LastVisitAt is null ? "—" : LocalDate(r.LastVisitAt.Value),
                r.Next is null ? "—" : LocalDateTime(r.Next.Value)]).ToList(),
            rows.Select(r => (Guid?)r.Id).ToList());
        return new ToolOutput(new
        {
            currency, matches = rows.Select(r => new
            {
                patient = Who(r.FullName, r.FileNumber), arabicName = Identifiable ? r.FullNameAr : null, phone = Phone(r.Phone),
                file = r.FileNumber, status = r.Status.ToString(), completedVisits = r.Visits,
                balance = r.Balance, openTreatmentValue = r.OpenTreatment,
                lastVisit = r.LastVisitAt is null ? null : LocalDate(r.LastVisitAt.Value),
                nextAppointment = r.Next is null ? null : LocalDateTime(r.Next.Value)
            })
        }, table);
    }

    // ================================================================== helpers

    private Task<string> CurrencyAsync(CancellationToken ct) => db.Clinics.AsNoTracking().Select(c => c.Currency).FirstAsync(ct);

    public static string Money(decimal v, string currency) => $"{v.ToString("#,0.##", CultureInfo.InvariantCulture)} {currency}";

    private string LocalDate(DateTime utc) => clinicTime.ToLocal(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string LocalDateTime(DateTime utc) => clinicTime.ToLocal(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Humanize(RecoveryIssueType t) => t switch
    {
        RecoveryIssueType.TreatmentNotStarted => "Treatment not started",
        RecoveryIssueType.TreatmentUnfinished => "Treatment unfinished",
        RecoveryIssueType.MissedAppointment => "Missed appointment",
        RecoveryIssueType.OutstandingBalance => "Outstanding balance",
        RecoveryIssueType.OverdueFollowUp => "Overdue follow-up",
        RecoveryIssueType.RecallDue => "Recall due",
        _ => t.ToString()
    };

    private static int Limit(JsonElement e) => Math.Clamp(Int(e, "limit") ?? 10, 1, MaxRows);

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static decimal? Dec(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static T? Enum<T>(JsonElement e, string name) where T : struct, System.Enum =>
        Str(e, name) is { } s && System.Enum.TryParse<T>(s, true, out var r) ? r : null;

    private static DateOnly? Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
