namespace Tahona.Domain.Common;

/// <summary>FDI (ISO 3950) tooth numbering helpers.</summary>
public static class Teeth
{
    public static readonly IReadOnlyList<int> Permanent =
    [
        18, 17, 16, 15, 14, 13, 12, 11, 21, 22, 23, 24, 25, 26, 27, 28,
        48, 47, 46, 45, 44, 43, 42, 41, 31, 32, 33, 34, 35, 36, 37, 38
    ];

    public static readonly IReadOnlyList<int> Primary =
    [
        55, 54, 53, 52, 51, 61, 62, 63, 64, 65,
        85, 84, 83, 82, 81, 71, 72, 73, 74, 75
    ];

    public static bool IsValid(int tooth) => IsPermanent(tooth) || IsPrimaryTooth(tooth);

    public static bool IsPermanent(int tooth) => tooth / 10 is >= 1 and <= 4 && tooth % 10 is >= 1 and <= 8;

    public static bool IsPrimaryTooth(int tooth) => tooth / 10 is >= 5 and <= 8 && tooth % 10 is >= 1 and <= 5;

    public static bool IsValidSurfaces(string? surfaces) =>
        string.IsNullOrEmpty(surfaces) || surfaces.All(c => "MODBLIF".Contains(char.ToUpperInvariant(c)));
}
