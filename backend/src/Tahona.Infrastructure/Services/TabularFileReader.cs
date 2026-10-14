using System.Globalization;
using System.Text;
using ExcelDataReader;
using Tahona.Application.Common;
using Tahona.Application.Features.Import;

namespace Tahona.Infrastructure.Services;

/// <summary>
/// Reads an uploaded spreadsheet into text cells. CSV files are decoded as UTF-8 when valid, otherwise as
/// Windows-1256, the encoding older Arabic Windows software exports. Excel files use the first sheet that has data.
/// </summary>
public sealed class TabularFileReader : ITabularFileReader
{
    static TabularFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public TabularFile Read(Stream content, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            return extension switch
            {
                ".csv" or ".txt" or ".tsv" => CsvText.Parse(Decode(content)),
                ".xlsx" or ".xls" => ReadExcel(content),
                _ => throw RequestValidationException.For("file", "Upload a CSV file or an Excel workbook (.xlsx or .xls).")
            };
        }
        catch (Exception ex) when (ex is not AppException)
        {
            throw RequestValidationException.For("file", "This file could not be read. Save it again as CSV or .xlsx and upload it once more.");
        }
    }

    private static string Decode(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        var bytes = buffer.ToArray();
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1256).GetString(bytes);
        }
    }

    private static TabularFile ReadExcel(Stream content)
    {
        using var reader = ExcelReaderFactory.CreateReader(content);
        do
        {
            var rows = new List<IReadOnlyList<string>>();
            while (reader.Read())
            {
                var cells = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) cells[i] = Text(reader.GetValue(i));
                rows.Add(cells);
            }
            // The header is the first row with any content; leading blank rows are layout.
            var header = rows.FindIndex(r => r.Any(c => c.Length > 0));
            if (header >= 0 && rows.Skip(header + 1).Any(r => r.Any(c => c.Length > 0)))
                return CsvText.Shape(rows[header], rows.Skip(header + 1));
        } while (reader.NextResult());

        throw RequestValidationException.For("file", "No sheet in this workbook has a header row followed by data.");
    }

    /// <summary>Renders a typed Excel cell the way <see cref="ImportParsing"/> reads it back.</summary>
    private static string Text(object? value) => value switch
    {
        null => "",
        DateTime d when d.Year <= 1900 => d.ToString("HH:mm:ss", CultureInfo.InvariantCulture),            // a time-of-day cell
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        double n => n.ToString("0.############", CultureInfo.InvariantCulture),                              // phone numbers typed as numbers
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? ""
    };
}
