using System;
using System.Collections.Generic;

namespace Aion2TR.Core.Models;

public record ScannedFileInfo(
    string RelativePath,
    long SizeBytes,
    string Extension,
    string Sha256Hash,
    DateTime LastModifiedUtc,
    string? Category = null
);

public record GameScanReport(
    string GameRootPath,
    DateTime ScanTimestampUtc,
    int TotalFilesScanned,
    long TotalSizeBytes,
    IReadOnlyList<ScannedFileInfo> Files,
    IReadOnlyDictionary<string, int> ExtensionSummary
);
