using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

namespace Aion2TR.Phase3Analyzer;

public static class Phase7_2_2DiagnosticBuilder
{
    private const string GameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
    private const string Aion2AesKeyHex = "0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF";

    public static async Task BuildAndValidateDiagnosticTestDatAsync(string repoRoot)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("    PHASE 7.2.2: BUILD & OFFLINE VALIDATION OF ASCII VS UNICODE DIAGNOSTIC DAT ");
        Console.WriteLine("================================================================================");

        string testArtifactDir = Path.Combine(repoRoot, "artifacts", "phase7_2_2", "test");
        string reportsDir = Path.Combine(repoRoot, "reports", "phase7_2_2");
        Directory.CreateDirectory(testArtifactDir);
        Directory.CreateDirectory(reportsDir);

        string outputDatPath = Path.Combine(testArtifactDir, "L10NString.phase7_2_2-test.dat");

        // 1. In-memory clean extraction of original game files
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

        // 2. Select explicit diagnostic strings (ASCII markers vs Turkish Unicode markers)
        Console.WriteLine("\n[2/4] Mapping ASCII diagnostic markers vs Turkish Unicode markers...");
        var asciiDiagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Settings"] = "TESTASCII",
            ["Confirm"] = "ASCII123",
            ["Close"] = "CLOSEASCII",
            ["Back"] = "BACKASCII",
            ["Select"] = "SELECTASCII",
            ["Reset"] = "RESETASCII"
        };

        var unicodeDiagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cancel"] = "İPTALTEST",
            ["Exit"] = "ÇIKIŞTEST",
            ["Yes"] = "EVETTEST",
            ["No"] = "HAYIRTEST",
            ["Resolution"] = "ÇÖZÜNÜRLÜKTEST",
            ["Controls"] = "GELİŞMİŞKONTROL"
        };

        var selectedTranslations = new Dictionary<string, string>(StringComparer.Ordinal);
        var selectedSources = new Dictionary<string, string>(StringComparer.Ordinal);

        // Map ASCII markers
        foreach (var (word, diagText) in asciiDiagnostics)
        {
            var matches = origParseResult.Entries
                .Where(e => e.Text.Trim().Equals(word, StringComparison.OrdinalIgnoreCase))
                .Take(4);

            foreach (var m in matches)
            {
                if (!string.IsNullOrEmpty(m.Key) && !selectedTranslations.ContainsKey(m.Key))
                {
                    selectedTranslations[m.Key] = diagText;
                    selectedSources[m.Key] = m.Text;
                }
            }
        }

        // Map Unicode markers
        foreach (var (word, diagText) in unicodeDiagnostics)
        {
            var matches = origParseResult.Entries
                .Where(e => e.Text.Trim().Equals(word, StringComparison.OrdinalIgnoreCase))
                .Take(4);

            foreach (var m in matches)
            {
                if (!string.IsNullOrEmpty(m.Key) && !selectedTranslations.ContainsKey(m.Key))
                {
                    selectedTranslations[m.Key] = diagText;
                    selectedSources[m.Key] = m.Text;
                }
            }
        }

        Console.WriteLine($"[SELECTED] Total diagnostic modified records: {selectedTranslations.Count}");

        // 3. Serialize RAW table
        Console.WriteLine("\n[3/4] Serializing modified RAW localization table...");
        var modifiedEntries = new List<L10NEntry>(origParseResult.Entries.Count);

        int asciiCount = 0;
        int unicodeCount = 0;

        foreach (var origEntry in origParseResult.Entries)
        {
            if (origEntry.Key != null && selectedTranslations.TryGetValue(origEntry.Key, out var replacementText))
            {
                bool hasNonAscii = replacementText.Any(c => c > 127);
                if (hasNonAscii) unicodeCount++;
                else asciiCount++;

                modifiedEntries.Add(new L10NEntry(
                    Id: origEntry.Id,
                    Key: origEntry.Key,
                    Text: replacementText,
                    Offset: 0,
                    RawLength: 0,
                    Encoding: hasNonAscii ? "UTF-16" : "UTF-8",
                    IsDecrypted: true,
                    IsTextUtf16: hasNonAscii,
                    IsKeyUtf16: origEntry.IsKeyUtf16
                ));
            }
            else
            {
                modifiedEntries.Add(origEntry);
            }
        }

        Console.WriteLine($"  Modified entries: ASCII={asciiCount}, Unicode={unicodeCount}");
        byte[] rawModifiedBytes = L10NStringSerializer.Serialize(modifiedEntries, tableNamespace, origParseResult.Version);

        // Round-trip parse check
        var roundTripParse = parser.ParseRawTable(rawModifiedBytes, "RoundTripCheck");
        if (!roundTripParse.Success || roundTripParse.EntryCount != totalEntries)
        {
            throw new InvalidOperationException($"Roundtrip check failed! Expected {totalEntries}, got {roundTripParse.EntryCount}");
        }

        // 4. Encrypt and pack into Container V2
        Console.WriteLine("\n[4/4] Packing Container V2...");
        byte[] containerBytes = Aion2L10NBuilder.BuildContainer(rawModifiedBytes, "en-US", manifestKeys);
        await File.WriteAllBytesAsync(outputDatPath, containerBytes);

        string datSha256 = ComputeSha256(containerBytes);
        Console.WriteLine($"[SAVED] Diagnostic DAT generated at: {outputDatPath}");
        Console.WriteLine($"  File Size: {containerBytes.Length:N0} bytes");
        Console.WriteLine($"  SHA-256:   {datSha256}");

        // Write diagnostic manifest
        string manifestPath = Path.Combine(testArtifactDir, "phase7_2_2_diagnostic_manifest.json");
        var manifestObj = new
        {
            CreatedAt = DateTime.UtcNow.ToString("O"),
            TotalEntries = totalEntries,
            ModifiedEntriesCount = selectedTranslations.Count,
            AsciiCount = asciiCount,
            UnicodeCount = unicodeCount,
            Sha256 = datSha256,
            Size = containerBytes.Length,
            Mappings = selectedTranslations.Select(kvp => new
            {
                Key = kvp.Key,
                Original = selectedSources[kvp.Key],
                Diagnostic = kvp.Value,
                IsUnicode = kvp.Value.Any(c => c > 127)
            })
        };
        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifestObj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[SAVED] Diagnostic manifest: {manifestPath}");
    }

    private static byte[] ExtractFile(string pakDir, string virtualPath)
    {
        var provider = new DefaultFileProvider(new DirectoryInfo(pakDir), SearchOption.AllDirectories, true, new VersionContainer(EGame.GAME_UE5_4));
        provider.Initialize();
        provider.SubmitKey(new CUE4Parse.UE4.Objects.Core.Misc.FGuid(), new FAesKey(Aion2AesKeyHex));

        if (!provider.Files.TryGetValue(virtualPath, out var fileEntry))
        {
            throw new FileNotFoundException($"Virtual file not found in PAK: {virtualPath}");
        }
        return fileEntry.Read();
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
}
