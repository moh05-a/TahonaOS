using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using static Tahona.Application.Features.Import.ImportDefinitions;

namespace Tahona.Application.Features.Import;

/// <summary>
/// Imports treatment plans, one source row per planned procedure. Rows are grouped into plans by patient and plan
/// reference (or title). A plan is imported whole or not at all: one bad row rejects every row of its plan.
/// Nothing clinical is invented: an item is completed only where the source says so, a plan without a status is
/// imported as a draft, and no treatment history, invoice or payment is created.
/// </summary>
public class TreatmentPlanImportHandler(IAppDbContext db, ISequenceGenerator sequences, IClock clock, IClinicTime clinicTime) : IImportHandler
{
    public ImportKind Kind => ImportKind.TreatmentPlans;

    private sealed record Item(int Row, string Name, int? Tooth, int Quantity, decimal UnitPrice, TreatmentItemStatus Status, DateTime? CompletedUtc);

    private sealed record Plan(Guid PatientId, Guid DentistId, string Title, string? Reference, TreatmentPlanStatus Status, DateTime? PlanUtc, List<Item> Items);

    public async Task<IReadOnlyList<RowEvaluation>> EvaluateAsync(ImportInput input, CancellationToken ct)
    {
        var patients = await PatientResolver.LoadAsync(db, ct);
        var dentists = await db.Dentists.AsNoTracking().Select(d => new { d.Id, d.FullName, d.FullNameAr }).ToListAsync(ct);
        var defaultDentist = input.Options.DefaultDentistId is { } id ? dentists.FirstOrDefault(d => d.Id == id) : null;
        var existing = await db.TreatmentPlans.AsNoTracking().Select(p => new { p.PatientId, p.ExternalId, p.Title, p.PlanNumber }).ToListAsync(ct);
        var existingByReference = existing.Where(p => p.ExternalId != null).Select(p => (p.PatientId, p.ExternalId!.ToUpperInvariant())).ToHashSet();
        var today = clinicTime.Today;
        var order = input.Options.DateFormat;

        // Pass 1: read each row on its own.
        var parsed = new List<(SourceRow Row, string Display, List<string> Errors, List<string> Warnings, Guid? PatientId, string? PatientLabel, Guid? DentistId,
            string Title, string Reference, TreatmentPlanStatus? Status, DateTime? PlanUtc, Item? Item)>();
        foreach (var row in input.Rows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var title = row[F.PlanTitle];
            var reference = row[F.PlanReference];
            var name = row[F.ItemName];
            var display = string.Join(" · ", new[] { row[F.PatientName], row[F.PatientFileNumber], title.Length > 0 ? title : reference, name }.Where(v => v.Length > 0));
            if (display.Length == 0) display = $"Row {row.Number}";

            if (ImportRules.IsTemplateExample(row[F.PatientName], row[F.PatientFileNumber]))
            {
                parsed.Add((row, display, [ImportRules.TemplateExampleMessage], warnings, null, null, null, "", "", null, null, null));
                continue;
            }

            var (patientId, patientLabel, patientError) = patients.Resolve(row, input.SourceSystem);
            if (patientError is not null) errors.Add(patientError);
            if (title.Length == 0 && reference.Length == 0) errors.Add("The plan has no title and no reference, so its rows cannot be grouped.");
            if (name.Length == 0) errors.Add("The treatment name is missing.");
            else if (name.Length > 200) errors.Add("The treatment name is longer than 200 characters.");

            decimal? price = row.Has(F.Price) ? ImportParsing.Amount(row[F.Price]) : null;
            if (!row.Has(F.Price)) errors.Add("The price is missing. Use 0 for a procedure that is not charged.");
            else if (price is null) errors.Add($"The price '{row[F.Price]}' could not be read.");
            else if (price < 0 || price > 1_000_000) errors.Add("The price must be between 0 and 1,000,000.");

            var quantity = 1;
            if (row.Has(F.Quantity))
            {
                if (ImportParsing.Integer(row[F.Quantity]) is { } q and >= 1 and <= 100) quantity = q;
                else errors.Add($"The quantity '{row[F.Quantity]}' must be a whole number from 1 to 100.");
            }

            int? tooth = null;
            if (row.Has(F.Tooth))
            {
                if (ImportParsing.Integer(row[F.Tooth]) is { } number && Teeth.IsValid(number)) tooth = number;
                else warnings.Add($"Tooth '{row[F.Tooth]}' is not an FDI tooth number (11–48, 51–85) and was left empty.");
            }

            TreatmentPlanStatus? status = null;
            if (row.Has(F.Status) && (status = ImportParsing.PlanStatusOf(row[F.Status])) is null)
                errors.Add($"Plan status '{row[F.Status]}' was not recognised. Use Draft, Presented, Accepted, In progress, Completed or Rejected.");

            TreatmentItemStatus? itemStatus = null;
            if (row.Has(F.ItemStatus) && (itemStatus = ImportParsing.ItemStatusOf(row[F.ItemStatus])) is null)
                errors.Add($"Treatment status '{row[F.ItemStatus]}' was not recognised. Use Planned, Completed or Cancelled.");

            DateTime? planUtc = null;
            if (row.Has(F.PlanDate))
            {
                if (ImportParsing.Date(row[F.PlanDate], order) is not { } d) errors.Add($"The plan date '{row[F.PlanDate]}' could not be read.");
                else if (d > today) errors.Add("The plan date is in the future.");
                else planUtc = clinicTime.ToUtc(d.ToDateTime(new TimeOnly(12, 0)));
            }
            DateTime? completedUtc = null;
            if (row.Has(F.CompletedDate))
            {
                if (ImportParsing.Date(row[F.CompletedDate], order) is not { } d) errors.Add($"The completion date '{row[F.CompletedDate]}' could not be read.");
                else if (d > today) errors.Add("The completion date is in the future.");
                else completedUtc = clinicTime.ToUtc(d.ToDateTime(new TimeOnly(12, 0)));
            }

            Guid? dentistId = null;
            var dentistName = row[F.Dentist];
            var matches = dentistName.Length == 0 ? [] : dentists.Where(d => ImportRules.SameDentist(d.FullName, dentistName) || ImportRules.SameDentist(d.FullNameAr, dentistName)).ToList();
            if (matches.Count == 1) dentistId = matches[0].Id;
            else if (defaultDentist is not null)
            {
                dentistId = defaultDentist.Id;
                if (dentistName.Length > 0) warnings.Add($"Dentist '{dentistName}' was not found; {defaultDentist.FullName} is used.");
            }
            else errors.Add(dentistName.Length == 0
                ? "No dentist is given. Map a dentist column or choose a default dentist."
                : matches.Count > 1 ? $"More than one dentist matches '{dentistName}'." : $"Dentist '{dentistName}' was not found. Add the dentist under Staff, or choose a default dentist.");

            // A completion date means the source recorded the procedure as done.
            var resolvedItemStatus = itemStatus ?? (completedUtc is not null ? TreatmentItemStatus.Completed : (TreatmentItemStatus?)null);
            if (resolvedItemStatus != TreatmentItemStatus.Completed && completedUtc is not null)
                errors.Add("A completion date is given for a treatment that is not completed.");

            var item = errors.Count == 0 && price is not null
                ? new Item(row.Number, name, tooth, quantity, Money.Round(price.Value), resolvedItemStatus ?? TreatmentItemStatus.Planned, completedUtc)
                : null;
            // Whether the item status was stated or is still to be decided from the plan status.
            if (item is not null && resolvedItemStatus is null) item = item with { Status = (TreatmentItemStatus)0 };
            parsed.Add((row, display, errors, warnings, patientId, patientLabel, dentistId, title, reference, status, planUtc, item));
        }

        // Pass 2: group rows into plans and judge each plan as a whole.
        var results = new Dictionary<int, RowEvaluation>();
        foreach (var row in parsed.Where(p => p.PatientId is null || (p.Title.Length == 0 && p.Reference.Length == 0)))
            results[row.Row.Number] = ImportRules.Error(row.Row.Number, row.Display, row.Errors.Count > 0 ? row.Errors : ["The row is incomplete."], row.Warnings);

        var groups = parsed.Where(p => !results.ContainsKey(p.Row.Number))
            .GroupBy(p => (p.PatientId, Key: p.Reference.Length > 0 ? "R:" + p.Reference.ToUpperInvariant() : "T:" + ImportParsing.Key(p.Title)));
        foreach (var group in groups)
        {
            var rows = group.OrderBy(r => r.Row.Number).ToList();
            var first = rows[0];
            var planErrors = new List<string>();
            var statuses = rows.Where(r => r.Status is not null).Select(r => r.Status!.Value).Distinct().ToList();
            if (statuses.Count > 1) planErrors.Add($"The rows of this plan give different plan statuses ({string.Join(", ", statuses)}).");
            if (rows.Select(r => r.DentistId).Where(d => d is not null).Distinct().Count() > 1) planErrors.Add("The rows of this plan name different dentists.");

            if (rows.Any(r => r.Errors.Count > 0) || planErrors.Count > 0)
            {
                foreach (var r in rows)
                    results[r.Row.Number] = ImportRules.Error(r.Row.Number, r.Display,
                        r.Errors.Count > 0 || planErrors.Count > 0 ? [.. r.Errors, .. planErrors] : ["Another row of this plan has an error, so the whole plan is held back."], r.Warnings);
                continue;
            }

            var title = rows.Select(r => r.Title).FirstOrDefault(t => t.Length > 0) ?? $"Imported plan {first.Reference}";
            var reference = first.Reference.Length > 0 ? first.Reference : null;
            var warnings = new List<string>();
            var status = statuses.Count == 1 ? statuses[0] : TreatmentPlanStatus.Draft;
            if (statuses.Count == 0) warnings.Add("The file gives no plan status, so the plan is imported as a draft.");

            // Items with no status of their own follow the plan: completed in a completed plan, otherwise still planned.
            var items = rows.Select(r => r.Item!).Select(i => (int)i.Status == 0
                ? i with { Status = status == TreatmentPlanStatus.Completed ? TreatmentItemStatus.Completed : TreatmentItemStatus.Planned }
                : i).ToList();
            var completed = items.Count(i => i.Status == TreatmentItemStatus.Completed);
            var open = items.Count(i => i.Status == TreatmentItemStatus.Planned);
            if (status == TreatmentPlanStatus.Completed && open > 0)
                planErrors.Add("The plan is marked completed but some of its treatments are still planned.");
            if (status is TreatmentPlanStatus.Draft or TreatmentPlanStatus.Presented or TreatmentPlanStatus.Rejected && completed > 0)
                planErrors.Add($"The plan is marked {status} but some of its treatments are completed. Use In progress or Completed.");
            if (planErrors.Count > 0)
            {
                foreach (var r in rows) results[r.Row.Number] = ImportRules.Error(r.Row.Number, r.Display, planErrors, r.Warnings);
                continue;
            }
            if (items.Any(i => i.Status == TreatmentItemStatus.Completed && i.CompletedUtc is null))
                warnings.Add("Completed treatments without a completion date are imported as completed with no date.");

            var already = reference is not null
                ? existingByReference.Contains((first.PatientId!.Value, reference.ToUpperInvariant()))
                : existing.Any(p => p.PatientId == first.PatientId && ImportParsing.SameName(p.Title, title));
            var plan = new Plan(first.PatientId!.Value, rows.Select(r => r.DentistId).First(d => d is not null)!.Value, title.Length > 200 ? title[..200] : title,
                reference is { Length: <= 100 } ? reference : null, status, rows.Select(r => r.PlanUtc).FirstOrDefault(d => d is not null), items);

            foreach (var r in rows)
            {
                var rowWarnings = r == first ? [.. r.Warnings, .. warnings] : r.Warnings;
                if (already)
                {
                    var reason = reference is not null ? "This plan was imported before." : "This patient already has a plan with the same title.";
                    var allowed = reference is not null ? new[] { RowActions.Skip } : [RowActions.Skip, RowActions.Create];
                    // One decision per plan: the choice on its first row applies to every row.
                    var action = ImportRules.Decide(input, first.Row.Number, RowActions.Skip, allowed);
                    results[r.Row.Number] = new RowEvaluation(r.Row.Number, reference is not null ? RowVerdict.AlreadyImported : RowVerdict.Duplicate, action,
                        r == first ? allowed : [action], [], [.. rowWarnings, reason], r.Display, first.PatientId, first.PatientLabel)
                        { Model = r == first && action == RowActions.Create ? plan : null, Amount = r.Item!.Status == TreatmentItemStatus.Cancelled ? 0 : r.Item.UnitPrice * r.Item.Quantity };
                }
                else
                {
                    results[r.Row.Number] = new RowEvaluation(r.Row.Number, RowVerdict.Ready, RowActions.Create, [RowActions.Create], [], rowWarnings, r.Display,
                        first.PatientId, first.PatientLabel) { Model = r == first ? plan : null, Amount = r.Item!.Status == TreatmentItemStatus.Cancelled ? 0 : r.Item.UnitPrice * r.Item.Quantity };
                }
            }
        }
        return input.Rows.Select(r => results[r.Number]).ToList();
    }

    public async Task<ApplyResult> ApplyAsync(ImportBatch batch, IReadOnlyList<RowEvaluation> rows, ImportInput input, CancellationToken ct)
    {
        var outcome = new Dictionary<int, (ImportRowStatus, string, Guid)>();
        decimal total = 0;
        var now = clock.UtcNow;
        foreach (var row in rows.Where(r => r.Action == RowActions.Create))
        {
            if (row.Model is not Plan p) continue;
            var at = p.PlanUtc ?? now;
            var presented = p.Status != TreatmentPlanStatus.Draft;
            var accepted = p.Status is TreatmentPlanStatus.Accepted or TreatmentPlanStatus.InProgress or TreatmentPlanStatus.PartiallyCompleted or TreatmentPlanStatus.Completed;
            var lastCompleted = p.Items.Where(i => i.CompletedUtc is not null).Max(i => i.CompletedUtc);
            var plan = new TreatmentPlan
            {
                PlanNumber = await sequences.NextAsync(SequenceKind.TreatmentPlan, ct),
                PatientId = p.PatientId, DentistId = p.DentistId, Title = p.Title, Status = p.Status, CreatedAt = at,
                Notes = $"Imported from {input.SourceSystem}.{(p.PlanUtc is null ? " The source gave no plan date; the import date is used." : "")}",
                PresentedAt = presented ? at : null,
                AcceptedAt = accepted ? at : null,
                StartedAt = p.Items.Any(i => i.Status == TreatmentItemStatus.Completed) ? p.Items.Where(i => i.CompletedUtc is not null).Min(i => i.CompletedUtc) ?? at : null,
                CompletedAt = p.Status == TreatmentPlanStatus.Completed ? lastCompleted ?? at : null,
                RejectedAt = p.Status == TreatmentPlanStatus.Rejected ? at : null,
                LastActivityAt = lastCompleted ?? at,
                ExternalId = p.Reference, ImportBatchId = batch.Id
            };
            var sequence = 1;
            foreach (var i in p.Items)
            {
                plan.Items.Add(new TreatmentPlanItem
                {
                    Name = i.Name, ToothNumber = i.Tooth, Quantity = i.Quantity, UnitPrice = i.UnitPrice, Total = Money.Round(i.UnitPrice * i.Quantity),
                    Status = i.Status, Sequence = sequence++, CompletedAt = i.Status == TreatmentItemStatus.Completed ? i.CompletedUtc : null,
                    // Work done before the switch was billed in the previous system; its unpaid part arrives as an opening balance.
                    IsInvoiced = i.Status == TreatmentItemStatus.Completed
                });
            }
            plan.TotalAmount = Money.Round(plan.Items.Where(i => i.Status != TreatmentItemStatus.Cancelled).Sum(i => i.Total));
            db.TreatmentPlans.Add(plan);
            total += plan.TotalAmount;
            foreach (var i in p.Items) outcome[i.Row] = (ImportRowStatus.Created, nameof(TreatmentPlan), plan.Id);
        }
        return new ApplyResult(outcome, total);
    }
}
