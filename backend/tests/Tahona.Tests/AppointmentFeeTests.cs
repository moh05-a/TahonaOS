using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Appointments;
using Tahona.Application.Features.Billing;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

/// <summary>The price agreed when booking, and taking it as a payment once.</summary>
public class AppointmentFeeTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static AppointmentUpsertRequest Booking(Guid patientId, Guid dentistId, DateTime start, decimal? fee) =>
        new(patientId, dentistId, new DateTimeOffset(start, TimeSpan.Zero), 30, "Composite filling", null, null, null, null, false, fee);

    private static RecordPaymentRequest TakeFee(Guid patientId, AppointmentDto a, decimal amount) =>
        new(patientId, null, amount, PaymentMethod.Cash, null, null, null, new NewChargeRequest(a.Treatment, null, a.Fee!.Value, null, a.Id));

    [Fact]
    public async Task A_booking_carries_its_price_without_charging_anything()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var appointments = app.Get<AppointmentService>();

        var booked = await appointments.CreateAsync(Booking(patient.Id, clinic.DentistId, app.Clock.UtcNow.AddDays(1), 40.1234m), None);

        Assert.Equal(40.123m, booked.Fee);                       // money is kept to the fils
        Assert.Null(booked.FeeInvoiceId);
        Assert.Empty(await app.Db.Invoices.ToListAsync());       // booking creates no debt

        var changed = await appointments.UpdateAsync(booked.Id, Booking(patient.Id, clinic.DentistId, app.Clock.UtcNow.AddDays(1), 55), None);
        Assert.Equal(55m, changed.Fee);
        var cleared = await appointments.UpdateAsync(booked.Id, Booking(patient.Id, clinic.DentistId, app.Clock.UtcNow.AddDays(1), null), None);
        Assert.Null(cleared.Fee);

        Assert.False(new AppointmentUpsertValidator().Validate(Booking(patient.Id, clinic.DentistId, app.Clock.UtcNow, -1)).IsValid);
    }

    [Fact]
    public async Task Taking_the_fee_invoices_the_visit_once()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var appointments = app.Get<AppointmentService>();
        var payments = app.Get<PaymentService>();
        var visit = await appointments.CreateAsync(Booking(patient.Id, clinic.DentistId, app.Clock.UtcNow.AddHours(2), 40), None);

        await payments.RecordAsync(TakeFee(patient.Id, visit, 40), None);

        var invoice = await app.Db.Invoices.SingleAsync();
        Assert.Equal(visit.Id, invoice.AppointmentId);
        Assert.Equal(clinic.DentistId, invoice.DentistId);        // the visit's dentist, unless told otherwise
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        var after = await appointments.GetAsync(visit.Id, None);
        Assert.Equal(invoice.Id, after.FeeInvoiceId);
        Assert.Equal(0m, after.FeeBalance);

        // The same visit cannot be charged a second time.
        await Assert.ThrowsAsync<BusinessRuleException>(() => payments.RecordAsync(TakeFee(patient.Id, visit, 40), None));

        // The front desk shows the same thing.
        var row = Assert.Single((await app.Get<FrontDeskService>().GetAsync(null, None)).Appointments);
        Assert.Equal(40m, row.Appointment.Fee);
        Assert.Equal(invoice.Id, row.Appointment.FeeInvoiceId);
    }

    [Fact]
    public async Task A_fee_is_only_taken_from_the_patient_the_visit_belongs_to()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var sara = await app.AddPatientAsync("Sara");
        var omar = await app.AddPatientAsync("Omar", "0797654321");
        var visit = await app.Get<AppointmentService>().CreateAsync(Booking(sara.Id, clinic.DentistId, app.Clock.UtcNow.AddHours(2), 40), None);

        await Assert.ThrowsAsync<RequestValidationException>(() => app.Get<PaymentService>().RecordAsync(TakeFee(omar.Id, visit, 40), None));
        Assert.Empty(await app.Db.Invoices.ToListAsync());
    }
}
