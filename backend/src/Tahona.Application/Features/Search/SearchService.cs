using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Authorization;
using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Search;

public record SearchHitDto(string Kind, Guid Id, string Title, string? Subtitle, string? Meta, string Link, Guid? PatientId);

public record SearchResultDto(string Query, IReadOnlyList<SearchHitDto> Patients, IReadOnlyList<SearchHitDto> Appointments,
    IReadOnlyList<SearchHitDto> TreatmentPlans, IReadOnlyList<SearchHitDto> Invoices, long ElapsedMs);

/// <summary>
/// Global search. Phone-like input (e.g. 0791234567, +962 79...) is normalised and matched against the
/// indexed PhoneNormalized column; text uses trigram-indexed ILIKE on English and Arabic names.
/// </summary>
public class SearchService(IAppDbContext db, ISearchProvider search, ICurrentUser currentUser, IClock clock)
{
    private const int PerGroup = 6;

    public async Task<SearchResultDto> SearchAsync(string? query, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var term = (query ?? string.Empty).Trim();
        if (term.Length < 2) return new SearchResultDto(term, [], [], [], [], 0);

        var patientQuery = search.FilterPatients(db.Patients.AsNoTracking(), term);
        var patients = await patientQuery
            .OrderByDescending(p => p.LastVisitAt)
            .Take(PerGroup)
            .Select(p => new { p.Id, p.FullName, p.FullNameAr, p.Phone, p.FileNumber, p.Status })
            .ToListAsync(ct);
        var patientIds = patients.Select(p => p.Id).ToList();

        var patientHits = patients.Select(p => new SearchHitDto("patient", p.Id, p.FullName,
            p.FullNameAr is null ? p.Phone : $"{p.FullNameAr} · {p.Phone}", p.FileNumber, $"/patients/{p.Id}", p.Id)).ToList();

        var appointmentHits = new List<SearchHitDto>();
        if (currentUser.HasPermission(Permissions.AppointmentsView) && patientIds.Count > 0)
        {
            var now = clock.UtcNow;
            var appts = await db.Appointments.AsNoTracking()
                .Where(a => patientIds.Contains(a.PatientId) && a.Status != AppointmentStatus.Cancelled)
                .OrderBy(a => a.StartTime < now).ThenBy(a => a.StartTime > now ? a.StartTime : DateTime.MaxValue)
                .ThenByDescending(a => a.StartTime)
                .Take(PerGroup)
                .Select(a => new { a.Id, a.PatientId, a.StartTime, a.Treatment, a.Status, Patient = a.Patient.FullName, Dentist = a.Dentist.FullName })
                .ToListAsync(ct);
            appointmentHits = appts.Select(a => new SearchHitDto("appointment", a.Id, $"{a.Patient} — {a.Treatment}",
                a.Dentist, $"{a.StartTime:O}|{a.Status}", $"/appointments?date={a.StartTime:yyyy-MM-dd}&focus={a.Id}", a.PatientId)).ToList();
        }

        var planHits = new List<SearchHitDto>();
        if (currentUser.HasPermission(Permissions.TreatmentView))
        {
            var upper = term.ToUpperInvariant();
            var plans = await db.TreatmentPlans.AsNoTracking()
                .Where(t => patientIds.Contains(t.PatientId) || t.PlanNumber.Contains(upper))
                .OrderByDescending(t => t.CreatedAt).Take(PerGroup)
                .Select(t => new { t.Id, t.PatientId, t.PlanNumber, t.Title, t.Status, t.TotalAmount, Patient = t.Patient.FullName })
                .ToListAsync(ct);
            planHits = plans.Select(t => new SearchHitDto("plan", t.Id, $"{t.PlanNumber} · {t.Title}", t.Patient,
                $"{t.Status}|{t.TotalAmount}", $"/treatment-plans/{t.Id}", t.PatientId)).ToList();
        }

        var invoiceHits = new List<SearchHitDto>();
        if (currentUser.HasPermission(Permissions.BillingView))
        {
            var upper = term.ToUpperInvariant();
            var invoices = await db.Invoices.AsNoTracking()
                .Where(i => patientIds.Contains(i.PatientId) || i.InvoiceNumber.Contains(upper))
                .OrderByDescending(i => i.IssueDate).Take(PerGroup)
                .Select(i => new { i.Id, i.PatientId, i.InvoiceNumber, i.Total, i.Balance, i.Status, Patient = i.Patient.FullName })
                .ToListAsync(ct);
            invoiceHits = invoices.Select(i => new SearchHitDto("invoice", i.Id, i.InvoiceNumber, i.Patient,
                $"{i.Status}|{i.Total}|{i.Balance}", $"/invoices/{i.Id}", i.PatientId)).ToList();
        }

        return new SearchResultDto(term, patientHits, appointmentHits, planHits, invoiceHits, sw.ElapsedMilliseconds);
    }

    public static bool IsPhoneQuery(string term) => PhoneNumber.LooksLikePhone(term);
}
