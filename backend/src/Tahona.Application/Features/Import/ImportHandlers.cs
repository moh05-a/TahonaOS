using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Import;

/// <summary>One source row with access to its cells by mapped field.</summary>
public sealed class SourceRow(int number, IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> mapping)
{
    public int Number => number;

    /// <summary>The cell mapped to <paramref name="field"/>, normalised; empty when unmapped or blank.</summary>
    public string this[string field] => mapping.TryGetValue(field, out var i) && i < cells.Count ? ImportParsing.Normalize(cells[i]) : "";

    public bool Has(string field) => this[field].Length > 0;
}

public record ImportInput(IReadOnlyList<SourceRow> Rows, ImportOptions Options, IReadOnlyDictionary<int, string> Decisions, string SourceSystem);

/// <param name="Outcome">Row number → the record created or updated, for rows that produced one.</param>
public record ApplyResult(IReadOnlyDictionary<int, (ImportRowStatus Status, string EntityType, Guid EntityId)> Outcome, decimal? TotalAmount);

/// <summary>Checks and applies one kind of import. Evaluation never writes; applying adds to the unit of work without saving.</summary>
public interface IImportHandler
{
    ImportKind Kind { get; }
    Task<IReadOnlyList<RowEvaluation>> EvaluateAsync(ImportInput input, CancellationToken ct);
    Task<ApplyResult> ApplyAsync(ImportBatch batch, IReadOnlyList<RowEvaluation> rows, ImportInput input, CancellationToken ct);
}

internal static class ImportRules
{
    private static readonly string[] All = [RowActions.Skip, RowActions.Create, RowActions.FillBlanks];

    /// <summary>The action for a row: the person's decision when it is one of the allowed choices, otherwise the default.</summary>
    public static string Decide(ImportInput input, int row, string fallback, IReadOnlyList<string> allowed) =>
        input.Decisions.TryGetValue(row, out var chosen) && allowed.Contains(chosen) ? chosen : allowed.Contains(fallback) ? fallback : allowed[0];

    public static RowEvaluation Error(int row, string display, IEnumerable<string> errors, IEnumerable<string>? warnings = null) =>
        new(row, RowVerdict.Error, RowActions.Skip, [RowActions.Skip], errors.ToList(), warnings?.ToList() ?? [], display, null, null);

    /// <summary>The sample row of a downloaded template must never become a record.</summary>
    public static bool IsTemplateExample(params string[] values) =>
        values.Any(v => v.Equals("Example Patient", StringComparison.OrdinalIgnoreCase) || v.Equals("EXAMPLE-1", StringComparison.OrdinalIgnoreCase)
                        || v == "مريض تجريبي");

    /// <summary>Matches a dentist by name, ignoring a "Dr." / "د." title.</summary>
    public static bool SameDentist(string? known, string given)
    {
        static string Strip(string? s)
        {
            var key = ImportParsing.Key(s);
            foreach (var title in new[] { "doctor", "dr", "الدكتوره", "الدكتور", "دكتوره", "دكتور" })
                if (key.StartsWith(title) && key.Length > title.Length) return key[title.Length..];
            return key.StartsWith('د') && (s ?? "").TrimStart().StartsWith("د.") ? key[1..] : key;
        }
        var a = Strip(known);
        return a.Length > 0 && a == Strip(given);
    }

    public const string TemplateExampleMessage = "This is the example row from the template. Delete it from your file.";
}

/// <summary>Finds the existing patient a row refers to, by file number, previous-system ID, or phone (with the name to break ties).</summary>
internal sealed class PatientResolver
{
    private sealed record Entry(Guid Id, string FileNumber, string FullName, string? FullNameAr, string Phone, string? ExternalId, string? SourceSystem);

    private readonly ILookup<string, Entry> _byFile;
    private readonly ILookup<string, Entry> _byExternal;
    private readonly ILookup<string, Entry> _byPhone;

    private PatientResolver(List<Entry> patients)
    {
        _byFile = patients.ToLookup(p => p.FileNumber.Trim().ToUpperInvariant());
        _byExternal = patients.Where(p => !string.IsNullOrEmpty(p.ExternalId)).ToLookup(p => p.ExternalId!.Trim().ToUpperInvariant());
        _byPhone = patients.Where(p => p.Phone.Length > 0).ToLookup(p => p.Phone);
    }

    public static async Task<PatientResolver> LoadAsync(IAppDbContext db, CancellationToken ct) => new(
        await db.Patients.AsNoTracking()
            .Select(p => new Entry(p.Id, p.FileNumber, p.FullName, p.FullNameAr, p.PhoneNormalized, p.ExternalId, p.SourceSystem))
            .ToListAsync(ct));

    /// <summary>Returns the patient, or an explanation of why the row cannot be tied to exactly one.</summary>
    public (Guid? Id, string? Label, string? Error) Resolve(SourceRow row, string sourceSystem)
    {
        var file = row[ImportDefinitions.F.PatientFileNumber].ToUpperInvariant();
        var external = row[ImportDefinitions.F.PatientExternalId].ToUpperInvariant();
        var phone = PhoneNumber.Normalize(row[ImportDefinitions.F.PatientPhone]);
        var name = row[ImportDefinitions.F.PatientName];
        if (file.Length == 0 && external.Length == 0 && phone.Length == 0)
            return (null, null, "The row does not say which patient it belongs to (no file number, ID or phone).");

        // A file number may be the TahonaOS number or the one kept from the previous system.
        foreach (var key in new[] { file, external }.Where(k => k.Length > 0))
        {
            var matches = _byFile[key].Concat(_byExternal[key]).DistinctBy(p => p.Id).ToList();
            if (matches.Count > 1) matches = matches.Where(p => p.SourceSystem == sourceSystem).DefaultIfEmpty(matches[0]).ToList();
            if (matches.Count == 1) return Found(matches[0]);
            if (matches.Count > 1) return (null, null, $"More than one patient has the number '{key}'.");
        }

        if (phone.Length > 0)
        {
            var matches = _byPhone[phone].ToList();
            if (matches.Count > 1 && name.Length > 0)
                matches = matches.Where(p => ImportParsing.SameName(p.FullName, name) || ImportParsing.SameName(p.FullNameAr, name)).ToList();
            if (matches.Count == 1) return Found(matches[0]);
            if (matches.Count > 1) return (null, null, "Several patients share this phone number. Add the file number or the patient's name to tell them apart.");
        }

        var tried = string.Join(", ", new[] { file, external, row[ImportDefinitions.F.PatientPhone] }.Where(v => v.Length > 0));
        return (null, null, $"No patient found for {tried}. Import the patients first, or correct the value.");
    }

    private static (Guid? Id, string? Label, string? Error) Found(Entry p) => (p.Id, $"{p.FullName} · {p.FileNumber}", null);
}
