using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Persistence;
using Tahona.Infrastructure.Services;

namespace Tahona.Tests;

/// <summary>Detector behaviour against an in-memory database, including tenant isolation.</summary>
public class RecoveryDetectorTests
{
    private sealed class FixedClock(DateTime now) : IClock { public DateTime UtcNow => now; }

    private sealed class TestUser(Guid clinicId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public Guid? ClinicId => clinicId;
        public string? FullName => "Test";
        public string? Role => "Admin";
        public IReadOnlyCollection<string> Permissions => [];
        public string? IpAddress => null;
        public bool HasPermission(string permission) => true;
    }

    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private static AppDbContext Create(string dbName, Guid clinicId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        var user = new TestUser(clinicId);
        return new AppDbContext(options, new TenantContext(user), user, new FixedClock(Now));
    }

    private static void SeedClinic(AppDbContext db, Guid clinicId)
    {
        db.Clinics.Add(new Clinic { Id = clinicId, Name = "Test", Slug = clinicId.ToString("N") });
        db.ClinicSettings.Add(new ClinicSettings { ClinicId = clinicId });
        db.FollowUpRules.AddRange(
            new FollowUpRule { ClinicId = clinicId, Name = "not started", Trigger = FollowUpTrigger.TreatmentPlanNotStarted, DelayDays = 3 },
            new FollowUpRule { ClinicId = clinicId, Name = "missed", Trigger = FollowUpTrigger.MissedAppointment, DelayDays = 1 },
            new FollowUpRule { ClinicId = clinicId, Name = "balance", Trigger = FollowUpTrigger.OverdueBalance, DelayDays = 0 });
    }

    private static (Patient, Dentist) AddPatient(AppDbContext db, Guid clinicId, string name)
    {
        var dentist = new Dentist { ClinicId = clinicId, FullName = "Dr X" };
        var patient = new Patient { ClinicId = clinicId, FileNumber = name, FullName = name, Phone = "0790000000", PhoneNormalized = "790000000", RegisteredAt = Now.AddDays(-100) };
        db.Dentists.Add(dentist);
        db.Patients.Add(patient);
        return (patient, dentist);
    }

    private static TreatmentPlan Plan(Guid clinicId, Patient p, Dentist d, DateTime presented, decimal value) => new()
    {
        ClinicId = clinicId, PlanNumber = "TP-1", PatientId = p.Id, DentistId = d.Id, Title = "RCT + crown",
        Status = TreatmentPlanStatus.Presented, PresentedAt = presented, CreatedAt = presented, TotalAmount = value,
        Items = [new TreatmentPlanItem { ClinicId = clinicId, Name = "RCT", Total = value, UnitPrice = value }]
    };

    [Fact]
    public async Task Unbooked_presented_plan_becomes_treatment_not_started_after_rule_delay()
    {
        var clinic = Guid.NewGuid();
        await using var db = Create(nameof(Unbooked_presented_plan_becomes_treatment_not_started_after_rule_delay), clinic);
        SeedClinic(db, clinic);
        var (p, d) = AddPatient(db, clinic, "Ahmad");
        db.TreatmentPlans.Add(Plan(clinic, p, d, Now.AddDays(-10), 450));
        var (p2, d2) = AddPatient(db, clinic, "Fresh");
        db.TreatmentPlans.Add(Plan(clinic, p2, d2, Now.AddDays(-1), 300)); // within the 3-day grace period
        await db.SaveChangesAsync();

        var result = await new RecoveryDetector(db, new FixedClock(Now)).DetectAsync(CancellationToken.None);

        var opp = Assert.Single(result.Opportunities);
        Assert.Equal(RecoveryIssueType.TreatmentNotStarted, opp.IssueType);
        Assert.Equal(450m, opp.Value);
        Assert.Equal(p.Id, opp.PatientId);
    }

    [Fact]
    public async Task Upcoming_appointment_suppresses_the_opportunity()
    {
        var clinic = Guid.NewGuid();
        await using var db = Create(nameof(Upcoming_appointment_suppresses_the_opportunity), clinic);
        SeedClinic(db, clinic);
        var (p, d) = AddPatient(db, clinic, "Booked");
        db.TreatmentPlans.Add(Plan(clinic, p, d, Now.AddDays(-10), 450));
        db.Appointments.Add(new Appointment { ClinicId = clinic, PatientId = p.Id, DentistId = d.Id, Treatment = "RCT",
            StartTime = Now.AddDays(2), EndTime = Now.AddDays(2).AddHours(1) });
        await db.SaveChangesAsync();

        var result = await new RecoveryDetector(db, new FixedClock(Now)).DetectAsync(CancellationToken.None);
        Assert.Empty(result.Opportunities);
    }

    [Fact]
    public async Task Missed_appointment_and_overdue_balance_are_detected()
    {
        var clinic = Guid.NewGuid();
        await using var db = Create(nameof(Missed_appointment_and_overdue_balance_are_detected), clinic);
        SeedClinic(db, clinic);
        var (p, d) = AddPatient(db, clinic, "Missed");
        db.Appointments.Add(new Appointment { ClinicId = clinic, PatientId = p.Id, DentistId = d.Id, Treatment = "Filling",
            StartTime = Now.AddDays(-3), EndTime = Now.AddDays(-3).AddHours(1), Status = AppointmentStatus.NoShow });
        db.Invoices.Add(new Invoice { ClinicId = clinic, InvoiceNumber = "INV-1", PatientId = p.Id, IssueDate = Now.AddDays(-30),
            DueDate = Now.AddDays(-16), Total = 200, Balance = 120, AmountPaid = 80, Status = InvoiceStatus.PartiallyPaid });
        await db.SaveChangesAsync();

        var result = await new RecoveryDetector(db, new FixedClock(Now)).DetectAsync(CancellationToken.None);

        Assert.Contains(result.Opportunities, o => o.IssueType == RecoveryIssueType.MissedAppointment);
        Assert.Contains(result.Opportunities, o => o.IssueType == RecoveryIssueType.OutstandingBalance && o.Value == 120m);
    }

    [Fact]
    public async Task Other_clinics_data_is_never_visible()
    {
        var dbName = nameof(Other_clinics_data_is_never_visible);
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        await using (var dbA = Create(dbName, clinicA))
        {
            SeedClinic(dbA, clinicA);
            var (p, d) = AddPatient(dbA, clinicA, "Clinic A patient");
            dbA.TreatmentPlans.Add(Plan(clinicA, p, d, Now.AddDays(-10), 900));
            await dbA.SaveChangesAsync();
        }
        await using (var dbB = Create(dbName, clinicB))
        {
            SeedClinic(dbB, clinicB);
            await dbB.SaveChangesAsync();

            Assert.Empty(await dbB.Patients.ToListAsync());
            var result = await new RecoveryDetector(dbB, new FixedClock(Now)).DetectAsync(CancellationToken.None);
            Assert.Empty(result.Opportunities);
        }
    }
}
