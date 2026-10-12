using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tahona.Domain.Authorization;
using Tahona.Domain.Common;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds a realistic Amman clinic. Dates are generated relative to "now" so the Revenue Recovery dashboard is
/// populated on first launch: unstarted plans, stalled treatment, no-shows, overdue balances, recalls, clinical
/// follow-ups and already-recovered revenue. Deterministic (fixed random seed).
/// </summary>
public sealed class DemoDataSeeder(AppDbContext db, IPasswordHasher<User> hasher, ILogger<DemoDataSeeder> logger)
{
    private readonly Random _rng = new(20261007);
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");
    private Guid _clinicId;
    private ClinicSettings _settings = default!;
    private Dictionary<string, Procedure> _proc = default!;
    private List<Dentist> _dentists = default!;
    private List<User> _staff = default!;
    private readonly List<Appointment> _appointments = [];
    private readonly List<TreatmentPlan> _plans = [];
    private readonly List<TreatmentRecord> _records = [];
    private readonly List<Invoice> _invoices = [];
    private readonly List<Payment> _payments = [];
    private readonly List<DentalTooth> _teeth = [];
    private readonly List<DentalCondition> _conditions = [];
    private readonly List<ClinicalNote> _notes = [];
    private readonly List<FollowUp> _followUps = [];
    private readonly List<Message> _messages = [];

    public async Task SeedAsync(string demoPassword, CancellationToken ct)
    {
        if (await db.Clinics.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Database already contains data; demo seed skipped.");
            return;
        }

        logger.LogInformation("Seeding demo clinic data…");
        db.SuppressAudit = true;

        var clinic = new Clinic
        {
            Name = "Smile Line Dental Clinic",
            NameAr = "عيادة سمايل لاين لطب الأسنان",
            Slug = "smile-line",
            Phone = "+962 6 592 1234",
            Email = "hello@smileline.jo",
            Address = "Abdoun, Cairo Street, Building 14",
            City = "Amman",
            CreatedAt = _now.AddMonths(-20)
        };
        _clinicId = clinic.Id;
        db.Clinics.Add(clinic);

        _settings = new ClinicSettings { ClinicId = _clinicId, NextPatientNumber = 1, NextPlanNumber = 1, NextInvoiceNumber = 1001, NextReceiptNumber = 5001 };
        db.ClinicSettings.Add(_settings);

        var roles = SystemRoles.DefaultPermissions.ToDictionary(kv => kv.Key, kv => new Role
        {
            ClinicId = _clinicId, Name = kv.Key, IsSystem = true, Permissions = kv.Value.ToList(),
            Description = kv.Key switch
            {
                SystemRoles.Admin => "Full access to every module and setting",
                SystemRoles.Dentist => "Clinical work: charts, treatment plans, notes",
                SystemRoles.Receptionist => "Front desk: patients, appointments, payments, recovery calls",
                _ => "Finance: invoices, payments, expenses, reports"
            }
        });
        db.Roles.AddRange(roles.Values);

        User NewUser(string email, string name, string? nameAr, string role, string? phone = null)
        {
            var u = new User { ClinicId = _clinicId, Email = email, FullName = name, FullNameAr = nameAr, RoleId = roles[role].Id, Phone = phone, LastLoginAt = _now.AddHours(-_rng.Next(1, 48)) };
            u.PasswordHash = hasher.HashPassword(u, demoPassword);
            return u;
        }

        var admin = NewUser("admin@tahona.demo", "Dr. Rana Haddad", "د. رنا حداد", SystemRoles.Admin, "0795550101");
        var reception = NewUser("reception@tahona.demo", "Lina Saleh", "لينا صالح", SystemRoles.Receptionist, "0785550102");
        var accountant = NewUser("accountant@tahona.demo", "Omar Nasser", "عمر ناصر", SystemRoles.Accountant, "0775550103");
        var dentistUsers = new[]
        {
            NewUser("dentist@tahona.demo", "Dr. Sami Abu-Zeid", "د. سامي أبو زيد", SystemRoles.Dentist, "0795550111"),
            NewUser("dr.layla@tahona.demo", "Dr. Layla Mansour", "د. ليلى منصور", SystemRoles.Dentist, "0795550112"),
            NewUser("dr.khaled@tahona.demo", "Dr. Khaled Obeidat", "د. خالد العبيدات", SystemRoles.Dentist, "0795550113"),
            NewUser("dr.noor@tahona.demo", "Dr. Noor Al-Masri", "د. نور المصري", SystemRoles.Dentist, "0795550114")
        };
        _staff = [admin, reception, accountant, .. dentistUsers];
        db.Users.AddRange(_staff);

        _dentists =
        [
            new() { ClinicId = _clinicId, UserId = admin.Id, FullName = admin.FullName, FullNameAr = admin.FullNameAr, Specialty = "Prosthodontics & Implants", Color = "#0d9488", LicenseNumber = "JDA-10442", Phone = admin.Phone, Email = admin.Email },
            new() { ClinicId = _clinicId, UserId = dentistUsers[0].Id, FullName = dentistUsers[0].FullName, FullNameAr = dentistUsers[0].FullNameAr, Specialty = "Endodontics", Color = "#6366f1", LicenseNumber = "JDA-11873", Phone = dentistUsers[0].Phone, Email = dentistUsers[0].Email },
            new() { ClinicId = _clinicId, UserId = dentistUsers[1].Id, FullName = dentistUsers[1].FullName, FullNameAr = dentistUsers[1].FullNameAr, Specialty = "Cosmetic & General Dentistry", Color = "#ec4899", LicenseNumber = "JDA-12560", Phone = dentistUsers[1].Phone, Email = dentistUsers[1].Email },
            new() { ClinicId = _clinicId, UserId = dentistUsers[2].Id, FullName = dentistUsers[2].FullName, FullNameAr = dentistUsers[2].FullNameAr, Specialty = "Oral Surgery", Color = "#f59e0b", LicenseNumber = "JDA-09911", Phone = dentistUsers[2].Phone, Email = dentistUsers[2].Email },
            new() { ClinicId = _clinicId, UserId = dentistUsers[3].Id, FullName = dentistUsers[3].FullName, FullNameAr = dentistUsers[3].FullNameAr, Specialty = "Pediatric Dentistry", Color = "#0ea5e9", LicenseNumber = "JDA-13207", Phone = dentistUsers[3].Phone, Email = dentistUsers[3].Email }
        ];
        db.Dentists.AddRange(_dentists);

        SeedProcedures();
        var templates = SeedTemplates();
        SeedRules(templates);

        var patients = BuildPatients();
        db.Patients.AddRange(patients);
        BuildStories(patients);
        SeedExpenses(accountant.Id);

        db.Appointments.AddRange(_appointments);
        db.TreatmentPlans.AddRange(_plans);
        db.TreatmentRecords.AddRange(_records);
        db.Invoices.AddRange(_invoices);
        db.Payments.AddRange(_payments);
        db.DentalTeeth.AddRange(_teeth);
        db.DentalConditions.AddRange(_conditions);
        db.ClinicalNotes.AddRange(_notes);
        db.FollowUps.AddRange(_followUps);
        db.Messages.AddRange(_messages);

        foreach (var p in patients)
        {
            var visits = _appointments.Where(a => a.PatientId == p.Id && a.Status == AppointmentStatus.Completed).Select(a => (DateTime?)a.StartTime);
            var treatments = _records.Where(r => r.PatientId == p.Id).Select(r => (DateTime?)r.PerformedAt);
            p.LastVisitAt = visits.Concat(treatments).Max();
        }

        await db.SaveChangesAsync(ct);
        db.SuppressAudit = false;
        logger.LogInformation("Demo seed complete: {Patients} patients, {Appointments} appointments, {Plans} plans, {Invoices} invoices, {Payments} payments.",
            patients.Count, _appointments.Count, _plans.Count, _invoices.Count, _payments.Count);
    }

    // ================================================================== catalogue & configuration

    private void SeedProcedures()
    {
        (string Code, string Name, string Ar, ProcedureCategory Cat, decimal Price, int Min, ToothConditionType? Result)[] list =
        [
            ("EXAM", "Consultation & examination", "استشارة وفحص", ProcedureCategory.Diagnostic, 15, 20, null),
            ("PANO", "Panoramic X-ray", "صورة بانوراما", ProcedureCategory.Diagnostic, 25, 15, null),
            ("PA", "Periapical X-ray", "صورة أشعة ذروية", ProcedureCategory.Diagnostic, 20, 10, null),
            ("CLEAN", "Scaling & polishing", "تنظيف وتلميع", ProcedureCategory.Preventive, 35, 30, null),
            ("FLUOR", "Fluoride application", "تطبيق الفلورايد", ProcedureCategory.Preventive, 15, 15, null),
            ("SEAL", "Fissure sealant", "سد الشقوق", ProcedureCategory.Preventive, 20, 20, ToothConditionType.Sealant),
            ("COMP", "Composite filling", "حشوة تجميلية", ProcedureCategory.Restorative, 40, 45, ToothConditionType.Filling),
            ("RCT-A", "Root canal (anterior)", "علاج عصب أمامي", ProcedureCategory.Endodontic, 120, 60, ToothConditionType.RootCanal),
            ("RCT-M", "Root canal (molar)", "علاج عصب ضرس", ProcedureCategory.Endodontic, 180, 90, ToothConditionType.RootCanal),
            ("RERCT", "Root canal re-treatment", "إعادة علاج عصب", ProcedureCategory.Endodontic, 220, 90, ToothConditionType.RootCanal),
            ("POST", "Post & core", "وتد وقلب", ProcedureCategory.Restorative, 60, 30, null),
            ("PFM", "PFM crown", "تلبيسة خزف على معدن", ProcedureCategory.Prosthodontic, 180, 60, ToothConditionType.Crown),
            ("ZIRC", "Zirconia crown", "تلبيسة زيركون", ProcedureCategory.Prosthodontic, 250, 60, ToothConditionType.Crown),
            ("BRIDGE", "Bridge unit (zirconia)", "وحدة جسر زيركون", ProcedureCategory.Prosthodontic, 230, 60, ToothConditionType.Bridge),
            ("VENEER", "E-max veneer", "قشرة إيماكس", ProcedureCategory.Cosmetic, 280, 60, ToothConditionType.Veneer),
            ("WHITEN", "In-office whitening", "تبييض في العيادة", ProcedureCategory.Cosmetic, 200, 75, null),
            ("EXT", "Simple extraction", "خلع بسيط", ProcedureCategory.OralSurgery, 30, 30, ToothConditionType.Extraction),
            ("SEXT", "Surgical extraction", "خلع جراحي", ProcedureCategory.OralSurgery, 80, 45, ToothConditionType.Extraction),
            ("WISDOM", "Impacted wisdom tooth surgery", "جراحة ضرس العقل", ProcedureCategory.OralSurgery, 150, 60, ToothConditionType.Extraction),
            ("IMPL", "Dental implant (fixture)", "زراعة سن", ProcedureCategory.Implant, 650, 90, ToothConditionType.Implant),
            ("IMPLCR", "Implant crown", "تاج على الزرعة", ProcedureCategory.Implant, 350, 45, ToothConditionType.Crown),
            ("DEEP", "Deep cleaning (per quadrant)", "تنظيف عميق للربع", ProcedureCategory.Periodontal, 50, 45, null),
            ("PULPO", "Pulpotomy (primary tooth)", "بتر لب سن لبني", ProcedureCategory.Pediatric, 45, 30, ToothConditionType.RootCanal),
            ("SSC", "Stainless steel crown (child)", "تاج ستانلس للأطفال", ProcedureCategory.Pediatric, 55, 30, ToothConditionType.Crown),
            ("GUARD", "Night guard", "واقي ليلي", ProcedureCategory.Prosthodontic, 120, 20, null),
            ("ORTHO", "Orthodontic consultation", "استشارة تقويم", ProcedureCategory.Orthodontic, 20, 30, null)
        ];
        _proc = list.ToDictionary(p => p.Code, p => new Procedure
        {
            ClinicId = _clinicId, Code = p.Code, Name = p.Name, NameAr = p.Ar, Category = p.Cat, DefaultPrice = p.Price,
            DefaultDurationMinutes = p.Min, ResultingCondition = p.Result
        });
        db.Procedures.AddRange(_proc.Values);
    }

    private Dictionary<(MessageTemplateType, string), MessageTemplate> SeedTemplates()
    {
        (MessageTemplateType Type, string Lang, string Name, string Body)[] items =
        [
            (MessageTemplateType.AppointmentReminder, "en", "Appointment reminder",
                "Hello {{patient_name}}, this is a reminder that you have an appointment with {{doctor}} on {{date}} at {{time}} at {{clinic_name}}. Reply 1 to confirm or call us to reschedule."),
            (MessageTemplateType.AppointmentReminder, "ar", "تذكير بالموعد",
                "مرحباً {{patient_name}}، نذكّرك بموعدك مع {{doctor}} يوم {{date}} الساعة {{time}} في {{clinic_name}}. أرسل 1 للتأكيد أو اتصل بنا لتغيير الموعد."),
            (MessageTemplateType.TreatmentFollowUp, "en", "Treatment follow-up",
                "Hello {{patient_name}}, your dentist recommended a treatment plan ({{treatment}}). We would be happy to help you schedule your next appointment. — {{clinic_name}}"),
            (MessageTemplateType.TreatmentFollowUp, "ar", "متابعة خطة العلاج",
                "مرحباً {{patient_name}}، أوصى طبيب الأسنان بخطة علاج ({{treatment}}). يسعدنا مساعدتك في حجز موعدك القادم. — {{clinic_name}}"),
            (MessageTemplateType.MissedAppointment, "en", "Missed appointment",
                "Hello {{patient_name}}, we noticed you missed your appointment. Would you like us to help you reschedule? — {{clinic_name}}"),
            (MessageTemplateType.MissedAppointment, "ar", "موعد فائت",
                "مرحباً {{patient_name}}، لاحظنا أنك لم تتمكن من حضور موعدك. هل ترغب أن نساعدك في تحديد موعد جديد؟ — {{clinic_name}}"),
            (MessageTemplateType.Recall, "en", "Routine recall",
                "Hello {{patient_name}}, it has been a while since your last dental visit. Would you like to schedule your next check-up and cleaning? — {{clinic_name}}"),
            (MessageTemplateType.Recall, "ar", "تذكير بالفحص الدوري",
                "مرحباً {{patient_name}}، مضى وقت على زيارتك الأخيرة لطبيب الأسنان. هل ترغب بحجز موعد للفحص والتنظيف الدوري؟ — {{clinic_name}}"),
            (MessageTemplateType.PaymentReminder, "en", "Payment reminder",
                "Hello {{patient_name}}, this is a friendly reminder that your account at {{clinic_name}} has an outstanding balance of {{amount}}. You can pay at the clinic, by card, or via CliQ. Thank you!"),
            (MessageTemplateType.PaymentReminder, "ar", "تذكير بالدفع",
                "مرحباً {{patient_name}}، نود تذكيرك بوجود رصيد مستحق بقيمة {{amount}} في {{clinic_name}}. يمكنك الدفع في العيادة أو بالبطاقة أو عبر كليك. شكراً لك!"),
            (MessageTemplateType.PostTreatment, "en", "Clinical review",
                "Hello {{patient_name}}, {{doctor}} would like to see you for a short review after your recent treatment ({{treatment}}). Shall we book a convenient time? — {{clinic_name}}"),
            (MessageTemplateType.PostTreatment, "ar", "مراجعة بعد العلاج",
                "مرحباً {{patient_name}}، يرغب {{doctor}} برؤيتك لمراجعة قصيرة بعد علاجك الأخير ({{treatment}}). هل نحجز لك موعداً مناسباً؟ — {{clinic_name}}")
        ];
        var map = items.ToDictionary(i => (i.Type, i.Lang), i => new MessageTemplate
        {
            ClinicId = _clinicId, Type = i.Type, Language = i.Lang, Name = i.Name, Body = i.Body, IsSystem = true
        });
        db.MessageTemplates.AddRange(map.Values);
        return map;
    }

    private void SeedRules(Dictionary<(MessageTemplateType, string), MessageTemplate> t)
    {
        FollowUpRule R(string name, FollowUpTrigger trigger, int days, PriorityLevel p, MessageTemplateType? type, ProcedureCategory? cat = null) => new()
        {
            ClinicId = _clinicId, Name = name, Trigger = trigger, DelayDays = days, Priority = p, ProcedureCategory = cat,
            MessageTemplateId = type is null ? null : t[(type.Value, "en")].Id
        };
        db.FollowUpRules.AddRange(
            R("Missed appointment — follow up next day", FollowUpTrigger.MissedAppointment, 1, PriorityLevel.High, MessageTemplateType.MissedAppointment),
            R("Treatment plan not started", FollowUpTrigger.TreatmentPlanNotStarted, 3, PriorityLevel.High, MessageTemplateType.TreatmentFollowUp),
            R("Treatment plan still inactive — re-contact", FollowUpTrigger.TreatmentPlanInactive, 7, PriorityLevel.Medium, MessageTemplateType.TreatmentFollowUp),
            R("Treatment unfinished", FollowUpTrigger.TreatmentUnfinished, 14, PriorityLevel.High, MessageTemplateType.TreatmentFollowUp),
            R("Root canal review", FollowUpTrigger.ProcedureCompleted, 14, PriorityLevel.Medium, MessageTemplateType.PostTreatment, ProcedureCategory.Endodontic),
            R("Post-surgery check", FollowUpTrigger.ProcedureCompleted, 7, PriorityLevel.Medium, MessageTemplateType.PostTreatment, ProcedureCategory.OralSurgery),
            R("Routine cleaning recall (6 months)", FollowUpTrigger.RoutineRecall, 180, PriorityLevel.Low, MessageTemplateType.Recall),
            R("Overdue balance", FollowUpTrigger.OverdueBalance, 0, PriorityLevel.Medium, MessageTemplateType.PaymentReminder));
    }

    // ================================================================== patients

    private static readonly (string En, string Ar, Gender G)[] FirstNames =
    [
        ("Ahmad", "أحمد", Gender.Male), ("Mohammad", "محمد", Gender.Male), ("Omar", "عمر", Gender.Male), ("Yousef", "يوسف", Gender.Male),
        ("Khaled", "خالد", Gender.Male), ("Faris", "فارس", Gender.Male), ("Hamza", "حمزة", Gender.Male), ("Zaid", "زيد", Gender.Male),
        ("Tariq", "طارق", Gender.Male), ("Majd", "مجد", Gender.Male), ("Laith", "ليث", Gender.Male), ("Basel", "باسل", Gender.Male),
        ("Ibrahim", "إبراهيم", Gender.Male), ("Saif", "سيف", Gender.Male), ("Hasan", "حسن", Gender.Male), ("Anas", "أنس", Gender.Male),
        ("Rami", "رامي", Gender.Male), ("Fadi", "فادي", Gender.Male), ("Nour", "نور", Gender.Female), ("Lina", "لينا", Gender.Female),
        ("Sara", "سارة", Gender.Female), ("Rania", "رانيا", Gender.Female), ("Dana", "دانا", Gender.Female), ("Hala", "هالة", Gender.Female),
        ("Reem", "ريم", Gender.Female), ("Farah", "فرح", Gender.Female), ("Yasmin", "ياسمين", Gender.Female), ("Maha", "مها", Gender.Female),
        ("Aseel", "أسيل", Gender.Female), ("Haneen", "حنين", Gender.Female), ("Ruba", "ربى", Gender.Female), ("Lama", "لمى", Gender.Female),
        ("Tala", "تالا", Gender.Female), ("Bayan", "بيان", Gender.Female), ("Shatha", "شذى", Gender.Female), ("Muna", "منى", Gender.Female)
    ];

    private static readonly (string En, string Ar)[] LastNames =
    [
        ("Khalil", "خليل"), ("Haddad", "حداد"), ("Masri", "المصري"), ("Obeidat", "العبيدات"), ("Zoubi", "الزعبي"), ("Tarawneh", "الطراونة"),
        ("Majali", "المجالي"), ("Rawashdeh", "الرواشدة"), ("Khatib", "الخطيب"), ("Nasser", "ناصر"), ("Saleh", "صالح"), ("Hijazi", "حجازي"),
        ("Qudah", "القضاة"), ("Bataineh", "البطاينة"), ("Smadi", "الصمادي"), ("Jaber", "جابر"), ("Shami", "الشامي"), ("Awad", "عوض"),
        ("Abbadi", "العبادي"), ("Momani", "المومني"), ("Hourani", "الحوراني"), ("Darwish", "درويش"), ("Najjar", "النجار"), ("Ayoub", "أيوب")
    ];

    private static readonly string[] Areas = ["Abdoun", "Sweifieh", "Khalda", "Jubeiha", "Dabouq", "Shmeisani", "Tla' Al-Ali", "Marj Al-Hamam", "Abu Nseir", "Rabieh", "Deir Ghbar", "Um Uthaina"];
    private static readonly string[] Insurers = ["NatHealth", "MedNet Jordan", "GlobeMed", "Jordan Insurance Co.", "Arab Orient Insurance", "GIG Jordan"];

    private enum Story { Loyal, NotStarted, Unfinished, Missed, Balance, Recall, RootCanalReview, InProgressBooked, RecoveredThisMonth, RecoveredLastMonth, RecoveredEarlier, Established, New }

    private int _contactedNotStarted;
    private int _noResponseUnfinished;
    private int _earlierRecovered;
    private int _recentMisses;

    private readonly Dictionary<Guid, Story> _story = [];

    private List<Patient> BuildPatients()
    {
        var plan = new (Story Story, int Count)[]
        {
            (Story.Loyal, 14), (Story.NotStarted, 9), (Story.Unfinished, 8), (Story.Missed, 7), (Story.Balance, 7), (Story.Recall, 8),
            (Story.RootCanalReview, 3), (Story.InProgressBooked, 6), (Story.RecoveredThisMonth, 3), (Story.RecoveredLastMonth, 2), (Story.RecoveredEarlier, 6), (Story.Established, 70), (Story.New, 5)
        };

        var patients = new List<Patient>();
        var usedNames = new HashSet<string>();
        var number = 1;
        foreach (var (story, count) in plan)
        {
            for (var i = 0; i < count; i++)
            {
                (string En, string Ar, Gender G) first;
                (string En, string Ar) last;
                do
                {
                    first = FirstNames[_rng.Next(FirstNames.Length)];
                    last = LastNames[_rng.Next(LastNames.Length)];
                } while (!usedNames.Add(first.En + last.En));

                // The spec example patient.
                if (story == Story.NotStarted && i == 0) { first = ("Ahmad", "أحمد", Gender.Male); last = ("Khalil", "خليل"); }

                var isChild = story is Story.Loyal or Story.Recall or Story.Established && _rng.NextDouble() < 0.12;
                var age = isChild ? _rng.Next(6, 12) : _rng.Next(19, 72);
                var phone = $"07{new[] { 7, 8, 9 }[_rng.Next(3)]}{_rng.Next(1000000, 9999999)}";
                if (story == Story.NotStarted && i == 0) phone = "0791234567";

                var registered = story == Story.New ? _now.AddDays(-_rng.Next(0, 9)) : _now.AddDays(-_rng.Next(story == Story.Established ? 380 : 200, 700));
                var p = new Patient
                {
                    ClinicId = _clinicId,
                    FileNumber = $"P-{number++:D5}",
                    FullName = $"{first.En} {last.En}",
                    FullNameAr = $"{first.Ar} {last.Ar}",
                    Gender = first.G,
                    DateOfBirth = DateOnly.FromDateTime(_now.AddYears(-age).AddDays(-_rng.Next(0, 365))),
                    Phone = phone,
                    PhoneNormalized = PhoneNumber.Normalize(phone),
                    WhatsAppNumber = phone,
                    Email = _rng.NextDouble() < 0.5 ? $"{first.En.ToLowerInvariant()}.{last.En.ToLowerInvariant()}@gmail.com" : null,
                    City = "Amman",
                    Address = Areas[_rng.Next(Areas.Length)],
                    PreferredLanguage = _rng.NextDouble() < 0.7 ? "ar" : "en",
                    RegisteredAt = registered,
                    CreatedAt = registered,
                    ReferralSource = new[] { "Instagram", "Google", "Friend referral", "Walk-in", "Insurance network" }[_rng.Next(5)],
                    PrimaryDentistId = isChild ? _dentists[4].Id : _dentists[_rng.Next(4)].Id,
                    InsuranceProvider = _rng.NextDouble() < 0.35 ? Insurers[_rng.Next(Insurers.Length)] : null,
                    EmergencyContactName = _rng.NextDouble() < 0.6 ? $"{FirstNames[_rng.Next(FirstNames.Length)].En} {last.En}" : null,
                    EmergencyContactRelation = "Family",
                    Allergies = _rng.NextDouble() < 0.12 ? new[] { "Penicillin", "Latex", "Ibuprofen" }[_rng.Next(3)] : null,
                    MedicalConditions = _rng.NextDouble() < 0.15 ? new[] { "Type 2 diabetes", "Hypertension", "Asthma" }[_rng.Next(3)] : null,
                    CurrentMedications = null
                };
                if (p.InsuranceProvider is not null) { p.InsurancePolicyNumber = $"POL-{_rng.Next(100000, 999999)}"; p.InsuranceCoveragePercent = new[] { 50m, 60m, 80m }[_rng.Next(3)]; }
                if (p.EmergencyContactName is not null) p.EmergencyContactPhone = $"079{_rng.Next(1000000, 9999999)}";
                if (p.MedicalConditions == "Type 2 diabetes") p.CurrentMedications = "Metformin 500mg";
                if (p.MedicalConditions == "Hypertension") p.CurrentMedications = "Amlodipine 5mg";
                if (isChild) { p.EmergencyContactName ??= $"Parent of {first.En}"; p.EmergencyContactRelation = "Parent"; p.EmergencyContactPhone ??= phone; }
                patients.Add(p);
                _story[p.Id] = story;
            }
        }
        _settings.NextPatientNumber = number;
        return patients;
    }

    // ================================================================== stories

    private void BuildStories(List<Patient> patients)
    {
        var todayLocal = Local(_now).Date;
        var todaySlots = new Queue<(int Hour, int Minute)>([(9, 0), (9, 30), (10, 0), (10, 30), (11, 30), (12, 0), (13, 0), (14, 0), (15, 30), (16, 0), (17, 0), (18, 0)]);

        foreach (var p in patients)
        {
            var dentist = _dentists.First(d => d.Id == p.PrimaryDentistId);
            var isChild = dentist == _dentists[4];
            switch (_story[p.Id])
            {
                case Story.Loyal: Loyal(p, dentist, isChild, todaySlots); break;
                case Story.NotStarted: NotStarted(p, dentist); break;
                case Story.Unfinished: Unfinished(p, dentist); break;
                case Story.Missed: Missed(p, dentist); break;
                case Story.Balance: Balance(p, dentist); break;
                case Story.Recall: Recall(p, dentist, isChild); break;
                case Story.RootCanalReview: RootCanalReview(p); break;
                case Story.InProgressBooked: InProgressBooked(p, dentist, todaySlots); break;
                case Story.RecoveredThisMonth: Recovered(p, dentist, 0); break;
                case Story.RecoveredLastMonth: Recovered(p, dentist, 1); break;
                case Story.RecoveredEarlier: Recovered(p, dentist, 2 + _earlierRecovered++ % 4); break;
                case Story.Established: Established(p, dentist, isChild); break;
                case Story.New: NewPatient(p, dentist, todaySlots); break;
            }
        }
        _ = todayLocal;
    }

    /// <summary>Regular patient: check-ups every ~6 months, small treatments, everything paid. Some have a check-up booked.</summary>
    private void Loyal(Patient p, Dentist d, bool child, Queue<(int, int)> todaySlots)
    {
        var first = _rng.Next(420, 560);
        for (var daysAgo = first; daysAgo > 60; daysAgo -= _rng.Next(150, 200))
        {
            var visit = Visit(p, d, _now.AddDays(-daysAgo), child ? "Check-up & fluoride" : "Check-up & cleaning", 30, AppointmentStatus.Completed);
            var codes = child ? new[] { "EXAM", "FLUOR" } : new[] { "EXAM", "CLEAN" };
            var extra = _rng.NextDouble() < 0.4 ? (child ? "SEAL" : "COMP") : null;
            var tooth = child ? Pick(Teeth.Permanent.Where(t => t % 10 is 6 or 7).ToList()) : Pick(Teeth.Permanent.ToList());
            var items = codes.Select(c => (c, (int?)null)).ToList();
            if (extra is not null) items.Add((extra, tooth));
            CompletedVisit(p, d, visit.StartTime, items);
        }

        var roll = _rng.NextDouble();
        if (roll < 0.35 && todaySlots.Count > 4)
        {
            var (h, m) = todaySlots.Dequeue();
            TodayVisit(p, d, h, m, child ? "Check-up & fluoride" : "Check-up & cleaning", 30, child ? ["EXAM", "FLUOR"] : ["EXAM", "CLEAN"]);
        }
        else if (roll < 0.75)
        {
            Visit(p, d, Future(1, 20), child ? "Check-up & fluoride" : "Check-up & cleaning", 30,
                _rng.NextDouble() < 0.5 ? AppointmentStatus.Confirmed : AppointmentStatus.Scheduled);
            // Last check-up within recall window.
            var recent = Visit(p, d, _now.AddDays(-_rng.Next(30, 150)), "Check-up & cleaning", 30, AppointmentStatus.Completed);
            CompletedVisit(p, d, recent.StartTime, [("EXAM", null), ("CLEAN", null)]);
        }
        else
        {
            var recent = Visit(p, d, _now.AddDays(-_rng.Next(20, 140)), "Check-up & cleaning", 30, AppointmentStatus.Completed);
            CompletedVisit(p, d, recent.StartTime, [("EXAM", null), ("CLEAN", null)]);
            if (_rng.NextDouble() < 0.4)
            {
                // A plan the patient declined (abandoned / rejected metrics).
                var rejected = NewPlan(p, d, recent.StartTime, "Cosmetic veneers", TreatmentPlanStatus.Rejected,
                    [("VENEER", 11), ("VENEER", 21)]);
                rejected.PresentedAt = recent.StartTime;
                rejected.RejectedAt = recent.StartTime.AddDays(5);
                rejected.RejectionReason = "Cost — patient will reconsider next year";
            }
        }
    }

    /// <summary>Consultation done, plan presented/accepted, never booked — the classic leak.</summary>
    private void NotStarted(Patient p, Dentist d)
    {
        var isAhmad = p.PhoneNormalized == "791234567";
        var daysAgo = isAhmad ? 32 : _rng.Next(5, 75);
        var consult = Visit(p, d, _now.AddDays(-daysAgo), "Consultation", 30, AppointmentStatus.Completed);
        CompletedVisit(p, d, consult.StartTime, [("EXAM", null), ("PA", null)]);

        var tooth = isAhmad ? 16 : Pick([16, 26, 36, 46, 17, 27, 37, 47]);
        var options = new List<(string Title, (string, int?)[] Items)>
        {
            ("Root canal & crown", [("RCT-M", tooth), ("ZIRC", tooth), ("PA", tooth)]),
            ("Implant replacement", [("IMPL", tooth), ("IMPLCR", tooth)]),
            ("Fillings & cleaning", [("COMP", tooth), ("COMP", Pick([14, 15, 24, 25])), ("DEEP", null), ("DEEP", null)]),
            ("Smile makeover", [("WHITEN", null), ("VENEER", 11), ("VENEER", 21), ("VENEER", 12), ("VENEER", 22)]),
            ("Wisdom tooth surgery", [("PANO", null), ("WISDOM", 38), ("WISDOM", 48)])
        };
        var choice = isAhmad ? options[0] : options[_rng.Next(options.Count)];
        var status = _rng.NextDouble() < 0.55 ? TreatmentPlanStatus.Presented : TreatmentPlanStatus.Accepted;
        if (isAhmad) status = TreatmentPlanStatus.Presented;
        var plan = NewPlan(p, d, consult.StartTime, choice.Title, status, choice.Items);
        plan.PresentedAt = consult.StartTime.AddMinutes(20);
        if (status == TreatmentPlanStatus.Accepted) plan.AcceptedAt = consult.StartTime.AddDays(_rng.Next(0, 3));
        foreach (var item in plan.Items.Where(i => i.ToothNumber is not null).DistinctBy(i => i.ToothNumber))
            Condition(p, item.ToothNumber!.Value, item.Name.Contains("Implant") ? ToothConditionType.Missing : ToothConditionType.Caries,
                ConditionStatus.Planned, consult.StartTime, d, item.Id);
        Note(p, d, consult.StartTime, isAhmad ? "Pain in upper right molar" : "Routine consultation",
            isAhmad ? "Deep caries visible on 16; periapical radiolucency on X-ray." : "Findings recorded on chart.",
            isAhmad ? "Irreversible pulpitis 16 (dentist's assessment)." : null,
            $"Advised: {choice.Title}.", "Patient will call to book.");

        // Some were already contacted once and are waiting for a reply.
        if (!isAhmad && daysAgo > 8 && _contactedNotStarted++ < 3)
            Contacted(p, RecoveryIssueType.TreatmentNotStarted, nameof(TreatmentPlan), plan.Id, plan.PresentedAt!.Value, _now.AddDays(-_rng.Next(1, 4)),
                PlanRemaining(plan), "Treatment not started");
    }

    /// <summary>Treatment started, part done and paid, then the patient stopped coming.</summary>
    private void Unfinished(Patient p, Dentist d)
    {
        var start = _now.AddDays(-_rng.Next(45, 140));
        var tooth = Pick([16, 26, 36, 46]);
        var second = Pick([14, 15, 24, 25, 34, 35]);
        var plan = NewPlan(p, d, start.AddDays(-7), "Root canal, post & crown", TreatmentPlanStatus.InProgress,
            [("RCT-M", tooth), ("POST", tooth), ("ZIRC", tooth), ("COMP", second)]);
        plan.PresentedAt = start.AddDays(-7);
        plan.AcceptedAt = start.AddDays(-6);
        Visit(p, d, start.AddDays(-7), "Consultation", 30, AppointmentStatus.Completed);

        var v1 = Visit(p, d, start, "Root canal (session 1)", 90, AppointmentStatus.Completed, plan.Id);
        CompleteItem(plan, 0, v1.StartTime, d);
        var last = v1.StartTime;
        if (_rng.NextDouble() < 0.6)
        {
            var v2 = Visit(p, d, start.AddDays(_rng.Next(7, 14)), "Post & core", 30, AppointmentStatus.Completed, plan.Id);
            CompleteItem(plan, 1, v2.StartTime, d);
            last = v2.StartTime;
        }
        plan.StartedAt = v1.StartTime;
        plan.LastActivityAt = last;
        if (_rng.NextDouble() < 0.25) plan.Status = TreatmentPlanStatus.PartiallyCompleted;

        var inv = Invoice(p, d, plan, plan.Items.Where(i => i.Status == TreatmentItemStatus.Completed).ToList(), last);
        Pay(inv, last, inv.Total * (_rng.NextDouble() < 0.5 ? 1m : 0.5m));

        // Two patients were already chased without reply.
        if (_noResponseUnfinished++ < 2)
        {
            var f = Contacted(p, RecoveryIssueType.TreatmentUnfinished, nameof(TreatmentPlan), plan.Id, last, _now.AddDays(-_rng.Next(6, 12)),
                PlanRemaining(plan), "Treatment unfinished");
            f.Status = FollowUpStatus.NoResponse;
            f.Outcome = "NoResponse: no answer on WhatsApp or phone";
        }
    }

    private void Missed(Patient p, Dentist d)
    {
        var missedDaysAgo = _recentMisses++ < 3 ? _rng.Next(1, 5) : _rng.Next(6, 45);
        var history = Visit(p, d, _now.AddDays(-_rng.Next(90, 200)), "Check-up & cleaning", 30, AppointmentStatus.Completed);
        CompletedVisit(p, d, history.StartTime, [("EXAM", null), ("CLEAN", null)]);

        Guid? planId = null;
        var treatment = "Check-up & cleaning";
        if (_rng.NextDouble() < 0.5)
        {
            var tooth = Pick([14, 15, 24, 25, 35, 45]);
            var plan = NewPlan(p, d, history.StartTime, "Composite fillings", TreatmentPlanStatus.Accepted, [("COMP", tooth), ("COMP", Pick([36, 46]))]);
            plan.PresentedAt = history.StartTime;
            plan.AcceptedAt = history.StartTime;
            planId = plan.Id;
            treatment = "Composite fillings";
        }
        Visit(p, d, _now.AddDays(-missedDaysAgo), treatment, 45, AppointmentStatus.NoShow, planId);
    }

    private void Balance(Patient p, Dentist d)
    {
        var date = _now.AddDays(-_rng.Next(25, 110));
        var tooth = Pick([11, 21, 16, 26, 36, 46]);
        var visit = Visit(p, d, date, "Crown fitting", 60, AppointmentStatus.Completed);
        var plan = CompletedPlan(p, d, date, "Crown", [("PFM", tooth), ("PA", tooth)]);
        var inv = Invoice(p, d, plan, plan.Items.ToList(), visit.StartTime);
        var paidShare = new[] { 0m, 0.3m, 0.5m }[_rng.Next(3)];
        if (paidShare > 0) Pay(inv, visit.StartTime, Math.Round(inv.Total * paidShare, 0));
        if (_rng.NextDouble() < 0.4)
            Visit(p, d, Future(3, 25), "Review", 20, AppointmentStatus.Scheduled); // coming back, but still owes
    }

    private void Recall(Patient p, Dentist d, bool child)
    {
        var last = _now.AddDays(-_rng.Next(200, 420));
        var earlier = Visit(p, d, last.AddDays(-_rng.Next(170, 220)), "Check-up & cleaning", 30, AppointmentStatus.Completed);
        CompletedVisit(p, d, earlier.StartTime, [("EXAM", null), (child ? "FLUOR" : "CLEAN", null)]);
        var visit = Visit(p, d, last, "Check-up & cleaning", 30, AppointmentStatus.Completed);
        CompletedVisit(p, d, visit.StartTime, [("EXAM", null), (child ? "FLUOR" : "CLEAN", null)]);
    }

    private void RootCanalReview(Patient p)
    {
        var endo = _dentists[1];
        var date = _now.AddDays(-_rng.Next(18, 50));
        var tooth = Pick([11, 21, 12, 22]);
        var visit = Visit(p, endo, date, "Emergency root canal", 60, AppointmentStatus.Completed);
        var plan = CompletedPlan(p, endo, visit.StartTime, "Emergency root canal", [("PA", tooth), ("RCT-A", tooth)]);
        BillAndPay(p, endo, plan, visit.StartTime, full: true);
        Note(p, endo, date, "Severe throbbing pain, anterior tooth", $"Tooth {tooth} tender to percussion, deep caries.", "Symptomatic apical periodontitis (dentist's assessment).",
            "Root canal treatment completed in one visit.", "Review in 2 weeks; crown recommended.");
    }

    private void InProgressBooked(Patient p, Dentist d, Queue<(int, int)> todaySlots)
    {
        var start = _now.AddDays(-_rng.Next(10, 40));
        var tooth = Pick([36, 46, 37, 47]);
        var plan = NewPlan(p, d, start.AddDays(-5), "Root canal & crown", TreatmentPlanStatus.InProgress, [("RCT-M", tooth), ("ZIRC", tooth)]);
        plan.PresentedAt = start.AddDays(-5);
        plan.AcceptedAt = start.AddDays(-4);
        var v1 = Visit(p, d, start, "Root canal", 90, AppointmentStatus.Completed, plan.Id);
        CompleteItem(plan, 0, v1.StartTime, d);
        plan.StartedAt = v1.StartTime;
        plan.LastActivityAt = v1.StartTime;
        var inv = Invoice(p, d, plan, [plan.Items.ElementAt(0)], v1.StartTime);
        Pay(inv, v1.StartTime, inv.Total);

        if (todaySlots.Count > 2 && _rng.NextDouble() < 0.5)
        {
            var (h, m) = todaySlots.Dequeue();
            var appt = TodayVisit(p, d, h, m, "Crown preparation", 60, null);
            appt.TreatmentPlanId = plan.Id;
            // Mirrors AppointmentService: attending a plan visit counts as plan activity.
            if (appt.Status is AppointmentStatus.Completed or AppointmentStatus.InTreatment or AppointmentStatus.Arrived) plan.LastActivityAt = appt.StartTime;
        }
        else Visit(p, d, Future(1, 12), "Crown preparation", 60, AppointmentStatus.Confirmed, plan.Id);
    }

    /// <summary>Patient flagged by the engine, contacted by reception, came back and paid — counted as recovered revenue.</summary>
    private void Recovered(Patient p, Dentist d, int monthsBack)
    {
        var monthStartLocal = new DateTime(Local(_now).Year, Local(_now).Month, 1);
        var monthStart = ToUtc(monthStartLocal.AddMonths(-monthsBack));
        var monthEnd = monthsBack == 0 ? _now : ToUtc(monthStartLocal.AddMonths(-monthsBack + 1));
        var span = Math.Max(1, (monthEnd - monthStart).TotalHours - 2);
        var paidAt = monthStart.AddHours(1 + _rng.NextDouble() * span);
        var contactedAt = paidAt.AddDays(-_rng.Next(3, 10));
        var consult = contactedAt.AddDays(-_rng.Next(20, 50));

        Visit(p, d, consult, "Consultation", 30, AppointmentStatus.Completed);
        var tooth = Pick([16, 26, 36, 46]);
        var plan = NewPlan(p, d, consult, "Root canal & crown", TreatmentPlanStatus.InProgress, [("RCT-M", tooth), ("ZIRC", tooth)]);
        plan.PresentedAt = consult;
        plan.AcceptedAt = contactedAt.AddDays(1);

        var followUp = Contacted(p, RecoveryIssueType.TreatmentNotStarted, nameof(TreatmentPlan), plan.Id, consult, contactedAt, PlanRemaining(plan), "Treatment not started");
        var appt = Visit(p, d, paidAt.AddMinutes(-90), "Root canal", 90, AppointmentStatus.Completed, plan.Id);
        appt.FollowUpId = followUp.Id;
        appt.CreatedAt = contactedAt.AddHours(2);
        CompleteItem(plan, 0, appt.StartTime, d);
        plan.StartedAt = appt.StartTime;
        plan.LastActivityAt = appt.StartTime;

        var inv = Invoice(p, d, plan, [plan.Items.ElementAt(0)], appt.StartTime);
        paidAt = appt.EndTime > _now ? _now : appt.EndTime;
        if (paidAt < monthStart) paidAt = monthStart.AddHours(1);
        var pay = Pay(inv, paidAt, inv.Total);
        pay.RecoveredFollowUpId = followUp.Id;
        followUp.Status = FollowUpStatus.Recovered;
        followUp.RecoveredAmount = pay.Amount;
        followUp.ResolvedAt = paidAt;
        followUp.BookedAppointmentId = appt.Id;
        followUp.Outcome = "Revenue recovered";

        if (monthsBack >= 2)
        {
            // Older recoveries: the crown was completed too and the plan closed.
            var crownAt = appt.StartTime.AddDays(_rng.Next(10, 18));
            var crown = Visit(p, d, crownAt, "Crown fitting", 60, AppointmentStatus.Completed, plan.Id);
            CompleteItem(plan, 1, crown.StartTime, d);
            plan.Status = TreatmentPlanStatus.Completed;
            plan.CompletedAt = plan.LastActivityAt = crown.StartTime;
            var crownInvoice = Invoice(p, d, plan, [plan.Items.ElementAt(1)], crown.StartTime);
            var crownPay = Pay(crownInvoice, crown.EndTime, crownInvoice.Total);
            crownPay.RecoveredFollowUpId = followUp.Id;
            followUp.RecoveredAmount += crownPay.Amount;
        }
        else if (paidAt < _now.AddDays(-2)) Visit(p, d, Future(2, 14), "Crown preparation", 60, AppointmentStatus.Scheduled, plan.Id);
    }

    /// <summary>Established patient with a year of varied, fully paid treatment — the clinic's steady revenue base.</summary>
    private void Established(Patient p, Dentist d, bool child)
    {
        var visits = _rng.Next(4, 9);
        var days = Enumerable.Range(0, visits).Select(_ => _rng.Next(10, 365)).ToList();
        if (_rng.NextDouble() < 0.35) days.Add(_rng.Next(1, 7)); // seen this week
        days = days.OrderByDescending(x => x).ToList();
        foreach (var daysAgo in days)
        {
            var at = _now.AddDays(-daysAgo);
            var roll = _rng.NextDouble();
            var tooth = Pick(Teeth.Permanent.ToList());
            (string Title, List<(string, int?)> Items, int Minutes) visit = child
                ? (roll < 0.6 ? ("Check-up & fluoride", [("EXAM", null), ("FLUOR", null)], 30) : ("Pulpotomy & crown", [("PULPO", Pick([54, 64, 74, 84])), ("SSC", Pick([54, 64, 74, 84]))], 45))
                : roll switch
                {
                    < 0.30 => ("Check-up & cleaning", [("EXAM", null), ("CLEAN", null)], 30),
                    < 0.50 => ("Composite fillings", [("COMP", tooth), ("COMP", Pick([14, 15, 24, 25, 34, 35, 44, 45]))], 60),
                    < 0.62 => ("Root canal & crown", [("PA", tooth), ("RCT-M", tooth), ("ZIRC", tooth)], 90),
                    < 0.70 => ("Extraction", [("PA", tooth), ("SEXT", tooth)], 45),
                    < 0.77 => ("Deep cleaning", [("DEEP", null), ("DEEP", null), ("DEEP", null), ("DEEP", null)], 60),
                    < 0.83 => ("Teeth whitening", [("CLEAN", null), ("WHITEN", null)], 75),
                    < 0.89 => ("Crown", [("PA", tooth), ("PFM", tooth)], 60),
                    < 0.93 => ("Veneers", [("VENEER", 11), ("VENEER", 21), ("VENEER", 12), ("VENEER", 22)], 120),
                    < 0.97 => ("Implant", [("PANO", null), ("IMPL", tooth), ("IMPLCR", tooth)], 90),
                    _ => ("Night guard", [("EXAM", null), ("GUARD", null)], 30)
                };
            // ~1 in 8 visits was preceded by a no-show that the patient later rebooked.
            if (_rng.NextDouble() < 0.13) Visit(p, d, at.AddDays(-_rng.Next(3, 15)), visit.Title, visit.Minutes, AppointmentStatus.NoShow);
            var appt = Visit(p, d, at, visit.Title, visit.Minutes, AppointmentStatus.Completed);
            var planned = visit.Title is "Root canal & crown" or "Composite fillings" or "Crown" or "Veneers" or "Implant" or "Pulpotomy & crown";
            if (planned)
            {
                var plan = CompletedPlan(p, d, appt.StartTime, visit.Title, visit.Items);
                appt.TreatmentPlanId = plan.Id;
                BillAndPay(p, d, plan, appt.EndTime, full: true);
            }
            else CompletedVisit(p, d, appt.StartTime, visit.Items);
        }
        if (!child && _rng.NextDouble() < 0.6)
        {
            // Treatment the patient declined (keeps acceptance-rate metrics realistic).
            var declined = _now.AddDays(-_rng.Next(20, 200));
            var options = new (string Title, (string, int?)[] Items)[]
            {
                ("Smile makeover", [("WHITEN", null), ("VENEER", 11), ("VENEER", 21)]),
                ("Implant replacement", [("IMPL", 36), ("IMPLCR", 36)]),
                ("Orthodontic assessment", [("ORTHO", null), ("PANO", null)]),
                ("Crown replacement", [("ZIRC", Pick([16, 26, 36, 46]))])
            };
            var o = options[_rng.Next(options.Length)];
            var rejected = NewPlan(p, d, declined, o.Title, TreatmentPlanStatus.Rejected, o.Items);
            rejected.PresentedAt = declined;
            rejected.RejectedAt = declined.AddDays(_rng.Next(2, 20));
            rejected.RejectionReason = new[] { "Cost", "Wants to think about it", "Getting a second opinion", "Travelling abroad" }[_rng.Next(4)];
        }
        if (!child && _rng.NextDouble() < 0.1)
        {
            // Started, then abandoned by the patient after the first stage.
            var started = _now.AddDays(-_rng.Next(120, 330));
            var tooth = Pick([16, 26, 36, 46]);
            var abandoned = NewPlan(p, d, started.AddDays(-5), "Root canal & crown", TreatmentPlanStatus.Rejected, [("RCT-M", tooth), ("ZIRC", tooth)]);
            abandoned.PresentedAt = abandoned.AcceptedAt = started.AddDays(-5);
            var v = Visit(p, d, started, "Root canal", 90, AppointmentStatus.Completed, abandoned.Id);
            CompleteItem(abandoned, 0, v.StartTime, d);
            abandoned.StartedAt = abandoned.LastActivityAt = v.StartTime;
            abandoned.RejectedAt = v.StartTime.AddDays(40);
            abandoned.RejectionReason = "Patient chose to finish treatment abroad";
            var abandonedInvoice = Invoice(p, d, abandoned, [abandoned.Items.ElementAt(0)], v.EndTime);
            Pay(abandonedInvoice, v.EndTime, abandonedInvoice.Total);
        }
        if (_rng.NextDouble() < 0.5)
        {
            var upcoming = new[] { "Check-up & cleaning", "Composite filling", "Consultation", "Crown fitting", "Deep cleaning" }[_rng.Next(5)];
            Visit(p, d, Future(1, 14), child ? "Check-up & fluoride" : upcoming, upcoming == "Consultation" ? 20 : 45,
                _rng.NextDouble() < 0.45 ? AppointmentStatus.Confirmed : AppointmentStatus.Scheduled);
        }
    }

    private void NewPatient(Patient p, Dentist d, Queue<(int, int)> todaySlots)
    {
        if (todaySlots.Count > 0 && _rng.NextDouble() < 0.6)
        {
            var (h, m) = todaySlots.Dequeue();
            TodayVisit(p, d, h, m, "New patient consultation", 30, ["EXAM", "PANO"]);
        }
        else Visit(p, d, Future(1, 10), "New patient consultation", 30, AppointmentStatus.Scheduled);
    }

    // ================================================================== building blocks

    private Appointment Visit(Patient p, Dentist d, DateTime approxUtc, string treatment, int minutes, AppointmentStatus status, Guid? planId = null)
    {
        var local = Local(approxUtc).Date;
        // Clinic is closed on Fridays.
        if (local.DayOfWeek == DayOfWeek.Friday) local = local.AddDays(approxUtc > _now ? 1 : -1);
        var start = ToUtc(local.AddHours(_rng.Next(9, 19)).AddMinutes(_rng.Next(2) * 30));
        if (status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed && start <= _now) start = _now.AddDays(1);
        if (status is AppointmentStatus.Completed or AppointmentStatus.NoShow && start > _now) start = _now.AddHours(-2);

        var a = new Appointment
        {
            ClinicId = _clinicId, PatientId = p.Id, DentistId = d.Id, StartTime = start, EndTime = start.AddMinutes(minutes),
            Treatment = treatment, Status = status, TreatmentPlanId = planId, Chair = $"Chair {_rng.Next(1, 4)}",
            CreatedAt = start.AddDays(-_rng.Next(2, 15)) < p.RegisteredAt ? p.RegisteredAt : start.AddDays(-_rng.Next(2, 15))
        };
        if (status == AppointmentStatus.Completed) { a.ArrivedAt = start.AddMinutes(-5); a.CompletedAt = a.EndTime; }
        if (status == AppointmentStatus.Confirmed) a.ConfirmedAt = _now.AddHours(-_rng.Next(2, 40));
        _appointments.Add(a);
        return a;
    }

    /// <summary>Appointment today at a fixed local time; status follows the clock (completed in the morning, upcoming later).</summary>
    private Appointment TodayVisit(Patient p, Dentist d, int hour, int minute, string treatment, int minutes, string[]? completedCodes)
    {
        var start = ToUtc(Local(_now).Date.AddHours(hour).AddMinutes(minute));
        var end = start.AddMinutes(minutes);
        var status = end <= _now ? AppointmentStatus.Completed
            : start <= _now ? AppointmentStatus.InTreatment
            : start <= _now.AddMinutes(30) ? AppointmentStatus.Arrived
            : _rng.NextDouble() < 0.6 ? AppointmentStatus.Confirmed : AppointmentStatus.Scheduled;

        var a = new Appointment
        {
            ClinicId = _clinicId, PatientId = p.Id, DentistId = d.Id, StartTime = start, EndTime = end, Treatment = treatment,
            Status = status, Chair = $"Chair {_rng.Next(1, 4)}", CreatedAt = _now.AddDays(-_rng.Next(1, 10))
        };
        if (status is AppointmentStatus.Completed or AppointmentStatus.InTreatment or AppointmentStatus.Arrived) a.ArrivedAt = start.AddMinutes(-5);
        if (status == AppointmentStatus.Completed) a.CompletedAt = end;
        if (status == AppointmentStatus.Confirmed) a.ConfirmedAt = _now.AddHours(-3);
        _appointments.Add(a);

        if (status == AppointmentStatus.Completed && completedCodes is not null)
            CompletedVisit(p, d, start, completedCodes.Select(c => (c, (int?)null)));
        return a;
    }

    /// <summary>Single-visit treatment billed directly (check-ups, cleanings, emergency work) — no treatment plan.</summary>
    private void CompletedVisit(Patient p, Dentist d, DateTime at, IEnumerable<(string Code, int? Tooth)> items, bool pay = true)
    {
        var inv = new Invoice
        {
            ClinicId = _clinicId, InvoiceNumber = $"INV-{_settings.NextInvoiceNumber++}", PatientId = p.Id, DentistId = d.Id,
            IssueDate = at, DueDate = at.AddDays(14), Status = InvoiceStatus.Issued, CreatedAt = at
        };
        foreach (var (code, tooth) in items)
        {
            var proc = _proc[code];
            _records.Add(new TreatmentRecord
            {
                ClinicId = _clinicId, PatientId = p.Id, DentistId = d.Id, ProcedureId = proc.Id, ProcedureName = proc.Name, Category = proc.Category,
                ToothNumber = tooth, PerformedAt = at, Price = proc.DefaultPrice, CreatedAt = at
            });
            if (tooth is not null && proc.ResultingCondition is not null)
                Condition(p.Id, tooth.Value, proc.ResultingCondition.Value, ConditionStatus.Treated, at, d, null);
            inv.Items.Add(new InvoiceItem
            {
                ClinicId = _clinicId, InvoiceId = inv.Id, Description = tooth is null ? proc.Name : $"{proc.Name} (tooth {tooth})", ProcedureId = proc.Id,
                ToothNumber = tooth, Quantity = 1, UnitPrice = proc.DefaultPrice, Total = proc.DefaultPrice, CreatedAt = at
            });
        }
        inv.Subtotal = inv.Total = inv.Balance = inv.Items.Sum(i => i.Total);
        _invoices.Add(inv);
        if (pay) Pay(inv, at.AddMinutes(45), inv.Total);
    }

    private TreatmentPlan NewPlan(Patient p, Dentist d, DateTime created, string title, TreatmentPlanStatus status, IEnumerable<(string Code, int? Tooth)> items)
    {
        var plan = new TreatmentPlan
        {
            ClinicId = _clinicId, PlanNumber = $"TP-{_settings.NextPlanNumber++:D4}", PatientId = p.Id, DentistId = d.Id, Title = title,
            Status = status, CreatedAt = created
        };
        var seq = 1;
        foreach (var (code, tooth) in items)
        {
            var proc = _proc[code];
            plan.Items.Add(new TreatmentPlanItem
            {
                ClinicId = _clinicId, TreatmentPlanId = plan.Id, ProcedureId = proc.Id, Name = proc.Name, ToothNumber = tooth,
                Quantity = 1, UnitPrice = proc.DefaultPrice, Total = proc.DefaultPrice, Sequence = seq++, CreatedAt = created
            });
        }
        plan.TotalAmount = plan.Items.Sum(i => i.Total);
        _plans.Add(plan);
        return plan;
    }

    private TreatmentPlan CompletedPlan(Patient p, Dentist d, DateTime at, string title, List<(string Code, int? Tooth)> items)
    {
        var plan = NewPlan(p, d, at, title, TreatmentPlanStatus.Completed, items);
        plan.PresentedAt = plan.AcceptedAt = plan.StartedAt = plan.LastActivityAt = plan.CompletedAt = at;
        for (var i = 0; i < plan.Items.Count; i++) CompleteItem(plan, i, at, d);
        return plan;
    }

    private void CompleteItem(TreatmentPlan plan, int index, DateTime at, Dentist d)
    {
        var item = plan.Items.ElementAt(index);
        var proc = _proc.Values.First(x => x.Id == item.ProcedureId);
        item.Status = TreatmentItemStatus.Completed;
        item.CompletedAt = at;
        _records.Add(new TreatmentRecord
        {
            ClinicId = _clinicId, PatientId = plan.PatientId, DentistId = d.Id, TreatmentPlanItemId = item.Id, ProcedureId = proc.Id,
            ProcedureName = proc.Name, Category = proc.Category, ToothNumber = item.ToothNumber, PerformedAt = at, Price = item.Total, CreatedAt = at
        });
        if (item.ToothNumber is not null && proc.ResultingCondition is not null)
            Condition(plan.PatientId, item.ToothNumber.Value, proc.ResultingCondition.Value, ConditionStatus.Treated, at, d, item.Id);
    }

    private void Condition(Patient p, int tooth, ToothConditionType type, ConditionStatus status, DateTime at, Dentist d, Guid? itemId) =>
        Condition(p.Id, tooth, type, status, at, d, itemId);

    private void Condition(Guid patientId, int tooth, ToothConditionType type, ConditionStatus status, DateTime at, Dentist d, Guid? itemId)
    {
        var t = _teeth.FirstOrDefault(x => x.PatientId == patientId && x.ToothNumber == tooth);
        if (t is null)
        {
            t = new DentalTooth { ClinicId = _clinicId, PatientId = patientId, ToothNumber = tooth, IsPrimary = Teeth.IsPrimaryTooth(tooth), CreatedAt = at };
            _teeth.Add(t);
        }
        if (status == ConditionStatus.Treated)
            foreach (var open in _conditions.Where(c => c.DentalToothId == t.Id && c.Status is ConditionStatus.Diagnosed or ConditionStatus.Planned))
                open.Status = ConditionStatus.Resolved;
        _conditions.Add(new DentalCondition
        {
            ClinicId = _clinicId, PatientId = patientId, DentalToothId = t.Id, ToothNumber = tooth, Type = type, Status = status,
            RecordedAt = at, DentistId = d.Id, TreatmentPlanItemId = itemId, CreatedAt = at,
            Surfaces = type is ToothConditionType.Filling or ToothConditionType.Caries ? new[] { "O", "MO", "DO", "MOD" }[_rng.Next(4)] : null
        });
    }

    private Invoice Invoice(Patient p, Dentist d, TreatmentPlan plan, IReadOnlyList<TreatmentPlanItem> items, DateTime issued)
    {
        var inv = new Invoice
        {
            ClinicId = _clinicId, InvoiceNumber = $"INV-{_settings.NextInvoiceNumber++}", PatientId = p.Id, DentistId = d.Id,
            TreatmentPlanId = plan.Id, IssueDate = issued, DueDate = issued.AddDays(14), Status = InvoiceStatus.Issued, CreatedAt = issued,
            Notes = $"Treatment plan {plan.PlanNumber}"
        };
        foreach (var i in items)
        {
            i.IsInvoiced = true;
            inv.Items.Add(new InvoiceItem
            {
                ClinicId = _clinicId, InvoiceId = inv.Id, Description = i.ToothNumber is null ? i.Name : $"{i.Name} (tooth {i.ToothNumber})",
                ProcedureId = i.ProcedureId, TreatmentPlanItemId = i.Id, ToothNumber = i.ToothNumber, Quantity = i.Quantity,
                UnitPrice = i.UnitPrice, Total = i.Total, CreatedAt = issued
            });
        }
        inv.Subtotal = inv.Total = inv.Balance = inv.Items.Sum(i => i.Total);
        _invoices.Add(inv);
        return inv;
    }

    private Payment Pay(Invoice inv, DateTime at, decimal amount)
    {
        amount = Math.Min(amount, inv.Balance);
        var pay = new Payment
        {
            ClinicId = _clinicId, ReceiptNumber = $"RC-{_settings.NextReceiptNumber++}", PatientId = inv.PatientId, InvoiceId = inv.Id,
            Amount = amount, Method = PickMethod(), PaidAt = at > _now ? _now : at, CreatedAt = at > _now ? _now : at,
            ReceivedById = _staff[1].Id
        };
        if (pay.Method is PaymentMethod.Card or PaymentMethod.CliQ or PaymentMethod.BankTransfer) pay.Reference = $"TX{_rng.Next(100000, 999999)}";
        inv.AmountPaid += amount;
        inv.Balance = inv.Total - inv.AmountPaid;
        inv.Status = inv.Balance <= 0 ? InvoiceStatus.Paid : InvoiceStatus.PartiallyPaid;
        _payments.Add(pay);
        return pay;
    }

    private void BillAndPay(Patient p, Dentist d, TreatmentPlan plan, DateTime at, bool full)
    {
        var inv = Invoice(p, d, plan, plan.Items.ToList(), at);
        if (full) Pay(inv, at, inv.Total);
    }

    private FollowUp Contacted(Patient p, RecoveryIssueType type, string sourceType, Guid sourceId, DateTime triggered, DateTime contactedAt, decimal value, string title)
    {
        var f = new FollowUp
        {
            ClinicId = _clinicId, PatientId = p.Id, IssueType = type, SourceEntityType = sourceType, SourceEntityId = sourceId, Title = title,
            DueDate = triggered.AddDays(3), Status = FollowUpStatus.Contacted, Priority = PriorityLevel.High, EstimatedValue = value,
            AssignedToUserId = _staff[1].Id, ContactAttempts = 1, LastContactedAt = contactedAt, CreatedAt = contactedAt.AddMinutes(-5),
            Outcome = "Sent WhatsApp follow-up"
        };
        _followUps.Add(f);
        _messages.Add(new Message
        {
            ClinicId = _clinicId, PatientId = p.Id, Channel = MessageChannel.WhatsApp, Status = MessageStatus.Sent, ToNumber = PhoneNumber.ToInternational(p.Phone),
            Body = p.PreferredLanguage == "ar"
                ? $"مرحباً {p.FullNameAr}، أوصى طبيب الأسنان بخطة علاج. يسعدنا مساعدتك في حجز موعدك القادم. — عيادة سمايل لاين"
                : $"Hello {p.FullName}, your dentist recommended a treatment plan. We would be happy to help you schedule your next appointment. — Smile Line Dental Clinic",
            SentAt = contactedAt, CreatedAt = contactedAt, SentById = _staff[1].Id, FollowUpId = f.Id
        });
        return f;
    }

    private void Note(Patient p, Dentist d, DateTime at, string complaint, string findings, string? diagnosis, string recommendation, string followUp) =>
        _notes.Add(new ClinicalNote
        {
            ClinicId = _clinicId, PatientId = p.Id, DentistId = d.Id, NoteDate = at, CreatedAt = at, ChiefComplaint = complaint,
            ClinicalFindings = findings, Diagnosis = diagnosis, Recommendations = recommendation, FollowUpInstructions = followUp
        });

    private void SeedExpenses(Guid recordedBy)
    {
        var localNow = Local(_now);
        for (var m = 11; m >= 0; m--)
        {
            var month = new DateTime(localNow.Year, localNow.Month, 1).AddMonths(-m);
            void Add(ExpenseCategory c, string desc, decimal amount, int day, string? vendor = null)
            {
                var date = ToUtc(month.AddDays(day - 1).AddHours(11));
                if (date > _now) return;
                db.Expenses.Add(new Expense
                {
                    ClinicId = _clinicId, Category = c, Description = desc, Amount = amount, ExpenseDate = date, Vendor = vendor,
                    PaymentMethod = c == ExpenseCategory.Salaries ? PaymentMethod.BankTransfer : PaymentMethod.Cash, RecordedById = recordedBy, CreatedAt = date
                });
            }
            Add(ExpenseCategory.Rent, "Clinic rent", 950, 1, "Abdoun Properties");
            Add(ExpenseCategory.Salaries, "Staff salaries (assistants & reception)", 2300, 1);
            Add(ExpenseCategory.Lab, "Dental lab — crowns & prosthetics", 300 + _rng.Next(0, 400), 10, "Precision Dental Lab");
            Add(ExpenseCategory.Supplies, "Consumables & materials", 200 + _rng.Next(0, 220), 5, "Jordan Dental Supply");
            Add(ExpenseCategory.Utilities, "Electricity, water & internet", 170 + _rng.Next(0, 90), 20);
            if (_rng.NextDouble() < 0.7) Add(ExpenseCategory.Marketing, "Instagram & Google ads", 120 + _rng.Next(0, 180), 15);
        }
    }

    // ================================================================== helpers

    private decimal PlanRemaining(TreatmentPlan plan) =>
        plan.Items.Where(i => i.Status is TreatmentItemStatus.Planned or TreatmentItemStatus.Scheduled).Sum(i => i.Total);

    private DateTime Future(int minDays, int maxDays) => _now.AddDays(_rng.Next(minDays, maxDays + 1));

    private DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _tz);

    private DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _tz);

    private int Pick(IReadOnlyList<int> options) => options[_rng.Next(options.Count)];

    private PaymentMethod PickMethod()
    {
        var r = _rng.NextDouble();
        return r < 0.45 ? PaymentMethod.Cash : r < 0.75 ? PaymentMethod.Card : r < 0.92 ? PaymentMethod.CliQ : PaymentMethod.BankTransfer;
    }
}
