using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Recovery;

public record FollowUpRuleDto(Guid Id, string Name, FollowUpTrigger Trigger, int DelayDays, ProcedureCategory? ProcedureCategory,
    Guid? MessageTemplateId, string? MessageTemplateName, PriorityLevel Priority, bool IsEnabled, bool AutoSend);

public record FollowUpRuleRequest(string Name, FollowUpTrigger Trigger, int DelayDays, ProcedureCategory? ProcedureCategory,
    Guid? MessageTemplateId, PriorityLevel Priority, bool IsEnabled, bool AutoSend);

public record FollowUpTaskQuery : PageQuery
{
    public FollowUpStatus? Status { get; init; }
    public RecoveryIssueType? IssueType { get; init; }
    public Guid? PatientId { get; init; }
    public bool? OpenOnly { get; init; }
}

public record FollowUpTaskDto(Guid Id, Guid PatientId, string PatientName, string Phone, RecoveryIssueType IssueType, string Title,
    DateTime DueDate, FollowUpStatus Status, PriorityLevel Priority, decimal EstimatedValue, decimal RecoveredAmount,
    int ContactAttempts, DateTime? LastContactedAt, string? Outcome, string? AssignedTo, DateTime CreatedAt, DateTime? ResolvedAt);

public class FollowUpRuleRequestValidator : AbstractValidator<FollowUpRuleRequest>
{
    public FollowUpRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Trigger).IsInEnum();
        RuleFor(x => x.DelayDays).InclusiveBetween(0, 1095);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.ProcedureCategory).Null().When(x => x.Trigger != FollowUpTrigger.ProcedureCompleted)
            .WithMessage("Procedure category only applies to 'procedure completed' rules.");
    }
}

public class FollowUpRuleService(IAppDbContext db)
{
    public async Task<IReadOnlyList<FollowUpRuleDto>> ListAsync(CancellationToken ct) =>
        await db.FollowUpRules.AsNoTracking().OrderBy(r => r.Trigger).ThenBy(r => r.DelayDays)
            .Select(r => new FollowUpRuleDto(r.Id, r.Name, r.Trigger, r.DelayDays, r.ProcedureCategory, r.MessageTemplateId,
                r.MessageTemplate != null ? r.MessageTemplate.Name : null, r.Priority, r.IsEnabled, r.AutoSend))
            .ToListAsync(ct);

    public async Task<Guid> CreateAsync(FollowUpRuleRequest request, CancellationToken ct)
    {
        await ValidateTemplateAsync(request.MessageTemplateId, ct);
        var rule = new FollowUpRule();
        Apply(rule, request);
        db.FollowUpRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule.Id;
    }

    public async Task UpdateAsync(Guid id, FollowUpRuleRequest request, CancellationToken ct)
    {
        var rule = await db.FollowUpRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Rule", id);
        await ValidateTemplateAsync(request.MessageTemplateId, ct);
        Apply(rule, request);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var rule = await db.FollowUpRules.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Rule", id);
        db.FollowUpRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<FollowUpTaskDto>> ListTasksAsync(FollowUpTaskQuery query, CancellationToken ct)
    {
        var q = db.FollowUps.AsNoTracking();
        if (query.Status is not null) q = q.Where(f => f.Status == query.Status);
        if (query.IssueType is not null) q = q.Where(f => f.IssueType == query.IssueType);
        if (query.PatientId is not null) q = q.Where(f => f.PatientId == query.PatientId);
        if (query.OpenOnly == true)
            q = q.Where(f => f.Status == FollowUpStatus.Pending || f.Status == FollowUpStatus.Contacted || f.Status == FollowUpStatus.NoResponse);

        return await q.OrderByDescending(f => f.CreatedAt)
            .Select(f => new FollowUpTaskDto(f.Id, f.PatientId, f.Patient.FullName, f.Patient.Phone, f.IssueType, f.Title, f.DueDate,
                f.Status, f.Priority, f.EstimatedValue, f.RecoveredAmount, f.ContactAttempts, f.LastContactedAt, f.Outcome,
                f.AssignedToUser != null ? f.AssignedToUser.FullName : null, f.CreatedAt, f.ResolvedAt))
            .ToPagedAsync(query, ct);
    }

    private async Task ValidateTemplateAsync(Guid? templateId, CancellationToken ct)
    {
        if (templateId is not null && !await db.MessageTemplates.AnyAsync(t => t.Id == templateId, ct))
            throw RequestValidationException.For("messageTemplateId", "Template not found.");
    }

    private static void Apply(FollowUpRule r, FollowUpRuleRequest q)
    {
        r.Name = q.Name.Trim();
        r.Trigger = q.Trigger;
        r.DelayDays = q.DelayDays;
        r.ProcedureCategory = q.ProcedureCategory;
        r.MessageTemplateId = q.MessageTemplateId;
        r.Priority = q.Priority;
        r.IsEnabled = q.IsEnabled;
        r.AutoSend = q.AutoSend;
    }
}
