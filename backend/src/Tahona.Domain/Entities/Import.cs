using Tahona.Domain.Common;
using Tahona.Domain.Enums;

namespace Tahona.Domain.Entities;

/// <summary>
/// One uploaded source file and what became of it. The batch and its rows are the permanent record of what was
/// imported, from where, by whom, and which TahonaOS record each source row produced.
/// </summary>
public class ImportBatch : TenantEntity, IAuditable
{
    public ImportKind Kind { get; set; }
    public string FileName { get; set; } = default!;
    /// <summary>SHA-256 of the uploaded bytes: the same file uploaded twice is recognised.</summary>
    public string FileHash { get; set; } = default!;
    /// <summary>Where the data came from, as the clinic described it ("Excel sheet", "DentalSoft 4").</summary>
    public string SourceSystem { get; set; } = default!;
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Uploaded;
    /// <summary>Column headers of the source file, in order (JSON array).</summary>
    public string HeadersJson { get; set; } = "[]";
    /// <summary>Field → source column mapping used for the import (JSON object).</summary>
    public string? MappingJson { get; set; }
    /// <summary>Options chosen for the import (JSON object).</summary>
    public string? OptionsJson { get; set; }

    public int TotalRows { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    /// <summary>Sum of imported monetary amounts (opening balances), for reconciliation against the source.</summary>
    public decimal? TotalAmount { get; set; }

    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime? CommittedAt { get; set; }
    public Guid? CommittedById { get; set; }
    public DateTime? UndoneAt { get; set; }
    public string? LastError { get; set; }

    public ICollection<ImportRow> Rows { get; set; } = [];
}

/// <summary>One row of the source file, kept verbatim, with its outcome.</summary>
public class ImportRow : TenantEntity
{
    public Guid BatchId { get; set; }
    public ImportBatch Batch { get; set; } = default!;
    /// <summary>1-based row number in the source file, not counting the header.</summary>
    public int RowNumber { get; set; }
    /// <summary>The source cells in column order (JSON array of strings), exactly as read.</summary>
    public string DataJson { get; set; } = "[]";
    public ImportRowStatus Status { get; set; } = ImportRowStatus.Pending;
    /// <summary>Validation messages or the reason the row was skipped (JSON array of strings).</summary>
    public string? MessagesJson { get; set; }
    /// <summary>The record this row created or updated.</summary>
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
}
