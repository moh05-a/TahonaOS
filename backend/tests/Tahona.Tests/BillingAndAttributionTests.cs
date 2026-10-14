using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Billing;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

/// <summary>Payments, refunds and the rule that decides what counts as collected after follow-up.</summary>
public class BillingAndAttributionTests
{
    private static Invoice NewInvoice(Patient p, decimal total, DateTime issued, Guid? planId = null, string number = "INV-1") => new()
    {
        InvoiceNumber = number, PatientId = p.Id, IssueDate = issued, DueDate = issued.AddDays(14), Status = InvoiceStatus.Issued,
        Total = total, Balance = total, TreatmentPlanId = planId
    };

    private static FollowUp Contacted(Patient p, RecoveryIssueType issue, DateTime at, Guid? source = null, Guid? bookedAppointment = null) => new()
    {
        PatientId = p.Id, IssueType = issue, Title = issue.ToString(), DueDate = at, Status = FollowUpStatus.Contacted,
        LastContactedAt = at, ContactAttempts = 1, SourceEntityId = source, BookedAppointmentId = bookedAppointment
    };

    private static RecordPaymentRequest Pay(Patient p, Invoice i, decimal amount) =>
        new(p.Id, i.Id, amount, PaymentMethod.Cash, null, null, null);

    [Fact]
    public async Task Payment_and_refund_keep_the_invoice_balance_exact()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var invoice = NewInvoice(patient, 100.500m, app.Clock.UtcNow.AddDays(-1));
        app.Db.Invoices.Add(invoice);
        await app.Db.SaveChangesAsync();
        var payments = app.Get<PaymentService>();

        var first = await payments.RecordAsync(Pay(patient, invoice, 40.250m), CancellationToken.None);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(60.250m, invoice.Balance);

        await Assert.ThrowsAsync<RequestValidationException>(() => payments.RecordAsync(Pay(patient, invoice, 60.251m), CancellationToken.None));

        await payments.RecordAsync(Pay(patient, invoice, 60.250m), CancellationToken.None);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Balance);
        await Assert.ThrowsAsync<BusinessRuleException>(() => payments.RecordAsync(Pay(patient, invoice, 1), CancellationToken.None));

        await payments.RefundAsync(first.PaymentIds[0], new RefundRequest(10, "Overcharged", null), CancellationToken.None);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(10m, invoice.Balance);
        Assert.Equal(90.500m, invoice.AmountPaid);

        // A payment can never be refunded for more than what is left of it.
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            payments.RefundAsync(first.PaymentIds[0], new RefundRequest(30.251m, "Too much", null), CancellationToken.None));
    }

    [Fact]
    public async Task Payment_for_an_unrelated_invoice_is_not_credited_to_a_follow_up()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var contactedAt = app.Clock.UtcNow.AddDays(-5);
        // The clinic called about a recall. The patient has not booked; they simply settle an old invoice.
        app.Db.FollowUps.Add(Contacted(patient, RecoveryIssueType.RecallDue, contactedAt));
        var oldInvoice = NewInvoice(patient, 80, app.Clock.UtcNow.AddDays(-60));
        app.Db.Invoices.Add(oldInvoice);
        await app.Db.SaveChangesAsync();

        var result = await app.Get<PaymentService>().RecordAsync(Pay(patient, oldInvoice, 80), CancellationToken.None);

        Assert.Equal(0, result.RecoveredAmount);
        Assert.Null((await app.Db.Payments.SingleAsync()).RecoveredFollowUpId);
        Assert.Equal(FollowUpStatus.Contacted, (await app.Db.FollowUps.SingleAsync()).Status);
    }

    [Fact]
    public async Task Payment_of_the_balance_the_clinic_chased_is_credited_and_a_refund_reverses_it()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var overdue = NewInvoice(patient, 200, app.Clock.UtcNow.AddDays(-40));
        app.Db.Invoices.Add(overdue);
        app.Db.FollowUps.Add(Contacted(patient, RecoveryIssueType.OutstandingBalance, app.Clock.UtcNow.AddDays(-2), patient.Id));
        await app.Db.SaveChangesAsync();
        var payments = app.Get<PaymentService>();

        var result = await payments.RecordAsync(Pay(patient, overdue, 150), CancellationToken.None);
        var followUp = await app.Db.FollowUps.SingleAsync();
        Assert.Equal(150, result.RecoveredAmount);
        Assert.Equal(FollowUpStatus.Recovered, followUp.Status);
        Assert.Equal(150, followUp.RecoveredAmount);

        await payments.RefundAsync(result.PaymentIds[0], new RefundRequest(50, "Goodwill", null), CancellationToken.None);
        Assert.Equal(100, followUp.RecoveredAmount);
    }

    [Fact]
    public async Task Contact_outside_the_window_or_after_the_payment_does_not_count()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var overdue = NewInvoice(patient, 200, app.Clock.UtcNow.AddDays(-200));
        app.Db.Invoices.Add(overdue);
        app.Db.FollowUps.Add(Contacted(patient, RecoveryIssueType.OutstandingBalance, app.Clock.UtcNow.AddDays(-60), patient.Id)); // window is 45 days
        await app.Db.SaveChangesAsync();

        var result = await app.Get<PaymentService>().RecordAsync(Pay(patient, overdue, 200), CancellationToken.None);
        Assert.Equal(0, result.RecoveredAmount);
    }

    [Fact]
    public void An_invoice_relates_to_a_follow_up_only_when_it_is_about_the_same_matter()
    {
        var patient = new Patient { FullName = "Sara" };
        var contact = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var planId = Guid.NewGuid();
        var before = NewInvoice(patient, 50, contact.AddDays(-10));
        var after = NewInvoice(patient, 50, contact.AddDays(3));
        var afterForPlan = NewInvoice(patient, 50, contact.AddDays(3), planId);
        var beforeForPlan = NewInvoice(patient, 50, contact.AddDays(-10), planId);

        // Balance: only invoices that already existed when the clinic called.
        var balance = Contacted(patient, RecoveryIssueType.OutstandingBalance, contact);
        Assert.True(RecoveryAttributionService.Relates(balance, before));
        Assert.False(RecoveryAttributionService.Relates(balance, after));

        // Treatment plan: only new invoices for that same plan.
        var plan = Contacted(patient, RecoveryIssueType.TreatmentUnfinished, contact, planId);
        Assert.True(RecoveryAttributionService.Relates(plan, afterForPlan));
        Assert.False(RecoveryAttributionService.Relates(plan, beforeForPlan));
        Assert.False(RecoveryAttributionService.Relates(plan, after));
        Assert.False(RecoveryAttributionService.Relates(plan, NewInvoice(patient, 50, contact.AddDays(3), Guid.NewGuid())));

        // Missed appointment / recall: a visit must have been booked after the contact.
        var recall = Contacted(patient, RecoveryIssueType.RecallDue, contact);
        Assert.False(RecoveryAttributionService.Relates(recall, after));
        var recallBooked = Contacted(patient, RecoveryIssueType.RecallDue, contact, bookedAppointment: Guid.NewGuid());
        Assert.True(RecoveryAttributionService.Relates(recallBooked, after));
        Assert.False(RecoveryAttributionService.Relates(recallBooked, before));

        // Never contacted: nothing is credited.
        var untouched = new FollowUp { IssueType = RecoveryIssueType.OutstandingBalance };
        Assert.False(RecoveryAttributionService.Relates(untouched, before));
    }
}
