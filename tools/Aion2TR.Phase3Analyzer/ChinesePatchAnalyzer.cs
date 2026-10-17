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

namespace Aion2TR.Phase3Analyzer;

public static class ChinesePatchAnalyzer
{
    public static async Task AnalyzeAsync(string repoRoot)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("       PHASE 7.2.2: CHINESE PATCH + LEGACY PATCH + AION2TR DEEP ANALYSIS       ");
        Console.WriteLine("================================================================================");

        string chineseDatPath = Path.Combine(repoRoot, "reference", "chinese_patch", "Aion2_正式服一键汉化_v1.1.1", "zh", "L10NString.dat");
        string chineseStubPath = Path.Combine(repoRoot, "reference", "chinese_patch", "Aion2_正式服一键汉化_v1.1.1", "zh", "pakchunk502000-Windows_0_P.pak");
        string legacyStubPath = Path.Combine(repoRoot, "artifacts", "phase7_2", "legacy_extracted", "payload", "_internal", "english_empty_stub.pak");
        string keyManifestPath = Path.Combine(repoRoot, "artifacts", "phase7_2", "legacy_extracted", "payload", "_internal", "key_manifest.dat");
        if (!File.Exists(keyManifestPath))
        {
            keyManifestPath = Path.Combine(repoRoot, "data", "key_manifest.dat");
        }

        // 1. Analyze Stubs
        Console.WriteLine("\n[1] STUB PAK COMPARISON:");
        if (File.Exists(chineseStubPath))
        {
            var bytes = File.ReadAllBytes(chineseStubPath);
            Console.WriteLine($"  Chinese Stub: Size={bytes.Length} bytes | SHA256={ComputeSha256(bytes)} | Hex={BitConverter.ToString(bytes)} | ASCII={Encoding.ASCII.GetString(bytes)}");
        }
        if (File.Exists(legacyStubPath))
        {
            var bytes = File.ReadAllBytes(legacyStubPath);
            Console.WriteLine($"  Legacy Stub:  Size={bytes.Length} bytes | SHA256={ComputeSha256(bytes)} | Hex(first 32)={BitConverter.ToString(bytes.Take(32).ToArray())}");
        }

        // 2. Decrypt & Parse Chinese L10NString.dat
        Console.WriteLine("\n[2] CHINESE L10NString.dat INSPECTION:");
        if (File.Exists(chineseDatPath) && File.Exists(keyManifestPath))
        {
            var manifestBytes = File.ReadAllBytes(keyManifestPath);
            var manifestKeys = Aion2KeyManifestReader.ReadManifest(manifestBytes);
            var datBytes = File.ReadAllBytes(chineseDatPath);
            Console.WriteLine($"  Chinese DAT Size: {datBytes.Length:N0} bytes | SHA256={ComputeSha256(datBytes)}");

            var (header, rawBytes) = Aion2L10NDecryptor.Decrypt(datBytes, "en-US", manifestKeys);
            Console.WriteLine($"  Container Header: Version={header.Version}, PackedSize={header.PackedSize:N0}, AlignedSize={header.AlignedSize:N0}, RawSize={header.RawSize:N0}");
            Console.WriteLine($"  Decompressed RAW Size: {rawBytes.Length:N0} bytes | SHA256={ComputeSha256(rawBytes)}");

            var parser = new L10NStringParser();
            var parseResult = parser.ParseRawTable(rawBytes, "ChineseMainTable");
            Console.WriteLine($"  Parse Success: {parseResult.Success} | Total Entries: {parseResult.EntryCount:N0}");
            if (parseResult.Metadata.TryGetValue("Namespace", out var ns))
            {
                Console.WriteLine($"  Table Namespace: {ns}");
            }

            // Inspect Chinese strings vs pure ASCII strings in Chinese DAT
            int chineseUtf16Count = 0;
            int chineseAsciiCount = 0;
            var sampleChineseEntries = new List<L10NEntry>();
            var sampleAsciiEntries = new List<L10NEntry>();

            foreach (var entry in parseResult.Entries)
            {
                bool hasNonAscii = entry.Text.Any(c => c > 127);
                if (hasNonAscii)
                {
                    chineseUtf16Count++;
                    if (sampleChineseEntries.Count < 5) sampleChineseEntries.Add(entry);
                }
                else
                {
                    chineseAsciiCount++;
                    if (sampleAsciiEntries.Count < 5 && entry.Text.Length > 2) sampleAsciiEntries.Add(entry);
                }
            }

            Console.WriteLine($"  Entries with Non-ASCII (Chinese): {chineseUtf16Count:N0}");
            Console.WriteLine($"  Entries with Pure ASCII:          {chineseAsciiCount:N0}");

            Console.WriteLine("\n  Sample Chinese Entries in Chinese DAT:");
            foreach (var s in sampleChineseEntries)
            {
                Console.WriteLine($"    [{s.Key}] => '{s.Text}' (Length: {s.Text.Length}, IsTextUtf16: {s.IsTextUtf16})");
            }

            Console.WriteLine("\n  Sample Pure ASCII Entries in Chinese DAT:");
            foreach (var s in sampleAsciiEntries)
            {
                Console.WriteLine($"    [{s.Key}] => '{s.Text}' (Length: {s.Text.Length}, IsTextUtf16: {s.IsTextUtf16})");
            }
        }
        else
        {
            Console.WriteLine($"  [ERROR] Files not found: dat={chineseDatPath}, manifest={keyManifestPath}");
        }

        // 3. Inspect Python bytecode in patch_manager.pyc for game_paths
        Console.WriteLine("\n[3] PYTHON BYTECODE DISASSEMBLY (patch_manager.pyc -> game_paths):");
        string pycPath = Path.Combine(repoRoot, "artifacts", "phase7_2", "legacy_extracted", "pyz_extracted", "patch_manager.pyc");
        if (File.Exists(pycPath))
        {
            DisassemblePycGamePaths(pycPath);
        }

        await Task.CompletedTask;
    }

    private static void DisassemblePycGamePaths(string pycPath)
    {
        byte[] pyc = File.ReadAllBytes(pycPath);
        // Find 'game_paths' string in pyc
        int idx = IndexOfSequence(pyc, Encoding.ASCII.GetBytes("game_paths"));
        Console.WriteLine($"  'game_paths' found in pyc at offset: {idx}");

        // Let's find the code object corresponding to game_paths
        // In marshal format: code object starts with 'c' (0x63)
        // Let's search backwards from 'game_paths' for 'c'
        int codeStart = -1;
        for (int i = idx - 1; i >= 0; i--)
        {
            if (pyc[i] == (byte)'c' && pyc[i + 1] == 0x01) // argcount 1
            {
                codeStart = i;
                break;
            }
        }
        Console.WriteLine($"  Code object for game_paths starts at: {codeStart}");

        // Search for string table tuple right before or after
        // Let's print all ASCII strings between codeStart and idx
        var sb = new StringBuilder();
        for (int i = codeStart; i < idx; i++)
        {
            byte b = pyc[i];
            if (b >= 32 && b <= 126) sb.Append((char)b);
            else sb.Append(' ');
        }
        Console.WriteLine($"  Text in game_paths code object:\n  {sb.ToString()}");
    }

    private static int IndexOfSequence(byte[] buffer, byte[] pattern)
    {
        for (int i = 0; i <= buffer.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private static string ComputeSha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
}
