using Tahona.Domain.Common;

namespace Tahona.Domain.Entities;

public class ClinicalNote : TenantEntity, IAuditable
{
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = default!;
    public Guid DentistId { get; set; }
    public Dentist Dentist { get; set; } = default!;
    public Guid? AppointmentId { get; set; }
    public DateTime NoteDate { get; set; }

    public string? ChiefComplaint { get; set; }
    public string? Diagnosis { get; set; }
    public string? ClinicalFindings { get; set; }
    public string? TreatmentPerformed { get; set; }
    public string? Recommendations { get; set; }
    public string? PrescriptionNotes { get; set; }
    public string? FollowUpInstructions { get; set; }
    public string? FreeText { get; set; }

    /// <summary>True when the dentist used the AI note structurer. The dentist remains the author.</summary>
    public bool IsAiAssisted { get; set; }
}
