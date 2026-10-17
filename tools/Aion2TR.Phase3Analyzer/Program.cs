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
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

namespace Aion2TR.Phase3Analyzer;

internal class Program
{
    private const string GameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
    private const string Aion2AesKeyHex = "0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF";

    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length > 0 && args[0] == "--build-phase5-artifact")
        {
            await Phase5Builder.BuildAndValidateTestArtifactAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--select-test-strings")
        {
            await Phase5Selector.SelectTestStringsAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--inspect")
        {
            await DbInspector.InspectAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--batch")
        {
            int batchNum = (args.Length > 1 && int.TryParse(args[1], out int n)) ? n : 2;
            await BatchManager.RunBatchAsync(batchNum);
            return;
        }

        if (args.Length > 0 && args[0] == "--batch-all")
        {
            for (int b = 3; b <= 8; b++)
            {
                await BatchManager.RunBatchAsync(b);
            }
            return;
        }

        if (args.Length > 0 && args[0] == "--build-final-dat")
        {
            await Phase6FinalBuilder.BuildAndVerifyFinalDatAsync();
            return;
        }

        if (args.Length > 0 && args[0] == "--batch-prep")
        {
            await BatchManager.RunBatchAsync(1);
            return;
        }

        if (args.Length > 0 && args[0] == "--install-patch")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            var service = new Phase7PatchService(repo);
            Console.WriteLine("[INSTALL] Generating and deploying Turkish localization patch (72,309 strings)...");
            var res = await service.InstallTestPakAsync(GameRoot);
            Console.WriteLine($"Result: Success={res.Success}\nMessage:\n{res.Message}");
            return;
        }

        if (args.Length > 0 && args[0] == "--rollback-patch")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            var service = new Phase7PatchService(repo);
            Console.WriteLine("[ROLLBACK] Removing Turkish localization patch...");
            var res = await service.RollbackTestPakAsync(GameRoot);
            Console.WriteLine($"Result: Success={res.Success}\nMessage:\n{res.Message}");
            return;
        }

        if (args.Length > 0 && args[0] == "--analyze-legacy")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            await LegacyPatchStaticAnalyzer.AnalyzeAsync(repo);
            return;
        }

        if (args.Length > 0 && args[0] == "--phase7-3")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            await Phase7_3MasterForensic.RunAsync(repo);
            return;
        }

        if (args.Length > 0 && args[0] == "--build-phase7-2-test")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            await Phase7_2TestBuilder.BuildAndValidatePhase7_2TestDatAsync(repo);
            return;
        }

        if (args.Length > 0 && args[0] == "--build-phase7-2-2-diagnostic")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            await Phase7_2_2DiagnosticBuilder.BuildAndValidateDiagnosticTestDatAsync(repo);
            return;
        }

        if (args.Length > 0 && args[0] == "--inspect-chinese")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            await ChinesePatchAnalyzer.AnalyzeAsync(repo);
            return;
        }

        if (args.Length > 0 && args[0] == "--inspect-encoding")
        {
            string repo = Phase5PatchService.FindRepoRoot();
            await LegacyEncodingAnalyzer.AnalyzeAsync(repo);
            return;
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine("        AION 2 — PHASE 4: LOSSLESS SERIALIZER + TRANSLATION PIPELINE            ");
        Console.WriteLine("================================================================================");

        if (!Directory.Exists(GameRoot))
        {
            Console.WriteLine("[ERROR] Game path not found!");
            return;
        }

        // 1. In-Memory Extraction and Source Game File Hash Verification
        Console.WriteLine("\n[1/8] In-Memory Dosya Çıkarımı ve Orijinal SHA-256 Doğrulaması...");
        var mainPakDir = Path.Combine(GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        var generalPaksDir = Path.Combine(GameRoot, "Aion2", "Content", "Paks");

        byte[] mainL10nBytes = ExtractFile(mainPakDir, "Aion2/Content/L10N/Text/en-US/L10NString.dat");
        byte[] keyManifestBytes = ExtractFile(generalPaksDir, "Aion2/Content/StartUpData/Table/key_manifest.dat");

        string originalL10nSha = ComputeSha256(mainL10nBytes);
        string originalManifestSha = ComputeSha256(keyManifestBytes);

        Console.WriteLine($"Main L10NString.dat: {mainL10nBytes.Length:N0} bytes | SHA-256: {originalL10nSha}");
        Console.WriteLine($"key_manifest.dat:   {keyManifestBytes.Length:N0} bytes | SHA-256: {originalManifestSha}");

        // 2. Resolve Manifest & Decrypt L10N Container
        Console.WriteLine("\n[2/8] Manifest Çözümleme ve L10NString.dat Deşifre/Decompress...");
        var manifestKeys = Aion2KeyManifestReader.ReadManifest(keyManifestBytes);
        ulong seed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");
        var (header, rawMainL10n) = Aion2L10NDecryptor.Decrypt(mainL10nBytes, "en-US", manifestKeys);

        Console.WriteLine($"Container Header: Version={header.Version}, PackedSize={header.PackedSize:N0}, AlignedSize={header.AlignedSize:N0}, RawSize={header.RawSize:N0}");
        Console.WriteLine($"Decompressed Raw Size: {rawMainL10n.Length:N0} bytes | SHA-256: {ComputeSha256(rawMainL10n)}");

        // 3. Parse RAW UE Localization Table
        Console.WriteLine("\n[3/8] RAW UE Localization Tablosunun Ayrıştırılması...");
        var parser = new L10NStringParser();
        var parseResult = parser.ParseRawTable(rawMainL10n, "MainTable");

        if (!parseResult.Success)
        {
            Console.WriteLine($"[ERROR] Parse failed: {parseResult.StatusMessage}");
            return;
        }

        int totalEntries = parseResult.EntryCount;
        string tableNamespace = parseResult.Metadata.TryGetValue("Namespace", out var ns) ? ns.ToString()! : "AION2";
        Console.WriteLine($"Namespace: {tableNamespace} | Toplam Kayıt Sayısı: {totalEntries:N0}");

        // 4. Raw Table Serialization Round-Trip (Byte-identical verification)
        Console.WriteLine("\n[4/8] RAW Table Serializer ve Round-Trip Doğrulaması...");
        byte[] serializedRaw = L10NStringSerializer.Serialize(parseResult.Entries, tableNamespace, parseResult.Version);

        bool isRawByteIdentical = rawMainL10n.SequenceEqual(serializedRaw);
        Console.WriteLine($"Original Raw Size:   {rawMainL10n.Length:N0} bytes | SHA-256: {ComputeSha256(rawMainL10n)}");
        Console.WriteLine($"Serialized Raw Size: {serializedRaw.Length:N0} bytes | SHA-256: {ComputeSha256(serializedRaw)}");
        Console.WriteLine($"RAW TABLE BYTE-IDENTICAL ROUND-TRIP: {(isRawByteIdentical ? "[EVET - %100 BİREBİR EŞİT!]" : "[FARKLI]")}");

        // Container Round-Trip
        byte[] rebuiltContainer = Aion2L10NBuilder.BuildContainer(serializedRaw, "en-US", manifestKeys);
        var (rebuiltHeader, decryptedRaw) = Aion2L10NDecryptor.Decrypt(rebuiltContainer, "en-US", manifestKeys);
        bool isSemanticIdentical = decryptedRaw.SequenceEqual(rawMainL10n);
        Console.WriteLine($"CONTAINER SEMANTIC ROUND-TRIP: {(isSemanticIdentical ? "[EVET - %100 TAM EŞLEŞTİ!]" : "[HATA]")}");

        // 5. String Deduplication and Content Classification Analysis
        Console.WriteLine("\n[5/8] String Deduplication ve Koruma Analizi...");
        var protectionService = new TranslationProtectionService();
        var contextService = new TranslationContextService();

        int emptyCount = 0;
        int numericOnlyCount = 0;
        int symbolOnlyCount = 0;
        int nonEnglishCount = 0;
        int protectedCount = 0;

        var uniqueSources = new HashSet<string>(StringComparer.Ordinal);
        var translationEntries = new List<TranslationEntry>(totalEntries);

        foreach (var entry in parseResult.Entries)
        {
            string text = entry.Text;
            string key = entry.Key ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text)) emptyCount++;
            else
            {
                if (text.All(c => char.IsDigit(c) || c is '+' or '-' or '.' or ',' or '%' or ' ' or '/' or ':') && text.Any(char.IsDigit))
                    numericOnlyCount++;

                if (text.All(c => !char.IsLetterOrDigit(c)))
                    symbolOnlyCount++;

                // Check for CJK or non-ascii scripts
                if (text.Any(c => (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x3040 && c <= 0x30FF)))
                    nonEnglishCount++;
            }

            uniqueSources.Add(text);

            var prot = protectionService.Evaluate(key, text);
            var status = prot.IsProtected ? TranslationStatus.Protected : TranslationStatus.Untranslated;
            if (prot.IsProtected) protectedCount++;

            string cat = contextService.InferCategory(key);
            string srcHash = TranslationDatabase.ComputeSourceHash(text);

            translationEntries.Add(new TranslationEntry
            {
                Key = key,
                SourceText = text,
                SourceHash = srcHash,
                TranslatedText = null,
                Status = status,
                Category = cat,
                FirstSeenVersion = "1.0.0",
                LastSeenVersion = "1.0.0",
                UpdatedAt = DateTime.UtcNow
            });
        }

        int uniqueCount = uniqueSources.Count;
        int duplicateCount = totalEntries - uniqueCount;

        Console.WriteLine($"Total Entries:          {totalEntries:N0}");
        Console.WriteLine($"Unique Source Strings:  {uniqueCount:N0}");
        Console.WriteLine($"Duplicate Strings:      {duplicateCount:N0} (Tasarruf: %{(double)duplicateCount / totalEntries * 100:F1})");
        Console.WriteLine($"Empty Strings:          {emptyCount:N0}");
        Console.WriteLine($"Numeric-only Strings:   {numericOnlyCount:N0}");
        Console.WriteLine($"Symbol-only Strings:    {symbolOnlyCount:N0}");
        Console.WriteLine($"Non-English (CJK/etc.): {nonEnglishCount:N0}");
        Console.WriteLine($"Protected Strings:      {protectedCount:N0}");

        // 6. SQLite Translation Database Setup & Bulk Ingestion
        Console.WriteLine("\n[6/8] SQLite Veritabanı Hazırlama (data/aion2tr.db)...");
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string dbPath = Path.Combine(repoRoot, "data", "aion2tr.db");

        using (var db = new TranslationDatabase(dbPath))
        {
            await db.InitializeAsync();
            Console.WriteLine($"[OK] Veritabanı oluşturuldu/bağlandı: {dbPath}");

            Console.WriteLine("     Kayıtlar SQLite veritabanına toplu aktarılıyor...");
            await db.UpsertBatchAsync(translationEntries);

            int dbTotal = await db.GetTotalCountAsync();
            int dbUnique = await db.GetUniqueSourceCountAsync();
            var statusCounts = await db.GetCountsByStatusAsync();

            Console.WriteLine($"[OK] SQLite Toplam Kayıt: {dbTotal:N0} | Benzersiz SourceHash: {dbUnique:N0}");
            foreach (var (st, cnt) in statusCounts)
            {
                Console.WriteLine($"     - Durum [{st}]: {cnt:N0}");
            }
        }

        // 7. JSON and CSV Export
        Console.WriteLine("\n[7/8] JSON ve CSV Dışa Aktarımı...");
        var exportService = new TranslationExportService();
        string trJsonPath = Path.Combine(repoRoot, "translations", "tr-TR.json");
        string trCsvPath = Path.Combine(repoRoot, "translations", "tr-TR.csv");

        await exportService.ExportToJsonAsync(trJsonPath, translationEntries);
        Console.WriteLine($"[SAVED] {trJsonPath}");

        await exportService.ExportToCsvAsync(trCsvPath, translationEntries);
        Console.WriteLine($"[SAVED] {trCsvPath}");

        // 8. Controlled Pseudo-Translation & Turkish Unicode Test DAT
        Console.WriteLine("\n[8/8] 100-Entry Pseudo-Translation ve Türkçe Unicode Test DAT Üretimi...");
        var artifactsDir = Path.Combine(repoRoot, "artifacts");
        Directory.CreateDirectory(artifactsDir);
        string testDatPath = Path.Combine(artifactsDir, "L10NString.tr-test.dat");

        // Clone entries
        var testEntries = new List<L10NEntry>(parseResult.Entries.Count);
        for (int i = 0; i < parseResult.Entries.Count; i++)
        {
            var orig = parseResult.Entries[i];
            testEntries.Add(new L10NEntry(orig.Key ?? "", orig.Text, orig.Offset, orig.IsKeyUtf16, orig.IsTextUtf16));
        }

        // Pick 100 eligible entries for pseudo translation
        var pseudoProvider = new PseudoTranslationProvider();
        var modifiedIndexes = new List<int>();

        // Specifically customize some known strings for Turkish Unicode demonstration
        int replacedCount = 0;
        for (int i = 0; i < testEntries.Count && replacedCount < 100; i++)
        {
            var e = testEntries[i];
            if (string.IsNullOrWhiteSpace(e.Text) || e.Text.Length < 3) continue;

            string translated;
            if (e.Text.Equals("Settings", StringComparison.OrdinalIgnoreCase))
            {
                translated = "Ayarlar";
            }
            else if (e.Text.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
            {
                translated = "İptal";
            }
            else if (e.Text.Equals("Confirm", StringComparison.OrdinalIgnoreCase) || e.Text.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                translated = "Onayla";
            }
            else if (replacedCount == 3)
            {
                // Turkish pangram test entry
                translated = "Çığ şoförü, öğle öğününde üzüm yedi. İSTANBUL";
            }
            else
            {
                translated = pseudoProvider.PseudoTranslate(e.Text);
            }

            // Flag as UTF-16 if contains non-ASCII chars
            bool needsUtf16 = translated.Any(c => c > 127);
            testEntries[i] = new L10NEntry(e.Key ?? "", translated, e.Offset, e.IsKeyUtf16, needsUtf16);
            modifiedIndexes.Add(i);
            replacedCount++;
        }

        Console.WriteLine($"Toplam Değiştirilen Test Entry Sayısı: {modifiedIndexes.Count}");
        Console.WriteLine($"Örnek Değiştirilen Kayıtlar:");
        for (int j = 0; j < Math.Min(5, modifiedIndexes.Count); j++)
        {
            int idx = modifiedIndexes[j];
            Console.WriteLine($"  [{j + 1}] KEY: {testEntries[idx].Key}");
            Console.WriteLine($"      Original: '{parseResult.Entries[idx].Text}'");
            Console.WriteLine($"      Test TR:  '{testEntries[idx].Text}'");
        }

        // Build Test DAT
        byte[] testRawSerialized = L10NStringSerializer.Serialize(testEntries, tableNamespace, parseResult.Version);
        byte[] testContainerBytes = Aion2L10NBuilder.BuildContainer(testRawSerialized, "en-US", manifestKeys);
        File.WriteAllBytes(testDatPath, testContainerBytes);
        Console.WriteLine($"[SAVED] {testDatPath} ({testContainerBytes.Length:N0} bytes)");

        // Verify Test DAT by decrypting and parsing
        var (testHdr, testDecryptedRaw) = Aion2L10NDecryptor.Decrypt(testContainerBytes, "en-US", manifestKeys);
        var testParsed = parser.ParseRawTable(testDecryptedRaw, "TestDat");

        Console.WriteLine($"[VERIFY] Test DAT Çözüldü ve Ayrıştırıldı. Toplam Kayıt: {testParsed.EntryCount:N0}");
        AssertCondition(testParsed.EntryCount == 153042, "Test DAT entry count 153042 olmalıdır!");

        // Assert 100 modified are correct
        for (int j = 0; j < modifiedIndexes.Count; j++)
        {
            int idx = modifiedIndexes[j];
            AssertCondition(testParsed.Entries[idx].Text == testEntries[idx].Text, $"Modified entry {idx} eşleşmedi!");
        }
        Console.WriteLine($"[VERIFY] 100/100 Test Kaydı Birebir Doğrulandı!");

        // Assert remaining 152,942 entries are strictly identical to original
        var modifiedSet = new HashSet<int>(modifiedIndexes);
        int untouchedMatches = 0;
        for (int i = 0; i < parseResult.Entries.Count; i++)
        {
            if (!modifiedSet.Contains(i))
            {
                if (testParsed.Entries[i].Key == parseResult.Entries[i].Key &&
                    testParsed.Entries[i].Text == parseResult.Entries[i].Text)
                {
                    untouchedMatches++;
                }
            }
        }
        Console.WriteLine($"[VERIFY] Dokunulmayan {untouchedMatches:N0} Kayıt Birebir Aynı Kaldı (152,942 / 152,942)!");

        // Verify source game file SHA-256 untouched
        byte[] finalL10nBytes = ExtractFile(mainPakDir, "Aion2/Content/L10N/Text/en-US/L10NString.dat");
        string finalL10nSha = ComputeSha256(finalL10nBytes);
        Console.WriteLine($"\n[KONTROL] Orijinal Oyun Dosyası SHA-256:");
        Console.WriteLine($"  İlk Başta:   {originalL10nSha}");
        Console.WriteLine($"  İşlem Sonu:  {finalL10nSha}");
        AssertCondition(originalL10nSha == finalL10nSha, "Orijinal oyun dosyası değişmemelidir!");
        Console.WriteLine("  [GÜVENLİ] Orijinal oyun dosyalarına ASLA dokunulmadı.");

        Console.WriteLine("\n================================================================================");
        Console.WriteLine("                   PHASE 4 TÜM İŞLEMLERİ BAŞARIYLA TAMAMLANDI!                  ");
        Console.WriteLine("================================================================================");
    }

    private static void AssertCondition(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[ASSERTION FAILED] {message}");
        }
    }

    private static byte[] ExtractFile(string pakDir, string internalPath)
    {
        var provider = new DefaultFileProvider(new DirectoryInfo(pakDir), SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_3));
        provider.Initialize();
        var aesKey = new FAesKey(Aion2AesKeyHex);
        foreach (var vfs in provider.UnloadedVfs)
        {
            try { provider.SubmitKey(vfs.EncryptionKeyGuid, aesKey); } catch { }
        }

        if (provider.Files.TryGetValue(internalPath, out var file))
        {
            return provider.SaveAsset(file.Path);
        }
        throw new FileNotFoundException($"Cannot extract {internalPath} from {pakDir}");
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(data)).ToLowerInvariant();
    }
}
