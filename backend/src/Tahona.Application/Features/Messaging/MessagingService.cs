using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Application.Features.Notifications;
using Tahona.Domain.Authorization;
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

/// <summary>Outcome of processing one inbound provider message.</summary>
public enum InboundResult { Stored, Duplicate, Unmatched }

public record MessageQuery : PageQuery
{
    public Guid? PatientId { get; init; }
    public MessageChannel? Channel { get; init; }
    public MessageStatus? Status { get; init; }
    public string? Search { get; init; }
}

public record MessageDto(Guid Id, Guid PatientId, string PatientName, MessageChannel Channel, MessageDirection Direction,
    string Body, string? ToNumber, MessageStatus Status, string? Error, DateTime CreatedAt, DateTime? SentAt,
    string? SentBy, string? TemplateName, bool IsRecovery, bool IsAutomated, DateTime? DeliveredAt, DateTime? ReadAt);

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
    ITenantContext tenant,
    NotificationService notifications,
    IClock clock)
{
    /// <summary>WhatsApp lets a business reply with free text only within this period after the patient's last message.</summary>
    public static readonly TimeSpan ServiceWindow = TimeSpan.FromHours(24);

    /// <summary>An inbound message is matched to the clinic that most recently wrote to that number within this period.</summary>
    private static readonly TimeSpan InboundMatchWindow = TimeSpan.FromDays(30);

    /// <summary>A reply consisting of one of these words withdraws messaging consent.</summary>
    private static readonly HashSet<string> OptOutKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "stop", "unsubscribe", "cancel", "إيقاف", "ايقاف", "توقف", "إلغاء", "الغاء", "الغاء الاشتراك", "إلغاء الاشتراك" };

    /// <summary>True when <paramref name="now"/> falls inside the quiet period, which may cross midnight. Equal times mean no quiet period.</summary>
    public static bool InQuietHours(TimeOnly now, TimeOnly start, TimeOnly end) =>
        start != end && (start < end ? now >= start && now < end : now >= start || now < end);

    /// <summary>Whether the automation engine may message a patient with this consent state.</summary>
    public static bool AllowsAutomation(MessagingConsent consent, bool requireConsent) =>
        consent == MessagingConsent.Granted || (consent == MessagingConsent.Unknown && !requireConsent);

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
            ? await db.Appointments.AsNoTracking().Include(a => a.Dentist)
                  .FirstOrDefaultAsync(a => a.Id == request.AppointmentId && a.PatientId == patient.Id, ct)
              ?? throw RequestValidationException.For("appointmentId", "Appointment not found for this patient.")
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

    /// <summary>A staff member sends or logs a message.</summary>
    public Task<SendMessageResult> SendAsync(SendMessageRequest request, CancellationToken ct) => SendCoreAsync(request, false, ct);

    /// <summary>
    /// The automation engine sends a message. Callers decide eligibility (consent, quiet hours, attempt limits)
    /// with <see cref="AllowsAutomation"/> and <see cref="InQuietHours"/> before calling.
    /// </summary>
    public Task<SendMessageResult> SendAutomatedAsync(SendMessageRequest request, CancellationToken ct) => SendCoreAsync(request, true, ct);

    private async Task<SendMessageResult> SendCoreAsync(SendMessageRequest request, bool automated, CancellationToken ct)
    {
        if (request.Channel is MessageChannel.Sms or MessageChannel.Email)
            throw new BusinessRuleException("SMS and e-mail are not connected. Use WhatsApp, or log a phone call.");

        var consent = await db.Patients.AsNoTracking().Where(p => p.Id == request.PatientId)
                          .Select(p => (MessagingConsent?)p.MessagingConsent).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Patient", request.PatientId);
        if (consent == MessagingConsent.OptedOut && request.Channel != MessageChannel.Phone)
            throw new BusinessRuleException("This patient has opted out of messages. Call them instead, or record new consent on their profile.");

        if (request.FollowUpId is not null && !await db.FollowUps.AnyAsync(f => f.Id == request.FollowUpId && f.PatientId == request.PatientId, ct))
            throw RequestValidationException.For("followUpId", "Follow-up not found for this patient.");

        var rendered = await RenderAsync(new RenderRequest(request.PatientId, request.TemplateId, request.TemplateType, request.Body,
            request.AppointmentId, request.Amount, request.Treatment, request.DentistId), ct);

        var now = clock.UtcNow;
        var message = new Message
        {
            PatientId = request.PatientId,
            Channel = request.Channel,
            TemplateId = rendered.TemplateId,
            Body = rendered.Body,
            ToNumber = rendered.ToNumber,
            SentById = automated ? null : currentUser.UserId,
            IsAutomated = automated,
            FollowUpId = request.FollowUpId,
            AppointmentId = request.AppointmentId
        };

        string? manualUrl = null;
        var provider = "manual";
        if (request.Channel == MessageChannel.WhatsApp)
        {
            if (string.IsNullOrEmpty(rendered.ToNumber)) throw new BusinessRuleException("Patient has no phone number for WhatsApp.");
            var providerTemplate = rendered.TemplateId is null ? null
                : await db.MessageTemplates.Where(t => t.Id == rendered.TemplateId).Select(t => t.ProviderTemplateName).FirstOrDefaultAsync(ct);

            // Through the Cloud API, free text is only deliverable while the patient's service window is open.
            if (whatsApp.IsConfigured && string.IsNullOrWhiteSpace(providerTemplate) && !await ServiceWindowOpenAsync(request.PatientId, now, ct))
                throw new BusinessRuleException(
                    "WhatsApp only delivers free-text messages within 24 hours of the patient's last message. Choose a template that has an approved WhatsApp template name.");

            var result = await whatsApp.SendAsync(new WhatsAppSendRequest(rendered.ToNumber, rendered.Body, providerTemplate, rendered.Language, rendered.OrderedValues), ct);
            provider = whatsApp.Name;
            if (result.RequiresManualSend)
            {
                // Opened in WhatsApp for a person to send. Not sent until staff confirm it.
                message.Status = MessageStatus.ManualPending;
                manualUrl = result.ManualSendUrl;
            }
            else if (result.Success)
            {
                message.Status = MessageStatus.Sent;
                message.SentAt = now;
                message.ProviderMessageId = result.ProviderMessageId;
            }
            else
            {
                message.Status = MessageStatus.Failed;
                message.Error = result.Error;
            }
        }
        else
        {
            message.Status = MessageStatus.Logged; // phone call record
            message.SentAt = now;
        }

        db.Messages.Add(message);
        if (message.Status == MessageStatus.Sent) await MarkReminderSentAsync(message.AppointmentId, now, ct);
        await db.SaveChangesAsync(ct);
        return new SendMessageResult(message.Id, message.Status, message.Body, manualUrl, message.Error, provider);
    }

    private async Task<bool> ServiceWindowOpenAsync(Guid patientId, DateTime now, CancellationToken ct)
    {
        var since = now - ServiceWindow;
        return await db.Messages.AnyAsync(m => m.PatientId == patientId && m.Direction == MessageDirection.Inbound
                                               && m.Channel == MessageChannel.WhatsApp && m.CreatedAt >= since, ct);
    }

    private async Task MarkReminderSentAsync(Guid? appointmentId, DateTime now, CancellationToken ct)
    {
        if (appointmentId is null) return;
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appt is not null) appt.ReminderSentAt = now;
    }

    /// <summary>Staff confirm they sent a manual (wa.me) message.</summary>
    public async Task MarkSentAsync(Guid messageId, CancellationToken ct)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct) ?? throw new NotFoundException("Message", messageId);
        if (m.Status != MessageStatus.ManualPending) return;
        m.Status = MessageStatus.Sent;
        m.SentAt = clock.UtcNow;
        await MarkReminderSentAsync(m.AppointmentId, m.SentAt.Value, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Delivery status callbacks from the WhatsApp provider webhook (no tenant context: matched by provider id).
    /// Statuses only move forward (sent → delivered → read), so a repeated or late event changes nothing.
    /// </summary>
    public async Task<bool> ApplyProviderStatusAsync(string providerMessageId, MessageStatus status, string? error, CancellationToken ct)
    {
        var m = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.ProviderMessageId == providerMessageId && x.Direction == MessageDirection.Outbound, ct);
        if (m is null) return false;

        var now = clock.UtcNow;
        if (status == MessageStatus.Failed)
        {
            if (m.Status is MessageStatus.Delivered or MessageStatus.Read) return true; // already reached the patient
            m.Status = MessageStatus.Failed;
            m.Error = error;
        }
        else if (status is MessageStatus.Sent or MessageStatus.Delivered or MessageStatus.Read && status > m.Status)
        {
            m.Status = status;
            m.Error = null;
        }
        else return true;

        if (status is MessageStatus.Delivered or MessageStatus.Read) m.DeliveredAt ??= now;
        if (status == MessageStatus.Read) m.ReadAt ??= now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Stores a patient's WhatsApp message (no tenant context: called by the webhook).
    /// The clinic is the one whose connected number received it. Within that clinic, the message joins the conversation
    /// the clinic last had with that number, or the single patient who has that phone number. With deployment-wide
    /// credentials, where the receiving number says nothing, only the recent-conversation match is used.
    /// Anything that cannot be tied to exactly one patient is dropped, never guessed.
    /// A reply of "STOP" (or the Arabic equivalents) withdraws the patient's messaging consent.
    /// </summary>
    public async Task<InboundResult> ReceiveInboundAsync(string providerMessageId, string fromNumber, string text, DateTime receivedAt, CancellationToken ct,
        string? phoneNumberId = null)
    {
        if (await db.Messages.IgnoreQueryFilters().AnyAsync(m => m.ProviderMessageId == providerMessageId, ct))
            return InboundResult.Duplicate;

        Guid? clinicId = string.IsNullOrWhiteSpace(phoneNumberId) ? null
            : await db.ClinicWhatsApps.IgnoreQueryFilters().Where(w => w.PhoneNumberId == phoneNumberId && w.IsEnabled)
                .Select(w => (Guid?)w.ClinicId).FirstOrDefaultAsync(ct);

        var from = PhoneNumber.ToInternational(fromNumber);
        var since = clock.UtcNow - InboundMatchWindow;
        var conversations = db.Messages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Direction == MessageDirection.Outbound && m.Channel == MessageChannel.WhatsApp && m.ToNumber == from && m.CreatedAt >= since);
        if (clinicId is not null) conversations = conversations.Where(m => m.ClinicId == clinicId);
        var thread = await conversations.OrderByDescending(m => m.CreatedAt).Select(m => new { m.ClinicId, m.PatientId }).FirstOrDefaultAsync(ct);

        if (thread is null && clinicId is not null)
        {
            // The patient wrote first, to the clinic's own number.
            var national = PhoneNumber.Normalize(fromNumber);
            var matches = await db.Patients.IgnoreQueryFilters().Where(p => p.ClinicId == clinicId && !p.IsDeleted && p.PhoneNormalized == national)
                .Select(p => p.Id).Take(2).ToListAsync(ct);
            if (matches.Count == 1) thread = new { ClinicId = clinicId.Value, PatientId = matches[0] };
        }
        if (thread is null) return InboundResult.Unmatched;

        tenant.Use(thread.ClinicId);
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == thread.PatientId, ct);
        if (patient is null) return InboundResult.Unmatched;

        var body = text.Trim();
        var message = new Message
        {
            PatientId = patient.Id,
            Channel = MessageChannel.WhatsApp,
            Direction = MessageDirection.Inbound,
            Body = body.Length > 4000 ? body[..4000] : body.Length == 0 ? "(empty message)" : body,
            FromNumber = from,
            Status = MessageStatus.Received,
            ProviderMessageId = providerMessageId,
            SentAt = receivedAt
        };
        db.Messages.Add(message);

        var optedOut = OptOutKeywords.Contains(body.Trim('.', '!', ' ')) && patient.MessagingConsent != MessagingConsent.OptedOut;
        if (optedOut)
        {
            patient.MessagingConsent = MessagingConsent.OptedOut;
            patient.MessagingConsentAt = clock.UtcNow;
            patient.MessagingConsentSource = "whatsapp-reply";
        }
        await db.SaveChangesAsync(ct);

        await notifications.NotifyAsync(new NotifyRequest(
            NotificationType.PatientReply, optedOut ? PriorityLevel.High : PriorityLevel.Medium,
            optedOut ? $"{patient.FullName} opted out of messages" : $"WhatsApp reply from {patient.FullName}",
            message.Body, $"/patients/{patient.Id}?tab=messages", nameof(Message), message.Id,
            RequiredPermission: Permissions.MessagesSend), ct);
        return InboundResult.Stored;
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
                m.Template != null ? m.Template.Name : null, m.FollowUpId != null, m.IsAutomated, m.DeliveredAt, m.ReadAt))
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
            : "Manual mode: each message opens in WhatsApp for a staff member to send, and is recorded here as 'awaiting manual send' until they confirm it. Nothing is sent automatically.");
}
