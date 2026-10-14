using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

/// <summary>A contact made about a follow-up: a message sent, a call logged, or a patient's reply.</summary>
public record FollowUpContactDto(Guid MessageId, DateTime At, MessageChannel Channel, MessageDirection Direction, MessageStatus Status,
    string? By, bool IsAutomated, string Body);

/// <summary>The record behind one opportunity, from the event that raised it to how it ended.</summary>
/// <param name="SourceLabel">The record that raised it, e.g. "Treatment plan TP-0012 · Zirconia crown".</param>
/// <param name="SourceLink">Where to open that record in the app.</param>
/// <param name="EventAt">When the underlying event happened (plan presented, appointment missed, invoice due…).</param>
/// <param name="IdentifiedAt">When the system listed it for follow-up.</param>
/// <param name="CollectedAfterFollowUp">Payments counted against it under the attribution rule.</param>
public record FollowUpTrailDto(Guid Id, RecoveryIssueType IssueType, string Title, string? SourceLabel, string? SourceLink, DateTime? EventAt,
    DateTime IdentifiedAt, DateTime DueDate, FollowUpStatus Status, decimal EstimatedValue, int ContactAttempts, DateTime? LastContactedAt,
    string? Outcome, DateTime? ResolvedAt, string? AssignedTo, DateTime? BookedAppointmentAt, decimal CollectedAfterFollowUp,
    IReadOnlyList<FollowUpContactDto> Contacts);

/// <summary>
/// The evidence behind a patient's place on the recovery list: for each follow-up, which record raised it, when,
/// what staff did about it, and what was recorded as the outcome. Read-only.
/// </summary>
public class RecoveryHistoryService(IAppDbContext db)
{
    public async Task<IReadOnlyList<FollowUpTrailDto>> GetAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new NotFoundException("Patient", patientId);

        var followUps = await db.FollowUps.AsNoTracking().Include(f => f.AssignedToUser).Where(f => f.PatientId == patientId)
            .OrderByDescending(f => f.CreatedAt).Take(50).ToListAsync(ct);
        if (followUps.Count == 0) return [];

        var ids = followUps.Select(f => f.Id).ToList();
        var messages = await db.Messages.AsNoTracking().Where(m => m.FollowUpId != null && ids.Contains(m.FollowUpId.Value))
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.FollowUpId, Dto = new FollowUpContactDto(m.Id, m.SentAt ?? m.CreatedAt, m.Channel, m.Direction, m.Status,
                m.SentBy != null ? m.SentBy.FullName : null, m.IsAutomated, m.Body) })
            .ToListAsync(ct);

        // The records that raised each follow-up, fetched per type.
        Guid[] Sources(string type) => followUps.Where(f => f.SourceEntityType == type && f.SourceEntityId != null).Select(f => f.SourceEntityId!.Value).Distinct().ToArray();
        var planIds = Sources(nameof(TreatmentPlan));
        var plans = await db.TreatmentPlans.AsNoTracking().Where(t => planIds.Contains(t.Id))
            .Select(t => new { t.Id, t.PlanNumber, t.Title, At = t.AcceptedAt ?? t.PresentedAt ?? (DateTime?)t.CreatedAt, t.LastActivityAt }).ToDictionaryAsync(t => t.Id, ct);
        var appointmentIds = Sources(nameof(Appointment)).Concat(followUps.Where(f => f.BookedAppointmentId != null).Select(f => f.BookedAppointmentId!.Value)).Distinct().ToArray();
        var appointments = await db.Appointments.AsNoTracking().Where(a => appointmentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.StartTime, a.Treatment }).ToDictionaryAsync(a => a.Id, ct);
        var recordIds = Sources(nameof(TreatmentRecord));
        var records = await db.TreatmentRecords.AsNoTracking().Where(r => recordIds.Contains(r.Id))
            .Select(r => new { r.Id, r.ProcedureName, r.PerformedAt }).ToDictionaryAsync(r => r.Id, ct);
        var oldestDue = await db.Invoices.AsNoTracking()
            .Where(i => i.PatientId == patientId && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.Balance > 0)
            .MinAsync(i => (DateTime?)i.DueDate, ct);
        var lastVisit = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => p.LastVisitAt).FirstAsync(ct);

        return followUps.Select(f =>
        {
            string? label = null, link = null;
            DateTime? eventAt = null;
            var source = f.SourceEntityId ?? Guid.Empty;
            switch (f.SourceEntityType)
            {
                case nameof(TreatmentPlan) when plans.TryGetValue(source, out var plan):
                    label = $"{plan.PlanNumber} · {plan.Title}";
                    link = $"/treatment-plans/{plan.Id}";
                    eventAt = f.IssueType == RecoveryIssueType.TreatmentUnfinished ? plan.LastActivityAt ?? plan.At : plan.At;
                    break;
                case nameof(Appointment) when appointments.TryGetValue(source, out var missed):
                    label = missed.Treatment;
                    link = $"/patients/{patientId}?tab=appointments";
                    eventAt = missed.StartTime;
                    break;
                case nameof(TreatmentRecord) when records.TryGetValue(source, out var record):
                    label = record.ProcedureName;
                    link = $"/patients/{patientId}?tab=history";
                    eventAt = record.PerformedAt;
                    break;
                case nameof(Patient) when f.IssueType == RecoveryIssueType.OutstandingBalance:
                    link = $"/patients/{patientId}?tab=invoices";
                    eventAt = oldestDue;
                    break;
                case nameof(Patient):
                    link = $"/patients/{patientId}?tab=appointments";
                    eventAt = lastVisit;
                    break;
            }

            DateTime? bookedAt = f.BookedAppointmentId is { } booked && appointments.TryGetValue(booked, out var appointment) ? appointment.StartTime : null;
            return new FollowUpTrailDto(f.Id, f.IssueType, f.Title, label, link, eventAt, f.CreatedAt, f.DueDate, f.Status, f.EstimatedValue, f.ContactAttempts,
                f.LastContactedAt, f.Outcome, f.ResolvedAt, f.AssignedToUser?.FullName, bookedAt, f.RecoveredAmount,
                messages.Where(m => m.FollowUpId == f.Id).Select(m => m.Dto).ToList());
        }).ToList();
    }
}
