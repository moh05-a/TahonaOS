using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Tahona.Api.Infrastructure;
using Tahona.Application.Common;
using Tahona.Application.Features.Auth;
using Tahona.Application.Features.Patients;
using Tahona.Application.Features.Provisioning;
using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;
using Tahona.Infrastructure.Persistence;

namespace Tahona.Tests;

/// <summary>Creating a clinic without touching the database by hand, account recovery, onboarding and the subscription policy.</summary>
public class ProvisioningTests
{
    private const string Password = "Strong-Pass1";

    private static AcceptInvitationRequest Accept(string token, string clinic = "Al-Noor Dental") =>
        new(token, clinic, "عيادة النور", "Dr. Huda Salem", Password, "ar", "0791112233", true);

    private static async Task<IssuedInvitation> InviteAsync(TestApp app, string email = "owner@alnoor.test") =>
        await app.Get<InvitationService>().CreateAsync(new CreateInvitationCommand(email, "Al-Noor Dental", "Huda", SubscriptionPlans.Founding, 30, null, "tester"),
            CancellationToken.None);

    /// <summary>Public endpoints run without a signed-in user.</summary>
    private static void Anonymous(TestApp app)
    {
        app.User.ClinicId = null;
        app.User.UserId = null;
    }

    [Fact]
    public async Task Accepting_an_invitation_creates_a_complete_clinic_and_signs_the_owner_in()
    {
        using var app = new TestApp();
        Anonymous(app);
        var invitation = await InviteAsync(app);

        var info = await app.Get<InvitationService>().GetAsync(invitation.Token, CancellationToken.None);
        Assert.Equal("owner@alnoor.test", info.Email);

        var session = await app.Get<InvitationService>().AcceptAsync(Accept(invitation.Token), "10.0.0.1", CancellationToken.None);

        Assert.Equal("owner@alnoor.test", session.User.Email);
        Assert.Equal(SystemRoles.Admin, session.User.Role);
        Assert.Equal(SubscriptionStatus.Trial, session.User.Clinic.SubscriptionStatus);
        Assert.False(session.User.Clinic.ReadOnly);
        Assert.NotNull(session.User.DentistId);
        Assert.NotEmpty(session.AccessToken);

        // Everything a clinic needs on day one exists, and belongs to the new clinic only.
        app.ActAs(session.User.Clinic.Id, session.User.Id);
        using var scope = app.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.Roles.CountAsync());
        Assert.Single(await db.Users.ToListAsync());
        Assert.Single(await db.ClinicSettings.ToListAsync());
        Assert.True(await db.Procedures.CountAsync() > 20);
        Assert.Equal(12, await db.MessageTemplates.CountAsync());
        Assert.Equal(8, await db.FollowUpRules.CountAsync());
        Assert.Empty(await db.Patients.ToListAsync());
        var clinic = await db.Clinics.SingleAsync();
        Assert.Equal("Al-Noor Dental", clinic.Name);
        Assert.False(clinic.IsDemo);
        Assert.StartsWith("al-noor-dental-", clinic.Slug);
        Assert.NotNull(clinic.TrialEndsAt);

        // The owner can sign in again with the password they chose.
        Anonymous(app);
        var again = await app.Get<AuthService>().LoginAsync(new LoginRequest("owner@alnoor.test", Password), null, CancellationToken.None);
        Assert.Equal(clinic.Id, again.User.Clinic.Id);
    }

    [Fact]
    public async Task An_invitation_works_once_and_not_after_it_expires_or_is_revoked()
    {
        using var app = new TestApp();
        Anonymous(app);
        var invitations = app.Get<InvitationService>();

        var used = await InviteAsync(app, "a@clinic.test");
        await invitations.AcceptAsync(Accept(used.Token), null, CancellationToken.None);
        Anonymous(app);
        await Assert.ThrowsAsync<BusinessRuleException>(() => invitations.AcceptAsync(Accept(used.Token, "Second Clinic"), null, CancellationToken.None));

        var expired = await InviteAsync(app, "b@clinic.test");
        app.Clock.Advance(TimeSpan.FromDays(15));
        await Assert.ThrowsAsync<BusinessRuleException>(() => invitations.GetAsync(expired.Token, CancellationToken.None));
        await Assert.ThrowsAsync<BusinessRuleException>(() => invitations.AcceptAsync(Accept(expired.Token), null, CancellationToken.None));

        var revoked = await InviteAsync(app, "c@clinic.test");
        await invitations.RevokeAsync(revoked.Id, CancellationToken.None);
        await Assert.ThrowsAsync<BusinessRuleException>(() => invitations.AcceptAsync(Accept(revoked.Token), null, CancellationToken.None));

        // A newer invitation for the same address cancels the older link.
        var first = await InviteAsync(app, "d@clinic.test");
        var second = await InviteAsync(app, "d@clinic.test");
        await Assert.ThrowsAsync<BusinessRuleException>(() => invitations.GetAsync(first.Token, CancellationToken.None));
        Assert.Equal("d@clinic.test", (await invitations.GetAsync(second.Token, CancellationToken.None)).Email);

        await Assert.ThrowsAsync<BusinessRuleException>(() => invitations.GetAsync("not-a-token", CancellationToken.None));
        Assert.Single(await app.Db.Clinics.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task An_email_address_cannot_be_invited_or_registered_twice()
    {
        using var app = new TestApp();
        Anonymous(app);
        var invitation = await InviteAsync(app);
        await app.Get<InvitationService>().AcceptAsync(Accept(invitation.Token), null, CancellationToken.None);
        Anonymous(app);

        await Assert.ThrowsAsync<ConflictException>(() => InviteAsync(app));
        await Assert.ThrowsAsync<ConflictException>(() => app.Get<ClinicProvisioningService>().ProvisionAsync(
            new ProvisionClinicCommand("Copy", null, "OWNER@alnoor.test", "X", Password, "en", null, SubscriptionPlans.Founding, 30, false), CancellationToken.None));
    }

    [Fact]
    public async Task Only_the_token_hash_is_stored_and_weak_passwords_are_rejected()
    {
        using var app = new TestApp();
        Anonymous(app);
        var invitation = await InviteAsync(app);

        var stored = await app.Db.ClinicInvitations.SingleAsync();
        Assert.NotEqual(invitation.Token, stored.TokenHash);
        Assert.Equal(SecureToken.Hash(invitation.Token), stored.TokenHash);

        var validator = new AcceptInvitationValidator();
        Assert.False(validator.Validate(Accept(invitation.Token) with { Password = "short1A" }).IsValid);
        Assert.False(validator.Validate(Accept(invitation.Token) with { Password = "alllowercase1" }).IsValid);
        Assert.False(validator.Validate(Accept(invitation.Token) with { ClinicName = "" }).IsValid);
        Assert.True(validator.Validate(Accept(invitation.Token)).IsValid);
    }

    [Fact]
    public async Task A_new_clinic_sees_nothing_of_an_existing_one()
    {
        using var app = new TestApp();
        await app.AddClinicAsync("Existing");
        var secret = await app.AddPatientAsync("Existing Patient");

        Anonymous(app);
        var invitation = await InviteAsync(app);
        var session = await app.Get<InvitationService>().AcceptAsync(Accept(invitation.Token), null, CancellationToken.None);

        app.ActAs(session.User.Clinic.Id, session.User.Id);
        using var scope = app.NewScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Patients.ToListAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => scope.ServiceProvider.GetRequiredService<PatientService>().GetAsync(secret.Id, CancellationToken.None));
    }

    // ------------------------------------------------------------------ password recovery

    [Fact]
    public async Task A_reset_link_sets_a_new_password_once_and_signs_out_other_sessions()
    {
        using var app = new TestApp();
        Anonymous(app);
        var invitation = await InviteAsync(app);
        var session = await app.Get<InvitationService>().AcceptAsync(Accept(invitation.Token), null, CancellationToken.None);
        Anonymous(app);
        var recovery = app.Get<AccountRecoveryService>();
        var auth = app.Get<AuthService>();

        var first = await recovery.IssueAsync("owner@alnoor.test", "Operator: tester", CancellationToken.None);
        var second = await recovery.IssueAsync("owner@alnoor.test", "Operator: tester", CancellationToken.None);
        // Issuing a new link cancels the earlier one.
        await Assert.ThrowsAsync<BusinessRuleException>(() => recovery.ResetAsync(new ResetPasswordWithTokenRequest(first.Token, "Another-Pass2"), CancellationToken.None));

        await recovery.ResetAsync(new ResetPasswordWithTokenRequest(second.Token, "Another-Pass2"), CancellationToken.None);
        await Assert.ThrowsAsync<BusinessRuleException>(() => recovery.ResetAsync(new ResetPasswordWithTokenRequest(second.Token, "Third-Pass3"), CancellationToken.None));

        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.LoginAsync(new LoginRequest("owner@alnoor.test", Password), null, CancellationToken.None));
        await auth.LoginAsync(new LoginRequest("owner@alnoor.test", "Another-Pass2"), null, CancellationToken.None);
        // The session opened before the reset no longer refreshes.
        await Assert.ThrowsAsync<UnauthorizedException>(() => auth.RefreshAsync(session.RefreshToken, null, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(() => recovery.IssueAsync("nobody@nowhere.test", "tester", CancellationToken.None));
    }

    [Fact]
    public async Task A_reset_link_expires()
    {
        using var app = new TestApp();
        Anonymous(app);
        var invitation = await InviteAsync(app);
        await app.Get<InvitationService>().AcceptAsync(Accept(invitation.Token), null, CancellationToken.None);
        Anonymous(app);
        var recovery = app.Get<AccountRecoveryService>();

        var issued = await recovery.IssueAsync("owner@alnoor.test", "tester", CancellationToken.None);
        app.Clock.Advance(TimeSpan.FromHours(25));
        await Assert.ThrowsAsync<BusinessRuleException>(() => recovery.ResetAsync(new ResetPasswordWithTokenRequest(issued.Token, "Another-Pass2"), CancellationToken.None));
    }

    // ------------------------------------------------------------------ onboarding checklist

    [Fact]
    public async Task The_checklist_follows_the_clinic_and_cannot_be_completed_early()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var onboarding = app.Get<OnboardingService>();

        var start = await onboarding.GetAsync(CancellationToken.None);
        Assert.False(start.Completed);
        Assert.False(start.Steps.Single(s => s.Key == OnboardingService.Steps.ClinicProfile).Done);
        Assert.False(start.Steps.Single(s => s.Key == OnboardingService.Steps.GoLive).CanConfirm);
        await Assert.ThrowsAsync<BusinessRuleException>(() => onboarding.CompleteAsync(CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() => onboarding.SetConfirmedAsync(OnboardingService.Steps.TestInvoice, true, CancellationToken.None));

        // Observable steps complete themselves as the clinic works.
        var entity = await app.Db.Clinics.SingleAsync();
        entity.Phone = "065551234";
        entity.Address = "Abdoun";
        var patient = await app.AddPatientAsync("Sara");
        app.Db.Appointments.Add(new Appointment { PatientId = patient.Id, DentistId = clinic.DentistId, StartTime = app.Clock.UtcNow.AddDays(1),
            EndTime = app.Clock.UtcNow.AddDays(1).AddMinutes(30), Treatment = "Check-up" });
        app.Db.Invoices.Add(new Invoice { InvoiceNumber = "INV-1", PatientId = patient.Id, IssueDate = app.Clock.UtcNow,
            DueDate = app.Clock.UtcNow, Total = 20, Balance = 20 });
        await app.Db.SaveChangesAsync();
        foreach (var key in OnboardingService.Steps.Manual) await onboarding.SetConfirmedAsync(key, true, CancellationToken.None);

        var ready = await onboarding.GetAsync(CancellationToken.None);
        Assert.All(ready.Steps.Where(s => s.Key != OnboardingService.Steps.GoLive), s => Assert.True(s.Done, s.Key));
        Assert.Equal(1, ready.Figures.Patients);
        Assert.Equal(20, ready.Figures.OutstandingBalance);

        await onboarding.CompleteAsync(CancellationToken.None);
        Assert.True((await onboarding.GetAsync(CancellationToken.None)).Completed);

        // A confirmation can be withdrawn, and the checklist reopened to import more later.
        await onboarding.SetConfirmedAsync(OnboardingService.Steps.Messaging, false, CancellationToken.None);
        await onboarding.ReopenAsync(CancellationToken.None);
        var reopened = await onboarding.GetAsync(CancellationToken.None);
        Assert.False(reopened.Completed);
        Assert.False(reopened.Steps.Single(s => s.Key == OnboardingService.Steps.Messaging).Done);
    }

    // ------------------------------------------------------------------ subscription policy

    [Theory]
    [InlineData(SubscriptionStatus.Trial, false)]
    [InlineData(SubscriptionStatus.Active, false)]
    [InlineData(SubscriptionStatus.PastDue, false)]
    [InlineData(SubscriptionStatus.Cancelled, true)]
    [InlineData(SubscriptionStatus.Suspended, true)]
    public void Only_cancelled_and_suspended_clinics_are_read_only(SubscriptionStatus status, bool readOnly) =>
        Assert.Equal(readOnly, ClinicAccess.IsReadOnly(status));

    private static async Task<int> RequestAsync(TestApp app, string method, string path)
    {
        var reached = false;
        var middleware = new ClinicAccessMiddleware(_ => { reached = true; return Task.CompletedTask; }, new MemoryCache(new MemoryCacheOptions()));
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        using var scope = app.NewScope();
        await middleware.InvokeAsync(context, app.User, scope.ServiceProvider.GetRequiredService<AppDbContext>());
        return reached ? 200 : context.Response.StatusCode;
    }

    [Fact]
    public async Task A_suspended_clinic_can_read_and_export_but_not_change_records()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var admin = app.Get<ClinicAdminService>();
        var slug = (await app.Db.Clinics.SingleAsync()).Slug;

        Assert.Equal(200, await RequestAsync(app, "POST", "/api/patients"));

        var overview = await admin.SetStatusAsync(new SetClinicStatusCommand(slug, SubscriptionStatus.Suspended, "Invoice 3 unpaid", null, "tester"), CancellationToken.None);
        Assert.Equal(SubscriptionStatus.Suspended, overview.Status);

        Assert.Equal(403, await RequestAsync(app, "POST", "/api/patients"));
        Assert.Equal(403, await RequestAsync(app, "DELETE", $"/api/patients/{Guid.NewGuid()}"));
        Assert.Equal(200, await RequestAsync(app, "GET", "/api/patients"));
        Assert.Equal(200, await RequestAsync(app, "POST", "/api/export/full"));       // taking the data out stays possible
        Assert.Equal(200, await RequestAsync(app, "POST", "/api/auth/logout"));

        // The operator's action is recorded in the clinic's own audit log.
        var audit = await app.Db.AuditLogs.SingleAsync(a => a.Action == "SubscriptionStatusChanged");
        Assert.Equal("Operator: tester", audit.UserName);
        Assert.Contains("Suspended", audit.Changes);

        await admin.SetStatusAsync(new SetClinicStatusCommand(clinic.Id.ToString(), SubscriptionStatus.Active, null, app.Clock.UtcNow.AddMonths(1), "tester"), CancellationToken.None);
        Assert.Equal(200, await RequestAsync(app, "POST", "/api/patients"));

        // Archiving signs the clinic out altogether.
        await admin.SetArchivedAsync(slug, true, "tester", "Closed", CancellationToken.None);
        Assert.Equal(401, await RequestAsync(app, "POST", "/api/patients"));
        app.User.ClinicId = null;
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Get<AuthService>().LoginAsync(new LoginRequest("x@y.z", "whatever"), null, CancellationToken.None));
    }

    [Fact]
    public async Task The_operator_overview_shows_counts_but_no_patient_details()
    {
        using var app = new TestApp();
        await app.AddClinicAsync("Clinic A");
        await app.AddPatientAsync("Private Person");
        await app.AddClinicAsync("Clinic B");

        var overview = await app.Get<ClinicAdminService>().ListAsync(CancellationToken.None);

        Assert.Equal(2, overview.Count);
        Assert.Equal(1, overview.Single(c => c.Name == "Clinic A").Patients);
        Assert.Equal(0, overview.Single(c => c.Name == "Clinic B").Patients);
        Assert.DoesNotContain("Private Person", System.Text.Json.JsonSerializer.Serialize(overview));
    }

    [Fact]
    public async Task Administrators_receive_permissions_added_after_their_clinic_was_created()
    {
        using var app = new TestApp();
        var clinic = await app.AddClinicAsync();
        var hasher = app.Get<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
        var adminRole = await app.Db.Roles.SingleAsync();
        adminRole.Permissions = [Permissions.DashboardView, Permissions.PatientsView];     // as stored before newer permissions existed
        var admin = await app.Db.Users.SingleAsync();
        admin.PasswordHash = hasher.HashPassword(admin, Password);
        var limitedRole = new Role { Name = "Front desk", Permissions = [Permissions.PatientsView] };
        var limited = new User { Email = "desk@test.local", FullName = "Desk", Role = limitedRole };
        limited.PasswordHash = hasher.HashPassword(limited, Password);
        app.Db.Users.Add(limited);
        await app.Db.SaveChangesAsync();

        Anonymous(app);
        var auth = app.Get<AuthService>();
        var asAdmin = await auth.LoginAsync(new LoginRequest(admin.Email, Password), null, CancellationToken.None);
        Assert.Contains(Permissions.DataImport, asAdmin.User.Permissions);
        Assert.Contains(Permissions.DataExport, asAdmin.User.Permissions);
        Assert.Equal(Permissions.All.Count, asAdmin.User.Permissions.Count);

        // Any other role has exactly what it was given.
        var asDesk = await auth.LoginAsync(new LoginRequest("desk@test.local", Password), null, CancellationToken.None);
        Assert.Equal([Permissions.PatientsView], asDesk.User.Permissions);
        Assert.Equal(clinic.Id, asDesk.User.Clinic.Id);
    }
}
