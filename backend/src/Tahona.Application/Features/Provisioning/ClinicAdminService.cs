using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Provisioning;

/// <summary>
/// What an operator sees about a clinic: account state and activity counts only. No patient names, clinical records
/// or financial detail are exposed through operator tooling.
/// </summary>
public record ClinicOverview(Guid Id, string Name, string Slug, string Email, bool IsActive, bool IsDemo, string Plan,
    SubscriptionStatus Status, DateTime? TrialEndsAt, DateTime? PaidUntil, string? StatusReason, DateTime CreatedAt,
    int ActiveUsers, int Patients, int AppointmentsLast30Days, DateTime? LastLoginAt, DateTime? OnboardingCompletedAt);

/// <summary>
/// The figures that say whether the business is working, across real clinics (demo clinics excluded).
/// Counts of accounts and activity only; no clinic content.
/// </summary>
/// <param name="MedianDaysToOnboard">From account creation to the clinic declaring it uses the system daily.</param>
public record PlatformMetrics(int Clinics, IReadOnlyDictionary<string, int> ByStatus, int Onboarded, double OnboardedPercent, double? MedianDaysToOnboard,
    int ClinicsActive7Days, int StaffActive7Days, int AppointmentsBooked7Days, int FollowUpContacts7Days, int Imports30Days, int Churned);

public record SetClinicStatusCommand(string Clinic, SubscriptionStatus Status, string? Reason, DateTime? PaidUntil, string Operator);

/// <summary>Rules that follow from a clinic's commercial state.</summary>
public static class ClinicAccess
{
    /// <summary>
    /// Cancelled and suspended clinics can still sign in, read everything and export their data, but cannot change
    /// records. A clinic is never locked out of its own patient files because of billing.
    /// </summary>
    public static bool IsReadOnly(SubscriptionStatus status) => status is SubscriptionStatus.Cancelled or SubscriptionStatus.Suspended;
}

/// <summary>Operator tooling for clinic accounts. Works across tenants on account data only, and records every change.</summary>
public class ClinicAdminService(IAppDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<ClinicOverview>> ListAsync(CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-30);
        return await db.Clinics.IgnoreQueryFilters().AsNoTracking().OrderBy(c => c.CreatedAt)
            .Select(c => new ClinicOverview(c.Id, c.Name, c.Slug, c.Email ?? "", c.IsActive, c.IsDemo, c.SubscriptionPlan, c.SubscriptionStatus,
                c.TrialEndsAt, c.PaidUntil, c.StatusReason, c.CreatedAt,
                db.Users.IgnoreQueryFilters().Count(u => u.ClinicId == c.Id && u.IsActive),
                db.Patients.IgnoreQueryFilters().Count(p => p.ClinicId == c.Id && !p.IsDeleted),
                db.Appointments.IgnoreQueryFilters().Count(a => a.ClinicId == c.Id && a.CreatedAt >= since),
                db.Users.IgnoreQueryFilters().Where(u => u.ClinicId == c.Id).Max(u => u.LastLoginAt),
                db.ClinicSettings.IgnoreQueryFilters().Where(s => s.ClinicId == c.Id).Select(s => s.Onboarding.CompletedAt).FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<PlatformMetrics> MetricsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var week = now.AddDays(-7);
        var month = now.AddDays(-30);
        var clinics = await db.Clinics.IgnoreQueryFilters().AsNoTracking().Where(c => !c.IsDemo)
            .Select(c => new { c.Id, c.IsActive, c.SubscriptionStatus, c.CreatedAt }).ToListAsync(ct);
        var ids = clinics.Select(c => c.Id).ToList();

        var onboardedAt = (await db.ClinicSettings.IgnoreQueryFilters().AsNoTracking().Where(s => ids.Contains(s.ClinicId))
                .Select(s => new { s.ClinicId, s.Onboarding.CompletedAt }).ToListAsync(ct))
            .Where(s => s.CompletedAt is not null).ToDictionary(s => s.ClinicId, s => s.CompletedAt!.Value);
        var daysToOnboard = clinics.Where(c => onboardedAt.ContainsKey(c.Id))
            .Select(c => Math.Max(0, (onboardedAt[c.Id] - c.CreatedAt).TotalDays)).Order().ToList();
        double? median = daysToOnboard.Count == 0 ? null
            : daysToOnboard.Count % 2 == 1 ? daysToOnboard[daysToOnboard.Count / 2]
            : (daysToOnboard[daysToOnboard.Count / 2 - 1] + daysToOnboard[daysToOnboard.Count / 2]) / 2;

        var activeStaff = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.ClinicId) && u.LastLoginAt >= week)
            .Select(u => u.ClinicId).ToListAsync(ct);
        var appointments = await db.Appointments.IgnoreQueryFilters().CountAsync(a => ids.Contains(a.ClinicId) && a.CreatedAt >= week && a.ImportBatchId == null, ct);
        var contacts = await db.Messages.IgnoreQueryFilters().CountAsync(m => ids.Contains(m.ClinicId) && m.FollowUpId != null && m.CreatedAt >= week, ct);
        var imports = await db.ImportBatches.IgnoreQueryFilters().CountAsync(b => ids.Contains(b.ClinicId) && b.CommittedAt >= month, ct);

        return new PlatformMetrics(clinics.Count,
            clinics.GroupBy(c => c.IsActive ? c.SubscriptionStatus.ToString() : "Archived").OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            onboardedAt.Count, clinics.Count == 0 ? 0 : 100.0 * onboardedAt.Count / clinics.Count, median,
            activeStaff.Distinct().Count(), activeStaff.Count, appointments, contacts, imports,
            clinics.Count(c => !c.IsActive || ClinicAccess.IsReadOnly(c.SubscriptionStatus)));
    }

    /// <summary>Changes a clinic's subscription state (e.g. after confirming a manual payment). Takes effect within a minute.</summary>
    public async Task<ClinicOverview> SetStatusAsync(SetClinicStatusCommand cmd, CancellationToken ct)
    {
        var clinic = await FindAsync(cmd.Clinic, ct);
        var before = new { clinic.SubscriptionStatus, clinic.PaidUntil, clinic.StatusReason };
        var now = clock.UtcNow;

        clinic.SubscriptionStatus = cmd.Status;
        clinic.StatusReason = string.IsNullOrWhiteSpace(cmd.Reason) ? null : cmd.Reason.Trim();
        if (cmd.PaidUntil is not null) clinic.PaidUntil = cmd.PaidUntil;
        clinic.SuspendedAt = ClinicAccess.IsReadOnly(cmd.Status) ? clinic.SuspendedAt ?? now : null;

        Record(clinic, "SubscriptionStatusChanged", cmd.Operator, new
        {
            status = new { old = before.SubscriptionStatus.ToString(), @new = cmd.Status.ToString() },
            paidUntil = new { old = before.PaidUntil, @new = clinic.PaidUntil },
            reason = clinic.StatusReason
        }, now);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == clinic.Id);
    }

    /// <summary>Archives (or restores) an account. An archived clinic cannot sign in; its data is kept until deleted under the retention policy.</summary>
    public async Task SetArchivedAsync(string clinicRef, bool archived, string @operator, string? reason, CancellationToken ct)
    {
        var clinic = await FindAsync(clinicRef, ct);
        if (clinic.IsActive == !archived) return;
        clinic.IsActive = !archived;
        Record(clinic, archived ? "ClinicArchived" : "ClinicRestored", @operator, new { reason }, clock.UtcNow);
        if (archived)
        {
            var now = clock.UtcNow;
            var userIds = db.Users.IgnoreQueryFilters().Where(u => u.ClinicId == clinic.Id).Select(u => u.Id);
            var sessions = await db.RefreshTokens.IgnoreQueryFilters().Where(t => userIds.Contains(t.UserId) && t.RevokedAt == null).ToListAsync(ct);
            sessions.ForEach(t => t.RevokedAt = now);
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task<Clinic> FindAsync(string reference, CancellationToken ct)
    {
        var key = reference.Trim();
        var slug = key.ToLowerInvariant();
        var clinic = await db.Clinics.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Slug == slug, ct);
        if (clinic is null && Guid.TryParse(key, out var id))
            clinic = await db.Clinics.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        return clinic ?? throw new NotFoundException("Clinic", reference);
    }

    /// <summary>Operator actions appear in the clinic's own audit log, attributed to the operator.</summary>
    private void Record(Clinic clinic, string action, string @operator, object changes, DateTime now) =>
        db.AuditLogs.Add(new AuditLog
        {
            ClinicId = clinic.Id, UserName = $"Operator: {@operator}", Action = action, EntityType = nameof(Clinic), EntityId = clinic.Id,
            Changes = JsonSerializer.Serialize(changes), Timestamp = now
        });
}
