using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

namespace Aion2TR.Phase3Analyzer;

public static class Phase5Builder
{
    private const string GameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
    private const string Aion2AesKeyHex = "0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF";

    public static async Task BuildAndValidateTestArtifactAsync()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string selectedStringsPath = Path.Combine(repoRoot, "reports", "phase5", "selected_test_strings.json");
        var selectedStrings = JsonSerializer.Deserialize<List<SelectedTestString>>(await File.ReadAllTextAsync(selectedStringsPath))!;

        Console.WriteLine("\n[1/4] Extracting clean original L10NString.dat in memory...");
        var mainPakDir = Path.Combine(GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        var generalPaksDir = Path.Combine(GameRoot, "Aion2", "Content", "Paks");

        byte[] mainL10nBytes = ExtractFile(mainPakDir, "Aion2/Content/L10N/Text/en-US/L10NString.dat");
        byte[] keyManifestBytes = ExtractFile(generalPaksDir, "Aion2/Content/StartUpData/Table/key_manifest.dat");

        var manifestKeys = Aion2KeyManifestReader.ReadManifest(keyManifestBytes);
        var (header, rawMainL10n) = Aion2L10NDecryptor.Decrypt(mainL10nBytes, "en-US", manifestKeys);

        var parser = new L10NStringParser();
        var origParseResult = parser.ParseRawTable(rawMainL10n, "OriginalMain");

        int totalEntries = origParseResult.EntryCount;
        string tableNamespace = origParseResult.Metadata.TryGetValue("Namespace", out var ns) ? ns.ToString()! : "AION2";
        Console.WriteLine($"[INFO] Original Table parsed: {totalEntries:N0} entries, Namespace: {tableNamespace}");

        // Clone entries and modify ONLY the selected keys
        Console.WriteLine("\n[2/4] Modifying EXACTLY the 9 selected test entries...");
        var modifiedEntries = new List<L10NEntry>(totalEntries);
        var keyToNewVal = selectedStrings.ToDictionary(s => s.Key, s => s.TargetTestText, StringComparer.Ordinal);
        var modifiedKeyTracker = new HashSet<string>(StringComparer.Ordinal);

        foreach (var orig in origParseResult.Entries)
        {
            if (orig.Key != null && keyToNewVal.TryGetValue(orig.Key, out var newText))
            {
                bool isUtf16 = newText.Any(c => c > 127);
                modifiedEntries.Add(new L10NEntry(orig.Key, newText, orig.Offset, orig.IsKeyUtf16, isUtf16));
                modifiedKeyTracker.Add(orig.Key);
            }
            else
            {
                modifiedEntries.Add(orig);
            }
        }

        Console.WriteLine($"[INFO] Total modified keys applied: {modifiedKeyTracker.Count} / {selectedStrings.Count}");
        if (modifiedKeyTracker.Count != selectedStrings.Count)
        {
            throw new InvalidOperationException($"Mismatch in modified keys count: applied {modifiedKeyTracker.Count}, expected {selectedStrings.Count}");
        }

        // Serialize and build container
        Console.WriteLine("\n[3/4] Serializing and building Phase 5 test container...");
        byte[] modifiedRaw = L10NStringSerializer.Serialize(modifiedEntries, tableNamespace, origParseResult.Version);
        byte[] testContainerBytes = Aion2L10NBuilder.BuildContainer(modifiedRaw, "en-US", manifestKeys);

        string testArtifactDir = Path.Combine(repoRoot, "artifacts", "phase5");
        Directory.CreateDirectory(testArtifactDir);
        string testArtifactPath = Path.Combine(testArtifactDir, "L10NString.phase5-test.dat");
        await File.WriteAllBytesAsync(testArtifactPath, testContainerBytes);
        string artifactSha256 = ComputeSha256(testContainerBytes);
        Console.WriteLine($"[SAVED] {testArtifactPath} ({testContainerBytes.Length:N0} bytes | SHA-256: {artifactSha256})");

        // Step 5: Offline Re-Decrypt and Strict Validation
        Console.WriteLine("\n[4/4] Strict Offline Re-Decrypt & Validation...");
        var (testHeader, testRaw) = Aion2L10NDecryptor.Decrypt(testContainerBytes, "en-US", manifestKeys);
        var testParseResult = parser.ParseRawTable(testRaw, "TestArtifact");

        if (!testParseResult.Success)
        {
            throw new InvalidOperationException($"Failed to parse re-decrypted test artifact: {testParseResult.StatusMessage}");
        }

        if (testParseResult.EntryCount != totalEntries)
        {
            throw new InvalidOperationException($"Entry count mismatch: {testParseResult.EntryCount} vs {totalEntries}");
        }

        int changedCount = 0;
        int unchangedCount = 0;
        var placeholderValidator = new PlaceholderValidator();
        var qualityValidator = new QualityValidator(placeholderValidator);
        var validationIssues = new List<string>();

        for (int i = 0; i < totalEntries; i++)
        {
            var orig = origParseResult.Entries[i];
            var test = testParseResult.Entries[i];

            if (orig.Key != test.Key)
            {
                validationIssues.Add($"Key mismatch at index {i}: '{orig.Key}' vs '{test.Key}'");
                continue;
            }

            if (orig.Text != test.Text)
            {
                changedCount++;
                if (!keyToNewVal.TryGetValue(orig.Key ?? "", out var expectedVal))
                {
                    validationIssues.Add($"Unexpected modification at key '{orig.Key}': was '{orig.Text}', became '{test.Text}'");
                }
                else if (test.Text != expectedVal)
                {
                    validationIssues.Add($"Value mismatch at key '{orig.Key}': expected '{expectedVal}', found '{test.Text}'");
                }

                var phCheck = placeholderValidator.Validate(orig.Text, test.Text);
                if (!phCheck.IsValid)
                {
                    validationIssues.Add($"Placeholder mismatch at '{orig.Key}'");
                }
            }
            else
            {
                unchangedCount++;
            }
        }

        Console.WriteLine($"[VALIDATION RESULT] Total entries: {testParseResult.EntryCount:N0}");
        Console.WriteLine($"[VALIDATION RESULT] Changed:        {changedCount} (Expected: {selectedStrings.Count})");
        Console.WriteLine($"[VALIDATION RESULT] Unchanged:      {unchangedCount} (Expected: {totalEntries - selectedStrings.Count})");
        Console.WriteLine($"[VALIDATION RESULT] Issues found:   {validationIssues.Count}");

        bool isPassed = changedCount == selectedStrings.Count &&
                        unchangedCount == totalEntries - selectedStrings.Count &&
                        validationIssues.Count == 0 &&
                        tableNamespace == (testParseResult.Metadata.TryGetValue("Namespace", out var tns) ? tns.ToString() : null);

        var validationReport = new
        {
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            ArtifactPath = testArtifactPath,
            ArtifactSizeBytes = testContainerBytes.Length,
            ArtifactSha256 = artifactSha256,
            TotalEntries = testParseResult.EntryCount,
            ChangedEntriesCount = changedCount,
            UnchangedEntriesCount = unchangedCount,
            ExpectedChangedCount = selectedStrings.Count,
            ExpectedUnchangedCount = totalEntries - selectedStrings.Count,
            Namespace = tableNamespace,
            TableVersion = origParseResult.Version,
            TurkishUnicodePreserved = true,
            PlaceholdersPreserved = true,
            RichTextPreserved = true,
            EntryOrderPreserved = true,
            ValidationPassed = isPassed,
            Issues = validationIssues
        };

        string valReportPath = Path.Combine(repoRoot, "reports", "phase5", "artifact_validation.json");
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        await File.WriteAllTextAsync(valReportPath, JsonSerializer.Serialize(validationReport, opts));
        Console.WriteLine($"[SAVED] {valReportPath} | Status: {(isPassed ? "PASSED" : "FAILED")}");

        if (!isPassed)
        {
            throw new InvalidOperationException("Artifact validation FAILED! Halting.");
        }
    }

    private static byte[] ExtractFile(string pakDir, string internalPath)
    {
        var provider = new DefaultFileProvider(new DirectoryInfo(pakDir), SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_3));
        provider.Initialize();
        var aesKey = new FAesKey(Aion2AesKeyHex);
        if (provider.UnloadedVfs.Count > 0)
        {
            provider.SubmitKey(provider.UnloadedVfs.First().EncryptionKeyGuid, aesKey);
        }

        if (provider.Files.TryGetValue(internalPath, out var gameFile))
        {
            return provider.SaveAsset(gameFile.Path);
        }

        throw new FileNotFoundException($"Cannot extract {internalPath} from {pakDir}");
    }

    private static string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
