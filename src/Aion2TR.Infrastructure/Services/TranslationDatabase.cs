using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Microsoft.Data.Sqlite;

namespace Aion2TR.Infrastructure.Services;

public class TranslationDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbPath;

    public string DbPath => _dbPath;

    public TranslationDatabase(string dbPath)
    {
        _dbPath = dbPath;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _connectionString = $"Data Source={dbPath}";
    }

    public static string ComputeSourceHash(string text)
    {
        if (text == null) text = string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        string createTableSql = @"
CREATE TABLE IF NOT EXISTS translations (
    Key TEXT NOT NULL PRIMARY KEY,
    SourceText TEXT NOT NULL,
    SourceHash TEXT NOT NULL,
    TranslatedText TEXT,
    Status TEXT NOT NULL,
    Category TEXT NOT NULL,
    FirstSeenVersion TEXT NOT NULL,
    LastSeenVersion TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_translations_sourcehash ON translations(SourceHash);
CREATE INDEX IF NOT EXISTS idx_translations_status ON translations(Status);
CREATE INDEX IF NOT EXISTS idx_translations_category ON translations(Category);
";
        await using var cmd = new SqliteCommand(createTableSql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpsertBatchAsync(IReadOnlyList<TranslationEntry> entries, CancellationToken ct = default)
    {
        if (entries == null || entries.Count == 0)
            return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var tx = conn.BeginTransaction();

        string sql = @"
INSERT INTO translations (
    Key, SourceText, SourceHash, TranslatedText, Status, Category, FirstSeenVersion, LastSeenVersion, UpdatedAt
) VALUES (
    $key, $sourceText, $sourceHash, $translatedText, $status, $category, $firstSeenVersion, $lastSeenVersion, $updatedAt
)
ON CONFLICT(Key) DO UPDATE SET
    SourceText = excluded.SourceText,
    SourceHash = excluded.SourceHash,
    TranslatedText = COALESCE(translations.TranslatedText, excluded.TranslatedText),
    Status = CASE WHEN translations.Status IN ('Reviewed', 'Translated') THEN translations.Status ELSE excluded.Status END,
    Category = excluded.Category,
    LastSeenVersion = excluded.LastSeenVersion,
    UpdatedAt = excluded.UpdatedAt;
";

        await using var cmd = new SqliteCommand(sql, conn, tx);
        var pKey = cmd.Parameters.Add("$key", SqliteType.Text);
        var pSrc = cmd.Parameters.Add("$sourceText", SqliteType.Text);
        var pHash = cmd.Parameters.Add("$sourceHash", SqliteType.Text);
        var pTr = cmd.Parameters.Add("$translatedText", SqliteType.Text);
        var pStat = cmd.Parameters.Add("$status", SqliteType.Text);
        var pCat = cmd.Parameters.Add("$category", SqliteType.Text);
        var pV1 = cmd.Parameters.Add("$firstSeenVersion", SqliteType.Text);
        var pV2 = cmd.Parameters.Add("$lastSeenVersion", SqliteType.Text);
        var pUp = cmd.Parameters.Add("$updatedAt", SqliteType.Text);

        foreach (var entry in entries)
        {
            pKey.Value = entry.Key;
            pSrc.Value = entry.SourceText;
            pHash.Value = string.IsNullOrEmpty(entry.SourceHash) ? ComputeSourceHash(entry.SourceText) : entry.SourceHash;
            pTr.Value = (object?)entry.TranslatedText ?? DBNull.Value;
            pStat.Value = entry.Status.ToString();
            pCat.Value = entry.Category;
            pV1.Value = entry.FirstSeenVersion;
            pV2.Value = entry.LastSeenVersion;
            pUp.Value = entry.UpdatedAt.ToString("o");

            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<int> GetTotalCountAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand("SELECT COUNT(*) FROM translations;", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> GetUniqueSourceCountAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand("SELECT COUNT(DISTINCT SourceHash) FROM translations;", conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<Dictionary<TranslationStatus, int>> GetCountsByStatusAsync()
    {
        var dict = new Dictionary<TranslationStatus, int>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand("SELECT Status, COUNT(*) FROM translations GROUP BY Status;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var statStr = reader.GetString(0);
            int count = reader.GetInt32(1);
            if (Enum.TryParse<TranslationStatus>(statStr, out var status))
            {
                dict[status] = count;
            }
        }
        return dict;
    }

    public async Task<List<TranslationEntry>> GetAllEntriesAsync(CancellationToken ct = default)
    {
        var list = new List<TranslationEntry>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand("SELECT Key, SourceText, SourceHash, TranslatedText, Status, Category, FirstSeenVersion, LastSeenVersion, UpdatedAt FROM translations;", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new TranslationEntry
            {
                Key = reader.GetString(0),
                SourceText = reader.GetString(1),
                SourceHash = reader.GetString(2),
                TranslatedText = reader.IsDBNull(3) ? null : reader.GetString(3),
                Status = Enum.TryParse<TranslationStatus>(reader.GetString(4), out var s) ? s : TranslationStatus.Untranslated,
                Category = reader.GetString(5),
                FirstSeenVersion = reader.GetString(6),
                LastSeenVersion = reader.GetString(7),
                UpdatedAt = DateTime.TryParse(reader.GetString(8), out var dt) ? dt : DateTime.UtcNow
            });
        }
        return list;
    }

    public async Task<int> ApplyTranslationBySourceHashAsync(string sourceHash, string translatedText, TranslationStatus status, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        string sql = @"
UPDATE translations
SET TranslatedText = $tr, Status = $status, UpdatedAt = $now
WHERE SourceHash = $hash AND Status != 'Reviewed';";
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("$tr", translatedText);
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$hash", sourceHash);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}
