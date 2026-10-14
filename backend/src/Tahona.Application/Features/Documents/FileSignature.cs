using System.Text;

namespace Tahona.Application.Features.Documents;

/// <summary>
/// Checks that a file's first bytes match what its extension claims. The browser-supplied content type is not
/// trusted: an HTML page renamed to .png is rejected here, before anything is stored.
/// </summary>
public static class FileSignature
{
    /// <summary>Bytes needed to recognise every supported type (DICOM keeps its marker at offset 128).</summary>
    public const int HeaderLength = 132;

    /// <summary>The content type served on download, decided by the server from the extension.</summary>
    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".dcm"] = "application/dicom",
        [".stl"] = "model/stl"
    };

    public static bool Matches(string extension, ReadOnlySpan<byte> header, long totalLength) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => header.StartsWith<byte>([0xFF, 0xD8, 0xFF]),
        ".png" => header.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        ".webp" => header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8),
        ".pdf" => header.StartsWith("%PDF-"u8),
        ".dcm" => header.Length >= 132 && header.Slice(128, 4).SequenceEqual("DICM"u8),
        ".stl" => IsStl(header, totalLength),
        _ => false
    };

    /// <summary>ASCII STL starts with "solid"; binary STL is an 80-byte header, a triangle count, then 50 bytes per triangle.</summary>
    private static bool IsStl(ReadOnlySpan<byte> header, long totalLength)
    {
        if (header.Length >= 84)
        {
            var triangles = BitConverter.ToUInt32(header.Slice(80, 4));
            if (totalLength == 84 + 50L * triangles) return true;
        }
        return header.Length >= 5 && Encoding.ASCII.GetString(header[..5]).Equals("solid", StringComparison.OrdinalIgnoreCase);
    }
}
