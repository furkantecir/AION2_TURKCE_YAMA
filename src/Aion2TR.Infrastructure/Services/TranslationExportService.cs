using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Infrastructure.Services;

public class TranslationExportItem
{
    public string Key { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string? TranslatedText { get; set; }
    public string Status { get; set; } = "Untranslated";
    public string Category { get; set; } = "Unknown";
    public string SourceHash { get; set; } = string.Empty;
}

public class TranslationExportService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task ExportToJsonAsync(string filePath, IReadOnlyList<TranslationEntry> entries)
    {
        var items = entries.Select(e => new TranslationExportItem
        {
            Key = e.Key,
            SourceText = e.SourceText,
            TranslatedText = e.TranslatedText,
            Status = e.Status.ToString(),
            Category = e.Category,
            SourceHash = e.SourceHash
        }).ToList();

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(items, JsonOpts);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
    }

    public async Task ExportToCsvAsync(string filePath, IReadOnlyList<TranslationEntry> entries)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("Key,SourceText,TranslatedText,Status,Category,SourceHash");

        foreach (var e in entries)
        {
            sb.Append(EscapeCsv(e.Key)).Append(',');
            sb.Append(EscapeCsv(e.SourceText)).Append(',');
            sb.Append(EscapeCsv(e.TranslatedText ?? "")).Append(',');
            sb.Append(EscapeCsv(e.Status.ToString())).Append(',');
            sb.Append(EscapeCsv(e.Category)).Append(',');
            sb.Append(EscapeCsv(e.SourceHash));
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    public async Task<List<TranslationExportItem>> ImportFromJsonAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        var items = JsonSerializer.Deserialize<List<TranslationExportItem>>(json, JsonOpts);
        return items ?? [];
    }

    public async Task<List<TranslationExportItem>> ImportFromCsvAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return [];

        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        if (lines.Length <= 1) return [];

        var list = new List<TranslationExportItem>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = ParseCsvLine(line);
            if (parts.Count >= 6)
            {
                list.Add(new TranslationExportItem
                {
                    Key = parts[0],
                    SourceText = parts[1],
                    TranslatedText = string.IsNullOrEmpty(parts[2]) ? null : parts[2],
                    Status = parts[3],
                    Category = parts[4],
                    SourceHash = parts[5]
                });
            }
        }
        return list;
    }

    private static string EscapeCsv(string s)
    {
        if (s == null) return "\"\"";
        bool mustQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (mustQuote)
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var sb = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                {
                    sb.Append('\"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }
}
