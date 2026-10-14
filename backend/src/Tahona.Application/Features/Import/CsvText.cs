using System.Text;
using Tahona.Application.Common;

namespace Tahona.Application.Features.Import;

/// <summary>
/// CSV reading and writing as spreadsheets produce it: quoted cells, doubled quotes, line breaks inside quotes,
/// and comma, semicolon or tab as the separator (Excel uses a semicolon in many regional settings).
/// </summary>
public static class CsvText
{
    public static TabularFile Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var separator = DetectSeparator(text);
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        void EndCell()
        {
            record.Add(cell.ToString().Trim());
            cell.Clear();
        }

        void EndRecord()
        {
            EndCell();
            if (record.Any(c => c.Length > 0)) records.Add(record); // blank lines are not rows
            record = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
            }
            else if (c == '"' && cell.ToString().Trim().Length == 0)
            {
                cell.Clear();
                quoted = true;
            }
            else if (c == separator) EndCell();
            else if (c == '\n') EndRecord();
            else if (c != '\r') cell.Append(c);
        }
        if (quoted) throw RequestValidationException.For("file", "The file has an unclosed quotation mark, so its rows cannot be separated reliably.");
        if (cell.Length > 0 || record.Count > 0) EndRecord();

        if (records.Count == 0) throw RequestValidationException.For("file", "The file is empty.");
        return Shape(records[0], records.Skip(1));
    }

    /// <summary>Gives every row exactly one cell per header and names blank headers, so columns can be mapped.</summary>
    public static TabularFile Shape(IReadOnlyList<string> headerRow, IEnumerable<IReadOnlyList<string>> dataRows)
    {
        var rows = dataRows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        var width = Math.Max(headerRow.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
        // Trailing columns that are empty everywhere are spreadsheet padding.
        while (width > 0 && string.IsNullOrWhiteSpace(headerRow.ElementAtOrDefault(width - 1))
               && rows.All(r => string.IsNullOrWhiteSpace(r.ElementAtOrDefault(width - 1)))) width--;
        if (width == 0) throw RequestValidationException.For("file", "The file has no columns.");

        var headers = new List<string>();
        for (var i = 0; i < width; i++)
        {
            var name = (headerRow.ElementAtOrDefault(i) ?? "").Trim();
            if (name.Length == 0) name = $"Column {i + 1}";
            var unique = name;
            for (var n = 2; headers.Contains(unique, StringComparer.OrdinalIgnoreCase); n++) unique = $"{name} ({n})";
            headers.Add(unique);
        }
        return new TabularFile(headers, rows.Select(r => (IReadOnlyList<string>)Enumerable.Range(0, width)
            .Select(i => (r.ElementAtOrDefault(i) ?? "").Trim()).ToList()).ToList());
    }

    /// <summary>The separator that occurs most often outside quotes on the first line.</summary>
    private static char DetectSeparator(string text)
    {
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && c == '\n') break;
            else if (!quoted && counts.ContainsKey(c)) counts[c]++;
        }
        return counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    /// <summary>
    /// Writes rows as CSV with a byte-order mark, so Excel opens Arabic text correctly. A cell that starts with
    /// =, +, - or @ is prefixed with an apostrophe so a spreadsheet does not run it as a formula.
    /// </summary>
    public static byte[] Write(IEnumerable<IEnumerable<string?>> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder.AppendJoin(',', row.Select(Escape)).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    private static string Escape(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
