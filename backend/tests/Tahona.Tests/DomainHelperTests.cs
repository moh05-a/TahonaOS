using Tahona.Application.Features.Ai;
using Tahona.Domain.Common;

namespace Tahona.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0791234567", "791234567")]
    [InlineData("+962 79 123 4567", "791234567")]
    [InlineData("00962791234567", "791234567")]
    [InlineData("962-79-123-4567", "791234567")]
    [InlineData("", "")]
    public void Normalizes_jordanian_formats(string input, string expected) => Assert.Equal(expected, PhoneNumber.Normalize(input));

    [Fact]
    public void Builds_international_number_for_whatsapp() => Assert.Equal("962791234567", PhoneNumber.ToInternational("0791234567"));

    [Theory]
    [InlineData("0791234567", true)]
    [InlineData("079 123", true)]
    [InlineData("Ahmad", false)]
    public void Detects_phone_queries(string input, bool expected) => Assert.Equal(expected, PhoneNumber.LooksLikePhone(input));
}

public class TeethTests
{
    [Theory]
    [InlineData(11, true)]
    [InlineData(48, true)]
    [InlineData(55, true)]
    [InlineData(19, false)]
    [InlineData(56, false)]
    [InlineData(90, false)]
    public void Validates_fdi_numbers(int tooth, bool valid) => Assert.Equal(valid, Teeth.IsValid(tooth));

    [Fact]
    public void Has_full_dentitions()
    {
        Assert.Equal(32, Teeth.Permanent.Count);
        Assert.Equal(20, Teeth.Primary.Count);
    }
}

public class ClinicalNoteStructuringTests
{
    private const string Dictation =
        "Patient came complaining of pain in upper right molar. Caries visible. X-ray recommended. Patient advised root canal.";

    [Fact]
    public void Rule_based_structuring_keeps_only_dentist_words()
    {
        var note = ClinicalNoteAiService.StructureWithRules(Dictation);
        Assert.Contains("pain in upper right molar", note.ChiefComplaint);
        Assert.Equal("Caries visible.", note.ClinicalFindings);
        Assert.Contains("advised root canal", note.Recommendation);
        Assert.Null(note.Assessment); // never invents a diagnosis
    }

    [Fact]
    public void Grounding_guard_flags_invented_terms_and_teeth()
    {
        var invented = new StructuredNoteDto("Pain upper right molar", "Caries visible on 16", "Irreversible pulpitis", null,
            "Root canal", "Amoxicillin 500mg", null, "claude", []);
        var warnings = ClinicalNoteAiService.GroundingWarnings(Dictation, invented);

        Assert.Contains(warnings, w => w.Contains("pulpitis"));
        Assert.Contains(warnings, w => w.Contains("amoxicillin"));
        Assert.Contains(warnings, w => w.Contains("Tooth 16"));
        Assert.DoesNotContain(warnings, w => w.Contains("root canal"));
    }
}
