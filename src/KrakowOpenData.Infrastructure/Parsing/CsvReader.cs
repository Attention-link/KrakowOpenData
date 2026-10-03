using System.Text;

namespace KrakowOpenData.Infrastructure.Parsing;

/// <summary>
/// Minimal RFC 4180 CSV reader (quoted fields, escaped quotes, embedded commas and newlines,
/// CRLF or LF, optional UTF-8 BOM). First row is the header. Header names are trimmed.
/// Missing trailing columns read as empty strings.
/// </summary>
public static class CsvReader
{
    public static IEnumerable<CsvRow> Read(TextReader reader, char separator = ',')
    {
        string[]? header = null;
        Dictionary<string, int>? index = null;

        foreach (var fields in ReadRecords(reader, separator))
        {
            if (header is null)
            {
                header = fields.Select((f, i) => i == 0 ? f.TrimStart('﻿').Trim() : f.Trim()).ToArray();
                index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < header.Length; i++) index.TryAdd(header[i], i);
                continue;
            }

            // Skip completely empty lines.
            if (fields.Count == 1 && fields[0].Length == 0) continue;

            yield return new CsvRow(index!, fields);
        }
    }

    internal static IEnumerable<List<string>> ReadRecords(TextReader reader, char separator)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var anyContent = false;

        int read;
        while ((read = reader.Read()) != -1)
        {
            var c = (char)read;
            anyContent = true;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        field.Append('"');
                        reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r')
            {
                if (reader.Peek() == '\n') reader.Read();
                fields.Add(field.ToString());
                field.Clear();
                yield return fields;
                fields = [];
                anyContent = false;
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                yield return fields;
                fields = [];
                anyContent = false;
            }
            else
            {
                field.Append(c);
            }
        }

        if (anyContent || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields;
        }
    }
}

public sealed class CsvRow(IReadOnlyDictionary<string, int> index, IReadOnlyList<string> fields)
{
    public string this[string column] => Get(column) ?? string.Empty;

    public bool Has(string column) => index.ContainsKey(column);

    /// <summary>Value of a column, or null when the column is absent; empty strings become null.</summary>
    public string? Get(string column)
    {
        if (!index.TryGetValue(column, out var i) || i >= fields.Count) return null;
        var value = fields[i].Trim();
        return value.Length == 0 ? null : value;
    }
}
