using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Aion2TR.Phase3Analyzer;

public static class DbInspector
{
    public static async Task InspectAsync()
    {
        string dbPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "aion2tr.db"));
        Console.WriteLine($"Inspecting DB: {dbPath}");
        if (!File.Exists(dbPath))
        {
            Console.WriteLine("DB not found!");
            return;
        }

        await using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 0. sqlite_master
        await using (var cmd = new SqliteCommand("SELECT name, type FROM sqlite_master;", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            Console.WriteLine("sqlite_master entries:");
            while (await reader.ReadAsync())
            {
                Console.WriteLine($"  {reader.GetString(0)} ({reader.GetString(1)})");
            }
        }
        await using (var cmd = new SqliteCommand("SELECT COUNT(*), COUNT(DISTINCT SourceHash) FROM translations;", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                Console.WriteLine($"Total rows: {reader.GetInt32(0)}, Unique SourceHashes: {reader.GetInt32(1)}");
            }
        }

        // Columns
        await using (var cmd = new SqliteCommand("PRAGMA table_info(translations);", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            Console.WriteLine("Columns:");
            while (await reader.ReadAsync())
            {
                Console.WriteLine($"  {reader.GetString(1)} ({reader.GetString(2)})");
            }
        }

        // Sync status for TranslatedText
        await using (var cmd = new SqliteCommand("UPDATE translations SET Status = 'Translated' WHERE TranslatedText IS NOT NULL AND TranslatedText != '' AND Status != 'Protected';", conn))
        {
            int updated = await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"Synchronized Status to 'Translated' for {updated:N0} rows.");
        }

        // Distinct Statuses
        await using (var cmd = new SqliteCommand("SELECT Status, COUNT(*), COUNT(DISTINCT SourceHash) FROM translations GROUP BY Status;", conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            Console.WriteLine("Status Breakdown (rows, distinct hashes):");
            while (await reader.ReadAsync())
            {
                Console.WriteLine($"  Status '{reader.GetString(0)}': {reader.GetInt32(1)} rows, {reader.GetInt32(2)} unique hashes");
            }
        }

        // Check if any TranslatedText has non-null/non-empty
        await using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM translations WHERE TranslatedText IS NOT NULL;", conn))
        {
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Console.WriteLine($"Rows with TranslatedText IS NOT NULL: {count}");
        }

        // Check unique non-protected count
        await using (var cmd = new SqliteCommand("SELECT COUNT(DISTINCT SourceHash) FROM translations WHERE Status != 'Protected';", conn))
        {
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Console.WriteLine($"Unique SourceHashes where Status != 'Protected': {count}");
        }

        // Check if protection was applied strictly per rule
        // In Phase 4 analyzer:
        // totalEntries: 153,042
        // uniqueSources: 82,485
        // protectedCount: 10,176
        // 82,485 - 10,176 = 72,309
        await using (var cmd = new SqliteCommand("SELECT COUNT(DISTINCT SourceHash) FROM translations WHERE Status = 'Untranslated';", conn))
        {
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Console.WriteLine($"Untranslated unique SourceHashes: {count}");
        }
    }
}
