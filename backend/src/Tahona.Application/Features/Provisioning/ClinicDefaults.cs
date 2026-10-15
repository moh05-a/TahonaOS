using Tahona.Domain.Authorization;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Provisioning;

/// <summary>
/// What every new clinic starts with: the two system roles (Admin and Dentist), a starter procedure list, bilingual message templates
/// and the follow-up rules. Used by clinic provisioning and by the demo seeder, so both begin from the same baseline.
/// The procedure prices are placeholders for the clinic to replace with its own price list.
/// </summary>
public static class ClinicDefaults
{
    public static List<Role> Roles(Guid clinicId) => SystemRoles.DefaultPermissions.Select(kv => new Role
    {
        ClinicId = clinicId, Name = kv.Key, IsSystem = true, Permissions = kv.Value.ToList(),
        Description = kv.Key == SystemRoles.Admin
            ? "Full access to every module and setting"
            : "Clinical work (charts, treatment plans, notes) and recording payments"
    }).ToList();

    public static List<Procedure> Procedures(Guid clinicId)
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
        return list.Select(p => new Procedure
        {
            ClinicId = clinicId, Code = p.Code, Name = p.Name, NameAr = p.Ar, Category = p.Cat, DefaultPrice = p.Price,
            DefaultDurationMinutes = p.Min, ResultingCondition = p.Result
        }).ToList();
    }

    public static List<MessageTemplate> Templates(Guid clinicId)
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
        return items.Select(i => new MessageTemplate
        {
            ClinicId = clinicId, Type = i.Type, Language = i.Lang, Name = i.Name, Body = i.Body, IsSystem = true
        }).ToList();
    }

    /// <summary>The follow-up rules, each linked to the English template of its type (the patient's language is chosen at send time).</summary>
    public static List<FollowUpRule> Rules(Guid clinicId, IReadOnlyCollection<MessageTemplate> templates)
    {
        FollowUpRule R(string name, FollowUpTrigger trigger, int days, PriorityLevel p, MessageTemplateType? type, ProcedureCategory? cat = null) => new()
        {
            ClinicId = clinicId, Name = name, Trigger = trigger, DelayDays = days, Priority = p, ProcedureCategory = cat,
            MessageTemplateId = type is null ? null : templates.First(t => t.Type == type && t.Language == "en").Id
        };
        return
        [
            R("Missed appointment — follow up next day", FollowUpTrigger.MissedAppointment, 1, PriorityLevel.High, MessageTemplateType.MissedAppointment),
            R("Treatment plan not started", FollowUpTrigger.TreatmentPlanNotStarted, 3, PriorityLevel.High, MessageTemplateType.TreatmentFollowUp),
            R("Treatment plan still inactive — re-contact", FollowUpTrigger.TreatmentPlanInactive, 7, PriorityLevel.Medium, MessageTemplateType.TreatmentFollowUp),
            R("Treatment unfinished", FollowUpTrigger.TreatmentUnfinished, 14, PriorityLevel.High, MessageTemplateType.TreatmentFollowUp),
            R("Root canal review", FollowUpTrigger.ProcedureCompleted, 14, PriorityLevel.Medium, MessageTemplateType.PostTreatment, ProcedureCategory.Endodontic),
            R("Post-surgery check", FollowUpTrigger.ProcedureCompleted, 7, PriorityLevel.Medium, MessageTemplateType.PostTreatment, ProcedureCategory.OralSurgery),
            R("Routine cleaning recall (6 months)", FollowUpTrigger.RoutineRecall, 180, PriorityLevel.Low, MessageTemplateType.Recall),
            R("Overdue balance", FollowUpTrigger.OverdueBalance, 0, PriorityLevel.Medium, MessageTemplateType.PaymentReminder)
        ];
    }
}
