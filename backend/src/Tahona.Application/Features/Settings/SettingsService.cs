using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;

namespace Tahona.Application.Features.Settings;

public record ClinicProfileDto(Guid Id, string Name, string? NameAr, string? Phone, string? Email, string? Address, string? City,
    string Country, string TimeZone, string Currency, string DefaultLanguage, string SubscriptionPlan);

public record ClinicProfileRequest(string Name, string? NameAr, string? Phone, string? Email, string? Address, string? City,
    string TimeZone, string Currency, string DefaultLanguage);

public record ClinicSettingsDto(TimeOnly WorkingHoursStart, TimeOnly WorkingHoursEnd, IReadOnlyList<int> WorkingDays, int SlotMinutes,
    int AppointmentReminderHours, string InvoicePrefix, int InvoiceDueDays, decimal TaxRatePercent, bool WhatsAppEnabled,
    bool AutoSendFollowUps, bool AiAssistantEnabled, RecoverySettings Recovery);

public record IntegrationStatusDto(string WhatsAppProvider, bool WhatsAppConfigured, bool AiConfigured, string AiProvider, string? AiModel, string StorageProvider);

public class ClinicProfileValidator : AbstractValidator<ClinicProfileRequest>
{
    public ClinicProfileValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameAr).MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[A-Z]{3}$").WithMessage("Use an ISO currency code, e.g. JOD.");
        RuleFor(x => x.DefaultLanguage).Must(l => l is "en" or "ar");
        RuleFor(x => x.TimeZone).NotEmpty().Must(BeValidTimeZone).WithMessage("Unknown time zone.");
    }

    private static bool BeValidTimeZone(string tz)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(tz); return true; }
        catch { return false; }
    }
}

public class ClinicSettingsValidator : AbstractValidator<ClinicSettingsDto>
{
    public ClinicSettingsValidator()
    {
        RuleFor(x => x.WorkingHoursEnd).GreaterThan(x => x.WorkingHoursStart).WithMessage("Closing time must be after opening time.");
        RuleFor(x => x.WorkingDays).NotEmpty().Must(d => d.All(x => x is >= 0 and <= 6));
        RuleFor(x => x.SlotMinutes).Must(s => s is 10 or 15 or 20 or 30 or 45 or 60).WithMessage("Slot must be 10, 15, 20, 30, 45 or 60 minutes.");
        RuleFor(x => x.AppointmentReminderHours).InclusiveBetween(1, 168);
        RuleFor(x => x.InvoicePrefix).NotEmpty().MaximumLength(10).Matches("^[A-Z0-9-]+$");
        RuleFor(x => x.InvoiceDueDays).InclusiveBetween(0, 365);
        RuleFor(x => x.TaxRatePercent).InclusiveBetween(0, 50);
        RuleFor(x => x.Recovery).NotNull();
        RuleFor(x => x.Recovery.HighValueThreshold).GreaterThan(0);
        RuleFor(x => x.Recovery.AttributionWindowDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Recovery.MaxLookbackDays).InclusiveBetween(30, 1825);
        RuleFor(x => x.Recovery.MissedAppointmentLookbackDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Recovery.RecontactAfterDays).InclusiveBetween(1, 90);
        RuleFor(x => x.Recovery.RecallEstimatedValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Recovery.MissedAppointmentDefaultValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Recovery.FollowUpVisitValue).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Recovery).Must(r => r.WeightValue >= 0 && r.WeightUrgency >= 0 && r.WeightLikelihood >= 0 && r.WeightSeverity >= 0
                                           && r.WeightValue + r.WeightUrgency + r.WeightLikelihood + r.WeightSeverity > 0)
            .WithMessage("Score weights must be non-negative and not all zero.");
        RuleFor(x => x.Recovery).Must(r => r.HighPriorityThreshold is > 0 and <= 100 && r.MediumPriorityThreshold > 0
                                           && r.MediumPriorityThreshold < r.HighPriorityThreshold)
            .WithMessage("Thresholds must satisfy 0 < medium < high ≤ 100.");
    }
}

public class SettingsService(IAppDbContext db, IWhatsAppProvider whatsApp, IAiProvider ai)
{
    public async Task<ClinicProfileDto> GetProfileAsync(CancellationToken ct)
    {
        var c = await db.Clinics.AsNoTracking().FirstAsync(ct);
        return new ClinicProfileDto(c.Id, c.Name, c.NameAr, c.Phone, c.Email, c.Address, c.City, c.Country, c.TimeZone, c.Currency,
            c.DefaultLanguage, c.SubscriptionPlan);
    }

    public async Task UpdateProfileAsync(ClinicProfileRequest r, CancellationToken ct)
    {
        var c = await db.Clinics.FirstAsync(ct);
        c.Name = r.Name.Trim();
        c.NameAr = r.NameAr?.Trim();
        c.Phone = r.Phone?.Trim();
        c.Email = r.Email?.Trim();
        c.Address = r.Address?.Trim();
        c.City = r.City?.Trim();
        c.TimeZone = r.TimeZone;
        c.Currency = r.Currency;
        c.DefaultLanguage = r.DefaultLanguage;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ClinicSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await db.ClinicSettings.AsNoTracking().FirstAsync(ct);
        return new ClinicSettingsDto(s.WorkingHoursStart, s.WorkingHoursEnd,
            s.WorkingDays.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList(),
            s.SlotMinutes, s.AppointmentReminderHours, s.InvoicePrefix, s.InvoiceDueDays, s.TaxRatePercent, s.WhatsAppEnabled,
            s.AutoSendFollowUps, s.AiAssistantEnabled, s.Recovery);
    }

    public async Task UpdateSettingsAsync(ClinicSettingsDto r, CancellationToken ct)
    {
        var s = await db.ClinicSettings.FirstAsync(ct);
        s.WorkingHoursStart = r.WorkingHoursStart;
        s.WorkingHoursEnd = r.WorkingHoursEnd;
        s.WorkingDays = string.Join(',', r.WorkingDays.Distinct());
        s.SlotMinutes = r.SlotMinutes;
        s.AppointmentReminderHours = r.AppointmentReminderHours;
        s.InvoicePrefix = r.InvoicePrefix;
        s.InvoiceDueDays = r.InvoiceDueDays;
        s.TaxRatePercent = r.TaxRatePercent;
        s.WhatsAppEnabled = r.WhatsAppEnabled;
        s.AutoSendFollowUps = r.AutoSendFollowUps;
        s.AiAssistantEnabled = r.AiAssistantEnabled;
        s.Recovery = r.Recovery;
        await db.SaveChangesAsync(ct);
    }

    public IntegrationStatusDto Integrations() =>
        new(whatsApp.Name, whatsApp.IsConfigured, ai.IsConfigured, ai.ProviderName, ai.IsConfigured ? ai.ModelName : null, "Local disk");
}
