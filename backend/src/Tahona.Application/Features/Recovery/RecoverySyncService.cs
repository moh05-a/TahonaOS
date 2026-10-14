using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tahona.Application.Common;
using Tahona.Application.Features.Messaging;
using Tahona.Application.Features.Notifications;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

public record SyncResult(int Created, int Resolved, int Booked, int AutoMessages, int Reminders, int Notifications);

/// <summary>
/// The Follow-Up Automation Engine. Runs periodically per clinic (and on demand):
///  1. materialises a FollowUp task for every due opportunity (rules decide timing),
///  2. auto-resolves tasks whose issue cleared (booked → Booked, otherwise Resolved),
///  3. notifies staff about new high-value / missed / overdue items,
///  4. optionally auto-sends WhatsApp templates and appointment reminders when a provider is configured.
/// </summary>
public class RecoverySyncService(
    IAppDbContext db,
    RecoveryDetector detector,
    RecoveryService recovery,
    MessagingService messaging,
    NotificationService notifications,
    IWhatsAppProvider whatsApp,
    IClinicTime clinicTime,
    IClock clock,
    ILogger<RecoverySyncService> logger)
{
    /// <summary>An appointment reminder is attempted at most this many times, then left to staff.</summary>
    public const int MaxReminderAttempts = 3;

    /// <summary>A follow-up created longer ago than this is no longer messaged automatically; staff handle it.</summary>
    private static readonly TimeSpan AutoFollowUpWindow = TimeSpan.FromDays(3);

    private static readonly FollowUpStatus[] OpenStatuses = [FollowUpStatus.Pending, FollowUpStatus.Contacted, FollowUpStatus.NoResponse];

    public async Task<SyncResult> SyncAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var detection = await detector.DetectAsync(ct);
        var settings = detection.Settings;
        var clinicSettings = await db.ClinicSettings.AsNoTracking().FirstAsync(ct);
        var currency = await db.Clinics.AsNoTracking().Select(c => c.Currency).FirstAsync(ct);

        var patientIds = detection.Opportunities.Select(o => o.PatientId).Distinct().ToList();
        var existing = await db.FollowUps
            .Where(f => patientIds.Contains(f.PatientId) || OpenStatuses.Contains(f.Status))
            .ToListAsync(ct);

        var created = new List<(FollowUp FollowUp, RecoveryOpportunity Opportunity)>();
        var matched = new HashSet<Guid>();

        foreach (var o in detection.Opportunities.Where(o => o.DueAt <= now))
        {
            var f = RecoveryService.MatchFollowUp(existing, o);
            if (f is not null)
            {
                matched.Add(f.Id);
                if (f.Status == FollowUpStatus.Dismissed) continue;
                f.EstimatedValue = o.Value;
                if (f.Status is FollowUpStatus.Booked or FollowUpStatus.Recovered or FollowUpStatus.Resolved)
                    f.Status = f.LastContactedAt is null ? FollowUpStatus.Pending : FollowUpStatus.Contacted;
                continue;
            }

            f = recovery.NewFollowUp(o);
            db.FollowUps.Add(f);
            existing.Add(f);
            matched.Add(f.Id);
            created.Add((f, o));
        }

        // Open tasks whose issue no longer exists (patient booked, paid, plan closed...).
        var detectedKeys = detection.Opportunities.Select(o => (o.IssueType, o.SourceEntityId)).ToHashSet();
        var stale = existing.Where(f => OpenStatuses.Contains(f.Status) && !matched.Contains(f.Id)
                                        && !detectedKeys.Contains((f.IssueType, f.SourceEntityId ?? Guid.Empty))).ToList();
        var stalePatients = stale.Select(f => f.PatientId).Distinct().ToList();
        var bookings = await db.Appointments.AsNoTracking()
            .Where(a => stalePatients.Contains(a.PatientId) && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow)
            .Select(a => new { a.Id, a.PatientId, a.CreatedAt }).ToListAsync(ct);

        int resolved = 0, booked = 0;
        foreach (var f in stale)
        {
            var booking = bookings.Where(b => b.PatientId == f.PatientId && b.CreatedAt >= f.CreatedAt)
                .OrderBy(b => b.CreatedAt).FirstOrDefault();
            if (booking is not null && f.LastContactedAt is not null)
            {
                f.Status = FollowUpStatus.Booked;
                f.BookedAppointmentId ??= booking.Id;
                f.Outcome ??= "Appointment booked";
                booked++;
            }
            else
            {
                f.Status = FollowUpStatus.Resolved;
                f.Outcome ??= "Issue cleared";
                resolved++;
            }
            f.ResolvedAt = now;
        }
        await db.SaveChangesAsync(ct);

        // ---------------------------------------------------------------- notifications for new tasks
        var notified = 0;
        var names = await db.Patients.AsNoTracking().Where(p => created.Select(c => c.FollowUp.PatientId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName, ct);
        foreach (var (f, o) in created)
        {
            var name = names.GetValueOrDefault(f.PatientId, "Patient");
            var value = o.Value.ToString("#,0.##", CultureInfo.InvariantCulture);
            NotifyRequest? n = o.IssueType switch
            {
                RecoveryIssueType.TreatmentNotStarted or RecoveryIssueType.TreatmentUnfinished when o.Value >= settings.HighValueThreshold =>
                    new(NotificationType.HighValueInactiveTreatment, PriorityLevel.Urgent, $"High-value treatment inactive: {name}",
                        $"{o.Detail} — {value} {currency} at stake.", $"/recovery?patient={f.PatientId}", nameof(FollowUp), f.Id,
                        RequiredPermission: Permissions.RecoveryManage),
                RecoveryIssueType.TreatmentNotStarted or RecoveryIssueType.TreatmentUnfinished =>
                    new(NotificationType.TreatmentFollowUp, PriorityLevel.Medium, $"Treatment follow-up due: {name}",
                        o.Detail, $"/recovery?patient={f.PatientId}", nameof(FollowUp), f.Id, RequiredPermission: Permissions.RecoveryManage),
                RecoveryIssueType.OutstandingBalance =>
                    new(NotificationType.OverduePayment, o.Value >= settings.HighValueThreshold ? PriorityLevel.High : PriorityLevel.Medium,
                        $"Overdue balance: {name}", $"{value} {currency} — {o.Detail}", $"/recovery?patient={f.PatientId}",
                        nameof(FollowUp), f.Id, RequiredPermission: Permissions.InvoicesManage),
                RecoveryIssueType.OverdueFollowUp =>
                    new(NotificationType.TreatmentFollowUp, PriorityLevel.Medium, $"Clinical follow-up overdue: {name}",
                        o.Detail, $"/recovery?patient={f.PatientId}", nameof(FollowUp), f.Id, RequiredPermission: Permissions.RecoveryManage),
                _ => null // missed appointments notify at no-show time; recalls are batch work and don't need pings
            };
            if (n is null) continue;
            await notifications.NotifyAsync(n, ct);
            notified++;
        }

        // ---------------------------------------------------------------- automatic outreach
        // Deterministic rules only: a real provider, the clinic's own settings, the patient's consent, and never in quiet hours.
        var autoMessages = 0;
        var reminders = 0;
        var localNow = TimeOnly.FromDateTime(clinicTime.ToLocal(now));
        if (whatsApp.IsConfigured && clinicSettings.WhatsAppEnabled
            && !MessagingService.InQuietHours(localNow, clinicSettings.QuietHoursStart, clinicSettings.QuietHoursEnd))
        {
            if (clinicSettings.AutoSendFollowUps) autoMessages = await SendAutoFollowUpsAsync(detection, clinicSettings, ct);
            reminders = await SendRemindersAsync(clinicSettings, ct);
        }

        notified += await NotifyUpcomingAsync(ct);

        return new SyncResult(created.Count, resolved, booked, autoMessages, reminders, notified);
    }

    /// <summary>
    /// Sends the rule's template once for each new, untouched follow-up whose rule has AutoSend. The attempt is
    /// recorded before sending, so a crash or a failed send can never lead to a second automatic message.
    /// </summary>
    private async Task<int> SendAutoFollowUpsAsync(RecoveryDetector.DetectionResult detection, ClinicSettings settings, CancellationToken ct)
    {
        var autoRules = detection.Rules.Where(r => r.AutoSend).ToDictionary(r => r.Id);
        if (autoRules.Count == 0) return 0;

        var since = clock.UtcNow - AutoFollowUpWindow;
        var ruleIds = autoRules.Keys.ToList();
        var candidates = await db.FollowUps
            .Where(f => f.Status == FollowUpStatus.Pending && f.AutoSendAttempts == 0 && f.ContactAttempts == 0
                        && f.RuleId != null && ruleIds.Contains(f.RuleId.Value) && f.CreatedAt >= since)
            .ToListAsync(ct);
        if (candidates.Count == 0) return 0;

        var consent = await ConsentAsync(candidates.Select(f => f.PatientId), ct);
        var sent = 0;
        foreach (var f in candidates)
        {
            if (!MessagingService.AllowsAutomation(consent.GetValueOrDefault(f.PatientId), settings.RequireConsentForAutomation)) continue;
            // Only while the issue still exists: a patient who booked or paid in the meantime is left alone.
            var o = detection.Opportunities.FirstOrDefault(x => x.IssueType == f.IssueType && x.SourceEntityId == f.SourceEntityId);
            if (o is null) continue;

            f.AutoSendAttempts++;
            await db.SaveChangesAsync(ct);
            try
            {
                var rule = autoRules[f.RuleId!.Value];
                var result = await messaging.SendAutomatedAsync(new SendMessageRequest(f.PatientId, MessageChannel.WhatsApp, rule.MessageTemplateId,
                    rule.MessageTemplateId is null ? RecoveryService.TemplateFor(o.IssueType) : null, null, null, f.Id,
                    o.IssueType == RecoveryIssueType.OutstandingBalance ? o.Value : null, o.TreatmentName, o.DentistId), ct);
                if (result.Status != MessageStatus.Sent) continue;

                f.Status = FollowUpStatus.Contacted;
                f.ContactAttempts++;
                f.LastContactedAt = clock.UtcNow;
                await db.SaveChangesAsync(ct);
                sent++;
            }
            catch (AppException ex)
            {
                logger.LogWarning(ex, "Auto follow-up message not sent for follow-up {FollowUpId}", f.Id);
            }
        }
        return sent;
    }

    /// <summary>
    /// Reminds patients of upcoming appointments. Each attempt is counted before sending and capped at
    /// <see cref="MaxReminderAttempts"/>, so a failing number is not retried forever.
    /// </summary>
    private async Task<int> SendRemindersAsync(ClinicSettings settings, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var horizon = now.AddHours(settings.AppointmentReminderHours);
        var due = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Scheduled && a.ReminderSentAt == null && a.ReminderAttempts < MaxReminderAttempts
                        && a.StartTime > now && a.StartTime <= horizon)
            .OrderBy(a => a.StartTime).Take(100).ToListAsync(ct);
        if (due.Count == 0) return 0;

        var consent = await ConsentAsync(due.Select(a => a.PatientId), ct);
        var sent = 0;
        foreach (var a in due)
        {
            if (!MessagingService.AllowsAutomation(consent.GetValueOrDefault(a.PatientId), settings.RequireConsentForAutomation)) continue;

            a.ReminderAttempts++;
            await db.SaveChangesAsync(ct);
            try
            {
                var r = await messaging.SendAutomatedAsync(new SendMessageRequest(a.PatientId, MessageChannel.WhatsApp, null,
                    MessageTemplateType.AppointmentReminder, null, a.Id, null, null, null, null), ct);
                if (r.Status == MessageStatus.Sent) sent++;
            }
            catch (AppException ex)
            {
                logger.LogWarning(ex, "Reminder not sent for appointment {AppointmentId}", a.Id);
            }
        }
        return sent;
    }

    private async Task<Dictionary<Guid, MessagingConsent>> ConsentAsync(IEnumerable<Guid> patientIds, CancellationToken ct)
    {
        var ids = patientIds.Distinct().ToList();
        return await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.MessagingConsent, ct);
    }

    /// <summary>Gives dentists a heads-up for appointments starting within the next hour.</summary>
    private async Task<int> NotifyUpcomingAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var soon = await db.Appointments.AsNoTracking()
            .Where(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
                        && a.StartTime > now && a.StartTime <= now.AddMinutes(60) && a.Dentist.UserId != null)
            .Select(a => new { a.Id, a.StartTime, a.Treatment, Patient = a.Patient.FullName, a.Dentist.UserId, a.PatientId, Alert = a.Patient.Allergies })
            .ToListAsync(ct);

        foreach (var a in soon)
        {
            var minutes = (int)Math.Round((a.StartTime - now).TotalMinutes);
            await notifications.NotifyAsync(new NotifyRequest(NotificationType.UpcomingAppointment,
                a.Alert is null ? PriorityLevel.Low : PriorityLevel.Medium,
                $"In {minutes} min: {a.Patient}", a.Alert is null ? a.Treatment : $"{a.Treatment} · Allergy: {a.Alert}",
                $"/patients/{a.PatientId}", nameof(Appointment), a.Id, UserIds: [a.UserId!.Value]), ct);
        }
        return soon.Count;
    }
}
