using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tahona.Application.Common;
using Tahona.Application.Features.Messaging;
using Tahona.Application.Features.Recovery;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

/// <summary>Consent, quiet hours, retry limits and idempotency of outgoing and incoming messages.</summary>
public class MessagingSafetyTests
{
    [Theory]
    [InlineData("22:30", "21:00", "09:00", true)]   // late evening, window crosses midnight
    [InlineData("03:00", "21:00", "09:00", true)]   // small hours
    [InlineData("09:00", "21:00", "09:00", false)]  // the end is exclusive
    [InlineData("12:00", "21:00", "09:00", false)]
    [InlineData("13:30", "13:00", "15:00", true)]   // same-day window (siesta)
    [InlineData("12:00", "00:00", "00:00", false)]  // equal times: no quiet period
    public void Quiet_hours_cover_windows_that_cross_midnight(string now, string start, string end, bool expected) =>
        Assert.Equal(expected, MessagingService.InQuietHours(TimeOnly.Parse(now), TimeOnly.Parse(start), TimeOnly.Parse(end)));

    [Theory]
    [InlineData(MessagingConsent.Granted, true, true)]
    [InlineData(MessagingConsent.Unknown, true, false)]
    [InlineData(MessagingConsent.Unknown, false, true)]
    [InlineData(MessagingConsent.OptedOut, false, false)]
    public void Automation_needs_consent_and_never_reaches_opted_out_patients(MessagingConsent consent, bool requireConsent, bool expected) =>
        Assert.Equal(expected, MessagingService.AllowsAutomation(consent, requireConsent));

    [Fact]
    public async Task Opted_out_patient_cannot_be_messaged_but_a_phone_call_can_be_logged()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara", consent: MessagingConsent.OptedOut);
        var messaging = app.Get<MessagingService>();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => messaging.SendAsync(
            new SendMessageRequest(patient.Id, MessageChannel.WhatsApp, null, null, "Hello", null, null, null, null, null), CancellationToken.None));
        Assert.Contains("opted out", ex.Message);
        Assert.Empty(app.WhatsApp.Sent);

        var call = await messaging.SendAsync(
            new SendMessageRequest(patient.Id, MessageChannel.Phone, null, null, "Called about balance", null, null, null, null, null), CancellationToken.None);
        Assert.Equal(MessageStatus.Logged, call.Status);
    }

    [Theory]
    [InlineData(MessageChannel.Sms)]
    [InlineData(MessageChannel.Email)]
    public async Task Unconnected_channels_are_refused_instead_of_pretending_to_send(MessageChannel channel)
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");

        await Assert.ThrowsAsync<BusinessRuleException>(() => app.Get<MessagingService>().SendAsync(
            new SendMessageRequest(patient.Id, channel, null, null, "Hello", null, null, null, null, null), CancellationToken.None));
        Assert.Empty(await app.Db.Messages.ToListAsync());
    }

    [Fact]
    public async Task Free_text_through_the_cloud_api_needs_an_open_service_window()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var messaging = app.Get<MessagingService>();
        var request = new SendMessageRequest(patient.Id, MessageChannel.WhatsApp, null, null, "Free text", null, null, null, null, null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => messaging.SendAsync(request, CancellationToken.None));
        Assert.Contains("24 hours", ex.Message);

        // An approved template can start the conversation; the patient's reply then opens the window.
        var template = await messaging.SendAsync(request with { Body = null, TemplateType = MessageTemplateType.PaymentReminder, Amount = 50 }, CancellationToken.None);
        Assert.Equal(MessageStatus.Sent, template.Status);
        await messaging.ReceiveInboundAsync("wamid.in1", "962791234567", "Thanks", app.Clock.UtcNow, CancellationToken.None);

        var free = await messaging.SendAsync(request, CancellationToken.None);
        Assert.Equal(MessageStatus.Sent, free.Status);
    }

    [Fact]
    public async Task Manual_mode_never_reports_a_message_as_sent_until_staff_confirm()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var appt = new Appointment { PatientId = patient.Id, DentistId = clinic.DentistId, StartTime = app.Clock.UtcNow.AddHours(20),
            EndTime = app.Clock.UtcNow.AddHours(21), Treatment = "Check-up" };
        app.Db.Appointments.Add(appt);
        await app.Db.SaveChangesAsync();

        // Manual mode behaves like the real ManualWhatsAppProvider: it returns a link and sends nothing.
        var manual = new Tahona.Infrastructure.Messaging.ManualWhatsAppProvider();
        var link = await manual.SendAsync(new WhatsAppSendRequest("962791234567", "Hi", null, "en"), CancellationToken.None);
        Assert.True(link.RequiresManualSend);
        Assert.False(link.Success);

        // Through the service, an unconfirmed manual message leaves the appointment's reminder unsent.
        var pending = new Message { PatientId = patient.Id, Channel = MessageChannel.WhatsApp, Body = "Reminder", Status = MessageStatus.ManualPending, AppointmentId = appt.Id };
        app.Db.Messages.Add(pending);
        await app.Db.SaveChangesAsync();
        Assert.Null((await app.Db.Appointments.SingleAsync()).ReminderSentAt);

        await app.Get<MessagingService>().MarkSentAsync(pending.Id, CancellationToken.None);
        Assert.NotNull((await app.Db.Appointments.SingleAsync()).ReminderSentAt);
        Assert.Equal(MessageStatus.Sent, (await app.Db.Messages.SingleAsync()).Status);
    }

    [Fact]
    public async Task Follow_up_and_appointment_references_must_belong_to_the_patient()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var sara = await app.AddPatientAsync("Sara");
        var omar = await app.AddPatientAsync("Omar", "0795550000");
        var omarsAppointment = new Appointment { PatientId = omar.Id, DentistId = clinic.DentistId, StartTime = app.Clock.UtcNow.AddDays(1),
            EndTime = app.Clock.UtcNow.AddDays(1).AddMinutes(30), Treatment = "Filling" };
        var omarsFollowUp = new FollowUp { PatientId = omar.Id, IssueType = RecoveryIssueType.RecallDue, Title = "Recall", DueDate = app.Clock.UtcNow };
        app.Db.AddRange(omarsAppointment, omarsFollowUp);
        await app.Db.SaveChangesAsync();
        var messaging = app.Get<MessagingService>();

        await Assert.ThrowsAsync<RequestValidationException>(() => messaging.SendAsync(
            new SendMessageRequest(sara.Id, MessageChannel.Phone, null, null, "note", null, omarsFollowUp.Id, null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() => messaging.SendAsync(
            new SendMessageRequest(sara.Id, MessageChannel.Phone, null, null, "note", omarsAppointment.Id, null, null, null, null), CancellationToken.None));
    }

    // ------------------------------------------------------------------ automation

    private static async Task<Appointment> BookTomorrowAsync(TestApp app, TestClinic clinic, Patient patient, int hoursAhead = 20)
    {
        var appt = new Appointment { PatientId = patient.Id, DentistId = clinic.DentistId, StartTime = app.Clock.UtcNow.AddHours(hoursAhead),
            EndTime = app.Clock.UtcNow.AddHours(hoursAhead + 1), Treatment = "Check-up" };
        app.Db.Appointments.Add(appt);
        await app.Db.SaveChangesAsync();
        return appt;
    }

    [Fact]
    public async Task A_reminder_is_sent_once_however_often_the_engine_runs()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        await BookTomorrowAsync(app, clinic, await app.AddPatientAsync("Sara"));
        var sync = app.Get<RecoverySyncService>();

        var first = await sync.SyncAsync(CancellationToken.None);
        for (var i = 0; i < 4; i++) await sync.SyncAsync(CancellationToken.None);

        Assert.Equal(1, first.Reminders);
        Assert.Single(app.WhatsApp.Sent);
        var message = await app.Db.Messages.SingleAsync();
        Assert.True(message.IsAutomated);
        Assert.Null(message.SentById);
    }

    [Fact]
    public async Task A_failing_reminder_stops_after_the_attempt_limit()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        await BookTomorrowAsync(app, clinic, await app.AddPatientAsync("Sara"));
        app.WhatsApp.Fail = true;
        var sync = app.Get<RecoverySyncService>();

        for (var i = 0; i < 10; i++) await sync.SyncAsync(CancellationToken.None);

        Assert.Equal(RecoverySyncService.MaxReminderAttempts, app.WhatsApp.Sent.Count);
        Assert.All(await app.Db.Messages.ToListAsync(), m => Assert.Equal(MessageStatus.Failed, m.Status));
        Assert.Null((await app.Db.Appointments.SingleAsync()).ReminderSentAt);
    }

    [Fact]
    public async Task No_automatic_message_in_quiet_hours_or_without_consent()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        // 30 hours ahead: inside the 24-hour reminder horizon at both times checked below.
        await BookTomorrowAsync(app, clinic, await app.AddPatientAsync("Consented"), 30);
        await BookTomorrowAsync(app, clinic, await app.AddPatientAsync("Unknown", "0795550001", MessagingConsent.Unknown), 30);
        await BookTomorrowAsync(app, clinic, await app.AddPatientAsync("Refused", "0795550002", MessagingConsent.OptedOut), 30);
        var sync = app.Get<RecoverySyncService>();

        app.Clock.UtcNow = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc); // 23:00 in Amman
        await sync.SyncAsync(CancellationToken.None);
        Assert.Empty(app.WhatsApp.Sent);

        app.Clock.UtcNow = new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc); // 11:00 in Amman
        await sync.SyncAsync(CancellationToken.None);
        var sent = Assert.Single(app.WhatsApp.Sent);
        Assert.Contains("Consented", sent.Body);
    }

    [Fact]
    public async Task Clinic_can_allow_reminders_for_patients_whose_consent_was_never_recorded()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        await BookTomorrowAsync(app, clinic, await app.AddPatientAsync("Unknown", consent: MessagingConsent.Unknown));
        var settings = await app.Db.ClinicSettings.SingleAsync();
        settings.RequireConsentForAutomation = false;
        await app.Db.SaveChangesAsync();

        await app.Get<RecoverySyncService>().SyncAsync(CancellationToken.None);
        Assert.Single(app.WhatsApp.Sent);
    }

    [Fact]
    public async Task An_automatic_follow_up_is_attempted_once_and_never_repeated()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var template = await app.Db.MessageTemplates.FirstAsync(t => t.Type == MessageTemplateType.TreatmentFollowUp);
        app.Db.FollowUpRules.Add(new FollowUpRule { Name = "Not started", Trigger = FollowUpTrigger.TreatmentPlanNotStarted, DelayDays = 3,
            AutoSend = true, MessageTemplateId = template.Id });
        app.Db.TreatmentPlans.Add(new TreatmentPlan
        {
            PlanNumber = "TP-1", PatientId = patient.Id, DentistId = clinic.DentistId, Title = "Crown", Status = TreatmentPlanStatus.Presented,
            PresentedAt = app.Clock.UtcNow.AddDays(-10), CreatedAt = app.Clock.UtcNow.AddDays(-10), TotalAmount = 300,
            Items = [new TreatmentPlanItem { Name = "Crown", Total = 300, UnitPrice = 300 }]
        });
        var settings = await app.Db.ClinicSettings.SingleAsync();
        settings.AutoSendFollowUps = true;
        await app.Db.SaveChangesAsync();
        app.WhatsApp.Fail = true;
        var sync = app.Get<RecoverySyncService>();

        for (var i = 0; i < 5; i++) await sync.SyncAsync(CancellationToken.None);

        Assert.Single(app.WhatsApp.Sent);
        var followUp = await app.Db.FollowUps.SingleAsync();
        Assert.Equal(1, followUp.AutoSendAttempts);
        Assert.Equal(FollowUpStatus.Pending, followUp.Status); // a failed send is not a contact
        Assert.Null(followUp.LastContactedAt);
    }

    // ------------------------------------------------------------------ inbound and status events

    [Fact]
    public async Task Inbound_reply_is_stored_once_on_the_right_clinic_and_STOP_withdraws_consent()
    {
        using var app = new TestApp();
        var other = await app.AddClinicAsync("Other clinic");
        var othersPatient = await app.AddPatientAsync("Same Number", "0791234567");
        var clinic = await app.AddClinicAsync("Messaging clinic");
        var patient = await app.AddPatientAsync("Sara", "0791234567");
        var messaging = app.Get<MessagingService>();
        await messaging.SendAsync(new SendMessageRequest(patient.Id, MessageChannel.WhatsApp, null, MessageTemplateType.PaymentReminder, null,
            null, null, 40, null, null), CancellationToken.None);

        // The webhook has no user and no tenant.
        app.User.ClinicId = null;
        app.User.UserId = null;
        using (var scope = app.NewScope())
        {
            var webhook = scope.ServiceProvider.GetRequiredService<MessagingService>();
            Assert.Equal(InboundResult.Stored, await webhook.ReceiveInboundAsync("wamid.A", "962791234567", "إيقاف", app.Clock.UtcNow, CancellationToken.None));
            Assert.Equal(InboundResult.Duplicate, await webhook.ReceiveInboundAsync("wamid.A", "962791234567", "إيقاف", app.Clock.UtcNow, CancellationToken.None));
            Assert.Equal(InboundResult.Unmatched, await webhook.ReceiveInboundAsync("wamid.B", "962790000000", "hello", app.Clock.UtcNow, CancellationToken.None));
        }

        app.ActAs(clinic.Id, clinic.AdminId);
        using (var scope = app.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tahona.Infrastructure.Persistence.AppDbContext>();
            var inbound = await db.Messages.SingleAsync(m => m.Direction == MessageDirection.Inbound);
            Assert.Equal(patient.Id, inbound.PatientId);
            Assert.Equal(MessageStatus.Received, inbound.Status);
            var stored = await db.Patients.SingleAsync(p => p.Id == patient.Id);
            Assert.Equal(MessagingConsent.OptedOut, stored.MessagingConsent);
            Assert.Equal("whatsapp-reply", stored.MessagingConsentSource);
            Assert.Contains(await db.Notifications.ToListAsync(), n => n.Type == NotificationType.PatientReply);
        }

        // The other clinic never wrote to this number: it sees nothing and its patient is untouched.
        app.ActAs(other.Id, other.AdminId);
        using (var scope = app.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tahona.Infrastructure.Persistence.AppDbContext>();
            Assert.Empty(await db.Messages.ToListAsync());
            Assert.Equal(MessagingConsent.Granted, (await db.Patients.SingleAsync(p => p.Id == othersPatient.Id)).MessagingConsent);
        }
    }

    [Fact]
    public async Task Delivery_statuses_only_move_forward_and_repeats_change_nothing()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var messaging = app.Get<MessagingService>();
        await messaging.SendAsync(new SendMessageRequest(patient.Id, MessageChannel.WhatsApp, null, MessageTemplateType.PaymentReminder, null,
            null, null, 40, null, null), CancellationToken.None);
        var id = (await app.Db.Messages.SingleAsync()).ProviderMessageId!;

        await messaging.ApplyProviderStatusAsync(id, MessageStatus.Read, null, CancellationToken.None);
        var readAt = (await app.Db.Messages.SingleAsync()).ReadAt;
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        await messaging.ApplyProviderStatusAsync(id, MessageStatus.Delivered, null, CancellationToken.None); // late, out of order
        await messaging.ApplyProviderStatusAsync(id, MessageStatus.Read, null, CancellationToken.None);      // repeated
        await messaging.ApplyProviderStatusAsync(id, MessageStatus.Failed, "late failure", CancellationToken.None);

        var message = await app.Db.Messages.SingleAsync();
        Assert.Equal(MessageStatus.Read, message.Status);
        Assert.Equal(readAt, message.ReadAt);
        Assert.Null(message.Error);
        Assert.False(await messaging.ApplyProviderStatusAsync("wamid.unknown", MessageStatus.Delivered, null, CancellationToken.None));
    }
}
