using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Tahona.Application.Common;

namespace Tahona.Application.Features.Ai;

public record StructureNoteRequest(string Text);

/// <summary>Draft fields for a clinical note. The dentist reviews and edits before saving.</summary>
public record StructuredNoteDto(
    string? ChiefComplaint, string? ClinicalFindings, string? Assessment, string? TreatmentPerformed,
    string? Recommendation, string? PrescriptionNotes, string? FollowUp, string Source, IReadOnlyList<string> Warnings);

public class StructureNoteRequestValidator : AbstractValidator<StructureNoteRequest>
{
    public StructureNoteRequestValidator() => RuleFor(x => x.Text).NotEmpty().MinimumLength(10).MaximumLength(6000);
}

/// <summary>
/// Organises a dentist's free-text dictation into structured sections. It must not diagnose:
/// the prompt forbids adding information, the schema has no free "suggestions" field, and a
/// post-check flags any section containing clinical terms the dentist did not write.
/// </summary>
public partial class ClinicalNoteAiService(IAiProvider ai, IAiDataPolicy policy)
{
    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "chief_complaint":     { "type": "string" },
            "clinical_findings":   { "type": "string" },
            "assessment":          { "type": "string" },
            "treatment_performed": { "type": "string" },
            "recommendation":      { "type": "string" },
            "prescription_notes":  { "type": "string" },
            "follow_up":           { "type": "string" }
          },
          "required": ["chief_complaint", "clinical_findings", "assessment", "treatment_performed", "recommendation", "prescription_notes", "follow_up"],
          "additionalProperties": false
        }
        """;

    private const string System = """
        You are a documentation assistant for dentists. You reorganise the dentist's own dictated note into sections.

        Strict rules:
        - Use ONLY information explicitly written by the dentist. Do not add diagnoses, findings, tooth numbers,
          medications, doses, recommendations or follow-up intervals that are not in the text.
        - Do not interpret, infer or "complete" the clinical picture. If a section is not covered, return an empty string.
        - "assessment" holds only a diagnosis or assessment the dentist explicitly stated.
        - Keep the dentist's wording; light grammar cleanup is fine. Keep the original language (English or Arabic).
        - Tooth references must be copied exactly as written.
        """;

    public async Task<StructuredNoteDto> StructureAsync(StructureNoteRequest request, CancellationToken ct)
    {
        var text = request.Text.Trim();
        StructuredNoteDto result;

        // A dictated note is patient-level clinical text: with Ai:PatientData=Off it never leaves the server.
        if (ai.IsConfigured && policy.PatientData != AiPatientDataMode.Off)
        {
            var json = await ai.CompleteJsonAsync(System, $"Dentist's note:\n\"\"\"\n{text}\n\"\"\"", JsonDocument.Parse(Schema).RootElement, ct);
            if (json is null) throw new BusinessRuleException("The AI declined to structure this note. Please fill the sections manually.");
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string? Get(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim() : null;
            result = new StructuredNoteDto(Get("chief_complaint"), Get("clinical_findings"), Get("assessment"), Get("treatment_performed"),
                Get("recommendation"), Get("prescription_notes"), Get("follow_up"), "llm", []);
        }
        else
        {
            result = StructureWithRules(text);
        }

        return result with { Warnings = [.. result.Warnings, .. GroundingWarnings(text, result)] };
    }

    // ------------------------------------------------------------------ rule-based fallback

    [GeneratedRegex(@"(?<=[.!?؟\n])\s+|;\s*")]
    private static partial Regex SentenceSplit();

    private static readonly (string Section, string[] Keywords)[] Rules =
    [
        ("follow", ["follow-up", "follow up", "review in", "recall", "come back", "return in", "next visit", "متابعة", "مراجعة"]),
        ("prescription", ["prescribed", "prescription", "rx ", " mg", "amoxicillin", "ibuprofen", "paracetamol", "chlorhexidine", "دواء", "وصفة"]),
        ("recommendation", ["advised", "recommended", "recommend", "suggested", "plan:", "needs", "should", "نصح", "ينصح", "يوصى"]),
        ("treatment", ["performed", "done", "placed", "extracted", "filled", "completed", "administered", "scaling", "تم", "أجري"]),
        ("assessment", ["diagnosis", "diagnosed", "assessment", "impression", "dx", "تشخيص"]),
        ("findings", ["visible", "noted", "observed", "found", "x-ray shows", "radiograph", "caries", "swelling", "tender", "mobility", "bleeding", "ظاهر", "لوحظ"]),
        ("complaint", ["complain", "complaining", "pain", "came with", "presented with", "c/o", "sensitivity", "يشكو", "ألم"])
    ];

    public static StructuredNoteDto StructureWithRules(string text)
    {
        var buckets = Rules.ToDictionary(r => r.Section, _ => new List<string>());
        var sentences = SentenceSplit().Split(text).Select(s => s.Trim()).Where(s => s.Length > 0);
        var unassigned = new List<string>();

        foreach (var sentence in sentences)
        {
            var lower = sentence.ToLowerInvariant();
            var match = Rules.FirstOrDefault(r => r.Keywords.Any(lower.Contains));
            if (match.Section is null) unassigned.Add(sentence);
            else buckets[match.Section].Add(sentence);
        }

        // Unclassified sentences are findings (they describe what the dentist saw) only if nothing else is there; otherwise keep with complaint.
        if (unassigned.Count > 0) (buckets["findings"].Count == 0 ? buckets["findings"] : buckets["complaint"]).AddRange(unassigned);

        string? Join(string key) => buckets[key].Count == 0 ? null : string.Join(" ", buckets[key]);
        return new StructuredNoteDto(Join("complaint"), Join("findings"), Join("assessment"), Join("treatment"),
            Join("recommendation"), Join("prescription"), Join("follow"), "built-in",
            ["Structured with built-in rules (no AI key configured). Review every section."]);
    }

    // ------------------------------------------------------------------ grounding guard

    private static readonly string[] ClinicalTerms =
    [
        "pulpitis", "periodontitis", "gingivitis", "abscess", "necrosis", "necrotic", "fracture", "caries", "cyst",
        "root canal", "extraction", "crown", "implant", "bridge", "filling", "amoxicillin", "ibuprofen", "paracetamol",
        "metronidazole", "clindamycin", "chlorhexidine", "antibiotic", "x-ray", "cbct", "periapical"
    ];

    [GeneratedRegex(@"\b[1-8][1-8]\b")]
    private static partial Regex ToothNumber();

    /// <summary>Flags clinical terms or tooth numbers in the output that never appeared in the dentist's text.</summary>
    public static IReadOnlyList<string> GroundingWarnings(string source, StructuredNoteDto note)
    {
        var src = source.ToLowerInvariant();
        var output = string.Join(" ", new[] { note.ChiefComplaint, note.ClinicalFindings, note.Assessment, note.TreatmentPerformed,
            note.Recommendation, note.PrescriptionNotes, note.FollowUp }.Where(s => s is not null)).ToLowerInvariant();

        var warnings = ClinicalTerms.Where(t => output.Contains(t) && !src.Contains(t))
            .Select(t => $"\"{t}\" does not appear in your original note — please verify.").ToList();
        var srcTeeth = ToothNumber().Matches(src).Select(m => m.Value).ToHashSet();
        warnings.AddRange(ToothNumber().Matches(output).Select(m => m.Value).Distinct().Where(t => !srcTeeth.Contains(t))
            .Select(t => $"Tooth {t} does not appear in your original note — please verify."));
        return warnings;
    }
}
