using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.UE4.Pak;
using CUE4Parse.UE4.Versions;

namespace Aion2TR.Phase3Analyzer;

public static class GameUpdatePipeline
{
    private const string GameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
    private const string PakAesKey = "0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF";

    public static async Task RunAsync()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("================================================================================");
        Console.WriteLine("        AION 2 — GAME UPDATE LOCALIZATION ANALYSIS & DELTA PIPELINE             ");
        Console.WriteLine("================================================================================");

        string repoRoot = Phase5PatchService.FindRepoRoot();
        Console.WriteLine($"Repo Root: {repoRoot}");

        string officialPakPath = Path.Combine(GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");
        if (!File.Exists(officialPakPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Official PAK not found at: {officialPakPath}");
            Console.ResetColor();
            return;
        }

        var pakFileInfo = new FileInfo(officialPakPath);
        string pakSha256 = ComputeFileSha256(officialPakPath);
        Console.WriteLine($"Official PAK Path:      {officialPakPath}");
        Console.WriteLine($"Official PAK Size:      {pakFileInfo.Length:N0} bytes");
        Console.WriteLine($"Official PAK SHA-256:   {pakSha256}");
        Console.WriteLine($"Official PAK Modified:  {pakFileInfo.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC");

        // 1. Extract L10NString.dat directly from official PAK
        Console.WriteLine("\n[1/6] Extracting L10NString.dat using CUE4Parse PakFileReader...");
        byte[] newOfficialDatBytes;
        using (var reader = new PakFileReader(officialPakPath, new VersionContainer(EGame.GAME_UE5_3)))
        {
            reader.AesKey = new FAesKey(PakAesKey);
            reader.Mount(StringComparer.OrdinalIgnoreCase);

            const string internalPath = "Aion2/Content/L10N/Text/en-US/L10NString.dat";
            if (!reader.Files.ContainsKey(internalPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {internalPath} not found in PAK index!");
                Console.ResetColor();
                return;
            }

            var entry = (CUE4Parse.UE4.VirtualFileSystem.VfsEntry)reader.Files[internalPath];
            newOfficialDatBytes = reader.Extract(entry);
        }

        string newDatSha = ComputeSha256(newOfficialDatBytes);
        Console.WriteLine($"Extracted DAT Size:     {newOfficialDatBytes.Length:N0} bytes");
        Console.WriteLine($"Extracted DAT SHA-256:  {newDatSha}");

        string astraAuditDir = Path.Combine(repoRoot, "analysis", "astra_audit");
        Directory.CreateDirectory(astraAuditDir);
        string updatedDatPath = Path.Combine(astraAuditDir, "official_updated_candidate.dat");
        await File.WriteAllBytesAsync(updatedDatPath, newOfficialDatBytes);
        Console.WriteLine($"Saved extracted DAT to: {updatedDatPath}");

        // 2. Load / check key_manifest.dat
        Console.WriteLine("\n[2/6] Loading key manifest & decrypting L10N container...");
        string officialManifestPath = Path.Combine(astraAuditDir, "official_key_manifest.dat");
        byte[] manifestBytes = await File.ReadAllBytesAsync(officialManifestPath);
        var manifestKeys = Aion2KeyManifestReader.ReadManifest(manifestBytes);
        Console.WriteLine($"Key manifest loaded:    {manifestKeys.Count:N0} keys");

        ulong enSeed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");
        var (header, rawDecrypted) = Aion2L10NDecryptor.Decrypt(newOfficialDatBytes, "en-US", manifestKeys);

        if (!header.IsValid)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Container decryption failed: {header.ErrorMessage}");
            Console.ResetColor();
            return;
        }

        // Extract 32-byte prefix from decrypted payload
        byte[] officialPrefix32 = new byte[32];
        Array.Copy(newOfficialDatBytes, Aion2L10NDecryptor.PayloadOffset, officialPrefix32, 0, 32);
        string prefixHex = Convert.ToHexString(officialPrefix32);

        string rawSha = ComputeSha256(rawDecrypted);
        Console.WriteLine($"Container Version:      {header.Version}");
        Console.WriteLine($"Packed Size:            {header.PackedSize:N0} bytes");
        Console.WriteLine($"Aligned Size:           {header.AlignedSize:N0} bytes");
        Console.WriteLine($"Raw Decompressed Size:  {rawDecrypted.Length:N0} bytes | SHA-256: {rawSha}");
        Console.WriteLine($"32-byte Prefix Hex:     {prefixHex}");

        string updatedPlainPath = Path.Combine(astraAuditDir, "official_updated_candidate.plain");
        await File.WriteAllBytesAsync(updatedPlainPath, rawDecrypted);
        Console.WriteLine($"Saved plain table to:   {updatedPlainPath}");

        // 3. Parse New Official Table
        Console.WriteLine("\n[3/6] Parsing new official table...");
        var parser = new L10NStringParser();
        var newParseResult = parser.ParseRawTable(rawDecrypted, "NewOfficialTable");
        if (!newParseResult.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] Failed to parse new table: {newParseResult.StatusMessage}");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"New Official Entries:   {newParseResult.EntryCount:N0} rows");
        Console.WriteLine($"Namespace:              {newParseResult.Metadata.GetValueOrDefault("Namespace", "AION2")}");
        Console.WriteLine($"Version:                {newParseResult.Version}");

        // 4. Load Previous Official Table for Diff
        Console.WriteLine("\n[4/6] Comparing with previous official table...");
        string prevPlainPath = Path.Combine(astraAuditDir, "current_backup_candidate.plain");
        L10NParseResult? prevParseResult = null;
        if (File.Exists(prevPlainPath))
        {
            byte[] prevPlainBytes = await File.ReadAllBytesAsync(prevPlainPath);
            prevParseResult = parser.ParseRawTable(prevPlainBytes, "PrevOfficialTable");
            Console.WriteLine($"Previous Official Rows: {prevParseResult.EntryCount:N0} rows");
        }
        else
        {
            Console.WriteLine("[WARN] Previous plain table not found, skipping old vs new English diff.");
        }

        // 5. Load Current Turkish Translations
        Console.WriteLine("\n[5/6] Comparing with current Turkish patch database & artifacts...");
        string turkishPlainPath = Path.Combine(repoRoot, "v2", "artifacts", "step8", "L10NString.tr-TR.full-candidate.plain");
        L10NParseResult? turkishParseResult = null;
        if (File.Exists(turkishPlainPath))
        {
            byte[] turkishPlainBytes = await File.ReadAllBytesAsync(turkishPlainPath);
            turkishParseResult = parser.ParseRawTable(turkishPlainBytes, "TurkishFullTable");
            Console.WriteLine($"Current Turkish Rows:   {turkishParseResult.EntryCount:N0} rows");
        }

        // Build Delta analysis
        var newEntries = newParseResult.Entries;
        var prevEntriesMap = prevParseResult?.Entries.ToDictionary(e => e.Key ?? "", e => e.Text) ?? new Dictionary<string, string>();
        var turkishEntriesMap = turkishParseResult?.Entries.ToDictionary(e => e.Key ?? "", e => e.Text) ?? new Dictionary<string, string>();

        int exactEnglishMatchCount = 0;
        int englishChangedCount = 0;
        int brandNewKeysCount = 0;

        var changedRecords = new List<ChangedKeyRecord>();
        var brandNewRecords = new List<BrandNewKeyRecord>();

        for (int i = 0; i < newEntries.Count; i++)
        {
            var entry = newEntries[i];
            string key = entry.Key ?? "";
            string newText = entry.Text;

            if (prevEntriesMap.TryGetValue(key, out string? oldText))
            {
                if (oldText == newText)
                {
                    exactEnglishMatchCount++;
                }
                else
                {
                    englishChangedCount++;
                    turkishEntriesMap.TryGetValue(key, out string? tr);
                    changedRecords.Add(new ChangedKeyRecord(key, oldText, newText, tr));
                }
            }
            else
            {
                brandNewKeysCount++;
                brandNewRecords.Add(new BrandNewKeyRecord(key, newText));
            }
        }

        int removedKeysCount = prevEntriesMap.Count(k => !newEntries.Any(e => e.Key == k.Key));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("                         DELTA ANALYSIS SUMMARY                                 ");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Total Entries in New Official:   {newEntries.Count:N0}");
        Console.WriteLine($"Total Entries in Old Official:   {prevEntriesMap.Count:N0}");
        Console.WriteLine($"Exact Identical English Keys:    {exactEnglishMatchCount:N0} ({(double)exactEnglishMatchCount / newEntries.Count * 100:F2}%)");
        Console.WriteLine($"Modified English Source Keys:    {englishChangedCount:N0}");
        Console.WriteLine($"Brand New Keys Added:            {brandNewKeysCount:N0}");
        Console.WriteLine($"Keys Removed:                    {removedKeysCount:N0}");
        Console.ResetColor();

        // 6. Write Delta Report
        string reportsDir = Path.Combine(repoRoot, "reports");
        Directory.CreateDirectory(reportsDir);
        string deltaReportPath = Path.Combine(reportsDir, "GAME_UPDATE_DELTA_REPORT.md");

        var sb = new StringBuilder();
        sb.AppendLine("# Aion2TR — Oyun Güncellemesi Delta ve Uyumluluk Raporu");
        sb.AppendLine();
        sb.AppendLine($"**Tarih (UTC):** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"**Yeni Resmi PAK:** `pakchunk502000-Windows_0_P.pak` ({pakFileInfo.Length:N0} bytes)  ");
        sb.AppendLine($"**Yeni PAK SHA-256:** `{pakSha256}`  ");
        sb.AppendLine($"**Yeni DAT SHA-256:** `{newDatSha}`  ");
        sb.AppendLine($"**Yeni Deşifre Plain SHA-256:** `{rawSha}`  ");
        sb.AppendLine();
        sb.AppendLine("## 1. Özet İstatistikler");
        sb.AppendLine();
        sb.AppendLine("| Metrik | Değer |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine($"| **Eski Toplam Satır** | {prevEntriesMap.Count:N0} |");
        sb.AppendLine($"| **Yeni Toplam Satır** | {newEntries.Count:N0} |");
        sb.AppendLine($"| **Birebir Değişmeyen Satırlar (Mevcut Türkçe Korunacak)** | **{exactEnglishMatchCount:N0}** (%{(double)exactEnglishMatchCount / newEntries.Count * 100:F2}) |");
        sb.AppendLine($"| **İngilizce Metni Değişen Satırlar** | **{englishChangedCount:N0}** |");
        sb.AppendLine($"| **Yeni Eklenen Satırlar (Brand New)** | **{brandNewKeysCount:N0}** |");
        sb.AppendLine($"| **Silinen / Çıkarılan Satırlar** | **{removedKeysCount:N0}** |");
        sb.AppendLine();

        if (brandNewRecords.Count > 0)
        {
            sb.AppendLine("## 2. Yeni Eklenen Satırlar (İlk 50 Örnek)");
            sb.AppendLine();
            sb.AppendLine("| # | Key | Yeni İngilizce Metin |");
            sb.AppendLine("| :--- | :--- | :--- |");
            for (int i = 0; i < Math.Min(50, brandNewRecords.Count); i++)
            {
                var r = brandNewRecords[i];
                string safeKey = r.Key.Replace("|", "\\|");
                string safeText = r.NewEnglish.Replace("\n", " ").Replace("|", "\\|");
                if (safeText.Length > 80) safeText = safeText[..77] + "...";
                sb.AppendLine($"| {i + 1} | `{safeKey}` | {safeText} |");
            }
            sb.AppendLine();
        }

        if (changedRecords.Count > 0)
        {
            sb.AppendLine("## 3. İngilizce Metni Değişen Satırlar (İlk 50 Örnek)");
            sb.AppendLine();
            sb.AppendLine("| # | Key | Eski İngilizce | Yeni İngilizce | Mevcut Türkçe |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
            for (int i = 0; i < Math.Min(50, changedRecords.Count); i++)
            {
                var r = changedRecords[i];
                string safeKey = r.Key.Replace("|", "\\|");
                string safeOld = r.OldEnglish.Replace("\n", " ").Replace("|", "\\|");
                string safeNew = r.NewEnglish.Replace("\n", " ").Replace("|", "\\|");
                string safeTr = (r.ExistingTurkish ?? "-").Replace("\n", " ").Replace("|", "\\|");
                if (safeOld.Length > 40) safeOld = safeOld[..37] + "...";
                if (safeNew.Length > 40) safeNew = safeNew[..37] + "...";
                if (safeTr.Length > 40) safeTr = safeTr[..37] + "...";
                sb.AppendLine($"| {i + 1} | `{safeKey}` | {safeOld} | {safeNew} | {safeTr} |");
            }
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(deltaReportPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"\nDelta report saved to: {deltaReportPath}");

        // Save full reports for all records
        var sbNew = new StringBuilder();
        sbNew.AppendLine("# Aion2TR — Yeni Eklenen Tüm Satırlar (143 Satır)");
        sbNew.AppendLine();
        sbNew.AppendLine("| # | Key | Yeni İngilizce Metin |");
        sbNew.AppendLine("| :--- | :--- | :--- |");
        for (int i = 0; i < brandNewRecords.Count; i++)
        {
            var r = brandNewRecords[i];
            string safeKey = r.Key.Replace("|", "\\|");
            string safeText = r.NewEnglish.Replace("\n", "<br/>").Replace("|", "\\|");
            sbNew.AppendLine($"| {i + 1} | `{safeKey}` | {safeText} |");
        }
        await File.WriteAllTextAsync(Path.Combine(reportsDir, "GAME_UPDATE_BRAND_NEW_KEYS.md"), sbNew.ToString(), Encoding.UTF8);

        var sbChanged = new StringBuilder();
        sbChanged.AppendLine("# Aion2TR — İngilizce Metni Değişen Tüm Satırlar (227 Satır)");
        sbChanged.AppendLine();
        sbChanged.AppendLine("| # | Key | Eski İngilizce | Yeni İngilizce | Mevcut Türkçe |");
        sbChanged.AppendLine("| :--- | :--- | :--- | :--- | :--- |");
        for (int i = 0; i < changedRecords.Count; i++)
        {
            var r = changedRecords[i];
            string safeKey = r.Key.Replace("|", "\\|");
            string safeOld = r.OldEnglish.Replace("\n", "<br/>").Replace("|", "\\|");
            string safeNew = r.NewEnglish.Replace("\n", "<br/>").Replace("|", "\\|");
            string safeTr = (r.ExistingTurkish ?? "-").Replace("\n", "<br/>").Replace("|", "\\|");
            sbChanged.AppendLine($"| {i + 1} | `{safeKey}` | {safeOld} | {safeNew} | {safeTr} |");
        }
        await File.WriteAllTextAsync(Path.Combine(reportsDir, "GAME_UPDATE_CHANGED_KEYS.md"), sbChanged.ToString(), Encoding.UTF8);

        // Save raw delta JSON files for translation pipeline
        string deltaDataDir = Path.Combine(repoRoot, "data", "update_delta");
        Directory.CreateDirectory(deltaDataDir);
        string newKeysJson = Path.Combine(deltaDataDir, "brand_new_keys.json");
        string changedKeysJson = Path.Combine(deltaDataDir, "changed_keys.json");
        await File.WriteAllTextAsync(newKeysJson, JsonSerializer.Serialize(brandNewRecords, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        await File.WriteAllTextAsync(changedKeysJson, JsonSerializer.Serialize(changedRecords, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        Console.WriteLine($"Saved delta JSONs and full markdown tables to: {deltaDataDir} and {reportsDir}");
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }
}

public record BrandNewKeyRecord(string Key, string NewEnglish);
public record ChangedKeyRecord(string Key, string OldEnglish, string NewEnglish, string? ExistingTurkish);

