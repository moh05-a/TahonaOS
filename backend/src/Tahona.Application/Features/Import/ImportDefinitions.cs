using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Import;

/// <summary>The fields each kind of import accepts, with the header names recognised automatically in English and Arabic.</summary>
public static class ImportDefinitions
{
    public static class F
    {
        public const string FullName = "fullName", FirstName = "firstName", LastName = "lastName", FullNameAr = "fullNameAr";
        public const string Phone = "phone", WhatsApp = "whatsApp", Email = "email", DateOfBirth = "dateOfBirth", Gender = "gender";
        public const string NationalId = "nationalId", Address = "address", City = "city", FileNumber = "fileNumber", ExternalId = "externalId";
        public const string Allergies = "allergies", MedicalConditions = "medicalConditions", Medications = "medications", MedicalNotes = "medicalNotes";
        public const string InsuranceProvider = "insuranceProvider", InsurancePolicyNumber = "insurancePolicyNumber";
        public const string RegisteredAt = "registeredAt", LastVisitAt = "lastVisitAt", Language = "language", Consent = "messagingConsent";

        public const string PatientExternalId = "patientExternalId", PatientFileNumber = "patientFileNumber", PatientPhone = "patientPhone", PatientName = "patientName";
        public const string Date = "date", Time = "time", Duration = "durationMinutes", Dentist = "dentist", Treatment = "treatment", Status = "status", Notes = "notes";
        public const string Amount = "amount", AsOfDate = "asOfDate", DueDate = "dueDate", Reference = "reference";
        public const string PlanTitle = "planTitle", PlanReference = "planReference", PlanDate = "planDate", ItemName = "itemName", Tooth = "tooth";
        public const string Price = "price", Quantity = "quantity", ItemStatus = "itemStatus", CompletedDate = "completedDate";
    }

    /// <summary>The ways a row can say which patient it belongs to. At least one must be mapped.</summary>
    private static readonly ImportField[] PatientReference =
    [
        new(F.PatientFileNumber, "Patient file number", "رقم ملف المريض", false, "EXAMPLE-1",
            ["file number", "file no", "file", "patient file", "chart number", "chart no", "رقم الملف", "ملف المريض", "رقم ملف المريض", "الملف"],
            "The file number the patient has in TahonaOS, or had in the previous system if it was imported."),
        new(F.PatientExternalId, "Patient ID in the previous system", "رقم المريض في النظام السابق", false, "10432",
            ["patient id", "patientid", "patient number", "patient no", "id", "رقم المريض", "معرف المريض", "رمز المريض"]),
        new(F.PatientPhone, "Patient phone", "هاتف المريض", false, "0791234567",
            ["phone", "mobile", "patient phone", "patient mobile", "tel", "الهاتف", "الجوال", "الموبايل", "رقم الهاتف", "هاتف المريض"]),
        new(F.PatientName, "Patient name", "اسم المريض", false, "Example Patient",
            ["patient", "patient name", "name", "full name", "اسم المريض", "المريض", "الاسم"],
            "Used to tell apart patients who share a phone number.")
    ];

    public static readonly ImportDefinition Patients = new(ImportKind.Patients,
    [
        new(F.FullName, "Full name", "الاسم الكامل", false, "Example Patient",
            ["full name", "name", "patient name", "patient", "english name", "name en", "الاسم", "اسم المريض", "الاسم الكامل", "المريض", "الاسم بالانجليزي"]),
        new(F.FirstName, "First name", "الاسم الأول", false, "Example", ["first name", "firstname", "given name", "الاسم الاول"]),
        new(F.LastName, "Last name", "اسم العائلة", false, "Patient", ["last name", "lastname", "surname", "family name", "اسم العائله", "العائله", "الكنيه"]),
        new(F.FullNameAr, "Name in Arabic", "الاسم بالعربية", false, "مريض تجريبي", ["arabic name", "name ar", "name arabic", "الاسم بالعربي", "الاسم العربي", "الاسم بالعربيه"]),
        new(F.Phone, "Phone", "الهاتف", false, "0791234567",
            ["phone", "mobile", "phone number", "mobile number", "tel", "telephone", "cell", "الهاتف", "الجوال", "الموبايل", "رقم الهاتف", "رقم الجوال", "رقم الموبايل", "تلفون"]),
        new(F.WhatsApp, "WhatsApp number", "رقم واتساب", false, "0791234567", ["whatsapp", "whats app", "واتساب", "رقم الواتساب", "واتس"]),
        new(F.Email, "E-mail", "البريد الإلكتروني", false, "patient@example.com", ["email", "e-mail", "mail", "البريد", "البريد الالكتروني", "ايميل"]),
        new(F.DateOfBirth, "Date of birth", "تاريخ الميلاد", false, "14/03/1988", ["date of birth", "dob", "birth date", "birthdate", "birthday", "تاريخ الميلاد", "الميلاد", "تاريخ الولاده"]),
        new(F.Gender, "Gender", "الجنس", false, "Male", ["gender", "sex", "الجنس", "النوع"]),
        new(F.NationalId, "National ID", "الرقم الوطني", false, "9881012345", ["national id", "national number", "id number", "nid", "الرقم الوطني", "رقم الهويه", "الهويه"]),
        new(F.FileNumber, "File number", "رقم الملف", false, "EXAMPLE-1",
            ["file number", "file no", "file", "chart number", "chart no", "patient file", "رقم الملف", "الملف", "رقم ملف المريض"],
            "The number on the patient's paper file or in the previous system."),
        new(F.ExternalId, "ID in the previous system", "الرقم في النظام السابق", false, "10432",
            ["id", "patient id", "patientid", "patient number", "patient no", "record id", "رقم المريض", "المعرف", "معرف المريض", "رمز المريض"],
            "Lets a later import recognise the same patient and skip them."),
        new(F.Address, "Address", "العنوان", false, "Abdoun, Cairo Street", ["address", "street", "العنوان", "عنوان السكن"]),
        new(F.City, "City", "المدينة", false, "Amman", ["city", "town", "المدينه", "المحافظه"]),
        new(F.Allergies, "Allergies", "الحساسية", false, "Penicillin", ["allergies", "allergy", "الحساسيه", "حساسيه"]),
        new(F.MedicalConditions, "Medical conditions", "الأمراض المزمنة", false, "Diabetes", ["medical conditions", "conditions", "chronic diseases", "medical history", "الامراض", "الامراض المزمنه", "التاريخ المرضي", "الحاله الصحيه"]),
        new(F.Medications, "Current medications", "الأدوية الحالية", false, "Metformin", ["medications", "medication", "current medications", "drugs", "الادويه", "الادويه الحاليه"]),
        new(F.MedicalNotes, "Notes", "ملاحظات", false, "Prefers evening appointments", ["notes", "note", "remarks", "comments", "medical notes", "ملاحظات", "ملاحظه"]),
        new(F.InsuranceProvider, "Insurance provider", "شركة التأمين", false, "MedNet Jordan", ["insurance", "insurance provider", "insurance company", "insurer", "التامين", "شركه التامين"]),
        new(F.InsurancePolicyNumber, "Insurance policy number", "رقم بوليصة التأمين", false, "MN-204411", ["policy number", "policy no", "insurance number", "insurance no", "رقم التامين", "رقم البوليصه"]),
        new(F.RegisteredAt, "Registered on", "تاريخ التسجيل", false, "02/09/2021", ["registered", "registration date", "registered on", "created", "created at", "date added", "تاريخ التسجيل", "تاريخ الاضافه"]),
        new(F.LastVisitAt, "Last visit", "آخر زيارة", false, "18/06/2026", ["last visit", "last visit date", "last seen", "last appointment", "اخر زياره", "تاريخ اخر زياره"],
            "Used for recall reminders. Only the date is stored; no appointment is created."),
        new(F.Language, "Preferred language", "اللغة المفضلة", false, "Arabic", ["language", "preferred language", "lang", "اللغه", "اللغه المفضله"]),
        new(F.Consent, "Agreed to receive messages", "الموافقة على الرسائل", false, "Yes", ["consent", "messaging consent", "whatsapp consent", "marketing consent", "sms consent", "الموافقه", "موافقه الرسائل", "الموافقه علي الرسائل"],
            "Yes or No. Leave unmapped if the previous system did not record it.")
    ], [[F.FullName, F.FirstName]]);

    public static readonly ImportDefinition Appointments = new(ImportKind.Appointments,
    [
        .. PatientReference,
        new(F.Date, "Date", "التاريخ", true, "21/10/2026", ["date", "appointment date", "day", "start", "start date", "start time", "التاريخ", "تاريخ الموعد", "اليوم"],
            "May include the time, e.g. 21/10/2026 16:30."),
        new(F.Time, "Time", "الوقت", false, "16:30", ["time", "appointment time", "hour", "at", "الوقت", "الساعه", "وقت الموعد"]),
        new(F.Duration, "Duration (minutes)", "المدة (دقائق)", false, "30", ["duration", "minutes", "length", "duration minutes", "المده", "الدقائق", "مده الموعد"]),
        new(F.Dentist, "Dentist", "الطبيب", false, "Dr. Sami Abu-Zeid", ["dentist", "doctor", "provider", "dr", "الطبيب", "الدكتور", "طبيب الاسنان", "الطبيب المعالج"]),
        new(F.Treatment, "Treatment / reason", "العلاج / السبب", false, "Check-up", ["treatment", "reason", "procedure", "service", "type", "visit type", "description", "العلاج", "السبب", "الاجراء", "نوع الموعد", "الخدمه"]),
        new(F.Status, "Status", "الحالة", false, "Scheduled", ["status", "state", "attendance", "الحاله", "حاله الموعد", "الحضور"],
            "Scheduled, Confirmed, Completed, Cancelled or No-show."),
        new(F.Notes, "Notes", "ملاحظات", false, "", ["notes", "note", "remarks", "comments", "ملاحظات", "ملاحظه"]),
        new(F.ExternalId, "Appointment ID in the previous system", "رقم الموعد في النظام السابق", false, "A-55120", ["appointment id", "appointment no", "booking id", "رقم الموعد"])
    ], [[F.PatientFileNumber, F.PatientExternalId, F.PatientPhone]]);

    public static readonly ImportDefinition OpeningBalances = new(ImportKind.OpeningBalances,
    [
        .. PatientReference,
        new(F.Amount, "Amount owed", "المبلغ المستحق", true, "120.500", ["amount", "balance", "outstanding", "amount due", "due", "debt", "owed", "remaining", "المبلغ", "الرصيد", "المستحق", "المتبقي", "الذمه", "المبلغ المستحق", "الدين"],
            "What the patient owes the clinic on the balance date. Zero rows are skipped."),
        new(F.AsOfDate, "Balance date", "تاريخ الرصيد", false, "30/09/2026", ["as of", "as of date", "balance date", "date", "invoice date", "تاريخ الرصيد", "التاريخ", "تاريخ الفاتوره"],
            "Defaults to today."),
        new(F.DueDate, "Due date", "تاريخ الاستحقاق", false, "30/09/2026", ["due date", "due", "payment due", "تاريخ الاستحقاق", "الاستحقاق"]),
        new(F.Reference, "Reference", "المرجع", false, "INV-2091", ["reference", "ref", "invoice", "invoice number", "invoice no", "المرجع", "رقم الفاتوره", "الفاتوره"]),
        new(F.Notes, "Notes", "ملاحظات", false, "", ["notes", "note", "remarks", "comments", "ملاحظات", "ملاحظه"])
    ], [[F.PatientFileNumber, F.PatientExternalId, F.PatientPhone]]);

    public static readonly ImportDefinition TreatmentPlans = new(ImportKind.TreatmentPlans,
    [
        .. PatientReference,
        new(F.PlanTitle, "Plan title", "عنوان الخطة", false, "Root canal and crown", ["plan", "plan title", "treatment plan", "plan name", "title", "الخطه", "عنوان الخطه", "خطه العلاج", "اسم الخطه"],
            "Rows with the same patient and the same title (or plan number) become one plan."),
        new(F.PlanReference, "Plan number in the previous system", "رقم الخطة في النظام السابق", false, "TP-204", ["plan id", "plan number", "plan no", "plan ref", "رقم الخطه", "معرف الخطه"],
            "Lets a later import recognise the same plan and skip it."),
        new(F.ItemName, "Treatment", "العلاج", true, "Zirconia crown", ["treatment", "procedure", "item", "service", "description", "treatment name", "العلاج", "الاجراء", "البند", "الخدمه", "الوصف"]),
        new(F.Tooth, "Tooth (FDI)", "السن (FDI)", false, "16", ["tooth", "tooth number", "tooth no", "السن", "رقم السن", "الضرس"]),
        new(F.Price, "Price", "السعر", true, "250", ["price", "fee", "cost", "amount", "unit price", "السعر", "التكلفه", "المبلغ", "الاجر"]),
        new(F.Quantity, "Quantity", "الكمية", false, "1", ["quantity", "qty", "count", "الكميه", "العدد"]),
        new(F.Status, "Plan status", "حالة الخطة", false, "Accepted", ["plan status", "status", "state", "حاله الخطه", "الحاله"],
            "Draft, Presented, Accepted, In progress, Completed or Rejected. A plan with no status is imported as a draft."),
        new(F.ItemStatus, "Treatment status", "حالة العلاج", false, "Planned", ["treatment status", "item status", "procedure status", "done", "حاله العلاج", "حاله الاجراء", "حاله البند"],
            "Planned, Completed or Cancelled."),
        new(F.PlanDate, "Plan date", "تاريخ الخطة", false, "02/09/2026", ["plan date", "date", "created", "presented", "تاريخ الخطه", "التاريخ"]),
        new(F.CompletedDate, "Completed on", "تاريخ الإنجاز", false, "", ["completed", "completed on", "completion date", "done on", "date done", "تاريخ الانجاز", "تاريخ الانتهاء", "تاريخ التنفيذ"]),
        new(F.Dentist, "Dentist", "الطبيب", false, "Dr. Sami Abu-Zeid", ["dentist", "doctor", "provider", "dr", "الطبيب", "الدكتور", "طبيب الاسنان", "الطبيب المعالج"])
    ], [[F.PatientFileNumber, F.PatientExternalId, F.PatientPhone], [F.PlanTitle, F.PlanReference]]);

    public static readonly IReadOnlyList<ImportDefinition> All = [Patients, Appointments, OpeningBalances, TreatmentPlans];

    public static ImportDefinition For(ImportKind kind) => All.First(d => d.Kind == kind);

    /// <summary>
    /// Proposes a field → header mapping from the header names. Each header is used once, for the first field that
    /// names it most specifically; anything unrecognised is left for the person to map.
    /// </summary>
    public static Dictionary<string, string?> SuggestMapping(ImportDefinition definition, IReadOnlyList<string> headers)
    {
        var keyed = headers.Select(h => (Header: h, Key: ImportParsing.Key(h))).ToList();
        var used = new HashSet<string>();
        var mapping = definition.Fields.ToDictionary(f => f.Key, _ => (string?)null);

        // Pass 1: exact matches on the field key, label or a synonym. Pass 2: a header that contains a synonym.
        foreach (var exact in new[] { true, false })
        {
            foreach (var field in definition.Fields)
            {
                if (mapping[field.Key] is not null) continue;
                var names = field.Synonyms.Append(field.Key).Append(field.Label).Append(field.LabelAr).Select(ImportParsing.Key)
                    .Where(n => n.Length > 0).Distinct().OrderByDescending(n => n.Length).ToList();
                var match = keyed.FirstOrDefault(h => !used.Contains(h.Header) && h.Key.Length > 0
                    && (exact ? names.Contains(h.Key) : names.Any(n => n.Length >= 4 && h.Key.Contains(n))));
                if (match.Header is null) continue;
                mapping[field.Key] = match.Header;
                used.Add(match.Header);
            }
        }
        return mapping;
    }

    /// <summary>
    /// An example file for a kind: a header row and one sample row, in English or Arabic. The sample row is recognised
    /// and rejected on import (see <see cref="ImportRules.IsTemplateExample"/>), so it can never become a record.
    /// </summary>
    public static byte[] Template(ImportKind kind, string language)
    {
        var fields = For(kind).Fields;
        return CsvText.Write(
        [
            fields.Select(f => language == "ar" ? f.LabelAr : f.Label),
            fields.Select(f => f.Example)
        ]);
    }
}
