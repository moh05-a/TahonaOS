using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Billing;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

/// <summary>Taking a payment for something not invoiced yet, correcting an entry, and cancelling one made by mistake.</summary>
public class PaymentCorrectionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static Invoice NewInvoice(Patient p, decimal total, DateTime issued) => new()
    {
        InvoiceNumber = "INV-1", PatientId = p.Id, IssueDate = issued, DueDate = issued.AddDays(14), Status = InvoiceStatus.Issued,
        Total = total, Balance = total
    };

    private static RecordPaymentRequest Pay(Patient p, Invoice i, decimal amount) => new(p.Id, i.Id, amount, PaymentMethod.Cash, null, null, null);

    private static RecordPaymentRequest PayNewCharge(Patient p, decimal amount, decimal charge) =>
        new(p.Id, null, amount, PaymentMethod.Cash, null, null, null, new NewChargeRequest("Emergency visit", null, charge, null));

    [Fact]
    public async Task A_payment_for_something_not_invoiced_creates_the_invoice_with_it()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var payments = app.Get<PaymentService>();

        // With nothing outstanding and no charge named, there is nothing to apply the money to.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            payments.RecordAsync(new(patient.Id, null, 35, PaymentMethod.Cash, null, null, null), None));

        var result = await payments.RecordAsync(PayNewCharge(patient, 35, 35), None);

        var invoice = await app.Db.Invoices.Include(i => i.Items).Include(i => i.Payments).SingleAsync();
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(35m, invoice.Total);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal("Emergency visit", Assert.Single(invoice.Items).Description);
        Assert.Equal(result.PaymentIds[0], Assert.Single(invoice.Payments).Id);
        Assert.Equal(35m, result.Applied);
    }

    [Fact]
    public async Task Leaving_part_of_a_new_charge_unpaid_needs_the_invoicing_permission()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var payments = app.Get<PaymentService>();

        await Assert.ThrowsAsync<RequestValidationException>(() => payments.RecordAsync(PayNewCharge(patient, 60, 50), None));

        app.User.Granted.Remove(Permissions.InvoicesManage);
        await Assert.ThrowsAsync<BusinessRuleException>(() => payments.RecordAsync(PayNewCharge(patient, 35, 50), None));
        Assert.Empty(await app.Db.Invoices.ToListAsync());
        Assert.Empty(await app.Db.Payments.ToListAsync());

        // Paid in full is allowed for anyone who can take payments.
        await payments.RecordAsync(PayNewCharge(patient, 50, 50), None);
        Assert.Equal(InvoiceStatus.Paid, (await app.Db.Invoices.SingleAsync()).Status);

        app.User.Granted.Add(Permissions.InvoicesManage);
        await payments.RecordAsync(PayNewCharge(patient, 35, 50), None);
        var partial = await app.Db.Invoices.SingleAsync(i => i.Status == InvoiceStatus.PartiallyPaid);
        Assert.Equal(15m, partial.Balance);
    }

    [Fact]
    public async Task A_dentist_records_and_corrects_payments_but_cancelling_and_refunding_stay_with_the_admin()
    {
        var dentist = SystemRoles.DefaultPermissions[SystemRoles.Dentist];
        Assert.Contains(Permissions.PaymentsManage, dentist);
        Assert.DoesNotContain(Permissions.PaymentsRefund, dentist);
        Assert.Equal([SystemRoles.Admin, SystemRoles.Dentist], SystemRoles.DefaultPermissions.Keys.Order());

        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        app.User.Granted.Clear();
        app.User.Granted.UnionWith(dentist);
        var payments = app.Get<PaymentService>();

        var paid = await payments.RecordAsync(PayNewCharge(patient, 40, 40), None);
        await payments.UpdateAsync(paid.PaymentIds[0], new UpdatePaymentRequest(PaymentMethod.Card, new DateTimeOffset(app.Clock.UtcNow), "POS-1", null), None);
        Assert.Equal(PaymentMethod.Card, (await app.Db.Payments.SingleAsync()).Method);

        // Leaving part of a new charge unpaid is an invoicing decision, which dentists do not have.
        await Assert.ThrowsAsync<BusinessRuleException>(() => payments.RecordAsync(PayNewCharge(patient, 10, 40), None));
    }

    [Fact]
    public async Task Correcting_an_entry_changes_its_details_but_never_its_amount()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var invoice = NewInvoice(patient, 100, app.Clock.UtcNow.AddDays(-3));
        app.Db.Invoices.Add(invoice);
        await app.Db.SaveChangesAsync();
        var payments = app.Get<PaymentService>();
        var id = (await payments.RecordAsync(Pay(patient, invoice, 40), None)).PaymentIds[0];

        var yesterday = new DateTimeOffset(app.Clock.UtcNow.AddDays(-1));
        await payments.UpdateAsync(id, new UpdatePaymentRequest(PaymentMethod.CliQ, yesterday, " CLQ-77 ", "Paid by transfer, not cash"), None);

        var payment = await app.Db.Payments.SingleAsync();
        Assert.Equal(PaymentMethod.CliQ, payment.Method);
        Assert.Equal(yesterday.UtcDateTime, payment.PaidAt);
        Assert.Equal("CLQ-77", payment.Reference);
        Assert.Equal("Paid by transfer, not cash", payment.Notes);
        Assert.Equal(40m, payment.Amount);
        Assert.Equal(60m, invoice.Balance);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            payments.UpdateAsync(id, new UpdatePaymentRequest(PaymentMethod.Cash, new DateTimeOffset(app.Clock.UtcNow.AddDays(1)), null, null), None));
    }

    [Fact]
    public async Task Cancelling_a_mistaken_entry_restores_the_invoice_and_keeps_the_record()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var invoice = NewInvoice(patient, 100, app.Clock.UtcNow.AddDays(-3));
        app.Db.Invoices.Add(invoice);
        await app.Db.SaveChangesAsync();
        var payments = app.Get<PaymentService>();

        var mistake = await payments.RecordAsync(Pay(patient, invoice, 40), None);
        await payments.RecordAsync(Pay(patient, invoice, 60), None);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        await payments.CancelAsync(mistake.PaymentIds[0], new CancelPaymentRequest("Entered on the wrong patient"), None);

        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(60m, invoice.AmountPaid);
        Assert.Equal(40m, invoice.Balance);

        // It no longer counts anywhere...
        Assert.Equal(60m, (await payments.SummaryAsync(None)).Today);
        var listed = await payments.ListAsync(new PaymentQuery(), None);
        Assert.DoesNotContain(listed.Items, p => p.Id == mistake.PaymentIds[0]);

        // ...but it is still on record, with who cancelled it and why.
        var cancelled = Assert.Single((await payments.ListAsync(new PaymentQuery { Cancelled = true }, None)).Items);
        Assert.Equal(mistake.ReceiptNumbers[0], cancelled.ReceiptNumber);
        Assert.True(cancelled.IsCancelled);
        Assert.Equal("Entered on the wrong patient", cancelled.CancelReason);
        Assert.Equal("Admin", cancelled.CancelledBy);
        Assert.Equal(app.Clock.UtcNow, cancelled.CancelledAt);

        // Its receipt number is never handed out again, and the freed balance can be paid.
        var again = await payments.RecordAsync(Pay(patient, invoice, 40), None);
        Assert.NotEqual(mistake.ReceiptNumbers[0], again.ReceiptNumbers[0]);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        // Cancelling twice finds nothing to cancel.
        await Assert.ThrowsAsync<NotFoundException>(() => payments.CancelAsync(mistake.PaymentIds[0], new CancelPaymentRequest("Again"), None));
        Assert.Equal(clinic.AdminId, (await app.Db.Payments.IgnoreQueryFilters().SingleAsync(p => p.IsDeleted)).CancelledById);
    }

    [Fact]
    public async Task Cancelled_entries_of_another_clinic_are_never_listed()
    {
        using var app = new TestApp();
        await app.AddClinicAsync("First");
        var patient = await app.AddPatientAsync("Sara");
        var invoice = NewInvoice(patient, 100, app.Clock.UtcNow.AddDays(-3));
        app.Db.Invoices.Add(invoice);
        await app.Db.SaveChangesAsync();
        var paid = await app.Get<PaymentService>().RecordAsync(Pay(patient, invoice, 40), None);
        await app.Get<PaymentService>().CancelAsync(paid.PaymentIds[0], new CancelPaymentRequest("Mistake"), None);

        await app.AddClinicAsync("Second");

        Assert.Empty((await app.Get<PaymentService>().ListAsync(new PaymentQuery { Cancelled = true }, None)).Items);
    }

    [Fact]
    public async Task Refunds_and_refunded_payments_cannot_be_cancelled()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var invoice = NewInvoice(patient, 100, app.Clock.UtcNow.AddDays(-3));
        app.Db.Invoices.Add(invoice);
        await app.Db.SaveChangesAsync();
        var payments = app.Get<PaymentService>();
        var paid = await payments.RecordAsync(Pay(patient, invoice, 100), None);
        var refundId = await payments.RefundAsync(paid.PaymentIds[0], new RefundRequest(30, "Overcharged", null), None);

        await Assert.ThrowsAsync<BusinessRuleException>(() => payments.CancelAsync(paid.PaymentIds[0], new CancelPaymentRequest("Mistake"), None));
        await Assert.ThrowsAsync<BusinessRuleException>(() => payments.CancelAsync(refundId, new CancelPaymentRequest("Mistake"), None));

        Assert.Equal(70m, invoice.AmountPaid);
        var row = Assert.Single((await payments.ListAsync(new PaymentQuery(), None)).Items, p => p.Type == PaymentType.Payment);
        Assert.Equal(30m, row.RefundedAmount);
    }

    [Fact]
    public async Task Cancelling_takes_back_the_credit_given_to_a_follow_up()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var contactedAt = app.Clock.UtcNow.AddDays(-2);
        var followUp = new FollowUp
        {
            PatientId = patient.Id, IssueType = RecoveryIssueType.OutstandingBalance, Title = "Balance", DueDate = contactedAt,
            Status = FollowUpStatus.Contacted, LastContactedAt = contactedAt, ContactAttempts = 1
        };
        var invoice = NewInvoice(patient, 80, app.Clock.UtcNow.AddDays(-30));
        app.Db.FollowUps.Add(followUp);
        app.Db.Invoices.Add(invoice);
        await app.Db.SaveChangesAsync();
        var payments = app.Get<PaymentService>();

        var paid = await payments.RecordAsync(Pay(patient, invoice, 80), None);
        Assert.Equal(80m, paid.RecoveredAmount);
        Assert.Equal(FollowUpStatus.Recovered, followUp.Status);

        await payments.CancelAsync(paid.PaymentIds[0], new CancelPaymentRequest("Entered twice"), None);

        Assert.Equal(0m, followUp.RecoveredAmount);
        Assert.Equal(FollowUpStatus.Contacted, followUp.Status);
        Assert.Null(followUp.ResolvedAt);
        Assert.Equal(0m, (await payments.SummaryAsync(None)).RecoveredThisMonth);
    }
}
