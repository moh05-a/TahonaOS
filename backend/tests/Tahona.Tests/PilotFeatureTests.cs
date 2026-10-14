using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tahona.Application.Common;
using Tahona.Application.Features.Appointments;
using Tahona.Application.Features.Auth;
using Tahona.Application.Features.Dashboard;
using Tahona.Application.Features.Messaging;
using Tahona.Application.Features.Provisioning;
using Tahona.Application.Features.Recovery;
using Tahona.Application.Features.Settings;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Messaging;
using Tahona.Infrastructure.Persistence;
using Tahona.Infrastructure.Services;

namespace Tahona.Tests;

/// <summary>Reception's day view, the evidence behind recovery items, the owner report, per-clinic WhatsApp, e-mail and metrics.</summary>
public class PilotFeatureTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static Appointment Visit(TestApp app, Guid patientId, Guid dentistId, double hoursFromNow, AppointmentStatus status = AppointmentStatus.Scheduled) => new()
    {
        PatientId = patientId, DentistId = dentistId, StartTime = app.Clock.UtcNow.AddHours(hoursFromNow), EndTime = app.Clock.UtcNow.AddHours(hoursFromNow + 0.5),
        Treatment = "Check-up", Status = status
    };

    // ------------------------------------------------------------------ front desk

    [Fact]
    public async Task The_front_desk_shows_today_in_order_with_who_is_late_and_what_is_owed()
    {
        using var app = new TestApp();      // the test clock is Wednesday 7 October 2026, 12:00 in Amman
        var clinic = await app.AddClinicAsync();
        var late = await app.AddPatientAsync("Late Patient");
        var later = await app.AddPatientAsync("Later Patient", "0795550001");
        var tomorrow = await app.AddPatientAsync("Tomorrow Patient", "0795550002");
        app.Db.AddRange(
            Visit(app, later.Id, clinic.DentistId, 3),
            Visit(app, late.Id, clinic.DentistId, -1),                                     // an hour ago, still not checked in
            Visit(app, late.Id, clinic.DentistId, -3, AppointmentStatus.Completed),
            Visit(app, tomorrow.Id, clinic.DentistId, 24),
            new Invoice { InvoiceNumber = "INV-1", PatientId = late.Id, IssueDate = app.Clock.UtcNow.AddDays(-20), DueDate = app.Clock.UtcNow.AddDays(-6),
                Status = InvoiceStatus.Issued, Total = 60, Balance = 60 },
            new Payment { ReceiptNumber = "RC-1", PatientId = later.Id, Amount = 25, Method = PaymentMethod.Cash, PaidAt = app.Clock.UtcNow.AddHours(-2) });
        await app.Db.SaveChangesAsync();

        var day = await app.Get<FrontDeskService>().GetAsync(null, None);

        Assert.Equal(new DateOnly(2026, 10, 7), day.Date);
        Assert.Equal(3, day.Appointments.Count);                                           // tomorrow's visit is not on today's list
        Assert.Equal(["Late Patient", "Late Patient", "Later Patient"], day.Appointments.Select(a => a.Appointment.PatientName));
        var overdue = day.Appointments.Single(a => a.IsLate);
        Assert.Equal(AppointmentStatus.Scheduled, overdue.Appointment.Status);
        Assert.Equal(60, overdue.PatientBalance);
        Assert.Equal(0, day.Appointments.Last().PatientBalance);
        Assert.Equal(new FrontDeskCountsDto(2, 0, 0, 1, 0, 0), day.Counts);
        Assert.Equal(1, day.TomorrowUnconfirmed);
        Assert.Equal(25, day.CollectedToday);
        Assert.Null(day.Closed);

        // A receptionist without billing access sees the day, not the money.
        app.User.Granted.Remove(Permissions.BillingView);
        var withoutBilling = await app.Get<FrontDeskService>().GetAsync(null, None);
        Assert.All(withoutBilling.Appointments, a => Assert.Null(a.PatientBalance));
        Assert.Null(withoutBilling.CollectedToday);
    }

    [Fact]
    public async Task Closed_days_are_set_by_the_clinic_and_shown_at_the_front_desk()
    {
        using var app = new TestApp();
        await app.AddClinicAsync();
        var settings = app.Get<SettingsService>();
        var current = await settings.GetSettingsAsync(None);
        Assert.Empty(current.Holidays!);

        await settings.UpdateSettingsAsync(current with { Holidays = [new DateOnly(2026, 12, 25), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 7)] }, None);
        Assert.Equal([new DateOnly(2026, 10, 7), new DateOnly(2026, 12, 25)], (await settings.GetSettingsAsync(None)).Holidays);
        Assert.Equal("holiday", (await app.Get<FrontDeskService>().GetAsync(null, None)).Closed);

        // A client that does not send the list leaves it as it is.
        await settings.UpdateSettingsAsync(current with { Holidays = null }, None);
        Assert.Equal(2, (await settings.GetSettingsAsync(None)).Holidays!.Count);

        // Friday is not a working day in the default week.
        app.Clock.UtcNow = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal("day-off", (await app.Get<FrontDeskService>().GetAsync(null, None)).Closed);
    }

    // ------------------------------------------------------------------ recovery evidence and outcomes

    [Fact]
    public async Task Each_follow_up_shows_what_raised_it_what_was_done_and_how_it_ended()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        app.WhatsApp.IsConfigured = false;                                                 // manual mode
        var patient = await app.AddPatientAsync("Sara");
        var presented = app.Clock.UtcNow.AddDays(-10);
        var plan = new TreatmentPlan
        {
            PlanNumber = "TP-0007", PatientId = patient.Id, DentistId = clinic.DentistId, Title = "Zirconia crown", Status = TreatmentPlanStatus.Presented,
            PresentedAt = presented, CreatedAt = presented, TotalAmount = 250, Items = [new TreatmentPlanItem { Name = "Crown", Total = 250, UnitPrice = 250 }]
        };
        app.Db.TreatmentPlans.Add(plan);
        app.Db.FollowUpRules.Add(new FollowUpRule { Name = "Not started", Trigger = FollowUpTrigger.TreatmentPlanNotStarted, DelayDays = 3 });
        await app.Db.SaveChangesAsync();
        var recovery = app.Get<RecoveryService>();
        var history = app.Get<RecoveryHistoryService>();

        await recovery.ContactAsync(patient.Id, new ContactPatientRequest(MessageChannel.Phone, null, null, null, "Will think about it"), None);
        var trail = Assert.Single(await history.GetAsync(patient.Id, None));

        Assert.Equal(RecoveryIssueType.TreatmentNotStarted, trail.IssueType);
        Assert.Equal("TP-0007 · Zirconia crown", trail.SourceLabel);                       // the source record
        Assert.Equal($"/treatment-plans/{plan.Id}", trail.SourceLink);
        Assert.Equal(presented, trail.EventAt);                                            // the date of the underlying event
        Assert.Equal(250, trail.EstimatedValue);
        Assert.Equal(FollowUpStatus.Contacted, trail.Status);
        Assert.Equal(1, trail.ContactAttempts);
        var contact = Assert.Single(trail.Contacts);                                       // what was done, by whom
        Assert.Equal(MessageChannel.Phone, contact.Channel);
        Assert.Equal("Will think about it", contact.Body);
        Assert.False(contact.IsAutomated);
        Assert.Equal(0, trail.CollectedAfterFollowUp);

        // The recorded outcome closes the item and stays on the record.
        await recovery.RecordOutcomeAsync(patient.Id, new OutcomeRequest(ContactOutcome.Declined, "Too expensive", null), None);
        var closed = Assert.Single(await history.GetAsync(patient.Id, None));
        Assert.Equal(FollowUpStatus.Dismissed, closed.Status);
        Assert.Equal("Declined: Too expensive", closed.Outcome);
        Assert.NotNull(closed.ResolvedAt);
        Assert.Null(await recovery.GetPatientAsync(patient.Id, None));                     // no longer on the list

        await Assert.ThrowsAsync<NotFoundException>(() => history.GetAsync(Guid.NewGuid(), None));
    }

    [Theory]
    [InlineData(ContactOutcome.Unreachable, FollowUpStatus.Dismissed, 1)]                  // an attempt was made: it is counted
    [InlineData(ContactOutcome.NotApplicable, FollowUpStatus.Dismissed, 0)]                // nobody was contacted
    [InlineData(ContactOutcome.NoResponse, FollowUpStatus.NoResponse, 1)]
    [InlineData(ContactOutcome.CallBackLater, FollowUpStatus.Contacted, 0)]
    public async Task Outcomes_close_or_keep_the_item_and_count_real_attempts_only(ContactOutcome outcome, FollowUpStatus expected, int attempts)
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        app.Db.FollowUpRules.Add(new FollowUpRule { Name = "Balance", Trigger = FollowUpTrigger.OverdueBalance, DelayDays = 0 });
        app.Db.Invoices.Add(new Invoice { InvoiceNumber = "INV-1", PatientId = patient.Id, DentistId = clinic.DentistId, IssueDate = app.Clock.UtcNow.AddDays(-30),
            DueDate = app.Clock.UtcNow.AddDays(-10), Status = InvoiceStatus.Issued, Total = 90, Balance = 90 });
        await app.Db.SaveChangesAsync();

        await app.Get<RecoveryService>().RecordOutcomeAsync(patient.Id, new OutcomeRequest(outcome, null, null), None);

        var followUp = await app.Db.FollowUps.SingleAsync();
        Assert.Equal(expected, followUp.Status);
        Assert.Equal(attempts, followUp.ContactAttempts);
        Assert.Equal(outcome.ToString(), followUp.Outcome);
    }

    // ------------------------------------------------------------------ owner report

    [Fact]
    public async Task The_impact_report_puts_a_period_beside_the_one_before_it_using_recorded_events_only()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var patient = await app.AddPatientAsync("Sara");
        var now = app.Clock.UtcNow;
        Appointment At(int daysAgo, AppointmentStatus status) => new() { PatientId = patient.Id, DentistId = clinic.DentistId, StartTime = now.AddDays(-daysAgo),
            EndTime = now.AddDays(-daysAgo).AddMinutes(30), Treatment = "Visit", Status = status };
        var contacted = new FollowUp { PatientId = patient.Id, IssueType = RecoveryIssueType.OutstandingBalance, Title = "Balance", DueDate = now.AddDays(-6),
            Status = FollowUpStatus.Recovered, LastContactedAt = now.AddDays(-5), ContactAttempts = 1, RecoveredAmount = 40, CreatedAt = now.AddDays(-6) };
        app.Db.AddRange(
            At(2, AppointmentStatus.Completed), At(3, AppointmentStatus.Completed), At(4, AppointmentStatus.Completed), At(5, AppointmentStatus.NoShow),
            At(40, AppointmentStatus.Completed), At(41, AppointmentStatus.NoShow),         // the previous 30 days
            contacted,
            new Payment { ReceiptNumber = "RC-1", PatientId = patient.Id, Amount = 40, Method = PaymentMethod.Cash, PaidAt = now.AddDays(-4), RecoveredFollowUp = contacted },
            new Payment { ReceiptNumber = "RC-2", PatientId = patient.Id, Amount = 100, Method = PaymentMethod.Card, PaidAt = now.AddDays(-3) },
            new Payment { ReceiptNumber = "RC-3", PatientId = patient.Id, Amount = 10, Type = PaymentType.Refund, Method = PaymentMethod.Cash, PaidAt = now.AddDays(-2) },
            new Payment { ReceiptNumber = "RC-4", PatientId = patient.Id, Amount = 70, Method = PaymentMethod.Cash, PaidAt = now.AddDays(-45) });
        await app.Db.SaveChangesAsync();

        var report = await app.Get<ImpactReportService>().GetAsync(null, null, None);

        Assert.Equal(new DateOnly(2026, 9, 8), report.Current.From);                       // the last 30 days by default
        Assert.Equal(new DateOnly(2026, 10, 7), report.Current.To);
        Assert.Equal(report.Current.From.AddDays(-1), report.Previous.To);
        Assert.Equal(30, report.Previous.To.DayNumber - report.Previous.From.DayNumber + 1);

        Assert.Equal(4, report.Current.AppointmentsDue);
        Assert.Equal(1, report.Current.NoShows);
        Assert.Equal(25.0, report.Current.NoShowRate);
        Assert.Equal(1, report.Current.FollowUpsContacted);
        Assert.Equal(1, report.Current.PaidAfterContact);
        Assert.Equal(130, report.Current.Collected);                                       // 40 + 100 − 10 refund
        Assert.Equal(40, report.Current.CollectedAfterFollowUp);
        Assert.Equal(1, report.Current.PatientsSeen);
        Assert.Equal(1, report.Current.ReturningPatients);

        Assert.Equal(50.0, report.Previous.NoShowRate);
        Assert.Equal(70, report.Previous.Collected);
        Assert.Equal(0, report.Previous.FollowUpsContacted);
        Assert.Contains(report.Caveats, c => c.Contains("does not prove"));
    }

    // ------------------------------------------------------------------ account page and operator metrics

    [Fact]
    public async Task The_account_page_and_the_operator_metrics_report_state_without_clinic_content()
    {
        using var app = new TestApp();
        app.User.ClinicId = null;
        app.User.UserId = null;
        AuthResult session;
        using (var signUp = app.NewScope())     // its own unit of work, like the real sign-up request
        {
            var invitations = signUp.ServiceProvider.GetRequiredService<InvitationService>();
            var invitation = await invitations.CreateAsync(new CreateInvitationCommand("owner@a.test", "Clinic A", null, SubscriptionPlans.Founding, 30, null, "tester"), None);
            session = await invitations.AcceptAsync(new AcceptInvitationRequest(invitation.Token, "Clinic A", null, "Dr A", "Strong-Pass1", "en", null, true), null, None);
        }
        app.ActAs(session.User.Clinic.Id, session.User.Id);
        await app.AddPatientAsync("Private Person");

        var account = await app.Get<AccountService>().GetAsync(None);
        Assert.Equal(SubscriptionStatus.Trial, account.Status);
        Assert.Equal(SubscriptionPlans.Founding, account.Plan);
        Assert.False(account.ReadOnly);
        Assert.Equal(1, account.Patients);
        Assert.Equal("Pay by CliQ to TAHONA", account.PaymentInstructions);
        Assert.NotNull(account.TrialEndsAt);

        await app.AddClinicAsync("Demo");
        (await app.Db.Clinics.SingleAsync()).IsDemo = true;
        await app.Db.SaveChangesAsync();

        var metrics = await app.Get<ClinicAdminService>().MetricsAsync(None);
        Assert.Equal(1, metrics.Clinics);                                                  // the demo clinic is not counted
        Assert.Equal(1, metrics.ByStatus["Trial"]);
        Assert.Equal(0, metrics.Onboarded);
        Assert.Equal(1, metrics.StaffActive7Days);                                         // the owner signed in when accepting
        Assert.Equal(1, metrics.ClinicsActive7Days);
        Assert.Null(metrics.MedianDaysToOnboard);
        Assert.DoesNotContain("Private Person", System.Text.Json.JsonSerializer.Serialize(metrics));
    }

    // ------------------------------------------------------------------ secrets and per-clinic WhatsApp

    [Fact]
    public void Stored_secrets_are_encrypted_and_unreadable_without_the_right_key()
    {
        AesSecretProtector With(string? key) => new(Options.Create(new SecretsOptions { EncryptionKey = key }));
        var keyA = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());
        var keyB = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray());
        var a = With(keyA);

        var stored = a.Protect("EAAG-token-value");
        Assert.DoesNotContain("EAAG", stored);
        Assert.NotEqual(stored, a.Protect("EAAG-token-value"));                            // a fresh nonce every time
        Assert.Equal("EAAG-token-value", a.Unprotect(stored));

        Assert.Null(With(keyB).Unprotect(stored));                                         // another key
        Assert.Null(a.Unprotect(stored[..^4] + "AAAA"));                                   // tampered
        Assert.Null(a.Unprotect("not-encrypted"));

        Assert.False(With(null).IsAvailable);
        Assert.False(With("too-short").IsAvailable);
        Assert.Throws<InvalidOperationException>(() => With(null).Protect("x"));
    }

    [Fact]
    public async Task A_clinic_connects_its_own_number_after_meta_confirms_it_and_the_token_is_never_exposed()
    {
        using var app = new TestApp();
        var first = await app.AddClinicAsync("First");
        var whatsApp = app.Get<WhatsAppSettingsService>();
        app.WhatsApp.IsConfigured = false;
        Assert.Equal("manual", (await whatsApp.GetAsync(None)).Mode);

        app.WhatsAppVerifier.Error = "Invalid OAuth access token.";
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => whatsApp.ConnectAsync(new ConnectWhatsAppRequest("1055550001", "bad"), None));
        Assert.Contains("Invalid OAuth access token", refused.Message);
        Assert.Empty(await app.Db.ClinicWhatsApps.ToListAsync());

        app.WhatsAppVerifier.Error = null;
        var connected = await whatsApp.ConnectAsync(new ConnectWhatsAppRequest(" 1055550001 ", "EAAG-secret-token"), None);
        Assert.Equal("clinic", connected.Mode);
        Assert.Equal("+962 6 555 1234", connected.DisplayPhoneNumber);
        Assert.DoesNotContain("EAAG", System.Text.Json.JsonSerializer.Serialize(connected));

        var stored = await app.Db.ClinicWhatsApps.SingleAsync();
        Assert.DoesNotContain("EAAG", stored.EncryptedAccessToken);
        Assert.Equal("EAAG-secret-token", app.Get<ISecretProtector>().Unprotect(stored.EncryptedAccessToken));
        Assert.DoesNotContain(await app.Db.AuditLogs.ToListAsync(), a => (a.Changes ?? "").Contains("EAAG") || (a.Changes ?? "").Contains(stored.EncryptedAccessToken));

        // Another clinic cannot claim the same number.
        await app.AddClinicAsync("Second");
        using (var scope = app.NewScope())
        {
            var other = scope.ServiceProvider.GetRequiredService<WhatsAppSettingsService>();
            await Assert.ThrowsAsync<ConflictException>(() => other.ConnectAsync(new ConnectWhatsAppRequest("1055550001", "another-token"), None));
            Assert.Equal("manual", (await other.GetAsync(None)).Mode);
        }

        app.ActAs(first.Id, first.AdminId);
        using (var scope = app.NewScope())
        {
            var mine = scope.ServiceProvider.GetRequiredService<WhatsAppSettingsService>();
            Assert.Equal("manual", (await mine.DisconnectAsync(None)).Mode);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().ClinicWhatsApps.ToListAsync());
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Url, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"messages":[{"id":"wamid.X"}]}""", Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Each_clinic_sends_from_its_own_number_and_one_without_a_number_sends_nothing_automatically()
    {
        using var app = new TestApp();
        var connectedClinic = await app.AddClinicAsync("Connected");
        await app.Get<WhatsAppSettingsService>().ConnectAsync(new ConnectWhatsAppRequest("1055550001", "token-of-connected-clinic"), None);
        var other = await app.AddClinicAsync("Not connected");

        var handler = new RecordingHandler();
        var options = Options.Create(new WhatsAppOptions());
        TenantWhatsAppProvider Provider(IServiceScope scope) => new(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            new MetaWhatsAppClient(new HttpClient(handler), options, NullLogger<MetaWhatsAppClient>.Instance),
            scope.ServiceProvider.GetRequiredService<ISecretProtector>(), options, NullLogger<TenantWhatsAppProvider>.Instance);
        var request = new WhatsAppSendRequest("962791234567", "Hello", "appointment_reminder", "ar", ["Sara"]);

        using (var scope = app.NewScope())                                                  // acting as the clinic without a number
        {
            var provider = Provider(scope);
            Assert.False(provider.IsConfigured);
            var result = await provider.SendAsync(request, None);
            Assert.True(result.RequiresManualSend);
            Assert.False(result.Success);
            Assert.Empty(handler.Requests);                                                 // it never borrows another clinic's number
        }

        app.ActAs(connectedClinic.Id, connectedClinic.AdminId);
        using (var scope = app.NewScope())
        {
            var provider = Provider(scope);
            Assert.True(provider.IsConfigured);
            var result = await provider.SendAsync(request, None);
            Assert.True(result.Success);
            Assert.Equal("wamid.X", result.ProviderMessageId);
            var sent = Assert.Single(handler.Requests);
            Assert.EndsWith("/1055550001/messages", sent.Url);
            Assert.Equal("Bearer token-of-connected-clinic", sent.Authorization);
            var body = JsonNode.Parse(sent.Body)!;
            Assert.Equal("template", body["type"]!.GetValue<string>());
            Assert.Equal("ar", body["template"]!["language"]!["code"]!.GetValue<string>());
            Assert.Equal("Sara", body["template"]!["components"]![0]!["parameters"]![0]!["text"]!.GetValue<string>());
        }
        Assert.NotEqual(connectedClinic.Id, other.Id);
    }

    [Fact]
    public async Task A_reply_goes_to_the_clinic_whose_number_received_it()
    {
        using var app = new TestApp();
        var a = await app.AddClinicAsync("Clinic A");
        var patientA = await app.AddPatientAsync("Shared Phone At A", "0791234567");
        await app.Get<WhatsAppSettingsService>().ConnectAsync(new ConnectWhatsAppRequest("1000000001", "token-a"), None);
        var b = await app.AddClinicAsync("Clinic B");
        using (var scope = app.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Patients.Add(new Patient { FileNumber = "P-1", FullName = "Shared Phone At B", Phone = "0791234567", PhoneNormalized = "791234567", RegisteredAt = app.Clock.UtcNow });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<WhatsAppSettingsService>().ConnectAsync(new ConnectWhatsAppRequest("1000000002", "token-b"), None);
            // Clinic B wrote to this number most recently.
            await scope.ServiceProvider.GetRequiredService<MessagingService>().SendAsync(
                new SendMessageRequest((await db.Patients.SingleAsync()).Id, MessageChannel.WhatsApp, null, MessageTemplateType.PaymentReminder, null, null, null, 10, null, null), None);
        }

        app.User.ClinicId = null;
        app.User.UserId = null;
        using (var scope = app.NewScope())
        {
            var webhook = scope.ServiceProvider.GetRequiredService<MessagingService>();
            // The patient writes to clinic A's number, without A having written first.
            Assert.Equal(InboundResult.Stored, await webhook.ReceiveInboundAsync("wamid.to-A", "962791234567", "Hello", app.Clock.UtcNow, None, "1000000001"));
        }
        using (var scope = app.NewScope())
        {
            // A number nobody connected, and no earlier conversation: dropped.
            Assert.Equal(InboundResult.Unmatched,
                await scope.ServiceProvider.GetRequiredService<MessagingService>().ReceiveInboundAsync("wamid.unknown", "962790000000", "Hi", app.Clock.UtcNow, None, "9999999999"));
        }

        app.ActAs(a.Id, a.AdminId);
        using (var scope = app.NewScope())
        {
            var inbound = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Messages.SingleAsync(m => m.Direction == MessageDirection.Inbound);
            Assert.Equal(patientA.Id, inbound.PatientId);
        }
        app.ActAs(b.Id, b.AdminId);
        using (var scope = app.NewScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Messages.Where(m => m.Direction == MessageDirection.Inbound).ToListAsync());
    }

    [Fact]
    public void The_webhook_reports_which_business_number_received_a_message()
    {
        var body = Encoding.UTF8.GetBytes("""
            {"entry":[{"changes":[{"value":{"metadata":{"display_phone_number":"96265551234","phone_number_id":"1000000001"},
              "messages":[{"id":"wamid.M1","from":"962791234567","timestamp":"1791360000","type":"text","text":{"body":"Hi"}}]}}]}]}
            """);
        var inbound = Assert.IsType<WhatsAppWebhook.InboundEvent>(Assert.Single(WhatsAppWebhook.Parse(body)));
        Assert.Equal("1000000001", inbound.PhoneNumberId);
    }

    // ------------------------------------------------------------------ e-mail

    [Fact]
    public async Task Invitations_are_emailed_when_mail_is_configured_and_otherwise_handed_to_the_operator()
    {
        using var app = new TestApp();
        app.User.ClinicId = null;
        app.User.UserId = null;
        var invitations = app.Get<InvitationService>();

        var manual = await invitations.CreateAsync(new CreateInvitationCommand("a@clinic.test", "Clinic A", null, SubscriptionPlans.Founding, 30, null, "tester"), None);
        Assert.False(manual.EmailSent);
        Assert.Empty(app.Email.Sent);

        app.Email.IsConfigured = true;
        var mailed = await invitations.CreateAsync(new CreateInvitationCommand("b@clinic.test", "Clinic B", null, SubscriptionPlans.Founding, 30, null, "tester"), None);
        Assert.True(mailed.EmailSent);
        var message = Assert.Single(app.Email.Sent);
        Assert.Equal("b@clinic.test", message.To);
        Assert.Contains($"https://app.example.test/welcome?token={mailed.Token}", message.TextBody);
        Assert.Contains("Clinic B", message.TextBody);
        Assert.Contains("فترة تجريبية", message.TextBody);                                  // Arabic and English in one message

        // A mail server that fails does not lose the invitation: the operator still has the link.
        app.Email.Fail = true;
        var failed = await invitations.CreateAsync(new CreateInvitationCommand("c@clinic.test", null, null, SubscriptionPlans.Founding, 30, null, "tester"), None);
        Assert.False(failed.EmailSent);
        Assert.Equal("c@clinic.test", (await invitations.GetAsync(failed.Token, None)).Email);
    }

    [Fact]
    public async Task Forgot_password_mails_a_working_link_and_never_reveals_whether_an_address_exists()
    {
        using var app = new TestApp();
        app.User.ClinicId = null;
        app.User.UserId = null;
        var invitation = await app.Get<InvitationService>().CreateAsync(new CreateInvitationCommand("owner@a.test", "Clinic A", null, SubscriptionPlans.Founding, 30, null, "tester"), None);
        await app.Get<InvitationService>().AcceptAsync(new AcceptInvitationRequest(invitation.Token, "Clinic A", null, "Dr A", "Strong-Pass1", "en", null, false), null, None);
        app.User.ClinicId = null;
        app.User.UserId = null;
        var recovery = app.Get<AccountRecoveryService>();

        // Without a mail server the request does nothing, quietly.
        Assert.False(recovery.SelfServiceAvailable);
        await recovery.RequestByEmailAsync("owner@a.test", None);
        Assert.Empty(app.Email.Sent);

        app.Email.IsConfigured = true;
        Assert.True(recovery.SelfServiceAvailable);
        await recovery.RequestByEmailAsync("nobody@nowhere.test", None);                    // unknown address: same silence
        await recovery.RequestByEmailAsync("", None);
        Assert.Empty(app.Email.Sent);

        await recovery.RequestByEmailAsync(" Owner@A.test ", None);
        await recovery.RequestByEmailAsync("owner@a.test", None);                           // repeated at once: ignored
        var mail = Assert.Single(app.Email.Sent);
        Assert.Equal("owner@a.test", mail.To);
        var token = mail.TextBody.Split("reset-password?token=")[1].Split('\n')[0].Trim();

        await recovery.ResetAsync(new ResetPasswordWithTokenRequest(token, "Brand-New-Pass2"), None);
        await app.Get<AuthService>().LoginAsync(new LoginRequest("owner@a.test", "Brand-New-Pass2"), null, None);

        // After the cooldown a new link can be requested.
        app.Clock.Advance(TimeSpan.FromMinutes(3));
        await recovery.RequestByEmailAsync("owner@a.test", None);
        Assert.Equal(2, app.Email.Sent.Count);
    }
}
