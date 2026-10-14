using FluentValidation;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Patients;

public record PatientListQuery : PageQuery
{
    public string? Search { get; init; }
    public PatientStatus? Status { get; init; }
    public Guid? DentistId { get; init; }
    /// <summary>name | lastVisit | registered | balance</summary>
    public string? Sort { get; init; }
    public bool Desc { get; init; }
}

public record PatientListItemDto(
    Guid Id, string FileNumber, string FullName, string? FullNameAr, string Phone, Gender? Gender, int? Age,
    PatientStatus Status, DateTime? LastVisitAt, DateTime? NextAppointmentAt, decimal OutstandingBalance,
    decimal OpenTreatmentValue, string? PrimaryDentistName, DateTime RegisteredAt, bool HasMedicalAlert);

public record PatientUpsertRequest
{
    public string FullName { get; init; } = default!;
    public string? FullNameAr { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public Gender? Gender { get; init; }
    public string Phone { get; init; } = default!;
    public string? WhatsAppNumber { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? NationalId { get; init; }
    public string? EmergencyContactName { get; init; }
    public string? EmergencyContactPhone { get; init; }
    public string? EmergencyContactRelation { get; init; }
    public string? MedicalNotes { get; init; }
    public string? Allergies { get; init; }
    public string? MedicalConditions { get; init; }
    public string? CurrentMedications { get; init; }
    public string? InsuranceProvider { get; init; }
    public string? InsurancePolicyNumber { get; init; }
    public decimal? InsuranceCoveragePercent { get; init; }
    public string PreferredLanguage { get; init; } = "ar";
    /// <summary>Omit to leave the recorded consent unchanged.</summary>
    public MessagingConsent? MessagingConsent { get; init; }
    public PatientStatus Status { get; init; } = PatientStatus.Active;
    public string? ReferralSource { get; init; }
    public Guid? PrimaryDentistId { get; init; }
}

public record PatientStatsDto(
    int CompletedVisits, int NoShows, int UpcomingAppointments, decimal TotalBilled, decimal TotalPaid,
    decimal OutstandingBalance, decimal OpenTreatmentValue, int ActivePlans, DateTime? NextAppointmentAt, int? DaysSinceLastVisit);

public record PatientDetailDto(
    Guid Id, string FileNumber, string FullName, string? FullNameAr, DateOnly? DateOfBirth, int? Age, Gender? Gender,
    string Phone, string? WhatsAppNumber, string? Email, string? Address, string? City, string? NationalId,
    string? EmergencyContactName, string? EmergencyContactPhone, string? EmergencyContactRelation,
    string? MedicalNotes, string? Allergies, string? MedicalConditions, string? CurrentMedications,
    string? InsuranceProvider, string? InsurancePolicyNumber, decimal? InsuranceCoveragePercent,
    string PreferredLanguage, PatientStatus Status, DateTime RegisteredAt, string? ReferralSource,
    Guid? PrimaryDentistId, string? PrimaryDentistName, DateTime? LastVisitAt, PatientStatsDto Stats,
    MessagingConsent MessagingConsent, DateTime? MessagingConsentAt, string? MessagingConsentSource);

public record TimelineEventDto(
    string Kind, string Title, string? Description, DateTime Date, decimal? Amount, string? Status, Guid? EntityId);

public record PatientNoteDto(Guid Id, string Content, bool IsPinned, string? AuthorName, DateTime CreatedAt);

public record PatientNoteRequest(string Content, bool IsPinned);

public class PatientUpsertValidator : AbstractValidator<PatientUpsertRequest>
{
    public PatientUpsertValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FullNameAr).MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30)
            .Must(p => PhoneNumber.Normalize(p).Length is >= 7 and <= 15).WithMessage("Enter a valid phone number.");
        RuleFor(x => x.WhatsAppNumber).MaximumLength(30)
            .Must(p => string.IsNullOrWhiteSpace(p) || PhoneNumber.Normalize(p).Length is >= 7 and <= 15)
            .WithMessage("Enter a valid WhatsApp number.");
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).MaximumLength(200);
        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || (d <= DateOnly.FromDateTime(DateTime.UtcNow) && d > new DateOnly(1900, 1, 1)))
            .WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.InsuranceCoveragePercent).InclusiveBetween(0, 100).When(x => x.InsuranceCoveragePercent.HasValue);
        RuleFor(x => x.PreferredLanguage).Must(l => l is "en" or "ar").WithMessage("Language must be en or ar.");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.MessagingConsent).IsInEnum().When(x => x.MessagingConsent.HasValue);
        RuleFor(x => x.Gender).IsInEnum().When(x => x.Gender.HasValue);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.City).MaximumLength(80);
        RuleFor(x => x.NationalId).MaximumLength(40);
        RuleFor(x => x.MedicalNotes).MaximumLength(4000);
        RuleFor(x => x.Allergies).MaximumLength(1000);
        RuleFor(x => x.MedicalConditions).MaximumLength(1000);
        RuleFor(x => x.CurrentMedications).MaximumLength(1000);
        RuleFor(x => x.EmergencyContactName).MaximumLength(150);
        RuleFor(x => x.EmergencyContactPhone).MaximumLength(30);
    }
}

public class PatientNoteRequestValidator : AbstractValidator<PatientNoteRequest>
{
    public PatientNoteRequestValidator() => RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
}
