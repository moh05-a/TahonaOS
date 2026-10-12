namespace Tahona.Domain.Common;

/// <summary>Phone helpers tuned for Jordanian numbers (+962 7X XXX XXXX) but tolerant of other formats.</summary>
public static class PhoneNumber
{
    /// <summary>
    /// Reduces any input to a canonical digits-only national form so that
    /// "0791234567", "+962 79 123 4567" and "00962791234567" all become "791234567".
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00962")) digits = digits[5..];
        else if (digits.StartsWith("962")) digits = digits[3..];
        if (digits.StartsWith('0')) digits = digits.TrimStart('0');
        return digits;
    }

    /// <summary>International E.164 digits (no plus) for WhatsApp, e.g. 962791234567.</summary>
    public static string ToInternational(string? input, string countryCode = "962")
    {
        var national = Normalize(input);
        if (national.Length == 0) return string.Empty;
        // Already international for a different country (long number not starting with a Jordan mobile prefix).
        if (national.Length > 10) return national;
        return countryCode + national;
    }

    /// <summary>True when the text looks like a phone search (mostly digits).</summary>
    public static bool LooksLikePhone(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var digitCount = text.Count(char.IsDigit);
        return digitCount >= 3 && digitCount >= text.Trim().Length - 3;
    }
}
