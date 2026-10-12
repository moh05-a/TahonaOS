using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Tahona.Application.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Staff;

public record DentistDto(
    Guid Id, string FullName, string? FullNameAr, string? Specialty, string? Phone, string? Email,
    string? LicenseNumber, string Color, bool IsActive, Guid? UserId, int UpcomingAppointments, decimal ProductionThisMonth);

public record DentistUpsertRequest(
    string FullName, string? FullNameAr, string? Specialty, string? Phone, string? Email,
    string? LicenseNumber, string Color, bool IsActive, Guid? UserId);

public class DentistUpsertValidator : AbstractValidator<DentistUpsertRequest>
{
    public DentistUpsertValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FullNameAr).MaximumLength(150);
        RuleFor(x => x.Specialty).MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Color).NotEmpty().Matches("^#[0-9a-fA-F]{6}$").WithMessage("Colour must be a hex value like #0ea5e9.");
        RuleFor(x => x.LicenseNumber).MaximumLength(50);
    }
}

public class DentistService(IAppDbContext db, IClock clock, IClinicTime clinicTime)
{
    public async Task<IReadOnlyList<DentistDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var monthStart = clinicTime.StartOfMonthUtc(clinicTime.Today);
        var q = db.Dentists.AsNoTracking();
        if (!includeInactive) q = q.Where(d => d.IsActive);

        return await q.OrderBy(d => d.FullName).Select(d => new DentistDto(
            d.Id, d.FullName, d.FullNameAr, d.Specialty, d.Phone, d.Email, d.LicenseNumber, d.Color, d.IsActive, d.UserId,
            db.Appointments.Count(a => a.DentistId == d.Id && a.StartTime > now &&
                                       (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)),
            db.TreatmentRecords.Where(r => r.DentistId == d.Id && r.PerformedAt >= monthStart).Sum(r => (decimal?)r.Price) ?? 0
        )).ToListAsync(ct);
    }

    public async Task<Guid> CreateAsync(DentistUpsertRequest request, CancellationToken ct)
    {
        await EnsureUserAsync(request.UserId, null, ct);
        var dentist = new Dentist();
        Apply(dentist, request);
        db.Dentists.Add(dentist);
        await db.SaveChangesAsync(ct);
        return dentist.Id;
    }

    public async Task UpdateAsync(Guid id, DentistUpsertRequest request, CancellationToken ct)
    {
        var dentist = await db.Dentists.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Dentist", id);
        await EnsureUserAsync(request.UserId, id, ct);
        Apply(dentist, request);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureUserAsync(Guid? userId, Guid? dentistId, CancellationToken ct)
    {
        if (userId is null) return;
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            throw RequestValidationException.For("userId", "Linked user account does not exist.");
        if (await db.Dentists.AnyAsync(d => d.UserId == userId && d.Id != dentistId, ct))
            throw RequestValidationException.For("userId", "This user is already linked to another dentist profile.");
    }

    private static void Apply(Dentist d, DentistUpsertRequest r)
    {
        d.FullName = r.FullName.Trim();
        d.FullNameAr = r.FullNameAr?.Trim();
        d.Specialty = r.Specialty?.Trim();
        d.Phone = r.Phone?.Trim();
        d.Email = r.Email?.Trim().ToLowerInvariant();
        d.LicenseNumber = r.LicenseNumber?.Trim();
        d.Color = r.Color;
        d.IsActive = r.IsActive;
        d.UserId = r.UserId;
    }
}
