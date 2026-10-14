using System.Globalization;
using Tahona.Application.Common;
using Tahona.Application.Features.Auth;
using Tahona.Application.Features.Provisioning;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Api.Operator;

/// <summary>
/// Operator commands, run on the server with the same configuration as the API:
/// <c>dotnet Tahona.Api.dll operator &lt;command&gt; [--option value]</c>.
/// There is deliberately no HTTP surface for these: whoever can run them already controls the deployment.
/// They work on account data only and never print patient records.
/// </summary>
public static class OperatorCli
{
    public const string Verb = "operator";

    public static bool IsOperatorCommand(string[] args) => args.Length > 0 && args[0].Equals(Verb, StringComparison.OrdinalIgnoreCase);

    private const string Help = """
        TahonaOS operator commands

          operator invite --email <address> [--clinic <name>] [--contact <name>] [--plan Founding|Standard]
                          [--trial-days 30] [--valid-days 14] [--note <text>]
              Issues a sign-up invitation and prints the link to send to the clinic owner.

          operator invitations
              Lists invitations and their state.

          operator revoke-invitation --id <invitation id>

          operator clinics
              Lists clinic accounts with subscription state and activity counts.

          operator metrics
              Business figures across clinics: activation, onboarding, weekly activity, churn.

          operator set-status --clinic <slug or id> --status Trial|Active|PastDue|Cancelled|Suspended
                              [--reason <text>] [--paid-until yyyy-MM-dd]
              Changes a clinic's subscription state. Cancelled and Suspended make the clinic read-only (view and export).

          operator archive --clinic <slug or id> [--reason <text>]     Signs the clinic out; data is kept.
          operator restore --clinic <slug or id>

          operator reset-link --email <address>
              Issues a single-use password reset link (valid 24 hours). Confirm the person's identity first.

        Options for every command: --operator <your name>  (recorded with the action; defaults to OPERATOR_NAME or the OS user)
        Links use APP__PUBLICURL as the address of the web app.
        """;

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var command = args.Length > 1 ? args[1].ToLowerInvariant() : "help";
        var options = ParseOptions(args.Skip(2).ToArray());
        var @operator = options.GetValueOrDefault("operator") ?? Environment.GetEnvironmentVariable("OPERATOR_NAME") ?? Environment.UserName;
        var baseUrl = (app.Configuration["App:PublicUrl"] ?? "http://localhost:5173").TrimEnd('/');

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var ct = CancellationToken.None;

        try
        {
            switch (command)
            {
                case "invite":
                {
                    var issued = await services.GetRequiredService<InvitationService>().CreateAsync(new CreateInvitationCommand(
                        Required(options, "email"), options.GetValueOrDefault("clinic"), options.GetValueOrDefault("contact"),
                        options.GetValueOrDefault("plan") ?? SubscriptionPlans.Founding, Int(options, "trial-days", 30),
                        options.GetValueOrDefault("note"), @operator, Int(options, "valid-days", 14)), ct);
                    Console.WriteLine($"Invitation for {issued.Email} (expires {issued.ExpiresAt:yyyy-MM-dd HH:mm} UTC).");
                    Console.WriteLine(issued.EmailSent
                        ? "The link was e-mailed to that address. It is also shown here, once:"
                        : "Send this link to the clinic owner. It is shown only once:");
                    Console.WriteLine($"  {baseUrl}/welcome?token={issued.Token}");
                    return 0;
                }
                case "invitations":
                {
                    var rows = await services.GetRequiredService<InvitationService>().ListAsync(ct);
                    Console.WriteLine($"{"State",-9} {"E-mail",-34} {"Clinic",-28} {"Plan",-9} {"Created",-11} {"Expires",-11} Id");
                    foreach (var i in rows)
                        Console.WriteLine($"{i.State,-9} {Cut(i.Email, 34),-34} {Cut(i.ClinicName ?? "—", 28),-28} {i.Plan,-9} {i.CreatedAt:yyyy-MM-dd}  {i.ExpiresAt:yyyy-MM-dd}  {i.Id}");
                    if (rows.Count == 0) Console.WriteLine("(no invitations)");
                    return 0;
                }
                case "revoke-invitation":
                    await services.GetRequiredService<InvitationService>().RevokeAsync(Guid.Parse(Required(options, "id")), ct);
                    Console.WriteLine("Invitation revoked.");
                    return 0;
                case "clinics":
                {
                    var rows = await services.GetRequiredService<ClinicAdminService>().ListAsync(ct);
                    Console.WriteLine($"{"Slug",-30} {"Status",-10} {"Plan",-9} {"Users",5} {"Patients",8} {"Appts 30d",9} {"Last login",-11} {"Onboarded",-10} Name");
                    foreach (var c in rows)
                        Console.WriteLine($"{Cut(c.Slug, 30),-30} {(c.IsActive ? c.Status.ToString() : "Archived"),-10} {c.Plan,-9} {c.ActiveUsers,5} {c.Patients,8} " +
                                          $"{c.AppointmentsLast30Days,9} {Date(c.LastLoginAt),-11} {Date(c.OnboardingCompletedAt),-10} {c.Name}{(c.IsDemo ? " (demo)" : "")}");
                    if (rows.Count == 0) Console.WriteLine("(no clinics)");
                    return 0;
                }
                case "metrics":
                {
                    var m = await services.GetRequiredService<ClinicAdminService>().MetricsAsync(ct);
                    Console.WriteLine($"Clinics (demo clinics excluded): {m.Clinics}");
                    foreach (var (status, count) in m.ByStatus) Console.WriteLine($"  {status,-10} {count}");
                    Console.WriteLine($"Finished onboarding:        {m.Onboarded} of {m.Clinics} ({m.OnboardedPercent:0}%)");
                    Console.WriteLine($"Median days to onboard:     {(m.MedianDaysToOnboard is null ? "—" : m.MedianDaysToOnboard.Value.ToString("0.0"))}");
                    Console.WriteLine($"Clinics active in 7 days:   {m.ClinicsActive7Days}");
                    Console.WriteLine($"Staff signed in, 7 days:    {m.StaffActive7Days}");
                    Console.WriteLine($"Appointments booked, 7 d:   {m.AppointmentsBooked7Days}");
                    Console.WriteLine($"Follow-up contacts, 7 d:    {m.FollowUpContacts7Days}");
                    Console.WriteLine($"Imports applied, 30 d:      {m.Imports30Days}");
                    Console.WriteLine($"Cancelled or suspended:     {m.Churned}");
                    return 0;
                }
                case "set-status":
                {
                    if (!Enum.TryParse<SubscriptionStatus>(Required(options, "status"), true, out var status))
                        throw new ArgumentException("Status must be Trial, Active, PastDue, Cancelled or Suspended.");
                    DateTime? paidUntil = options.TryGetValue("paid-until", out var p)
                        ? DateTime.SpecifyKind(DateTime.ParseExact(p, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Utc) : null;
                    var c = await services.GetRequiredService<ClinicAdminService>().SetStatusAsync(
                        new SetClinicStatusCommand(Required(options, "clinic"), status, options.GetValueOrDefault("reason"), paidUntil, @operator), ct);
                    Console.WriteLine($"{c.Name} is now {c.Status}{(c.PaidUntil is null ? "" : $", paid until {c.PaidUntil:yyyy-MM-dd}")}.");
                    if (ClinicAccess.IsReadOnly(c.Status)) Console.WriteLine("The clinic is read-only: staff can view and export records but not change them.");
                    return 0;
                }
                case "archive":
                case "restore":
                    await services.GetRequiredService<ClinicAdminService>().SetArchivedAsync(Required(options, "clinic"), command == "archive", @operator,
                        options.GetValueOrDefault("reason"), ct);
                    Console.WriteLine(command == "archive" ? "Clinic archived: nobody can sign in. Data is kept." : "Clinic restored.");
                    return 0;
                case "reset-link":
                {
                    var issued = await services.GetRequiredService<AccountRecoveryService>().IssueAsync(Required(options, "email"), $"Operator: {@operator}", ct);
                    Console.WriteLine($"Password reset for {issued.FullName} <{issued.Email}> (expires {issued.ExpiresAt:yyyy-MM-dd HH:mm} UTC).");
                    Console.WriteLine("Send this link to that person only. It is shown once and works once:");
                    Console.WriteLine($"  {baseUrl}/reset-password?token={issued.Token}");
                    return 0;
                }
                default:
                    Console.WriteLine(Help);
                    return command is "help" or "--help" or "-h" ? 0 : 2;
            }
        }
        catch (Exception ex) when (ex is AppException or ArgumentException or FormatException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument '{args[i]}'. Options look like --name value.");
            var name = args[i][2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new ArgumentException($"Option --{name} needs a value.");
            options[name] = args[++i];
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Missing --{name}.");

    private static int Int(Dictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";
}
