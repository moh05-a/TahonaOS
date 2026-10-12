using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Ai;

public record AssistantMessageDto(string Role, string Content);

public record AssistantRequest(string Question, IReadOnlyList<AssistantMessageDto>? History);

public record AssistantAnswerDto(string Answer, string Source, IReadOnlyList<string> ToolsUsed, IReadOnlyList<DataTableDto> Data, bool Refused);

public record AssistantStatusDto(bool LlmConfigured, string Mode, string? Provider, string? Model, IReadOnlyList<string> SuggestedQuestions);

public class AssistantRequestValidator : AbstractValidator<AssistantRequest>
{
    public AssistantRequestValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.History).Must(h => h is null || h.Count <= 20).WithMessage("Conversation too long; start a new one.");
        RuleForEach(x => x.History).ChildRules(m =>
        {
            m.RuleFor(x => x.Role).Must(r => r is "user" or "assistant");
            m.RuleFor(x => x.Content).NotEmpty().MaximumLength(8000);
        });
    }
}

/// <summary>
/// AI Revenue Assistant. With an API key, the configured LLM (Gemini or Claude) answers using only the read-only <see cref="AssistantTools"/>;
/// without one, a deterministic intent router answers the common questions from the same tools.
/// Either way every figure shown is pulled from the clinic database — the data tables are returned alongside.
/// </summary>
public partial class AssistantService(IAppDbContext db, IAiProvider ai, AssistantTools tools, IClinicTime clinicTime)
{
    public static readonly IReadOnlyList<string> SuggestedQuestions =
    [
        "Which patients haven't completed their treatment?",
        "Who owes us the most money?",
        "Which patients have high-value treatment plans that haven't started?",
        "How much potential revenue are we currently losing?",
        "Which patients missed appointments this week?",
        "Show me patients who haven't returned in 6 months",
        "How much revenue did we recover this month?"
    ];

    public AssistantStatusDto Status() => new(ai.IsConfigured, ai.IsConfigured ? "llm" : "built-in",
        ai.IsConfigured ? ai.ProviderName : null, ai.IsConfigured ? ai.ModelName : null, SuggestedQuestions);

    public async Task<AssistantAnswerDto> AskAsync(AssistantRequest request, CancellationToken ct)
    {
        var enabled = await db.ClinicSettings.AsNoTracking().Select(s => s.AiAssistantEnabled).FirstAsync(ct);
        if (!enabled) throw new BusinessRuleException("The AI assistant is disabled in clinic settings.");

        return ai.IsConfigured ? await AskLlmAsync(request, ct) : await AskBuiltInAsync(request.Question, ct);
    }

    // ================================================================== LLM

    private async Task<AssistantAnswerDto> AskLlmAsync(AssistantRequest request, CancellationToken ct)
    {
        var clinic = await db.Clinics.AsNoTracking().Select(c => new { c.Name, c.Currency }).FirstAsync(ct);
        var today = clinicTime.Today;
        var tables = new List<DataTableDto>();

        var aiTools = AssistantTools.Definitions.Select(d => new AiTool(d.Name, d.Description, JsonDocument.Parse(d.SchemaJson).RootElement,
            async (input, token) =>
            {
                var output = await tools.ExecuteAsync(d.Name, input, token);
                if (output.Table is not null) tables.Add(output.Table);
                return AssistantTools.Serialize(output.ModelPayload);
            })).ToList();

        var system = $"""
            You are the Revenue Assistant inside {clinic.Name}'s dental clinic management system. You help clinic staff
            (owners, receptionists, accountants, dentists) understand their operational and financial data.

            Today is {today:dddd yyyy-MM-dd} (clinic local time). Currency: {clinic.Currency}. Weeks run Saturday to Friday.

            Rules:
            - Answer ONLY from the results of your tools. Never invent, estimate or guess patient names, phone numbers,
              amounts, dates or counts. If the tools do not contain the answer, say what you could not find.
            - Call tools before answering any question about clinic data; use several tools when a question needs it.
            - Quote amounts with the currency and patient names exactly as returned.
            - Be concise and practical: lead with the direct answer, then a short list of the most relevant patients
              or figures (at most 10), then one suggested next action when useful. Use Markdown.
            - The full data tables are shown to the user next to your answer, so do not reproduce large tables.
            - You do not give clinical diagnoses or treatment advice; for clinical questions refer to the treating dentist.
            - Reply in the language of the question (English or Arabic).
            """;

        var conversation = (request.History ?? [])
            .TakeLast(10)
            .Select(m => new AiChatTurn(m.Role == "user", m.Content))
            .Append(new AiChatTurn(true, request.Question.Trim()))
            .ToList();
        // The API requires the conversation to start with a user turn.
        while (conversation.Count > 0 && !conversation[0].IsUser) conversation.RemoveAt(0);

        var result = await ai.RunWithToolsAsync(system, conversation, aiTools, 8, ct);
        return new AssistantAnswerDto(
            string.IsNullOrWhiteSpace(result.Text) ? "I couldn't find an answer in the clinic data." : result.Text,
            "llm", result.ToolCalls.Select(t => t.Name).Distinct().ToList(), Dedupe(tables), result.Refused);
    }

    private static List<DataTableDto> Dedupe(List<DataTableDto> tables) =>
        tables.GroupBy(t => t.Title).Select(g => g.Last()).ToList();

    // ================================================================== built-in (no LLM)

    [GeneratedRegex(@"(\d{1,2})\s*(month|months|شهر|أشهر|شهور)", RegexOptions.IgnoreCase)]
    private static partial Regex MonthsPattern();

    [GeneratedRegex(@"\d{6,}|(?<!\S)\+?\d[\d\s-]{6,}")]
    private static partial Regex PhonePattern();

    private async Task<AssistantAnswerDto> AskBuiltInAsync(string question, CancellationToken ct)
    {
        var q = question.Trim().ToLowerInvariant();
        bool Has(params string[] words) => words.Any(w => q.Contains(w));
        var currency = await db.Clinics.AsNoTracking().Select(c => c.Currency).FirstAsync(ct);
        var today = clinicTime.Today;

        ToolOutput output;
        string tool;
        string answer;

        if (PhonePattern().Match(question) is { Success: true } phone)
        {
            tool = "find_patient";
            output = await tools.FindPatientAsync(phone.Value, ct);
            answer = output.Table is { Rows.Count: > 0 } t
                ? $"Found **{t.Rows.Count}** patient(s) matching **{phone.Value.Trim()}**."
                : $"No patient matches **{phone.Value.Trim()}**.";
        }
        else if (Has("recover", "استرد", "استرجاع") && Has("month", "this month", "شهر", "recovered", "how much"))
        {
            tool = "get_revenue_summary";
            output = await tools.RevenueAsync(new DateOnly(today.Year, today.Month, 1), today, ct);
            var recovered = AssistantTools.ToElement(output.ModelPayload).GetProperty("recovered").GetDecimal();
            answer = $"This month the clinic has recovered **{AssistantTools.Money(recovered, currency)}** from patients contacted through Revenue Recovery.";
        }
        else if (Has("owe", "owes", "balance", "debt", "unpaid", "ديون", "مديون", "رصيد", "مستحق"))
        {
            tool = "list_outstanding_balances";
            output = await tools.BalancesAsync(10, ct);
            var total = AssistantTools.ToElement(output.ModelPayload).GetProperty("totalOutstanding").GetDecimal();
            answer = output.Table is { Rows.Count: > 0 } t
                ? $"Patients owe a total of **{AssistantTools.Money(total, currency)}**. The largest balance is **{t.Rows[0][0]}** with **{t.Rows[0][2]}**."
                : "No patients currently have an outstanding balance.";
        }
        else if (Has("missed", "no-show", "no show", "noshow", "تغيب", "لم يحضر", "فائت"))
        {
            tool = "list_missed_appointments";
            var weekStart = today.AddDays(-(((int)today.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));
            var from = Has("month", "شهر") ? new DateOnly(today.Year, today.Month, 1) : Has("today", "اليوم") ? today : weekStart;
            output = await tools.MissedAsync(from, today, ct);
            var count = output.Table?.Rows.Count ?? 0;
            var notRebooked = output.Table?.Rows.Count(r => r[^1] == "No") ?? 0;
            answer = count == 0 ? $"No missed appointments since {from:yyyy-MM-dd}."
                : $"**{count}** missed appointment(s) since {from:yyyy-MM-dd}; **{notRebooked}** of these patients have not rebooked yet.";
        }
        else if (Has("haven't returned", "not returned", "haven't been", "inactive", "not seen", "returned", "لم يعد", "لم يراجع"))
        {
            tool = "list_inactive_patients";
            var months = MonthsPattern().Match(q) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 6;
            output = await tools.InactiveAsync(months, 25, ct);
            var total = AssistantTools.ToElement(output.ModelPayload).GetProperty("totalMatching").GetInt32();
            answer = total == 0 ? $"Every active patient has visited within the last {months} months or has an appointment booked."
                : $"**{total}** active patient(s) have not returned in over {months} months and have nothing booked.";
        }
        else if (Has("high-value", "high value", "haven't started", "not started", "لم يبدأ", "قيمة عالية"))
        {
            tool = "list_recovery_patients";
            var settingsThreshold = await db.ClinicSettings.AsNoTracking().Select(s => s.Recovery.HighValueThreshold).FirstAsync(ct);
            output = await tools.RecoveryPatientsAsync(RecoveryIssueType.TreatmentNotStarted,
                Has("high") || Has("عالية") ? settingsThreshold : null, null, 15, ct);
            var count = output.Table?.Rows.Count ?? 0;
            answer = count == 0 ? "No unstarted treatment plans match."
                : $"**{count}** patient(s) have treatment plans that were presented or accepted but never started.";
        }
        else if (Has("complete", "unfinished", "finish", "incomplete", "لم يكمل", "غير مكتمل"))
        {
            tool = "list_recovery_patients";
            output = await tools.RecoveryPatientsAsync(RecoveryIssueType.TreatmentUnfinished, null, null, 15, ct);
            var count = output.Table?.Rows.Count ?? 0;
            answer = count == 0 ? "No patients currently have stalled unfinished treatment."
                : $"**{count}** patient(s) started treatment but have not completed it and have nothing booked.";
        }
        else if (Has("potential", "losing", "lost", "at risk", "خسارة", "نخسر", "محتمل"))
        {
            tool = "get_recovery_summary";
            output = await tools.RecoverySummaryAsync(ct);
            var s = AssistantTools.ToElement(output.ModelPayload).GetProperty("summary");
            answer = $"There is currently **{AssistantTools.Money(s.GetProperty("potentialRevenue").GetDecimal(), currency)}** of potential revenue at risk across " +
                     $"**{s.GetProperty("patientsToContact").GetInt32()}** patient(s) who need contact now.";
        }
        else if (Has("revenue", "income", "collected", "إيراد", "دخل"))
        {
            tool = "get_revenue_summary";
            output = await tools.RevenueAsync(new DateOnly(today.Year, today.Month, 1), today, ct);
            var net = AssistantTools.ToElement(output.ModelPayload).GetProperty("collectedNet").GetDecimal();
            answer = $"Collected this month so far: **{AssistantTools.Money(net, currency)}**.";
        }
        else if (q.Length >= 3 && !Has("?", "who", "which", "how", "what", "من", "كم", "ما"))
        {
            tool = "find_patient";
            output = await tools.FindPatientAsync(question, ct);
            answer = output.Table is { Rows.Count: > 0 } t ? $"Found **{t.Rows.Count}** matching patient(s)." : "No matching patient found.";
        }
        else
        {
            return new AssistantAnswerDto(
                "I can answer questions about recovery opportunities, outstanding balances, missed appointments, inactive patients, " +
                "treatment plans and revenue. Try one of the suggested questions.\n\n" +
                "_Built-in mode: connect an Anthropic API key (AI__APIKEY) for free-form questions._",
                "built-in", [], [], false);
        }

        return new AssistantAnswerDto(answer, "built-in", [tool], output.Table is null ? [] : [output.Table], false);
    }
}
