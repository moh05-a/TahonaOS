using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tahona.Application.Common;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Infrastructure.Persistence;

namespace Tahona.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Tenant = the authenticated user's clinic, unless a background job pins one explicitly.</summary>
public sealed class TenantContext(ICurrentUser currentUser) : ITenantContext
{
    private Guid? _override;
    public Guid? ClinicId => _override ?? currentUser.ClinicId;
    public void Use(Guid clinicId) => _override = clinicId;
}

/// <summary>Clinic-local time (Asia/Amman by default). The zone is cached per clinic.</summary>
public sealed class ClinicTime(ITenantContext tenant, AppDbContext db, IMemoryCache cache, IClock clock) : IClinicTime
{
    public const string DefaultZone = "Asia/Amman";
    private TimeZoneInfo? _zone;

    public TimeZoneInfo TimeZone => _zone ??= Resolve();

    private TimeZoneInfo Resolve()
    {
        var id = tenant.ClinicId is { } clinicId
            ? cache.GetOrCreate($"tz:{clinicId}", e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return db.Clinics.IgnoreQueryFilters().Where(c => c.Id == clinicId).Select(c => c.TimeZone).FirstOrDefault();
            })
            : null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id ?? DefaultZone); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(DefaultZone); }
    }

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(ToLocal(clock.UtcNow));

    public DateTime StartOfDayUtc(DateOnly date) => ToUtc(date.ToDateTime(TimeOnly.MinValue));

    public DateTime StartOfMonthUtc(DateOnly date) => ToUtc(new DateOnly(date.Year, date.Month, 1).ToDateTime(TimeOnly.MinValue));
}

/// <summary>Allocates document numbers with a single atomic UPDATE … RETURNING (no duplicate numbers under concurrency).</summary>
public sealed class SequenceGenerator(AppDbContext db, ITenantContext tenant) : ISequenceGenerator
{
    public async Task<string> NextAsync(SequenceKind kind, CancellationToken ct)
    {
        var clinicId = tenant.ClinicId ?? throw new InvalidOperationException("No clinic context.");
        var column = kind switch
        {
            SequenceKind.Patient => nameof(ClinicSettings.NextPatientNumber),
            SequenceKind.TreatmentPlan => nameof(ClinicSettings.NextPlanNumber),
            SequenceKind.Invoice => nameof(ClinicSettings.NextInvoiceNumber),
            SequenceKind.Receipt => nameof(ClinicSettings.NextReceiptNumber),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        int value;
        string prefix = "INV";
        if (db.Database.IsNpgsql())
        {
            // Column name comes from the fixed switch above, never from user input.
            var sql = $"UPDATE \"ClinicSettings\" SET \"{column}\" = \"{column}\" + 1 WHERE \"ClinicId\" = {{0}} RETURNING \"{column}\" - 1 AS \"Value\"";
            value = (await db.Database.SqlQueryRaw<int>(sql, clinicId).ToListAsync(ct)).Single();
            if (kind == SequenceKind.Invoice)
                prefix = await db.ClinicSettings.AsNoTracking().Where(s => s.ClinicId == clinicId).Select(s => s.InvoicePrefix).FirstAsync(ct);
        }
        else
        {
            var settings = await db.ClinicSettings.FirstAsync(s => s.ClinicId == clinicId, ct);
            value = kind switch
            {
                SequenceKind.Patient => settings.NextPatientNumber++,
                SequenceKind.TreatmentPlan => settings.NextPlanNumber++,
                SequenceKind.Invoice => settings.NextInvoiceNumber++,
                _ => settings.NextReceiptNumber++
            };
            prefix = settings.InvoicePrefix;
        }

        return kind switch
        {
            SequenceKind.Patient => $"P-{value:D5}",
            SequenceKind.TreatmentPlan => $"TP-{value:D4}",
            SequenceKind.Invoice => $"{prefix}-{value}",
            _ => $"RC-{value}"
        };
    }
}

/// <summary>
/// Postgres search: phone-like input is normalised and matched on PhoneNormalized; text is matched token by token
/// with ILIKE on English/Arabic names (GIN trigram indexes make both fast). Falls back to LINQ for other providers.
/// </summary>
public sealed class SearchProvider(AppDbContext db) : ISearchProvider
{
    public IQueryable<Patient> FilterPatients(IQueryable<Patient> query, string term)
    {
        term = term.Trim();
        if (PhoneNumber.LooksLikePhone(term))
        {
            var digits = PhoneNumber.Normalize(term);
            if (digits.Length >= 3) return query.Where(p => p.PhoneNormalized.Contains(digits) || (p.WhatsAppNumber != null && p.WhatsAppNumber.Contains(digits)));
        }

        var npgsql = db.Database.IsNpgsql();
        foreach (var token in term.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5))
        {
            var pattern = $"%{Escape(token)}%";
            var lower = token.ToLowerInvariant();
            query = npgsql
                ? query.Where(p => EF.Functions.ILike(p.FullName, pattern) || (p.FullNameAr != null && EF.Functions.ILike(p.FullNameAr, pattern))
                                   || EF.Functions.ILike(p.FileNumber, pattern) || (p.Email != null && EF.Functions.ILike(p.Email, pattern)))
                : query.Where(p => p.FullName.ToLower().Contains(lower) || (p.FullNameAr != null && p.FullNameAr.Contains(token))
                                   || p.FileNumber.ToLower().Contains(lower) || (p.Email != null && p.Email.Contains(lower)));
        }
        return query;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
