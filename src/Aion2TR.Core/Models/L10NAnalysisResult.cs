using System.Collections.Generic;

namespace Aion2TR.Core.Models;

public record L10NAnalysisResult(
    string FilePath,
    long FileSizeBytes,
    string Sha256Hash,
    double Entropy,
    string MagicHeaderHex,
    int? PossibleRecordCount,
    string DetectedEncoding,
    int ExtractedAsciiStringCount,
    int ExtractedUtf16StringCount,
    IReadOnlyList<string> SampleStrings,
    IReadOnlyDictionary<string, string> OffsetDetails,
    string ConfidenceLevel,
    string Notes
);
