using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tahona.Application.Common;
using Tahona.Application.Features.Appointments;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

/// <summary>Booking rules, and the lock that keeps two simultaneous bookings from both succeeding.</summary>
public class AppointmentTests
{
    private static AppointmentUpsertRequest Slot(Guid patientId, Guid dentistId, DateTime start, int minutes = 30, bool allowOverlap = false) =>
        new(patientId, dentistId, new DateTimeOffset(start, TimeSpan.Zero), minutes, "Check-up", null, null, null, null, allowOverlap);

    [Fact]
    public async Task Overlapping_bookings_are_refused_unless_staff_explicitly_allow_them()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var sara = await app.AddPatientAsync("Sara");
        var omar = await app.AddPatientAsync("Omar", "0795550000");
        var appointments = app.Get<AppointmentService>();
        var start = app.Clock.UtcNow.AddDays(1);

        await appointments.CreateAsync(Slot(sara.Id, clinic.DentistId, start), CancellationToken.None);

        // Same dentist, overlapping time.
        var clash = await Assert.ThrowsAsync<ConflictException>(() =>
            appointments.CreateAsync(Slot(omar.Id, clinic.DentistId, start.AddMinutes(15)), CancellationToken.None));
        Assert.Contains("already has an appointment", clash.Message);

        // Back to back is fine; a deliberate double-booking needs the explicit flag.
        await appointments.CreateAsync(Slot(omar.Id, clinic.DentistId, start.AddMinutes(30)), CancellationToken.None);
        await appointments.CreateAsync(Slot(omar.Id, clinic.DentistId, start.AddMinutes(90), allowOverlap: true), CancellationToken.None);
        Assert.Equal(3, await app.Db.Appointments.CountAsync());
    }

    [Fact]
    public async Task Status_changes_follow_the_visit_and_cannot_be_recorded_early()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var sara = await app.AddPatientAsync("Sara");
        var appointments = app.Get<AppointmentService>();
        var tomorrow = await appointments.CreateAsync(Slot(sara.Id, clinic.DentistId, app.Clock.UtcNow.AddDays(1)), CancellationToken.None);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            appointments.ChangeStatusAsync(tomorrow.Id, new AppointmentStatusRequest(AppointmentStatus.NoShow, null), CancellationToken.None));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            appointments.ChangeStatusAsync(tomorrow.Id, new AppointmentStatusRequest(AppointmentStatus.Completed, null), CancellationToken.None));

        app.Clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(40)));
        var done = await appointments.ChangeStatusAsync(tomorrow.Id, new AppointmentStatusRequest(AppointmentStatus.Completed, null), CancellationToken.None);
        Assert.Equal(AppointmentStatus.Completed, done.Status);
        Assert.NotNull((await app.Db.Patients.SingleAsync()).LastVisitAt);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            appointments.ChangeStatusAsync(tomorrow.Id, new AppointmentStatusRequest(AppointmentStatus.Cancelled, "changed mind"), CancellationToken.None));
    }

    [Fact]
    public async Task Moving_an_appointment_resets_its_reminder()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var sara = await app.AddPatientAsync("Sara");
        var appointments = app.Get<AppointmentService>();
        var created = await appointments.CreateAsync(Slot(sara.Id, clinic.DentistId, app.Clock.UtcNow.AddDays(1)), CancellationToken.None);
        var entity = await app.Db.Appointments.SingleAsync();
        entity.ReminderSentAt = app.Clock.UtcNow;
        entity.ReminderAttempts = 1;
        await app.Db.SaveChangesAsync();

        var newStart = new DateTimeOffset(app.Clock.UtcNow.AddDays(3), TimeSpan.Zero);
        await appointments.RescheduleAsync(created.Id, new RescheduleRequest(newStart, newStart.AddMinutes(30), null), CancellationToken.None);

        Assert.Null(entity.ReminderSentAt);
        Assert.Equal(0, entity.ReminderAttempts);
    }

    /// <summary>
    /// Twelve receptionists book the same dentist and slot at the same instant, each on its own connection.
    /// Exactly one booking may exist afterwards. Without the booking lock several pass the overlap check together.
    /// </summary>
    [PostgresFact]
    public async Task Simultaneous_bookings_for_one_slot_create_exactly_one_appointment()
    {
        using var app = new TestApp(postgres: true);
        var clinic = await app.AddClinicAsync();
        var patients = new List<Guid>();
        for (var i = 0; i < 12; i++) patients.Add((await app.AddPatientAsync($"Patient {i}", $"07955500{i:D2}")).Id);
        var start = app.Clock.UtcNow.AddDays(2);

        using var gate = new ManualResetEventSlim(false);
        var attempts = patients.Select(patientId => Task.Run(async () =>
        {
            using var scope = app.NewScope();
            var service = scope.ServiceProvider.GetRequiredService<AppointmentService>();
            gate.Wait();
            try
            {
                await service.CreateAsync(Slot(patientId, clinic.DentistId, start), CancellationToken.None);
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        })).ToList();
        gate.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(booked => booked));
        using var check = app.NewScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<Tahona.Infrastructure.Persistence.AppDbContext>().Appointments.CountAsync());
    }

    /// <summary>The same guarantee when one patient is double-booked with two different dentists.</summary>
    [PostgresFact]
    public async Task One_patient_cannot_be_booked_twice_at_the_same_time_by_two_requests()
    {
        using var app = new TestApp(postgres: true);
        var clinic = await app.AddClinicAsync();
        var second = new Tahona.Domain.Entities.Dentist { FullName = "Dr Second" };
        app.Db.Dentists.Add(second);
        await app.Db.SaveChangesAsync();
        var patient = await app.AddPatientAsync("Sara");
        var start = app.Clock.UtcNow.AddDays(2);

        var results = await Task.WhenAll(new[] { clinic.DentistId, second.Id }.Select(dentistId => Task.Run(async () =>
        {
            using var scope = app.NewScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<AppointmentService>().CreateAsync(Slot(patient.Id, dentistId, start), CancellationToken.None);
                return true;
            }
            catch (ConflictException)
            {
                return false;
            }
        })));

        Assert.Equal(1, results.Count(booked => booked));
    }
}
