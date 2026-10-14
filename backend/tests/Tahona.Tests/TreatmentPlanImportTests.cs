using System.Text;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Features.Import;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

/// <summary>Treatment plans from a spreadsheet: grouping rows into plans, statuses as stated, nothing clinical invented.</summary>
public class TreatmentPlanImportTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static Stream Csv(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static async Task<(TestApp App, TestClinic Clinic, Patient Ahmad, Patient Sara)> SetUpAsync()
    {
        var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var ahmad = await app.AddPatientAsync("Ahmad Khalil", "0791234567");
        ahmad.FileNumber = "1001";
        var sara = await app.AddPatientAsync("Sara Haddad", "0795550002");
        sara.FileNumber = "1002";
        await app.Db.SaveChangesAsync();
        return (app, clinic, ahmad, sara);
    }

    private const string PlansCsv = """
        File No,Plan No,Plan,Treatment,Tooth,Price,Qty,Plan status,Treatment status,Plan date,Completed on,Dentist
        1001,TP-204,Root canal and crown,Root canal (molar),16,180,1,In progress,Completed,02/09/2026,10/09/2026,Dr Test
        1001,TP-204,Root canal and crown,Post & core,16,60,1,In progress,,02/09/2026,,Dr Test
        1001,TP-204,Root canal and crown,Zirconia crown,16,250,1,In progress,Planned,02/09/2026,,Dr Test
        1002,TP-300,Whitening,In-office whitening,,200,1,Presented,,20/09/2026,,Dr Test
        1002,TP-301,Fillings,Composite filling,99,40,2,,,,,Dr Test
        1002,TP-302,Old plan,Extraction,48,30,1,Completed,,01/03/2026,02/03/2026,Dr Test
        1002,TP-303,Mixed up,Crown,26,250,1,Presented,Completed,05/09/2026,06/09/2026,Dr Test
        1002,TP-304,Broken,Veneer,11,abc,1,Accepted,,05/09/2026,,Dr Test
        1002,TP-304,Broken,Veneer,21,280,1,Accepted,,05/09/2026,,Dr Test
        9999,TP-305,Nobody,Filling,36,40,1,Accepted,,05/09/2026,,Dr Test
        """;

    [Fact]
    public async Task Rows_become_plans_with_the_statuses_the_source_states_and_nothing_else()
    {
        var (app, clinic, ahmad, sara) = await SetUpAsync();
        using var _ = app;
        var imports = app.Get<ImportService>();
        var upload = await imports.UploadAsync(ImportKind.TreatmentPlans, "plans.csv", Csv(PlansCsv), "Old System", None);
        var request = new ImportRunRequest(upload.SuggestedMapping, null, null);

        Assert.Equal("Plan", upload.SuggestedMapping[ImportDefinitions.F.PlanTitle]);
        Assert.Equal("Plan No", upload.SuggestedMapping[ImportDefinitions.F.PlanReference]);
        Assert.Equal("Treatment", upload.SuggestedMapping[ImportDefinitions.F.ItemName]);
        Assert.Equal("Plan status", upload.SuggestedMapping[ImportDefinitions.F.Status]);
        Assert.Equal("Treatment status", upload.SuggestedMapping[ImportDefinitions.F.ItemStatus]);

        var preview = await imports.PreviewAsync(upload.Batch.Id, request, None);
        Assert.Empty(preview.MappingProblems);
        Assert.Equal(6, preview.Summary.WillCreate);                               // rows of four plans
        Assert.Equal(4, preview.Summary.Errors);
        string Error(int row) => string.Join(" ", preview.Rows.Single(r => r.RowNumber == row).Errors);
        Assert.Contains("marked Presented but some of its treatments are completed", Error(7));
        Assert.Contains("could not be read", Error(8));
        Assert.Contains("whole plan is held back", Error(9));                      // a sound row in a broken plan
        Assert.Contains("No patient found for 9999", Error(10));
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 5).Warnings, w => w.Contains("no plan status") && w.Contains("draft"));
        Assert.Contains(preview.Rows.Single(r => r.RowNumber == 5).Warnings, w => w.Contains("Tooth '99'"));
        Assert.Empty(await app.Db.TreatmentPlans.ToListAsync());                   // the check wrote nothing

        var committed = await imports.CommitAsync(upload.Batch.Id, request, None);
        Assert.Equal(6, committed.Batch.CreatedCount);
        Assert.Equal(800m, committed.Batch.TotalAmount);                           // 490 + 200 + 80 + 30

        var plans = await app.Db.TreatmentPlans.Include(p => p.Items).ToListAsync();
        Assert.Equal(4, plans.Count);

        var rootCanal = plans.Single(p => p.ExternalId == "TP-204");
        Assert.Equal(ahmad.Id, rootCanal.PatientId);
        Assert.Equal(clinic.DentistId, rootCanal.DentistId);
        Assert.Equal(TreatmentPlanStatus.InProgress, rootCanal.Status);
        Assert.Equal(490m, rootCanal.TotalAmount);
        Assert.Equal(upload.Batch.Id, rootCanal.ImportBatchId);
        Assert.Equal(new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc), rootCanal.PresentedAt);
        Assert.Equal([TreatmentItemStatus.Completed, TreatmentItemStatus.Planned, TreatmentItemStatus.Planned], rootCanal.Items.OrderBy(i => i.Sequence).Select(i => i.Status));
        var done = rootCanal.Items.Single(i => i.Status == TreatmentItemStatus.Completed);
        Assert.Equal(16, done.ToothNumber);
        Assert.Equal(new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc), done.CompletedAt);
        Assert.True(done.IsInvoiced);                                              // billed in the previous system
        Assert.All(rootCanal.Items.Where(i => i.Status == TreatmentItemStatus.Planned), i => Assert.False(i.IsInvoiced));

        var whitening = plans.Single(p => p.ExternalId == "TP-300");
        Assert.Equal(TreatmentPlanStatus.Presented, whitening.Status);
        Assert.Null(whitening.AcceptedAt);                                         // presented is not accepted

        var fillings = plans.Single(p => p.ExternalId == "TP-301");
        Assert.Equal(TreatmentPlanStatus.Draft, fillings.Status);                  // no status in the source
        Assert.Null(fillings.PresentedAt);
        Assert.Equal(80m, fillings.TotalAmount);
        Assert.Null(fillings.Items.Single().ToothNumber);

        var old = plans.Single(p => p.ExternalId == "TP-302");
        Assert.Equal(TreatmentPlanStatus.Completed, old.Status);
        Assert.Equal(TreatmentItemStatus.Completed, old.Items.Single().Status);
        Assert.NotNull(old.CompletedAt);

        // No clinical history, invoice, payment or appointment was created from the plans.
        Assert.Empty(await app.Db.TreatmentRecords.ToListAsync());
        Assert.Empty(await app.Db.Invoices.ToListAsync());
        Assert.Empty(await app.Db.Payments.ToListAsync());
        Assert.Empty(await app.Db.Appointments.ToListAsync());
        Assert.Null((await app.Db.Patients.SingleAsync(p => p.Id == sara.Id)).LastVisitAt);

        // The same file again adds nothing.
        var again = await imports.UploadAsync(ImportKind.TreatmentPlans, "plans.csv", Csv(PlansCsv), "Old System", None);
        var second = await imports.PreviewAsync(again.Batch.Id, new ImportRunRequest(again.SuggestedMapping, null, null), None);
        Assert.Equal(0, second.Summary.WillCreate);
        Assert.Equal(6, second.Summary.AlreadyImported);
    }

    [Fact]
    public async Task Plans_group_by_title_without_a_reference_and_undo_keeps_plans_already_in_use()
    {
        var (app, clinic, ahmad, sara) = await SetUpAsync();
        using var _ = app;
        var imports = app.Get<ImportService>();
        const string csv = """
            Phone,Patient,Plan,Treatment,Price,Plan status
            0791234567,Ahmad Khalil,Crown work,Crown 16,250,Accepted
            0791234567,Ahmad Khalil,crown  WORK,Crown 26,250,Accepted
            0795550002,Sara Haddad,Crown work,Crown 36,250,Accepted
            """;
        var upload = await imports.UploadAsync(ImportKind.TreatmentPlans, "plans.csv", Csv(csv), null, None);
        var options = new ImportOptions { DefaultDentistId = clinic.DentistId };
        var request = new ImportRunRequest(upload.SuggestedMapping, options, null);
        await imports.CommitAsync(upload.Batch.Id, request, None);

        var plans = await app.Db.TreatmentPlans.Include(p => p.Items).ToListAsync();
        Assert.Equal(2, plans.Count);                                              // one per patient; the title's case and spacing do not split a plan
        Assert.Equal(2, plans.Single(p => p.PatientId == ahmad.Id).Items.Count);
        Assert.All(plans, p => Assert.NotNull(p.AcceptedAt));
        Assert.All(plans, p => Assert.Contains("no plan date", p.Notes));

        // Same title for the same patient again: flagged, and imported only by decision.
        var again = await imports.UploadAsync(ImportKind.TreatmentPlans, "plans2.csv", Csv(csv), null, None);
        var preview = await imports.PreviewAsync(again.Batch.Id, request, None);
        Assert.Equal(0, preview.Summary.WillCreate);
        Assert.All(preview.Rows, r => Assert.Equal(RowVerdict.Duplicate, r.Verdict));

        // Sara's plan gets an appointment; undo removes Ahmad's and keeps hers.
        var sarasPlan = plans.Single(p => p.PatientId == sara.Id);
        app.Db.Appointments.Add(new Appointment { PatientId = sara.Id, DentistId = clinic.DentistId, StartTime = app.Clock.UtcNow.AddDays(2),
            EndTime = app.Clock.UtcNow.AddDays(2).AddMinutes(60), Treatment = "Crown 36", TreatmentPlanId = sarasPlan.Id });
        await app.Db.SaveChangesAsync();

        var undone = await imports.UndoAsync(upload.Batch.Id, None);
        Assert.Equal(1, undone.Removed);
        Assert.Equal(1, undone.Kept);
        Assert.Equal(sara.Id, (await app.Db.TreatmentPlans.SingleAsync()).PatientId);
        Assert.Single(await app.Db.TreatmentPlanItems.ToListAsync());
    }

    [Fact]
    public async Task An_imported_open_plan_shows_up_for_follow_up_with_its_true_age()
    {
        var (app, _, ahmad, _) = await SetUpAsync();
        using var __ = app;
        app.Db.FollowUpRules.Add(new FollowUpRule { Name = "Not started", Trigger = FollowUpTrigger.TreatmentPlanNotStarted, DelayDays = 3 });
        await app.Db.SaveChangesAsync();
        var imports = app.Get<ImportService>();
        const string csv = "File No,Plan,Treatment,Price,Plan status,Plan date,Dentist\n1001,Implant,Implant fixture,650,Accepted,01/09/2026,Dr Test\n";
        var upload = await imports.UploadAsync(ImportKind.TreatmentPlans, "plans.csv", Csv(csv), "Old System", None);
        await imports.CommitAsync(upload.Batch.Id, new ImportRunRequest(upload.SuggestedMapping, null, null), None);

        var onList = await app.Get<Tahona.Application.Features.Recovery.RecoveryService>().GetPatientAsync(ahmad.Id, None);

        Assert.NotNull(onList);
        var issue = Assert.Single(onList.Issues);
        Assert.Equal(RecoveryIssueType.TreatmentNotStarted, issue.IssueType);
        Assert.Equal(650, issue.Value);
        Assert.Equal(new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), issue.TriggeredAt);   // the date from the previous system, not the import date
    }
}
