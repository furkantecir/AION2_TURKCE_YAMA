using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;
using Aion2TR.Core.Models;

namespace Aion2TR.Infrastructure.Services;

public class L10NStringAnalyzer : IL10NStringAnalyzer
{
    private readonly IHashService _hashService;

    public L10NStringAnalyzer(IHashService hashService)
    {
        _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
    }

    public async Task<L10NAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("L10NString file not found", filePath);

        var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return AnalyzeBytes(data, filePath);
    }

    public L10NAnalysisResult AnalyzeBytes(byte[] data, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(data);

        var hash = _hashService.ComputeSha256(data);
        var entropy = CalculateEntropy(data);

        // Header bytes (first 16 bytes)
        var headerLen = Math.Min(16, data.Length);
        var headerHex = Convert.ToHexString(data[..headerLen]);

        int? possibleRecordCount = null;
        if (data.Length >= 4)
        {
            var firstInt = BitConverter.ToInt32(data, 0);
            if (firstInt is > 0 and < 5000000)
            {
                possibleRecordCount = firstInt;
            }
        }

        // String extraction
        var asciiStrings = ExtractPrintableStrings(data, minLength: 4, isUnicode: false);
        var utf16Strings = ExtractPrintableStrings(data, minLength: 4, isUnicode: true);

        var detectedEncoding = "Unknown";
        if (utf16Strings.Count > asciiStrings.Count && utf16Strings.Count > 50)
            detectedEncoding = "UTF-16LE";
        else if (asciiStrings.Count > 50)
            detectedEncoding = "UTF-8 / ASCII";
        else if (entropy >= 7.8)
            detectedEncoding = "Encrypted / Obfuscated Binary Table (High Entropy)";

        var sampleStrings = asciiStrings.Take(15).ToList();
        if (sampleStrings.Count < 5 && utf16Strings.Count > 0)
        {
            sampleStrings.AddRange(utf16Strings.Take(15 - sampleStrings.Count));
        }

        var offsetDetails = new Dictionary<string, string>
        {
            ["0x000000"] = $"Magic/Prefix: {headerHex}",
            ["FileLength"] = $"{data.Length:N0} bytes"
        };

        var confidenceLevel = entropy > 7.5 ? "LIKELY_ENCRYPTED_OR_COMPRESSED" : "UNCOMPRESSED_BINARY_TABLE";
        var notes = entropy >= 7.95
            ? "File has near-maximum entropy (8.00). It is encrypted with NCSoft's secondary layer (manifest-based key / XOR obfuscation) or heavily compressed."
            : "File contains observable table layout with potential plaintext markers.";

        return new L10NAnalysisResult(
            FilePath: sourceName,
            FileSizeBytes: data.Length,
            Sha256Hash: hash,
            Entropy: entropy,
            MagicHeaderHex: headerHex,
            PossibleRecordCount: possibleRecordCount,
            DetectedEncoding: detectedEncoding,
            ExtractedAsciiStringCount: asciiStrings.Count,
            ExtractedUtf16StringCount: utf16Strings.Count,
            SampleStrings: sampleStrings,
            OffsetDetails: offsetDetails,
            ConfidenceLevel: confidenceLevel,
            Notes: notes
        );
    }

    private static double CalculateEntropy(byte[] data)
    {
        if (data.Length == 0) return 0;
        var counts = new int[256];
        foreach (var b in data) counts[b]++;

        double entropy = 0.0;
        double len = data.Length;
        foreach (var count in counts)
        {
            if (count == 0) continue;
            double p = count / len;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    private static List<string> ExtractPrintableStrings(byte[] data, int minLength, bool isUnicode)
    {
        var result = new List<string>();
        var sb = new StringBuilder();

        if (!isUnicode)
        {
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b >= 32 && b <= 126)
                {
                    sb.Append((char)b);
                }
                else
                {
                    if (sb.Length >= minLength)
                        result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            if (sb.Length >= minLength)
                result.Add(sb.ToString());
        }
        else
        {
            for (int i = 0; i < data.Length - 1; i += 2)
            {
                char c = (char)(data[i] | (data[i + 1] << 8));
                if (c >= 32 && c <= 126)
                {
                    sb.Append(c);
                }
                else
                {
                    if (sb.Length >= minLength)
                        result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            if (sb.Length >= minLength)
                result.Add(sb.ToString());
        }

        return result;
    }
}
