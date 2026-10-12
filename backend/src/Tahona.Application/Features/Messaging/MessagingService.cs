using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Messaging;

public record MessageTemplateDto(Guid Id, string Name, MessageTemplateType Type, MessageChannel Channel, string Language,
    string Body, string? ProviderTemplateName, bool IsActive, bool IsSystem);

public record MessageTemplateRequest(string Name, MessageTemplateType Type, MessageChannel Channel, string Language,
    string Body, string? ProviderTemplateName, bool IsActive);

public record RenderRequest(Guid PatientId, Guid? TemplateId, MessageTemplateType? TemplateType, string? Body,
    Guid? AppointmentId, decimal? Amount, string? Treatment, Guid? DentistId);

public record RenderedMessage(string Body, string? ToNumber, string Language, Guid? TemplateId, IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> OrderedValues);

public record SendMessageRequest(Guid PatientId, MessageChannel Channel, Guid? TemplateId, MessageTemplateType? TemplateType,
    string? Body, Guid? AppointmentId, Guid? FollowUpId, decimal? Amount, string? Treatment, Guid? DentistId);

public record SendMessageResult(Guid MessageId, MessageStatus Status, string Body, string? ManualSendUrl, string? Error, string Provider);

public record MessageQuery : PageQuery
{
    public Guid? PatientId { get; init; }
    public MessageChannel? Channel { get; init; }
    public MessageStatus? Status { get; init; }
    public string? Search { get; init; }
}

public record MessageDto(Guid Id, Guid PatientId, string PatientName, MessageChannel Channel, MessageDirection Direction,
    string Body, string? ToNumber, MessageStatus Status, string? Error, DateTime CreatedAt, DateTime? SentAt,
    string? SentBy, string? TemplateName, bool IsRecovery);

public record ConversationDto(Guid PatientId, string PatientName, string? PatientNameAr, string Phone, string LastMessage,
    DateTime LastMessageAt, MessageStatus LastStatus, int MessageCount);

public record MessagingStatusDto(string Provider, bool IsConfigured, string Mode, string Note);

public class MessageTemplateRequestValidator : AbstractValidator<MessageTemplateRequest>
{
    public MessageTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x.Language).Must(l => l is "en" or "ar").WithMessage("Language must be en or ar.");
        RuleFor(x => x.Body).NotEmpty().MaximumLength(2000)
            .Must(b => MessagingService.UnknownVariables(b).Count == 0)
            .WithMessage(x => $"Unknown variables: {string.Join(", ", MessagingService.UnknownVariables(x.Body))}. Allowed: {string.Join(", ", MessagingService.AllowedVariables)}");
        RuleFor(x => x.ProviderTemplateName).MaximumLength(120);
    }
}

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.Channel).IsInEnum();
        RuleFor(x => x).Must(x => x.TemplateId is not null || x.TemplateType is not null || !string.IsNullOrWhiteSpace(x.Body))
            .WithMessage("Choose a template or write a message.");
        RuleFor(x => x.Body).MaximumLength(4000);
    }
}

public partial class MessagingService(
    IAppDbContext db,
    IWhatsAppProvider whatsApp,
    ISearchProvider search,
    ICurrentUser currentUser,
    IClinicTime clinicTime,
    IClock clock)
{
    public static readonly IReadOnlyList<string> AllowedVariables =
        ["patient_name", "doctor", "date", "time", "clinic_name", "clinic_phone", "amount", "treatment"];

    [GeneratedRegex(@"\{\{\s*([a-zA-Z_]+)\s*\}\}")]
    private static partial Regex VariablePattern();

    public static IReadOnlyList<string> UnknownVariables(string? body) =>
        string.IsNullOrEmpty(body) ? []
            : VariablePattern().Matches(body).Select(m => m.Groups[1].Value.ToLowerInvariant())
                .Where(v => !AllowedVariables.Contains(v)).Distinct().ToList();

    // ---------------------------------------------------------------- templates

    public async Task<IReadOnlyList<MessageTemplateDto>> ListTemplatesAsync(CancellationToken ct) =>
        await db.MessageTemplates.AsNoTracking().OrderBy(t => t.Type).ThenBy(t => t.Language)
            .Select(t => new MessageTemplateDto(t.Id, t.Name, t.Type, t.Channel, t.Language, t.Body, t.ProviderTemplateName, t.IsActive, t.IsSystem))
            .ToListAsync(ct);

    public async Task<Guid> CreateTemplateAsync(MessageTemplateRequest r, CancellationToken ct)
    {
        var t = new MessageTemplate();
        ApplyTemplate(t, r);
        db.MessageTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    public async Task UpdateTemplateAsync(Guid id, MessageTemplateRequest r, CancellationToken ct)
    {
        var t = await db.MessageTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Template", id);
        ApplyTemplate(t, r);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await db.MessageTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Template", id);
        if (t.IsSystem) throw new BusinessRuleException("System templates can be edited or deactivated, but not deleted.");
        db.MessageTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    private static void ApplyTemplate(MessageTemplate t, MessageTemplateRequest r)
    {
        t.Name = r.Name.Trim();
        t.Type = r.Type;
        t.Channel = r.Channel;
        t.Language = r.Language;
        t.Body = r.Body.Trim();
        t.ProviderTemplateName = string.IsNullOrWhiteSpace(r.ProviderTemplateName) ? null : r.ProviderTemplateName.Trim();
        t.IsActive = r.IsActive;
    }

    // ---------------------------------------------------------------- rendering

    public async Task<RenderedMessage> RenderAsync(RenderRequest request, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().Include(p => p.PrimaryDentist).FirstOrDefaultAsync(p => p.Id == request.PatientId, ct)
                      ?? throw new NotFoundException("Patient", request.PatientId);
        var clinic = await db.Clinics.AsNoTracking().FirstAsync(ct);

        MessageTemplate? template = null;
        if (request.TemplateId is not null)
            template = await db.MessageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct)
                       ?? throw RequestValidationException.For("templateId", "Template not found.");
        else if (request.TemplateType is not null && string.IsNullOrWhiteSpace(request.Body))
            template = await FindTemplateAsync(request.TemplateType.Value, patient.PreferredLanguage, ct)
                       ?? throw new BusinessRuleException($"No active {request.TemplateType} template. Create one in Settings → Message templates.");

        var language = template?.Language ?? patient.PreferredLanguage;
        var culture = CultureInfo.GetCultureInfo(language == "ar" ? "ar-JO" : "en-GB");

        // Appointment context: explicit appointment, else the next upcoming one.
        var now = clock.UtcNow;
        var appointment = request.AppointmentId is not null
            ? await db.Appointments.AsNoTracking().Include(a => a.Dentist).FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct)
            : await db.Appointments.AsNoTracking().Include(a => a.Dentist)
                .Where(a => a.PatientId == patient.Id && a.StartTime > now
                            && (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed))
                .OrderBy(a => a.StartTime).FirstOrDefaultAsync(ct);

        var dentist = request.DentistId is not null
            ? await db.Dentists.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DentistId, ct)
            : appointment?.Dentist ?? patient.PrimaryDentist;

        var amount = request.Amount ?? await db.Invoices.Where(i => i.PatientId == patient.Id
                         && (i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.PartiallyPaid))
                     .SumAsync(i => (decimal?)i.Balance, ct) ?? 0;

        var local = appointment is null ? (DateTime?)null : clinicTime.ToLocal(appointment.StartTime);
        var patientName = language == "ar" && !string.IsNullOrWhiteSpace(patient.FullNameAr) ? patient.FullNameAr! : patient.FullName;
        var doctorName = dentist is null ? (language == "ar" ? "طبيب الأسنان" : "your dentist")
            : language == "ar" && !string.IsNullOrWhiteSpace(dentist.FullNameAr) ? dentist.FullNameAr! : dentist.FullName;

        var variables = new Dictionary<string, string>
        {
            ["patient_name"] = patientName,
            ["doctor"] = doctorName,
            ["date"] = local?.ToString("dddd d MMMM", culture) ?? "",
            ["time"] = local?.ToString("h:mm tt", culture) ?? "",
            ["clinic_name"] = language == "ar" && !string.IsNullOrWhiteSpace(clinic.NameAr) ? clinic.NameAr! : clinic.Name,
            ["clinic_phone"] = clinic.Phone ?? "",
            ["amount"] = $"{amount.ToString("#,0.###", CultureInfo.InvariantCulture)} {clinic.Currency}",
            ["treatment"] = request.Treatment ?? appointment?.Treatment ?? ""
        };

        var source = string.IsNullOrWhiteSpace(request.Body) ? template!.Body : request.Body!;
        if (appointment is null && VariablePattern().Matches(source).Any(m => m.Groups[1].Value.ToLowerInvariant() is "date" or "time"))
            throw new BusinessRuleException("This message uses {{date}} / {{time}}, but the patient has no upcoming appointment. Book one first or choose another template.");
        var body = VariablePattern().Replace(source, m =>
            variables.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var v) ? v : m.Value);

        var ordered = VariablePattern().Matches(source)
            .Select(m => variables.GetValueOrDefault(m.Groups[1].Value.ToLowerInvariant(), "")).ToList();
        var number = string.IsNullOrWhiteSpace(patient.WhatsAppNumber) ? patient.Phone : patient.WhatsAppNumber;
        return new RenderedMessage(body.Trim(), PhoneNumber.ToInternational(number), language, template?.Id, variables, ordered);
    }

    public Task<MessageTemplate?> FindTemplateAsync(MessageTemplateType type, string language, CancellationToken ct) =>
        db.MessageTemplates.AsNoTracking()
            .Where(t => t.Type == type && t.IsActive)
            .OrderBy(t => t.Language == language ? 0 : t.Language == "en" ? 1 : 2)
            .FirstOrDefaultAsync(ct);

    // ---------------------------------------------------------------- sending

    public async Task<SendMessageResult> SendAsync(SendMessageRequest request, CancellationToken ct)
    {
        var rendered = await RenderAsync(new RenderRequest(request.PatientId, request.TemplateId, request.TemplateType, request.Body,
            request.AppointmentId, request.Amount, request.Treatment, request.DentistId), ct);

        var message = new Message
        {
            PatientId = request.PatientId,
            Channel = request.Channel,
            TemplateId = rendered.TemplateId,
            Body = rendered.Body,
            ToNumber = rendered.ToNumber,
            SentById = currentUser.UserId,
            FollowUpId = request.FollowUpId,
            AppointmentId = request.AppointmentId
        };

        string? manualUrl = null;
        var provider = "manual";
        switch (request.Channel)
        {
            case MessageChannel.WhatsApp:
            {
                if (string.IsNullOrEmpty(rendered.ToNumber)) throw new BusinessRuleException("Patient has no phone number for WhatsApp.");
                var providerTemplate = rendered.TemplateId is null ? null
                    : await db.MessageTemplates.Where(t => t.Id == rendered.TemplateId).Select(t => t.ProviderTemplateName).FirstOrDefaultAsync(ct);
                var result = await whatsApp.SendAsync(new WhatsAppSendRequest(rendered.ToNumber, rendered.Body, providerTemplate, rendered.Language, rendered.OrderedValues), ct);
                provider = whatsApp.Name;
                if (result.RequiresManualSend)
                {
                    message.Status = MessageStatus.ManualPending;
                    manualUrl = result.ManualSendUrl;
                }
                else if (result.Success)
                {
                    message.Status = MessageStatus.Sent;
                    message.SentAt = clock.UtcNow;
                    message.ProviderMessageId = result.ProviderMessageId;
                }
                else
                {
                    message.Status = MessageStatus.Failed;
                    message.Error = result.Error;
                }
                break;
            }
            case MessageChannel.Phone:
                message.Status = MessageStatus.Logged;
                message.SentAt = clock.UtcNow;
                break;
            default:
                // SMS / e-mail gateways are not connected yet: log for manual handling.
                message.Status = MessageStatus.ManualPending;
                break;
        }

        db.Messages.Add(message);
        if (request.AppointmentId is not null && message.Status is MessageStatus.Sent or MessageStatus.ManualPending)
        {
            var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct);
            if (appt is not null) appt.ReminderSentAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return new SendMessageResult(message.Id, message.Status, message.Body, manualUrl, message.Error, provider);
    }

    /// <summary>Staff confirm they sent a manual (wa.me) message.</summary>
    public async Task MarkSentAsync(Guid messageId, CancellationToken ct)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct) ?? throw new NotFoundException("Message", messageId);
        if (m.Status != MessageStatus.ManualPending) return;
        m.Status = MessageStatus.Sent;
        m.SentAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Delivery status callbacks from the WhatsApp provider webhook (no tenant context: matched by provider id).</summary>
    public async Task<bool> ApplyProviderStatusAsync(string providerMessageId, MessageStatus status, string? error, CancellationToken ct)
    {
        var m = await db.Messages.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ProviderMessageId == providerMessageId, ct);
        if (m is null) return false;
        if (status > m.Status || status == MessageStatus.Failed)
        {
            m.Status = status;
            m.Error = error;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PagedResult<MessageDto>> ListAsync(MessageQuery query, CancellationToken ct)
    {
        var q = db.Messages.AsNoTracking();
        if (query.PatientId is not null) q = q.Where(m => m.PatientId == query.PatientId);
        if (query.Channel is not null) q = q.Where(m => m.Channel == query.Channel);
        if (query.Status is not null) q = q.Where(m => m.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var ids = search.FilterPatients(db.Patients, query.Search.Trim()).Select(p => p.Id);
            q = q.Where(m => ids.Contains(m.PatientId));
        }

        return await q.OrderByDescending(m => m.CreatedAt)
            .Select(m => new MessageDto(m.Id, m.PatientId, m.Patient.FullName, m.Channel, m.Direction, m.Body, m.ToNumber, m.Status,
                m.Error, m.CreatedAt, m.SentAt, m.SentBy != null ? m.SentBy.FullName : null,
                m.Template != null ? m.Template.Name : null, m.FollowUpId != null))
            .ToPagedAsync(query, ct);
    }

    public async Task<IReadOnlyList<ConversationDto>> ConversationsAsync(int take, CancellationToken ct)
    {
        var latest = await db.Messages.AsNoTracking()
            .GroupBy(m => m.PatientId)
            .Select(g => new { PatientId = g.Key, Last = g.Max(m => m.CreatedAt), Count = g.Count() })
            .OrderByDescending(x => x.Last).Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        var ids = latest.Select(x => x.PatientId).ToList();
        var lastMessages = await db.Messages.AsNoTracking().Where(m => ids.Contains(m.PatientId))
            .Select(m => new { m.PatientId, m.CreatedAt, m.Body, m.Status, m.Patient.FullName, m.Patient.FullNameAr, m.Patient.Phone })
            .ToListAsync(ct);

        return latest.Select(l =>
        {
            var m = lastMessages.Where(x => x.PatientId == l.PatientId).OrderByDescending(x => x.CreatedAt).First();
            return new ConversationDto(l.PatientId, m.FullName, m.FullNameAr, m.Phone, m.Body, m.CreatedAt, m.Status, l.Count);
        }).ToList();
    }

    public MessagingStatusDto Status() => new(
        whatsApp.Name, whatsApp.IsConfigured,
        whatsApp.IsConfigured ? "automatic" : "manual",
        whatsApp.IsConfigured
            ? "Messages are sent through the WhatsApp Business Cloud API."
            : "No WhatsApp API credentials configured. Messages open in WhatsApp (wa.me) for staff to send, and are logged here. Add WHATSAPP__ACCESSTOKEN and WHATSAPP__PHONENUMBERID to enable automatic sending.");
}
