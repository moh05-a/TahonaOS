using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Import;

// ------------------------------------------------------------------ file reading

/// <summary>A spreadsheet as text: one header row and data rows, every cell a trimmed string.</summary>
public record TabularFile(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Reads CSV and Excel uploads. Implemented in infrastructure.</summary>
public interface ITabularFileReader
{
    /// <exception cref="Common.RequestValidationException">The file is not a readable CSV or Excel sheet.</exception>
    TabularFile Read(Stream content, string fileName);
}

// ------------------------------------------------------------------ definitions

/// <summary>A TahonaOS field a source column can be mapped to.</summary>
/// <param name="Synonyms">Header names recognised automatically, in English and Arabic.</param>
public record ImportField(string Key, string Label, string LabelAr, bool Required, string Example, string[] Synonyms, string? Help = null);

/// <param name="RequiredAnyOf">Groups of fields of which at least one must be mapped (e.g. a way to identify the patient).</param>
public record ImportDefinition(ImportKind Kind, IReadOnlyList<ImportField> Fields, IReadOnlyList<IReadOnlyList<string>> RequiredAnyOf);

// ------------------------------------------------------------------ requests

public record ImportOptions
{
    /// <summary>How ambiguous dates such as 03/04/2025 are read: "dmy" (day first, the default in Jordan), "mdy" or "ymd".</summary>
    public string DateFormat { get; init; } = "dmy";
    /// <summary>Patients: import rows that have no phone number instead of rejecting them.</summary>
    public bool AllowMissingPhone { get; init; }
    /// <summary>Patients: keep the source file number as the TahonaOS file number where it is free.</summary>
    public bool KeepFileNumbers { get; init; } = true;
    /// <summary>What to do with a row that matches an existing patient: "skip" or "fillBlanks" (never overwrites).</summary>
    public string DuplicateAction { get; init; } = "skip";
    /// <summary>What to do with a row that only might match an existing patient: "create" or "skip".</summary>
    public string PossibleDuplicateAction { get; init; } = "create";
    /// <summary>Appointments: a past appointment with no status in the source is recorded as completed. Off by default.</summary>
    public bool AssumePastAppointmentsCompleted { get; init; }
    /// <summary>Appointments: the dentist used when a row names none, or one that cannot be matched.</summary>
    public Guid? DefaultDentistId { get; init; }
    public int DefaultDurationMinutes { get; init; } = 30;
}

/// <param name="Mapping">Field key → source column header. Unmapped fields are omitted or null.</param>
/// <param name="Decisions">Per-row overrides for duplicates: row number → "skip", "create" or "fillBlanks".</param>
public record ImportRunRequest(Dictionary<string, string?> Mapping, ImportOptions? Options, Dictionary<int, string>? Decisions);

// ------------------------------------------------------------------ results

public enum RowVerdict
{
    /// <summary>Valid and new.</summary>
    Ready,
    /// <summary>Matches an existing record on strong evidence.</summary>
    Duplicate,
    /// <summary>Shares some details with an existing record; a person should decide.</summary>
    PossibleDuplicate,
    /// <summary>This exact source record was imported before.</summary>
    AlreadyImported,
    /// <summary>Cannot be imported as it is.</summary>
    Error
}

public static class RowActions
{
    public const string Create = "create";
    public const string Skip = "skip";
    public const string FillBlanks = "fillBlanks";
}

/// <summary>The outcome of checking one source row. Nothing has been written when this is produced.</summary>
/// <param name="Action">What will happen on import: create, skip or fillBlanks.</param>
/// <param name="AllowedActions">The choices a person may make for this row.</param>
/// <param name="Display">A short human label for the row (name, phone, date…).</param>
/// <param name="MatchLabel">The existing record this row matches, when there is one.</param>
public record RowEvaluation(int RowNumber, RowVerdict Verdict, string Action, IReadOnlyList<string> AllowedActions,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, string Display, Guid? MatchId, string? MatchLabel)
{
    /// <summary>The money amount the row carries (opening balances), for the reconciliation total.</summary>
    public decimal? Amount { get; init; }

    /// <summary>The parsed row, kept server-side for the apply step.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public object? Model { get; init; }
}

public record ImportSummaryDto(int TotalRows, int Ready, int Duplicates, int PossibleDuplicates, int AlreadyImported, int Errors,
    int WillCreate, int WillUpdate, int WillSkip, int RowsWithWarnings, decimal? TotalAmount);

/// <param name="Rows">Rows needing attention first (errors, duplicates, warnings), then a sample of clean rows; capped.</param>
public record ImportPreviewDto(ImportSummaryDto Summary, IReadOnlyList<RowEvaluation> Rows, bool RowsTruncated, IReadOnlyList<string> MappingProblems);

public record ImportBatchDto(Guid Id, ImportKind Kind, string FileName, string SourceSystem, ImportBatchStatus Status, int TotalRows,
    int CreatedCount, int UpdatedCount, int SkippedCount, int ErrorCount, decimal? TotalAmount, DateTime CreatedAt, string? CreatedBy,
    DateTime? CommittedAt, DateTime? UndoneAt, string? LastError);

/// <param name="PreviousImportAt">When this same file was last imported, if it was: a warning against importing it twice.</param>
public record ImportUploadDto(ImportBatchDto Batch, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> SampleRows,
    Dictionary<string, string?> SuggestedMapping, Dictionary<string, string?>? SavedMapping, ImportOptions? SavedOptions, DateTime? PreviousImportAt);

public record ImportCommitDto(ImportBatchDto Batch, ImportSummaryDto Summary);

public record ImportUndoDto(ImportBatchDto Batch, int Removed, int Kept, IReadOnlyList<string> Notes);
