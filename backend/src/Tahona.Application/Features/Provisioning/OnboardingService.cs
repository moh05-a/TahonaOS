using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Provisioning;

/// <param name="Kind">"auto" = derived from clinic data; "manual" = a person confirms it.</param>
/// <param name="Link">Where in the app the step is done.</param>
public record OnboardingStepDto(string Key, string Kind, bool Done, bool CanConfirm, string Link, string? Detail);

/// <param name="Figures">Counts and totals to compare against the previous system when verifying an import.</param>
public record OnboardingStatusDto(IReadOnlyList<OnboardingStepDto> Steps, int DoneCount, int TotalCount, bool Completed, DateTime? CompletedAt,
    OnboardingFiguresDto Figures);

public record OnboardingFiguresDto(int Patients, int ImportedPatients, int UpcomingAppointments, decimal OutstandingBalance,
    decimal ImportedOpeningBalance, int Dentists, int StaffUsers, string Currency);

/// <summary>
/// The switching checklist. A clinic can stay at any step for as long as it needs, and can keep its previous system
/// running alongside: nothing here blocks daily use, and the checklist can be resumed at any time.
/// </summary>
public class OnboardingService(IAppDbContext db, IClock clock)
{
    public static class Steps
    {
        public const string ClinicProfile = "clinicProfile";
        public const string Staff = "staff";
        public const string PriceList = "priceList";
        public const string ImportData = "importData";
        public const string VerifyTotals = "verifyTotals";
        public const string TestAppointment = "testAppointment";
        public const string TestInvoice = "testInvoice";
        public const string Messaging = "messaging";
        public const string StaffWalkthrough = "staffWalkthrough";
        public const string GoLive = "goLive";

        /// <summary>Steps only a person can vouch for.</summary>
        public static readonly IReadOnlySet<string> Manual =
            new HashSet<string> { Staff, PriceList, ImportData, VerifyTotals, Messaging, StaffWalkthrough };
    }

    public async Task<OnboardingStatusDto> GetAsync(CancellationToken ct)
    {
        var clinic = await db.Clinics.AsNoTracking().FirstAsync(ct);
        var settings = await db.ClinicSettings.AsNoTracking().FirstAsync(ct);
        var confirmed = settings.Onboarding.ConfirmedSteps.ToHashSet();
        var now = clock.UtcNow;

        var patients = await db.Patients.CountAsync(ct);
        var importedPatients = await db.Patients.CountAsync(p => p.ImportBatchId != null, ct);
        var dentists = await db.Dentists.CountAsync(d => d.IsActive, ct);
        var users = await db.Users.CountAsync(u => u.IsActive, ct);
        var appointments = await db.Appointments.AnyAsync(a => a.ImportBatchId == null, ct);
        var upcoming = await db.Appointments.CountAsync(a => a.StartTime > now
            && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed), ct);
        var invoices = await db.Invoices.AnyAsync(i => !i.IsOpeningBalance, ct);
        var outstanding = await db.Invoices.Where(i => (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid) && i.Balance > 0)
            .SumAsync(i => (decimal?)i.Balance, ct) ?? 0;
        var openingBalance = await db.Invoices.Where(i => i.IsOpeningBalance && i.Status != InvoiceStatus.Void).SumAsync(i => (decimal?)i.Total, ct) ?? 0;
        var imports = await db.ImportBatches.CountAsync(b => b.Status == ImportBatchStatus.Committed, ct);

        bool Confirmed(string key) => confirmed.Contains(key);
        var profileDone = !string.IsNullOrWhiteSpace(clinic.Phone) && !string.IsNullOrWhiteSpace(clinic.Address);
        // Staff: at least one dentist is required to book anything; whether the team is complete is the clinic's call.
        var staffDone = dentists > 0 && Confirmed(Steps.Staff);
        var importDone = imports > 0 || Confirmed(Steps.ImportData);

        var steps = new List<OnboardingStepDto>
        {
            new(Steps.ClinicProfile, "auto", profileDone, false, "/settings", null),
            new(Steps.Staff, "manual", staffDone, dentists > 0, "/staff", $"{dentists}|{users}"),
            new(Steps.PriceList, "manual", Confirmed(Steps.PriceList), true, "/settings?tab=procedures", null),
            // Done by importing; a clinic starting fresh confirms it by hand instead.
            new(Steps.ImportData, imports > 0 ? "auto" : "manual", importDone, true, "/import", imports.ToString()),
            new(Steps.VerifyTotals, "manual", Confirmed(Steps.VerifyTotals), importDone, "/import", null),
            new(Steps.TestAppointment, "auto", appointments, false, "/appointments", null),
            new(Steps.TestInvoice, "auto", invoices, false, "/invoices", null),
            new(Steps.Messaging, "manual", Confirmed(Steps.Messaging), true, "/messages", null),
            new(Steps.StaffWalkthrough, "manual", Confirmed(Steps.StaffWalkthrough), true, "/staff", null)
        };
        var ready = steps.All(s => s.Done);
        steps.Add(new(Steps.GoLive, "manual", settings.Onboarding.CompletedAt is not null, ready, "/onboarding", null));

        return new OnboardingStatusDto(steps, steps.Count(s => s.Done), steps.Count, settings.Onboarding.CompletedAt is not null,
            settings.Onboarding.CompletedAt,
            new OnboardingFiguresDto(patients, importedPatients, upcoming, outstanding, openingBalance, dentists, users, clinic.Currency));
    }

    /// <summary>A staff member confirms (or withdraws their confirmation of) a manual step.</summary>
    public async Task SetConfirmedAsync(string key, bool confirmed, CancellationToken ct)
    {
        if (!Steps.Manual.Contains(key)) throw RequestValidationException.For("key", "This step is completed automatically.");
        var settings = await db.ClinicSettings.FirstAsync(ct);
        var list = settings.Onboarding.ConfirmedSteps.ToHashSet();
        if (confirmed) list.Add(key);
        else list.Remove(key);
        settings.Onboarding = new() { ConfirmedSteps = list.Order().ToList(), CompletedAt = settings.Onboarding.CompletedAt };
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The clinic declares it now uses TahonaOS for daily work. Allowed only when every other step is done.</summary>
    public async Task CompleteAsync(CancellationToken ct)
    {
        var status = await GetAsync(ct);
        if (status.Completed) return;
        var remaining = status.Steps.Where(s => s.Key != Steps.GoLive && !s.Done).Select(s => s.Key).ToList();
        if (remaining.Count > 0) throw new BusinessRuleException($"Finish the remaining steps first: {string.Join(", ", remaining)}.");

        var settings = await db.ClinicSettings.FirstAsync(ct);
        settings.Onboarding = new() { ConfirmedSteps = settings.Onboarding.ConfirmedSteps, CompletedAt = clock.UtcNow };
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Reopens the checklist, e.g. when the clinic decides to import more data later.</summary>
    public async Task ReopenAsync(CancellationToken ct)
    {
        var settings = await db.ClinicSettings.FirstAsync(ct);
        settings.Onboarding = new() { ConfirmedSteps = settings.Onboarding.ConfirmedSteps, CompletedAt = null };
        await db.SaveChangesAsync(ct);
    }
}
