using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using static Tahona.Application.Features.Import.ImportDefinitions;

namespace Tahona.Application.Features.Import;

/// <summary>
/// Imports existing appointments, past and future. Nothing is invented: a past appointment whose source gives no
/// status is rejected unless the person explicitly chooses to record such rows as completed.
/// </summary>
public class AppointmentImportHandler(IAppDbContext db, IClock clock, IClinicTime clinicTime) : IImportHandler
{
    public ImportKind Kind => ImportKind.Appointments;

    private sealed record Parsed(Guid PatientId, Guid DentistId, DateTime StartUtc, DateTime EndUtc, string Treatment, AppointmentStatus Status,
        string? Notes, string? ExternalId);

    public async Task<IReadOnlyList<RowEvaluation>> EvaluateAsync(ImportInput input, CancellationToken ct)
    {
        var patients = await PatientResolver.LoadAsync(db, ct);
        var dentists = await db.Dentists.AsNoTracking().Select(d => new { d.Id, d.FullName, d.FullNameAr, d.IsActive }).ToListAsync(ct);
        var defaultDentist = input.Options.DefaultDentistId is { } id ? dentists.FirstOrDefault(d => d.Id == id) : null;
        var existing = (await db.Appointments.AsNoTracking().Select(a => new { a.PatientId, a.StartTime }).ToListAsync(ct))
            .Select(a => (a.PatientId, a.StartTime)).ToHashSet();
        var seen = new Dictionary<(Guid, DateTime), int>();
        var now = clock.UtcNow;
        var options = input.Options;
        var duration = Math.Clamp(options.DefaultDurationMinutes, 5, 480);
        var results = new List<RowEvaluation>();

        foreach (var row in input.Rows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var display = string.Join(" · ", new[] { row[F.PatientName], row[F.PatientFileNumber], row[F.Date], row[F.Time] }.Where(v => v.Length > 0));
            if (display.Length == 0) display = $"Row {row.Number}";

            if (ImportRules.IsTemplateExample(row[F.PatientName], row[F.PatientFileNumber]))
            {
                results.Add(ImportRules.Error(row.Number, display, [ImportRules.TemplateExampleMessage]));
                continue;
            }

            var (patientId, patientLabel, patientError) = patients.Resolve(row, input.SourceSystem);
            if (patientError is not null) errors.Add(patientError);

            // The date cell may carry the time; a separate time column wins when both are present.
            DateTime? local = null;
            if (!row.Has(F.Date)) errors.Add("The date is missing.");
            else if (ImportParsing.DateTimeOf(row[F.Date], options.DateFormat) is not { } parsedDate) errors.Add($"The date '{row[F.Date]}' could not be read.");
            else if (row.Has(F.Time))
            {
                if (ImportParsing.Time(row[F.Time]) is { } time) local = DateOnly.FromDateTime(parsedDate).ToDateTime(time);
                else errors.Add($"The time '{row[F.Time]}' could not be read.");
            }
            else if (parsedDate.TimeOfDay == TimeSpan.Zero) errors.Add("The time is missing. Map a time column, or include the time in the date.");
            else local = parsedDate;

            var minutes = duration;
            if (row.Has(F.Duration))
            {
                if (ImportParsing.Integer(row[F.Duration]) is { } m and >= 5 and <= 480) minutes = m;
                else warnings.Add($"Duration '{row[F.Duration]}' is not between 5 and 480 minutes; {duration} minutes is used.");
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

            AppointmentStatus? status = null;
            if (row.Has(F.Status) && (status = ImportParsing.AppointmentStatusOf(row[F.Status])) is null)
                errors.Add($"Status '{row[F.Status]}' was not recognised. Use Scheduled, Confirmed, Completed, Cancelled or No-show.");

            if (errors.Count > 0 || local is null || patientId is null || dentistId is null)
            {
                results.Add(ImportRules.Error(row.Number, display, errors.Count > 0 ? errors : ["The row is incomplete."], warnings));
                continue;
            }

            var start = clinicTime.ToUtc(local.Value);
            var isPast = start <= now;
            if (status is null && !row.Has(F.Status))
            {
                if (!isPast) status = AppointmentStatus.Scheduled;
                else if (options.AssumePastAppointmentsCompleted) status = AppointmentStatus.Completed;
                else errors.Add("This appointment is in the past and the file gives no status. Add a status column, or choose to record past appointments without a status as completed.");
            }
            if (!isPast && status is AppointmentStatus.Completed or AppointmentStatus.NoShow)
                errors.Add($"An appointment in the future cannot be {(status == AppointmentStatus.Completed ? "completed" : "a no-show")}.");
            if (isPast && status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
                warnings.Add("This appointment is in the past but is still marked as booked; it is imported as it is.");

            if (errors.Count > 0)
            {
                results.Add(ImportRules.Error(row.Number, display, errors, warnings));
                continue;
            }

            var key = (patientId.Value, start);
            if (existing.Contains(key))
            {
                results.Add(new RowEvaluation(row.Number, RowVerdict.AlreadyImported, RowActions.Skip, [RowActions.Skip], [],
                    [.. warnings, "This patient already has an appointment at exactly this time."], display, patientId, patientLabel));
                continue;
            }
            if (seen.TryGetValue(key, out var firstRow))
            {
                results.Add(new RowEvaluation(row.Number, RowVerdict.Duplicate, RowActions.Skip, [RowActions.Skip], [],
                    [.. warnings, $"Same patient and time as row {firstRow} of this file."], display, patientId, $"Row {firstRow}"));
                continue;
            }
            seen[key] = row.Number;

            var treatment = row.Has(F.Treatment) ? row[F.Treatment] : "Imported appointment";
            var notes = row[F.Notes];
            results.Add(new RowEvaluation(row.Number, RowVerdict.Ready, ImportRules.Decide(input, row.Number, RowActions.Create, [RowActions.Create, RowActions.Skip]),
                [RowActions.Create, RowActions.Skip], [], warnings, display, patientId, patientLabel)
            {
                Model = new Parsed(patientId.Value, dentistId.Value, start, start.AddMinutes(minutes), treatment.Length > 150 ? treatment[..150] : treatment,
                    status!.Value, notes.Length == 0 ? null : notes.Length > 2000 ? notes[..2000] : notes,
                    row.Has(F.ExternalId) ? row[F.ExternalId] : null)
            });
        }
        return results;
    }

    public async Task<ApplyResult> ApplyAsync(ImportBatch batch, IReadOnlyList<RowEvaluation> rows, ImportInput input, CancellationToken ct)
    {
        var outcome = new Dictionary<int, (ImportRowStatus, string, Guid)>();
        var creating = rows.Where(r => r.Action == RowActions.Create && r.Model is Parsed).ToList();
        var patientIds = creating.Select(r => ((Parsed)r.Model!).PatientId).Distinct().ToList();
        var patients = await db.Patients.Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        foreach (var row in creating)
        {
            var p = (Parsed)row.Model!;
            var appointment = new Appointment
            {
                PatientId = p.PatientId, DentistId = p.DentistId, StartTime = p.StartUtc, EndTime = p.EndUtc, Treatment = p.Treatment, Status = p.Status,
                Notes = p.Notes, ExternalId = p.ExternalId is { Length: <= 100 } ? p.ExternalId : null, ImportBatchId = batch.Id,
                CompletedAt = p.Status == AppointmentStatus.Completed ? p.EndUtc : null,
                CancelledAt = p.Status == AppointmentStatus.Cancelled ? p.StartUtc : null,
                CancellationReason = p.Status == AppointmentStatus.Cancelled ? $"Imported as cancelled from {input.SourceSystem}" : null
            };
            db.Appointments.Add(appointment);
            outcome[row.RowNumber] = (ImportRowStatus.Created, nameof(Appointment), appointment.Id);

            // A completed visit in the source is a real visit: it moves the patient's last-visit date forward.
            if (p.Status == AppointmentStatus.Completed && patients.TryGetValue(p.PatientId, out var patient)
                && (patient.LastVisitAt is null || patient.LastVisitAt < p.StartUtc))
                patient.LastVisitAt = p.StartUtc;
        }
        return new ApplyResult(outcome, null);
    }
}
