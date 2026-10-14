using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using static Tahona.Application.Features.Import.ImportDefinitions;

namespace Tahona.Application.Features.Import;

/// <summary>
/// Carries over what patients owed in the previous system. Each row becomes one invoice clearly marked as an
/// opening balance, with the amount outstanding. No payment history, treatment or invoice detail is invented:
/// the clinic's earlier ledger stays the record of how the balance arose.
/// </summary>
public class BalanceImportHandler(IAppDbContext db, ISequenceGenerator sequences, IClinicTime clinicTime) : IImportHandler
{
    public ImportKind Kind => ImportKind.OpeningBalances;

    private sealed record Parsed(Guid PatientId, decimal Amount, DateTime IssueUtc, DateTime DueUtc, string? Reference, string? Notes);

    public async Task<IReadOnlyList<RowEvaluation>> EvaluateAsync(ImportInput input, CancellationToken ct)
    {
        var patients = await PatientResolver.LoadAsync(db, ct);
        var withOpeningBalance = (await db.Invoices.AsNoTracking().Where(i => i.IsOpeningBalance && i.Status != InvoiceStatus.Void)
            .Select(i => i.PatientId).Distinct().ToListAsync(ct)).ToHashSet();
        var today = clinicTime.Today;
        var seenReference = new Dictionary<string, int>();
        var results = new List<RowEvaluation>();

        foreach (var row in input.Rows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var display = string.Join(" · ", new[] { row[F.PatientName], row[F.PatientFileNumber], row[F.Amount] }.Where(v => v.Length > 0));
            if (display.Length == 0) display = $"Row {row.Number}";

            if (ImportRules.IsTemplateExample(row[F.PatientName], row[F.PatientFileNumber]))
            {
                results.Add(ImportRules.Error(row.Number, display, [ImportRules.TemplateExampleMessage]));
                continue;
            }

            var amount = ImportParsing.Amount(row[F.Amount]);
            if (!row.Has(F.Amount)) errors.Add("The amount is missing.");
            else if (amount is null) errors.Add($"The amount '{row[F.Amount]}' could not be read.");
            else if (amount < 0) errors.Add("Negative amounts (credits owed to the patient) are not imported. Record the credit by hand after the import.");
            else if (amount > 1_000_000) errors.Add("The amount is larger than 1,000,000. Check the column and the decimal separator.");

            if (amount == 0)
            {
                // Nothing is owed: not an error, simply nothing to carry over.
                results.Add(new RowEvaluation(row.Number, RowVerdict.Ready, RowActions.Skip, [RowActions.Skip], [], ["The balance is zero, so there is nothing to import."],
                    display, null, null));
                continue;
            }

            var (patientId, patientLabel, patientError) = patients.Resolve(row, input.SourceSystem);
            if (patientError is not null) errors.Add(patientError);

            var asOf = today;
            if (row.Has(F.AsOfDate))
            {
                if (ImportParsing.Date(row[F.AsOfDate], input.Options.DateFormat) is not { } d) errors.Add($"The balance date '{row[F.AsOfDate]}' could not be read.");
                else if (d > today) errors.Add("The balance date is in the future.");
                else asOf = d;
            }
            var due = asOf;
            if (row.Has(F.DueDate))
            {
                if (ImportParsing.Date(row[F.DueDate], input.Options.DateFormat) is not { } d) errors.Add($"The due date '{row[F.DueDate]}' could not be read.");
                else if (d < asOf) warnings.Add("The due date is before the balance date; the balance date is used.");
                else due = d;
            }

            if (errors.Count > 0 || patientId is null || amount is null)
            {
                results.Add(ImportRules.Error(row.Number, display, errors, warnings));
                continue;
            }

            var parsed = new Parsed(patientId.Value, Money.Round(amount.Value), clinicTime.ToUtc(asOf.ToDateTime(new TimeOnly(12, 0))),
                clinicTime.ToUtc(due.ToDateTime(new TimeOnly(12, 0))), Clip(row[F.Reference], 100), Clip(row[F.Notes], 1500));
            if (parsed.Amount != amount.Value) warnings.Add($"The amount was rounded to three decimals ({parsed.Amount:0.000}).");

            // The same source invoice listed twice in the file.
            var referenceKey = parsed.Reference is null ? null : $"{patientId}|{parsed.Reference.ToUpperInvariant()}";
            if (referenceKey is not null && seenReference.TryGetValue(referenceKey, out var firstRow))
            {
                var allowed = new[] { RowActions.Skip, RowActions.Create };
                results.Add(new RowEvaluation(row.Number, RowVerdict.Duplicate, ImportRules.Decide(input, row.Number, RowActions.Skip, allowed), allowed, [],
                    [.. warnings, $"Same patient and reference as row {firstRow} of this file."], display, patientId, $"Row {firstRow}") { Model = parsed, Amount = parsed.Amount });
                continue;
            }
            if (referenceKey is not null) seenReference[referenceKey] = row.Number;

            if (withOpeningBalance.Contains(patientId.Value))
            {
                var allowed = new[] { RowActions.Skip, RowActions.Create };
                results.Add(new RowEvaluation(row.Number, RowVerdict.Duplicate, ImportRules.Decide(input, row.Number, RowActions.Skip, allowed), allowed, [],
                    [.. warnings, "This patient already has an imported opening balance. Importing again would count the debt twice."], display,
                    patientId, patientLabel) { Model = parsed, Amount = parsed.Amount });
                continue;
            }

            results.Add(new RowEvaluation(row.Number, RowVerdict.Ready, ImportRules.Decide(input, row.Number, RowActions.Create, [RowActions.Create, RowActions.Skip]),
                [RowActions.Create, RowActions.Skip], [], warnings, display, patientId, patientLabel) { Model = parsed, Amount = parsed.Amount });
        }
        return results;
    }

    public async Task<ApplyResult> ApplyAsync(ImportBatch batch, IReadOnlyList<RowEvaluation> rows, ImportInput input, CancellationToken ct)
    {
        var outcome = new Dictionary<int, (ImportRowStatus, string, Guid)>();
        decimal total = 0;
        foreach (var row in rows.Where(r => r.Action == RowActions.Create))
        {
            if (row.Model is not Parsed p) continue;
            var description = $"Opening balance carried over from {input.SourceSystem}";
            var invoice = new Invoice
            {
                InvoiceNumber = await sequences.NextAsync(SequenceKind.Invoice, ct),
                PatientId = p.PatientId, IssueDate = p.IssueUtc, DueDate = p.DueUtc, Status = InvoiceStatus.Issued,
                Subtotal = p.Amount, Total = p.Amount, Balance = p.Amount, IsOpeningBalance = true, ImportBatchId = batch.Id,
                Notes = string.Join("\n", new[] { description + ".", p.Reference is null ? null : $"Source reference: {p.Reference}", p.Notes }.Where(v => v is not null)),
                Items = [new InvoiceItem { Description = description, Quantity = 1, UnitPrice = p.Amount, Total = p.Amount }]
            };
            db.Invoices.Add(invoice);
            outcome[row.RowNumber] = (ImportRowStatus.Created, nameof(Invoice), invoice.Id);
            total += p.Amount;
        }
        return new ApplyResult(outcome, total);
    }

    private static string? Clip(string value, int max) => value.Length == 0 ? null : value.Length <= max ? value : value[..max];
}
