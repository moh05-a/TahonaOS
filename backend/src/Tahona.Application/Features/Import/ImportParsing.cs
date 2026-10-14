using System.Globalization;
using System.Text;
using Tahona.Domain.Enums;

namespace Tahona.Application.Features.Import;

/// <summary>
/// Reads the values clinics actually have in their spreadsheets: Arabic-Indic digits, day-first dates,
/// Arabic and English words for gender, status and yes/no. Every parser returns null for what it cannot read,
/// so the caller reports the cell instead of guessing.
/// </summary>
public static class ImportParsing
{
    /// <summary>Converts Arabic-Indic and Persian digits and separators to ASCII, and trims.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        // Our own CSV exports guard cells that start with = + - @ with an apostrophe; read them back as written.
        if (value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@') value = value[1..];
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),   // ٠-٩
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),   // ۰-۹
                '٫' => '.',                                               // Arabic decimal separator
                '٬' => ',',                                               // Arabic thousands separator
                '،' => ',',                                               // Arabic comma
                ' ' or '‏' or '‎' => ' ',                       // non-breaking space, direction marks
                _ => c
            });
        }
        return builder.ToString().Trim();
    }

    /// <summary>Lower-case comparison key for header and word matching: digits normalised, punctuation and Arabic diacritics removed.</summary>
    public static string Key(string? value)
    {
        var builder = new StringBuilder();
        foreach (var c in Normalize(value).ToLowerInvariant())
        {
            if (c is >= 'ً' and <= 'ْ' or 'ـ') continue; // tashkeel and tatweel
            var mapped = c switch { 'أ' or 'إ' or 'آ' => 'ا', 'ى' => 'ي', 'ة' => 'ه', _ => c };
            if (char.IsLetterOrDigit(mapped)) builder.Append(mapped);
        }
        return builder.ToString();
    }

    /// <summary>Compares two person names ignoring case, spacing, punctuation and Arabic letter variants.</summary>
    public static bool SameName(string? a, string? b)
    {
        var x = Key(a);
        return x.Length > 0 && x == Key(b);
    }

    private static readonly string[] IsoFormats = ["yyyy-M-d", "yyyy/M/d", "yyyy.M.d", "yyyyMMdd"];
    private static readonly string[] DayFirst = ["d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d/M/yy", "d-M-yy", "d MMM yyyy", "d MMMM yyyy", "d-MMM-yyyy", "d-MMM-yy"];
    private static readonly string[] MonthFirst = ["M/d/yyyy", "M-d-yyyy", "M.d.yyyy", "M/d/yy", "M-d-yy", "MMM d yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMMM d, yyyy"];

    /// <summary>Parses a date. Unambiguous year-first forms are always accepted; otherwise <paramref name="order"/> decides.</summary>
    public static DateOnly? Date(string? value, string order = "dmy") => DateTimeOf(value, order) is { } dt ? DateOnly.FromDateTime(dt) : null;

    /// <summary>Parses a date that may carry a time ("12/03/2025 14:30").</summary>
    public static DateTime? DateTimeOf(string? value, string order = "dmy")
    {
        var text = Normalize(value);
        if (text.Length == 0) return null;

        // Excel cells arrive as ISO date-times; "T" separates date and time.
        var datePart = text;
        TimeOnly? time = null;
        var split = text.IndexOf('T') is > 0 and var t ? t : text.IndexOf(' ', StringComparison.Ordinal);
        if (split > 0 && Time(text[(split + 1)..]) is { } parsedTime)
        {
            datePart = text[..split].Trim();
            time = parsedTime;
        }

        var formats = order switch { "mdy" => IsoFormats.Concat(MonthFirst), "ymd" => IsoFormats, _ => IsoFormats.Concat(DayFirst) };
        if (!DateOnly.TryParseExact(datePart, formats.ToArray(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        if (date.Year is < 1900 or > 2100) return null;
        return date.ToDateTime(time ?? TimeOnly.MinValue);
    }

    private static readonly string[] TimeFormats = ["H:mm", "H:mm:ss", "h:mm tt", "h:mmtt", "h tt", "htt", "H.mm", "HHmm"];

    public static TimeOnly? Time(string? value)
    {
        var text = Normalize(value).ToUpperInvariant().Replace("ص", "AM").Replace("م", "PM").Replace("A.M.", "AM").Replace("P.M.", "PM");
        if (text.Length == 0) return null;
        // A fractional Excel time-of-day survives as an ISO time such as 14:30:00.0000000.
        var dot = text.IndexOf('.');
        if (dot > 0 && text.Contains(':')) text = text[..dot];
        return TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }

    /// <summary>Parses a money amount such as "1,250.500", "1250,5 JOD" or "١٢٥٠٫٥ د.أ".</summary>
    public static decimal? Amount(string? value)
    {
        var text = Normalize(value);
        if (text.Length == 0) return null;
        var negative = text.StartsWith('-') || (text.StartsWith('(') && text.EndsWith(')'));
        // The number is the first run of digits and separators; currency names around it ("JOD", "د.أ") are ignored.
        var first = text.IndexOfAny(['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']);
        if (first < 0) return null;
        var last = first;
        while (last + 1 < text.Length && (char.IsAsciiDigit(text[last + 1]) || text[last + 1] is '.' or ',')) last++;
        var cleaned = text[first..(last + 1)].TrimEnd('.', ',');

        // One separator kind followed by exactly three digits at the end is a decimal in JOD ("12.500"), and a
        // comma used as the only separator with 1–3 trailing digits is a decimal comma ("1250,5").
        var lastDot = cleaned.LastIndexOf('.');
        var lastComma = cleaned.LastIndexOf(',');
        if (lastComma > lastDot)
        {
            var decimals = cleaned.Length - lastComma - 1;
            cleaned = lastDot < 0 && cleaned.Count(c => c == ',') == 1 && decimals is >= 1 and <= 3 && decimals != 3
                ? cleaned.Replace(',', '.')
                : lastDot >= 0 ? cleaned.Replace(".", "").Replace(',', '.') : cleaned.Replace(",", "");
        }
        else cleaned = cleaned.Replace(",", "");

        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) return null;
        return negative ? -amount : amount;
    }

    public static int? Integer(string? value) =>
        int.TryParse(Normalize(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i
        : decimal.TryParse(Normalize(value), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Truncate(d) ? (int)d : null;

    public static Gender? GenderOf(string? value) => Key(value) switch
    {
        "m" or "male" or "man" or "boy" or "ذكر" or "رجل" or "ولد" => Gender.Male,
        "f" or "female" or "woman" or "girl" or "انثي" or "امراه" or "بنت" => Gender.Female,
        _ => null
    };

    public static bool? YesNo(string? value) => Key(value) switch
    {
        "y" or "yes" or "true" or "1" or "نعم" or "موافق" or "اجل" => true,
        "n" or "no" or "false" or "0" or "لا" or "غيرموافق" or "رفض" => false,
        _ => null
    };

    public static AppointmentStatus? AppointmentStatusOf(string? value) => Key(value) switch
    {
        "scheduled" or "booked" or "pending" or "مجدول" or "محجوز" or "قادم" => AppointmentStatus.Scheduled,
        "confirmed" or "مؤكد" or "موكد" => AppointmentStatus.Confirmed,
        "completed" or "complete" or "done" or "attended" or "finished" or "seen" or "مكتمل" or "تم" or "حضر" or "منتهي" => AppointmentStatus.Completed,
        "cancelled" or "canceled" or "cancel" or "ملغي" or "الغاء" or "ملغى" => AppointmentStatus.Cancelled,
        "noshow" or "missed" or "absent" or "dna" or "لميحضر" or "غياب" or "تغيب" or "فائت" => AppointmentStatus.NoShow,
        _ => null
    };

    public static TreatmentPlanStatus? PlanStatusOf(string? value) => Key(value) switch
    {
        "draft" or "مسوده" => TreatmentPlanStatus.Draft,
        "presented" or "proposed" or "offered" or "pending" or "معروضه" or "مقترحه" or "بانتظارالموافقه" => TreatmentPlanStatus.Presented,
        "accepted" or "approved" or "مقبوله" or "موافقعليها" or "معتمده" => TreatmentPlanStatus.Accepted,
        "inprogress" or "started" or "ongoing" or "active" or "partiallycompleted" or "قيدالتنفيذ" or "جاريه" or "بدات" => TreatmentPlanStatus.InProgress,
        "completed" or "complete" or "done" or "finished" or "مكتمله" or "منتهيه" or "تمت" => TreatmentPlanStatus.Completed,
        "rejected" or "declined" or "refused" or "cancelled" or "canceled" or "مرفوضه" or "ملغيه" or "ملغاه" => TreatmentPlanStatus.Rejected,
        _ => null
    };

    public static TreatmentItemStatus? ItemStatusOf(string? value) => Key(value) switch
    {
        "planned" or "pending" or "todo" or "notdone" or "no" or "مخطط" or "لمينفذ" or "بانتظار" or "لا" => TreatmentItemStatus.Planned,
        "completed" or "complete" or "done" or "finished" or "yes" or "مكتمل" or "تم" or "منفذ" or "نعم" => TreatmentItemStatus.Completed,
        "cancelled" or "canceled" or "ملغي" or "ملغى" => TreatmentItemStatus.Cancelled,
        _ => null
    };

    public static string? Language(string? value) => Key(value) switch
    {
        "ar" or "arabic" or "عربي" or "العربيه" or "عربيه" => "ar",
        "en" or "english" or "انجليزي" or "الانجليزيه" or "انكليزي" => "en",
        _ => null
    };

    public static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.') && !value.Contains(' ');
    }
}
