using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

namespace Aion2TR.Phase3Analyzer;

public static class LegacyEncodingAnalyzer
{
    public static async Task AnalyzeAsync(string repoRoot)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("          PHASE 7.2.1: DEEP STRING ENCODING & SERIALIZATION ANALYSIS           ");
        Console.WriteLine("================================================================================");

        string legacyDir = Path.Combine(repoRoot, "artifacts", "phase7_2", "legacy_extracted", "payload", "_internal");
        string modifiedFilePath = Path.Combine(legacyDir, "MODIFIED_FILE.dat");
        string baselineFilePath = Path.Combine(legacyDir, "BASELINE.dat");
        string keyManifestPath = Path.Combine(legacyDir, "key_manifest.dat");
        string ourTestDatPath = Path.Combine(repoRoot, "artifacts", "phase7_2", "test", "L10NString.phase7_2-test.dat");

        Console.WriteLine($"[INFO] Checking files:");
        Console.WriteLine($"  MODIFIED_FILE.dat: {File.Exists(modifiedFilePath)} ({new FileInfo(modifiedFilePath).Length:N0} bytes)");
        Console.WriteLine($"  BASELINE.dat:      {File.Exists(baselineFilePath)} ({new FileInfo(baselineFilePath).Length:N0} bytes)");
        Console.WriteLine($"  key_manifest.dat:  {File.Exists(keyManifestPath)} ({new FileInfo(keyManifestPath).Length:N0} bytes)");
        Console.WriteLine($"  our test DAT:      {File.Exists(ourTestDatPath)} ({new FileInfo(ourTestDatPath).Length:N0} bytes)");

        // 1. Check if MODIFIED_FILE.dat is raw table or encrypted container
        byte[] modBytes = await File.ReadAllBytesAsync(modifiedFilePath);
        int version = BitConverter.ToInt32(modBytes, 0);
        Console.WriteLine($"\n[1] MODIFIED_FILE.dat starts with int32: {version} (0x{version:X8})");

        byte[] rawModTable;
        if (version == 1)
        {
            Console.WriteLine("  -> MODIFIED_FILE.dat IS DIRECT UNENCRYPTED RAW UNREAL TABLE (TableVersion=1)!");
            rawModTable = modBytes;
        }
        else if (version == 2)
        {
            Console.WriteLine("  -> MODIFIED_FILE.dat is encrypted container (Version=2). Decrypting...");
            var manifestKeys = Aion2KeyManifestReader.ReadManifest(await File.ReadAllBytesAsync(keyManifestPath));
            var (h, raw) = Aion2L10NDecryptor.Decrypt(modBytes, "en-US", manifestKeys);
            rawModTable = raw;
        }
        else
        {
            Console.WriteLine($"  -> Unknown version {version}, first 32 bytes: {Convert.ToHexString(modBytes.AsSpan(0, 32))}");
            return;
        }

        // 2. Parse rawModTable using L10NStringParser
        Console.WriteLine($"\n[2] Parsing Legacy MODIFIED_FILE.dat table ({rawModTable.Length:N0} bytes)...");
        var parser = new L10NStringParser();
        var modParse = parser.ParseRawTable(rawModTable, "LegacyModified");
        Console.WriteLine($"  Total entries parsed: {modParse.EntryCount:N0}");
        Console.WriteLine($"  Namespace: {modParse.Metadata.GetValueOrDefault("Namespace")}");

        // Analyze FString encodings in Legacy MODIFIED_FILE.dat
        int utf16Count = 0;
        int utf8Count = 0;
        int emptyCount = 0;
        foreach (var entry in modParse.Entries)
        {
            if (string.IsNullOrEmpty(entry.Text)) emptyCount++;
            else if (entry.IsTextUtf16) utf16Count++;
            else utf8Count++;
        }
        Console.WriteLine($"\n  Legacy Text Encodings Summary:");
        Console.WriteLine($"    UTF-16 entries: {utf16Count:N0}");
        Console.WriteLine($"    ANSI/UTF-8 entries: {utf8Count:N0}");
        Console.WriteLine($"    Empty entries: {emptyCount:N0}");

        // 3. Search for specific Turkish test strings in Legacy MODIFIED_FILE.dat
        Console.WriteLine($"\n[3] Searching for sample Turkish strings in Legacy MODIFIED_FILE.dat:");
        string[] searchWords = { "Ayarlar", "İptal", "Çıkış", "Görünüm", "Onayla", "Geri", "Kapat", "Tamam", "Evet", "Hayır" };

        foreach (var word in searchWords)
        {
            var exactMatches = modParse.Entries
                .Where(e => e.Text.Trim().Equals(word, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();

            var matches = exactMatches.Count > 0
                ? exactMatches
                : modParse.Entries.Where(e => e.Text.Contains(word, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();

            Console.WriteLine($"\n  Target: '{word}' -> Found {matches.Count} matches in legacy (Exact={exactMatches.Count > 0}):");
            foreach (var m in matches)
            {
                Console.WriteLine($"    Key: [{m.Key}]");
                Console.WriteLine($"    Text: '{m.Text}'");
                Console.WriteLine($"    IsTextUtf16 (parser flag): {m.IsTextUtf16}");
                Console.WriteLine($"    Original Text Offset: 0x{m.Offset:X8} ({m.Offset})");
                DumpExactEntryBytes(rawModTable, m, "LEGACY");
            }
        }

        // 4. Decrypt our test DAT and inspect how we serialized the same strings!
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("    [4] INSPECTING OUR PHASE 7.2 TEST DAT SERIALIZATION                         ");
        Console.WriteLine("================================================================================");
        var kmBytes = await File.ReadAllBytesAsync(keyManifestPath);
        var mKeys = Aion2KeyManifestReader.ReadManifest(kmBytes);
        var ourTestDatBytes = await File.ReadAllBytesAsync(ourTestDatPath);
        var (ourH, ourRaw) = Aion2L10NDecryptor.Decrypt(ourTestDatBytes, "en-US", mKeys);
        var ourParse = parser.ParseRawTable(ourRaw, "OurTestDat");

        Console.WriteLine($"  Our test DAT parsed: {ourParse.EntryCount:N0} entries");
        foreach (var word in searchWords)
        {
            var matches = ourParse.Entries
                .Where(e => e.Text.Contains(word, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();

            if (matches.Count > 0)
            {
                Console.WriteLine($"\n  Target in OUR DAT: '{word}' -> Found {matches.Count} matches:");
                foreach (var m in matches)
                {
                    Console.WriteLine($"    Key: [{m.Key}]");
                    Console.WriteLine($"    Text: '{m.Text}'");
                    Console.WriteLine($"    IsTextUtf16 (parser flag): {m.IsTextUtf16}");
                    DumpRawStringBytes(ourRaw, m.Key, m.Text, "OUR_PHASE7_2");
                }
            }
        }

        // 5. Compare EXACT KEYS between Legacy Modified and Our Test DAT
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("    [5] EXACT KEY COMPARISON: LEGACY VS OUR PHASE 7.2 DAT                       ");
        Console.WriteLine("================================================================================");

        string[] targetKeys = {
            "InputKeyMapping_UI_Setting_desc",
            "String_UI_FORCE_BUTTON_SETTING_body",
            "String_STR_PATCH_CONFIRM_body",
            "String_MsgBox_Ok_body",
            "InputKeyMapping_System_Cancel_desc",
            "String_STR_PATCH_CANCEL_body",
            "String_UI_UPDATE_DOWNLOAD_CLOSE_body",
            "String_UI_LOGIN_BUTTON_CLOSE_body",
            "String_MsgBox_Yes_body",
            "String_MsgBox_No_body"
        };

        var legacyDict = modParse.Entries.Where(e => !string.IsNullOrEmpty(e.Key)).ToDictionary(e => e.Key!, e => e);
        var ourDict = ourParse.Entries.Where(e => !string.IsNullOrEmpty(e.Key)).ToDictionary(e => e.Key!, e => e);

        foreach (var key in targetKeys)
        {
            Console.WriteLine($"\n>>> KEY: [{key}]");
            if (legacyDict.TryGetValue(key, out var legEntry))
            {
                Console.WriteLine($"    [LEGACY] Text: '{legEntry.Text}'");
                Console.WriteLine($"    [LEGACY] IsKeyUtf16: {legEntry.IsKeyUtf16}, IsTextUtf16: {legEntry.IsTextUtf16}");
                DumpExactEntryBytes(rawModTable, legEntry, "LEGACY");
            }
            if (ourDict.TryGetValue(key, out var ourEntry))
            {
                Console.WriteLine($"    [OUR DAT] Text: '{ourEntry.Text}'");
                Console.WriteLine($"    [OUR DAT] IsKeyUtf16: {ourEntry.IsKeyUtf16}, IsTextUtf16: {ourEntry.IsTextUtf16}");
                DumpExactEntryBytes(ourRaw, ourEntry, "OUR DAT");
            }
        }

        // 6. Analyze the EXACT UTF-16 vs ANSI decision rule in Legacy MODIFIED_FILE.dat
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("    [6] LEGACY MODIFIED_FILE.DAT ENCODING RULE ANALYSIS                        ");
        Console.WriteLine("================================================================================");

        int legacyUtf16WithNonAscii = 0;
        int legacyUtf16PureAscii = 0;
        int legacyAnsiWithNonAscii = 0;
        int legacyAnsiPureAscii = 0;

        foreach (var entry in modParse.Entries)
        {
            if (string.IsNullOrEmpty(entry.Text)) continue;
            bool hasNonAscii = entry.Text.Any(c => c > 127);
            if (entry.IsTextUtf16)
            {
                if (hasNonAscii) legacyUtf16WithNonAscii++;
                else legacyUtf16PureAscii++;
            }
            else
            {
                if (hasNonAscii) legacyAnsiWithNonAscii++;
                else legacyAnsiPureAscii++;
            }
        }

        Console.WriteLine($"  Legacy Entries with Non-ASCII (c > 127):");
        Console.WriteLine($"    Encoded as UTF-16: {legacyUtf16WithNonAscii:N0}");
        Console.WriteLine($"    Encoded as ANSI:   {legacyAnsiWithNonAscii:N0}");
        Console.WriteLine($"  Legacy Entries with Pure ASCII (c <= 127):");
        Console.WriteLine($"    Encoded as UTF-16: {legacyUtf16PureAscii:N0}");
        Console.WriteLine($"    Encoded as ANSI:   {legacyAnsiPureAscii:N0}");

        // Check our broken Phase 7.2 DAT
        int ourUtf16WithNonAscii = 0;
        int ourUtf16PureAscii = 0;
        int ourAnsiWithNonAscii = 0;
        int ourAnsiPureAscii = 0;

        foreach (var entry in ourParse.Entries)
        {
            if (string.IsNullOrEmpty(entry.Text)) continue;
            bool hasNonAscii = entry.Text.Any(c => c > 127);
            if (entry.IsTextUtf16)
            {
                if (hasNonAscii) ourUtf16WithNonAscii++;
                else ourUtf16PureAscii++;
            }
            else
            {
                if (hasNonAscii) ourAnsiWithNonAscii++;
                else ourAnsiPureAscii++;
            }
        }

        Console.WriteLine($"\n  OUR Phase 7.2 DAT Entries with Non-ASCII (c > 127):");
        Console.WriteLine($"    Encoded as UTF-16: {ourUtf16WithNonAscii:N0}");
        Console.WriteLine($"    Encoded as ANSI:   {ourAnsiWithNonAscii:N0}  <-- BUG IF > 0!");
        Console.WriteLine($"  OUR Phase 7.2 DAT Entries with Pure ASCII (c <= 127):");
        Console.WriteLine($"    Encoded as UTF-16: {ourUtf16PureAscii:N0}");
        Console.WriteLine($"    Encoded as ANSI:   {ourAnsiPureAscii:N0}");

        // Print the entries where our test DAT encoded non-ASCII as ANSI:
        Console.WriteLine("\n[7] Entries in OUR test DAT with Non-ASCII encoded as ANSI (the c > 255 bug!):");
        foreach (var entry in ourParse.Entries)
        {
            if (string.IsNullOrEmpty(entry.Text)) continue;
            if (entry.Text.Any(c => c > 127) && !entry.IsTextUtf16)
            {
                Console.WriteLine($"  Key: [{entry.Key}] Text: '{entry.Text}'");
                DumpExactEntryBytes(ourRaw, entry, "OUR BUGGED ENTRY");
            }
        }

        // Also check differences between CLEAN ORIGINAL and OUR test DAT:
        Console.WriteLine("\n[8] All differences between CLEAN ORIGINAL and OUR Phase 7.2 test DAT:");
        string cleanOriginalPakDir = Path.Combine(repoRoot, "artifacts", "phase7_2", "clean_original");
        // We can decrypt mainL10nBytes from GameRoot or use clean extraction:
        var (cleanH, cleanRaw) = Aion2L10NDecryptor.Decrypt(
            ExtractFile(Path.Combine(@"d:\uygulamalar\steam\steamapps\common\AION2", "Aion2", "Content", "Paks", "L10N", "Text", "en-US"), "Aion2/Content/L10N/Text/en-US/L10NString.dat"),
            "en-US",
            mKeys);
        var cleanParse = parser.ParseRawTable(cleanRaw, "CleanOriginal");

        int diffCount = 0;
        for (int i = 0; i < cleanParse.Entries.Count; i++)
        {
            var b = cleanParse.Entries[i];
            var o = ourParse.Entries[i];
            if (b.Text != o.Text)
            {
                diffCount++;
                if (diffCount <= 40)
                {
                    Console.WriteLine($"  #{diffCount} [{b.Key}]: Orig='{b.Text}' (utf16={b.IsTextUtf16}) -> Test='{o.Text}' (utf16={o.IsTextUtf16})");
                }
            }
        }
        Console.WriteLine($"  Total modified entries found in test DAT: {diffCount}");
    }

    private static byte[] ExtractFile(string pakDir, string virtualPath)
    {
        var provider = new DefaultFileProvider(new DirectoryInfo(pakDir), SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_3));
        provider.Initialize();
        var aesKey = new FAesKey("0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF");
        foreach (var vfs in provider.UnloadedVfs)
        {
            try { provider.SubmitKey(vfs.EncryptionKeyGuid, aesKey); } catch { }
        }

        if (provider.Files.TryGetValue(virtualPath, out var fileEntry))
        {
            return provider.SaveAsset(fileEntry.Path);
        }
        throw new FileNotFoundException($"Virtual file not found: {virtualPath}");
    }

    private static void DumpExactEntryBytes(byte[] rawTable, L10NEntry entry, string label)
    {
        // Search for key followed by text in rawTable
        byte[] keyBytesUtf8 = Encoding.UTF8.GetBytes(entry.Key);
        byte[] keyBytesUtf16 = Encoding.Unicode.GetBytes(entry.Key);

        int keyIdx = FindSubarray(rawTable, keyBytesUtf8);
        if (keyIdx < 0) keyIdx = FindSubarray(rawTable, keyBytesUtf16);

        if (keyIdx >= 0)
        {
            int keyLenOffset = keyIdx - 4;
            int keyLen = BitConverter.ToInt32(rawTable, keyLenOffset);
            int afterKey = (keyLen < 0) ? (keyIdx + (-keyLen * 2)) : (keyIdx + keyLen);

            int textLen = BitConverter.ToInt32(rawTable, afterKey);
            int textBytesCount = (textLen < 0) ? (-textLen * 2) : textLen;
            int totalEntryLen = (afterKey + 4 + textBytesCount) - keyLenOffset;

            Console.WriteLine($"      [{label}] Bytes (KeyLen={keyLen}, TextLen={textLen}):");
            Console.WriteLine($"        TextLen Header: {textLen} (0x{textLen:X8})");
            byte[] textBytes = rawTable.AsSpan(afterKey + 4, textBytesCount).ToArray();
            Console.WriteLine($"        Text Bytes ({textBytes.Length} bytes): {Convert.ToHexString(textBytes)}");
            Console.WriteLine($"        Full Entry Hex: {Convert.ToHexString(rawTable.AsSpan(keyLenOffset, totalEntryLen))}");
        }
    }

    private static void DumpRawStringBytes(byte[] rawTable, string key, string text, string label)
    {
        // Search for the UTF-16 and UTF-8 byte representations of the text in rawTable
        byte[] utf16Bytes = Encoding.Unicode.GetBytes(text);
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(text);

        int idx16 = FindSubarray(rawTable, utf16Bytes);
        int idx8 = FindSubarray(rawTable, utf8Bytes);

        Console.WriteLine($"      [{label}] Byte Dump:");
        if (idx16 >= 0)
        {
            int lenOffset = idx16 - 4;
            int lengthHeader = BitConverter.ToInt32(rawTable, lenOffset);
            int dumpStart = Math.Max(0, lenOffset);
            int dumpLen = Math.Min(rawTable.Length - dumpStart, 4 + utf16Bytes.Length + 4);
            Console.WriteLine($"        Found as UTF-16 at offset 0x{idx16:X8}:");
            Console.WriteLine($"          Length int32: {lengthHeader} (0x{lengthHeader:X8})");
            Console.WriteLine($"          Hex: {Convert.ToHexString(rawTable.AsSpan(dumpStart, dumpLen))}");
        }
        else if (idx8 >= 0)
        {
            int lenOffset = idx8 - 4;
            int lengthHeader = BitConverter.ToInt32(rawTable, lenOffset);
            int dumpStart = Math.Max(0, lenOffset);
            int dumpLen = Math.Min(rawTable.Length - dumpStart, 4 + utf8Bytes.Length + 2);
            Console.WriteLine($"        Found as UTF-8/ANSI at offset 0x{idx8:X8}:");
            Console.WriteLine($"          Length int32: {lengthHeader} (0x{lengthHeader:X8})");
            Console.WriteLine($"          Hex: {Convert.ToHexString(rawTable.AsSpan(dumpStart, dumpLen))}");
        }
        else
        {
            Console.WriteLine($"        Could not locate exact byte pattern for '{text}'");
        }
    }

    private static int FindSubarray(byte[] array, byte[] pattern)
    {
        if (pattern.Length == 0 || array.Length < pattern.Length) return -1;
        for (int i = 0; i <= array.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (array[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
