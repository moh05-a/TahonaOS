using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

public class Patient : TenantEntity, ISoftDelete, IAuditable
{
    public string FileNumber { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string? FullNameAr { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public Gender? Gender { get; set; }
    public string Phone { get; set; } = default!;
    /// <summary>Digits-only national form (e.g. 791234567) so +962 / 0 prefixed searches both match.</summary>
    public string PhoneNormalized { get; set; } = default!;
    public string? WhatsAppNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? NationalId { get; set; }

    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }

    public string? MedicalNotes { get; set; }
    public string? Allergies { get; set; }
    public string? MedicalConditions { get; set; }
    public string? CurrentMedications { get; set; }

    public string? InsuranceProvider { get; set; }
    public string? InsurancePolicyNumber { get; set; }
    public decimal? InsuranceCoveragePercent { get; set; }

    public string PreferredLanguage { get; set; } = "ar";

    public MessagingConsent MessagingConsent { get; set; } = MessagingConsent.Unknown;
    public DateTime? MessagingConsentAt { get; set; }
    /// <summary>Who or what recorded the consent state: a staff member's name, "whatsapp-reply", "import".</summary>
    public string? MessagingConsentSource { get; set; }

    public PatientStatus Status { get; set; } = PatientStatus.Active;
    public DateTime RegisteredAt { get; set; }
    public string? ReferralSource { get; set; }
    public Guid? PrimaryDentistId { get; set; }
    public Dentist? PrimaryDentist { get; set; }

    /// <summary>The patient's identifier in the system it was imported from, kept verbatim.</summary>
    public string? ExternalId { get; set; }
    /// <summary>Name of the source the record was imported from; null for patients registered in TahonaOS.</summary>
    public string? SourceSystem { get; set; }
    public Guid? ImportBatchId { get; set; }

    /// <summary>Denormalised: last completed visit, maintained by the appointment service (or taken from an import).</summary>
    public DateTime? LastVisitAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<TreatmentPlan> TreatmentPlans { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
}

/// <summary>Non-clinical staff notes (e.g. "prefers evening calls").</summary>
public class PatientNote : TenantEntity, IAuditable
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public string Content { get; set; } = default!;
    public bool IsPinned { get; set; }
    public Guid? AuthorId { get; set; }
    public User? Author { get; set; }
}
