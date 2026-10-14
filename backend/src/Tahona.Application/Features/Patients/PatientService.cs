using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Notifications;
using Tahona.Domain.Authorization;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Patients;

public class PatientService(
    IAppDbContext db,
    ISearchProvider search,
    ISequenceGenerator sequences,
    IClinicTime clinicTime,
    IClock clock,
    ICurrentUser currentUser,
    NotificationService notifications)
{
    private static readonly TreatmentPlanStatus[] OpenPlanStatuses =
        [TreatmentPlanStatus.Presented, TreatmentPlanStatus.Accepted, TreatmentPlanStatus.InProgress, TreatmentPlanStatus.PartiallyCompleted];

    private static readonly AppointmentStatus[] UpcomingStatuses = [AppointmentStatus.Scheduled, AppointmentStatus.Confirmed];

    public async Task<PagedResult<PatientListItemDto>> ListAsync(PatientListQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var patients = db.Patients.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search)) patients = search.FilterPatients(patients, query.Search.Trim());
        if (query.Status is not null) patients = patients.Where(p => p.Status == query.Status);
        if (query.DentistId is not null) patients = patients.Where(p => p.PrimaryDentistId == query.DentistId);

        var projected = patients.Select(p => new
        {
            p.Id, p.FileNumber, p.FullName, p.FullNameAr, p.Phone, p.Gender, p.DateOfBirth, p.Status, p.LastVisitAt,
            p.RegisteredAt, p.Allergies, p.MedicalConditions,
            PrimaryDentistName = p.PrimaryDentist != null ? p.PrimaryDentist.FullName : null,
            NextAppointmentAt = db.Appointments
                .Where(a => a.PatientId == p.Id && a.StartTime > now && UpcomingStatuses.Contains(a.Status))
                .Min(a => (DateTime?)a.StartTime),
            Balance = db.Invoices
                .Where(i => i.PatientId == p.Id && i.Status != InvoiceStatus.Void && i.Status != InvoiceStatus.Draft)
                .Sum(i => (decimal?)i.Balance) ?? 0,
            OpenValue = db.TreatmentPlanItems
                .Where(it => it.TreatmentPlan.PatientId == p.Id && OpenPlanStatuses.Contains(it.TreatmentPlan.Status)
                             && (it.Status == TreatmentItemStatus.Planned || it.Status == TreatmentItemStatus.Scheduled))
                .Sum(it => (decimal?)it.Total) ?? 0
        });

        projected = (query.Sort?.ToLowerInvariant(), query.Desc) switch
        {
            ("name", false) => projected.OrderBy(p => p.FullName),
            ("name", true) => projected.OrderByDescending(p => p.FullName),
            ("lastvisit", false) => projected.OrderBy(p => p.LastVisitAt),
            ("lastvisit", true) => projected.OrderByDescending(p => p.LastVisitAt),
            ("balance", false) => projected.OrderBy(p => p.Balance),
            ("balance", true) => projected.OrderByDescending(p => p.Balance),
            ("registered", false) => projected.OrderBy(p => p.RegisteredAt),
            _ => projected.OrderByDescending(p => p.RegisteredAt)
        };

        var page = await projected.ToPagedAsync(query, ct);
        var today = clinicTime.Today;
        var items = page.Items.Select(p => new PatientListItemDto(
            p.Id, p.FileNumber, p.FullName, p.FullNameAr, p.Phone, p.Gender, AgeOn(p.DateOfBirth, today), p.Status,
            p.LastVisitAt, p.NextAppointmentAt, p.Balance, p.OpenValue, p.PrimaryDentistName, p.RegisteredAt,
            !string.IsNullOrWhiteSpace(p.Allergies) || !string.IsNullOrWhiteSpace(p.MedicalConditions))).ToList();

        return new PagedResult<PatientListItemDto>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<PatientDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Patients.AsNoTracking().Include(x => x.PrimaryDentist).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Patient", id);
        var stats = await GetStatsAsync(id, p.LastVisitAt, ct);
        var today = clinicTime.Today;

        return new PatientDetailDto(
            p.Id, p.FileNumber, p.FullName, p.FullNameAr, p.DateOfBirth, AgeOn(p.DateOfBirth, today), p.Gender,
            p.Phone, p.WhatsAppNumber, p.Email, p.Address, p.City, p.NationalId,
            p.EmergencyContactName, p.EmergencyContactPhone, p.EmergencyContactRelation,
            p.MedicalNotes, p.Allergies, p.MedicalConditions, p.CurrentMedications,
            p.InsuranceProvider, p.InsurancePolicyNumber, p.InsuranceCoveragePercent,
            p.PreferredLanguage, p.Status, p.RegisteredAt, p.ReferralSource,
            p.PrimaryDentistId, p.PrimaryDentist?.FullName, p.LastVisitAt, stats,
            p.MessagingConsent, p.MessagingConsentAt, p.MessagingConsentSource);
    }

    private async Task<PatientStatsDto> GetStatsAsync(Guid patientId, DateTime? lastVisit, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var appts = await db.Appointments.Where(a => a.PatientId == patientId)
            .GroupBy(a => 1)
            .Select(g => new
            {
                Completed = g.Count(a => a.Status == AppointmentStatus.Completed),
                NoShows = g.Count(a => a.Status == AppointmentStatus.NoShow),
                Upcoming = g.Count(a => a.StartTime > now && UpcomingStatuses.Contains(a.Status)),
                Next = g.Where(a => a.StartTime > now && UpcomingStatuses.Contains(a.Status)).Min(a => (DateTime?)a.StartTime)
            }).FirstOrDefaultAsync(ct);

        var billing = await db.Invoices.Where(i => i.PatientId == patientId && i.Status != InvoiceStatus.Void && i.Status != InvoiceStatus.Draft)
            .GroupBy(i => 1)
            .Select(g => new { Billed = g.Sum(i => i.Total), Paid = g.Sum(i => i.AmountPaid), Balance = g.Sum(i => i.Balance) })
            .FirstOrDefaultAsync(ct);

        var openValue = await db.TreatmentPlanItems
            .Where(it => it.TreatmentPlan.PatientId == patientId && OpenPlanStatuses.Contains(it.TreatmentPlan.Status)
                         && (it.Status == TreatmentItemStatus.Planned || it.Status == TreatmentItemStatus.Scheduled))
            .SumAsync(it => (decimal?)it.Total, ct) ?? 0;

        var activePlans = await db.TreatmentPlans.CountAsync(t => t.PatientId == patientId && OpenPlanStatuses.Contains(t.Status), ct);

        int? daysSince = lastVisit is null ? null : (int)(now - lastVisit.Value).TotalDays;
        return new PatientStatsDto(
            appts?.Completed ?? 0, appts?.NoShows ?? 0, appts?.Upcoming ?? 0,
            billing?.Billed ?? 0, billing?.Paid ?? 0, billing?.Balance ?? 0, openValue, activePlans, appts?.Next, daysSince);
    }

    public async Task<Guid> CreateAsync(PatientUpsertRequest request, CancellationToken ct)
    {
        var normalized = PhoneNumber.Normalize(request.Phone);
        var duplicate = await db.Patients.AnyAsync(
            p => p.PhoneNormalized == normalized && p.FullName.ToLower() == request.FullName.Trim().ToLower(), ct);
        if (duplicate) throw new ConflictException("A patient with the same name and phone number already exists.");

        await EnsureDentistAsync(request.PrimaryDentistId, ct);

        var patient = new Patient
        {
            FileNumber = await sequences.NextAsync(SequenceKind.Patient, ct),
            RegisteredAt = clock.UtcNow
        };
        Apply(patient, request);
        ApplyConsent(patient, request.MessagingConsent);
        db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(new NotifyRequest(
            NotificationType.NewPatient, PriorityLevel.Low,
            $"New patient registered: {patient.FullName}",
            $"File {patient.FileNumber} · {patient.Phone}",
            $"/patients/{patient.Id}", nameof(Patient), patient.Id,
            RequiredPermission: Permissions.PatientsView, ExcludeUserId: currentUser.UserId), ct);

        return patient.Id;
    }

    public async Task UpdateAsync(Guid id, PatientUpsertRequest request, CancellationToken ct)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Patient", id);
        await EnsureDentistAsync(request.PrimaryDentistId, ct);
        Apply(patient, request);
        ApplyConsent(patient, request.MessagingConsent);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Patient", id);
        var hasOpenBalance = await db.Invoices.AnyAsync(i => i.PatientId == id && i.Balance > 0 && i.Status != InvoiceStatus.Void, ct);
        if (hasOpenBalance) throw new BusinessRuleException("Patient has an outstanding balance. Settle or void invoices before deleting, or archive the patient instead.");
        db.Patients.Remove(patient); // soft delete via ISoftDelete
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TimelineEventDto>> GetTimelineAsync(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Patient", id);
        var events = new List<TimelineEventDto>
        {
            new("registration", "Registered", $"File {patient.FileNumber}", patient.RegisteredAt, null, null, patient.Id)
        };

        var notes = await db.ClinicalNotes.AsNoTracking().Where(n => n.PatientId == id)
            .Select(n => new { n.Id, n.NoteDate, n.ChiefComplaint, n.Diagnosis, Dentist = n.Dentist.FullName }).ToListAsync(ct);
        foreach (var n in notes)
        {
            events.Add(new("consultation", "Consultation", n.ChiefComplaint ?? $"Seen by {n.Dentist}", n.NoteDate, null, null, n.Id));
            if (!string.IsNullOrWhiteSpace(n.Diagnosis))
                events.Add(new("diagnosis", "Diagnosis", n.Diagnosis, n.NoteDate.AddMinutes(1), null, null, n.Id));
        }

        var plans = await db.TreatmentPlans.AsNoTracking().Where(t => t.PatientId == id)
            .Select(t => new { t.Id, t.Title, t.PlanNumber, t.TotalAmount, t.Status, t.CreatedAt, t.AcceptedAt, t.CompletedAt }).ToListAsync(ct);
        foreach (var t in plans)
        {
            events.Add(new("treatment_plan", $"Treatment plan {t.PlanNumber}", t.Title, t.CreatedAt, t.TotalAmount, t.Status.ToString(), t.Id));
            if (t.AcceptedAt is not null)
                events.Add(new("plan_accepted", "Plan accepted", t.Title, t.AcceptedAt.Value, t.TotalAmount, null, t.Id));
            if (t.CompletedAt is not null)
                events.Add(new("plan_completed", "Plan completed", t.Title, t.CompletedAt.Value, t.TotalAmount, null, t.Id));
        }

        var appts = await db.Appointments.AsNoTracking().Where(a => a.PatientId == id)
            .Select(a => new { a.Id, a.StartTime, a.Treatment, a.Status, Dentist = a.Dentist.FullName }).ToListAsync(ct);
        events.AddRange(appts.Select(a => new TimelineEventDto(
            "appointment", a.Status == AppointmentStatus.NoShow ? "Missed appointment" : "Appointment",
            $"{a.Treatment} · {a.Dentist}", a.StartTime, null, a.Status.ToString(), a.Id)));

        var records = await db.TreatmentRecords.AsNoTracking().Where(r => r.PatientId == id)
            .Select(r => new { r.Id, r.ProcedureName, r.ToothNumber, r.PerformedAt, r.Price }).ToListAsync(ct);
        events.AddRange(records.Select(r => new TimelineEventDto(
            "treatment", r.ProcedureName, r.ToothNumber is null ? null : $"Tooth {r.ToothNumber}", r.PerformedAt, r.Price, null, r.Id)));

        var payments = await db.Payments.AsNoTracking().Where(p => p.PatientId == id)
            .Select(p => new { p.Id, p.Amount, p.Type, p.Method, p.PaidAt, p.ReceiptNumber }).ToListAsync(ct);
        events.AddRange(payments.Select(p => new TimelineEventDto(
            p.Type == PaymentType.Refund ? "refund" : "payment",
            p.Type == PaymentType.Refund ? "Refund" : "Payment received",
            $"{p.ReceiptNumber} · {p.Method}", p.PaidAt, p.Amount, null, p.Id)));

        var followUps = await db.FollowUps.AsNoTracking().Where(f => f.PatientId == id)
            .Select(f => new { f.Id, f.Title, f.Status, f.DueDate, f.LastContactedAt }).ToListAsync(ct);
        events.AddRange(followUps.Select(f => new TimelineEventDto(
            "follow_up", f.Title, $"Follow-up · {f.Status}", f.LastContactedAt ?? f.DueDate, null, f.Status.ToString(), f.Id)));

        return events.OrderByDescending(e => e.Date).ToList();
    }

    // ------------------------------------------------------------- staff notes

    public async Task<IReadOnlyList<PatientNoteDto>> ListNotesAsync(Guid patientId, CancellationToken ct) =>
        await db.PatientNotes.AsNoTracking().Where(n => n.PatientId == patientId)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt)
            .Select(n => new PatientNoteDto(n.Id, n.Content, n.IsPinned, n.Author != null ? n.Author.FullName : null, n.CreatedAt))
            .ToListAsync(ct);

    public async Task<Guid> AddNoteAsync(Guid patientId, PatientNoteRequest request, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient", patientId);
        var note = new PatientNote { PatientId = patientId, Content = request.Content.Trim(), IsPinned = request.IsPinned, AuthorId = currentUser.UserId };
        db.PatientNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return note.Id;
    }

    public async Task DeleteNoteAsync(Guid patientId, Guid noteId, CancellationToken ct)
    {
        var note = await db.PatientNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.PatientId == patientId, ct)
                   ?? throw new NotFoundException("Note", noteId);
        if (note.AuthorId != currentUser.UserId && !currentUser.HasPermission(Permissions.SettingsManage))
            throw new ForbiddenException("Only the author or an administrator can delete this note.");
        db.PatientNotes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------- helpers

    private async Task EnsureDentistAsync(Guid? dentistId, CancellationToken ct)
    {
        if (dentistId is not null && !await db.Dentists.AnyAsync(d => d.Id == dentistId, ct))
            throw RequestValidationException.For("primaryDentistId", "Selected dentist does not exist.");
    }

    /// <summary>Records who changed the patient's messaging consent and when; an unchanged value keeps its history.</summary>
    private void ApplyConsent(Patient p, MessagingConsent? consent)
    {
        if (consent is null || consent == p.MessagingConsent) return;
        p.MessagingConsent = consent.Value;
        p.MessagingConsentAt = clock.UtcNow;
        p.MessagingConsentSource = currentUser.FullName ?? "staff";
    }

    private static void Apply(Patient p, PatientUpsertRequest r)
    {
        p.FullName = r.FullName.Trim();
        p.FullNameAr = Clean(r.FullNameAr);
        p.DateOfBirth = r.DateOfBirth;
        p.Gender = r.Gender;
        p.Phone = r.Phone.Trim();
        p.PhoneNormalized = PhoneNumber.Normalize(r.Phone);
        p.WhatsAppNumber = Clean(r.WhatsAppNumber);
        p.Email = Clean(r.Email)?.ToLowerInvariant();
        p.Address = Clean(r.Address);
        p.City = Clean(r.City);
        p.NationalId = Clean(r.NationalId);
        p.EmergencyContactName = Clean(r.EmergencyContactName);
        p.EmergencyContactPhone = Clean(r.EmergencyContactPhone);
        p.EmergencyContactRelation = Clean(r.EmergencyContactRelation);
        p.MedicalNotes = Clean(r.MedicalNotes);
        p.Allergies = Clean(r.Allergies);
        p.MedicalConditions = Clean(r.MedicalConditions);
        p.CurrentMedications = Clean(r.CurrentMedications);
        p.InsuranceProvider = Clean(r.InsuranceProvider);
        p.InsurancePolicyNumber = Clean(r.InsurancePolicyNumber);
        p.InsuranceCoveragePercent = r.InsuranceCoveragePercent;
        p.PreferredLanguage = r.PreferredLanguage;
        p.Status = r.Status;
        p.ReferralSource = Clean(r.ReferralSource);
        p.PrimaryDentistId = r.PrimaryDentistId;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static int? AgeOn(DateOnly? dob, DateOnly today)
    {
        if (dob is null) return null;
        var age = today.Year - dob.Value.Year;
        if (dob.Value > today.AddYears(-age)) age--;
        return age;
    }
}
