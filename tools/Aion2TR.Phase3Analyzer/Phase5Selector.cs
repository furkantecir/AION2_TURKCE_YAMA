using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Aion2TR.Phase3Analyzer;

public record SelectedTestString(
    string Key,
    string SourceText,
    string TargetTestText,
    string? CurrentTranslatedText,
    string Category,
    string Context,
    string SourceHash
);

public static class Phase5Selector
{
    public static async Task<List<SelectedTestString>> SelectTestStringsAsync()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string dbPath = Path.Combine(repoRoot, "data", "aion2tr.db");

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // Candidates map: Target text, Turkish test translation, Key priority regex/keywords
        var targets = new (string En, string Tr, string PreferredKeySubstring)[]
        {
            ("Settings", "Ayarlar", "Setting"),
            ("Cancel", "İptal", "Cancel"),
            ("Confirm", "Onayla", "Confirm"),
            ("Exit", "Çıkış", "Exit"),
            ("Back", "Geri", "Back"),
            ("Apply", "Uygula", "Apply"),
            ("Close", "Kapat", "Close"),
            ("Yes", "Evet", "Yes"),
            ("No", "Hayır", "No")
        };

        var selected = new List<SelectedTestString>();

        foreach (var (en, tr, prefKey) in targets)
        {
            string sql = @"
SELECT Key, SourceText, SourceHash, TranslatedText, Category
FROM translations
WHERE LOWER(TRIM(SourceText)) = LOWER($src)
ORDER BY 
    CASE 
        WHEN Key LIKE '%UI%' AND Key LIKE '%' || $pref || '%' THEN 1
        WHEN Key LIKE '%' || $pref || '%' THEN 2
        WHEN Key LIKE '%UI%' THEN 3
        WHEN Key LIKE '%System%' THEN 4
        ELSE 5
    END ASC
LIMIT 1;";

            await using var cmd = new SqliteCommand(sql, conn);
            cmd.Parameters.AddWithValue("$src", en);
            cmd.Parameters.AddWithValue("$pref", prefKey);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                string key = reader.GetString(0);
                string srcText = reader.GetString(1);
                string srcHash = reader.GetString(2);
                string? curTr = reader.IsDBNull(3) ? null : reader.GetString(3);
                string cat = reader.GetString(4);

                selected.Add(new SelectedTestString(
                    key,
                    srcText,
                    tr,
                    curTr,
                    cat,
                    $"UI Common/Menu Button ({prefKey})",
                    srcHash
                ));
            }
        }

        string reportPath = Path.Combine(repoRoot, "reports", "phase5", "selected_test_strings.json");
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(selected, opts));
        Console.WriteLine($"[SAVED] {reportPath} ({selected.Count} test strings selected)");

        foreach (var s in selected)
        {
            Console.WriteLine($"  - [{s.Key}] '{s.SourceText}' -> '{s.TargetTestText}' (Cat: {s.Category}, Hash: {s.SourceHash[..8]}..)");
        }

        return selected;
    }
}
