using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;
using Aion2TR.Core.Models;

namespace Aion2TR.Infrastructure.Services;

public class Aion2DiscoveryService : IDiscoveryService
{
    private static readonly HashSet<string> TargetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pak", ".utoc", ".ucas", ".sig", ".dat", ".locres", ".locmeta", ".json", ".csv"
    };

    private readonly IHashService _hashService;

    public Aion2DiscoveryService(IHashService hashService)
    {
        _hashService = hashService ?? throw new ArgumentNullException(nameof(hashService));
    }

    public async Task<GameScanReport> ScanGameLocalizationAsync(
        string gameRootPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRootPath);
        if (!Directory.Exists(gameRootPath))
            throw new DirectoryNotFoundException($"Game directory does not exist: {gameRootPath}");

        var l10nPath = Path.Combine(gameRootPath, "Aion2", "Content", "Paks", "L10N");
        if (!Directory.Exists(l10nPath))
        {
            // If L10N subfolder does not exist, fallback to general Paks
            l10nPath = Path.Combine(gameRootPath, "Aion2", "Content", "Paks");
        }

        progress?.Report($"Scanning localization files in: {l10nPath}");

        var scannedFiles = new List<ScannedFileInfo>();
        var extSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;

        var allFiles = Directory.EnumerateFiles(l10nPath, "*.*", SearchOption.AllDirectories)
            .Where(f => TargetExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        progress?.Report($"Found {allFiles.Count} localization containers to inspect.");

        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fi = new FileInfo(file);
            var relPath = Path.GetRelativePath(gameRootPath, file).Replace('\\', '/');
            var ext = fi.Extension.ToLowerInvariant();

            progress?.Report($"Hashing: {relPath} ({fi.Length:N0} bytes)");
            var hash = await _hashService.ComputeSha256Async(file, cancellationToken);

            string category = "Other";
            if (relPath.Contains("/Text/", StringComparison.OrdinalIgnoreCase)) category = "Text Localization";
            else if (relPath.Contains("/Voice/", StringComparison.OrdinalIgnoreCase)) category = "Voice Localization";

            scannedFiles.Add(new ScannedFileInfo(
                RelativePath: relPath,
                SizeBytes: fi.Length,
                Extension: ext,
                Sha256Hash: hash,
                LastModifiedUtc: fi.LastWriteTimeUtc,
                Category: category
            ));

            totalSize += fi.Length;
            extSummary[ext] = extSummary.GetValueOrDefault(ext, 0) + 1;
        }

        var report = new GameScanReport(
            GameRootPath: gameRootPath,
            ScanTimestampUtc: DateTime.UtcNow,
            TotalFilesScanned: scannedFiles.Count,
            TotalSizeBytes: totalSize,
            Files: scannedFiles.OrderBy(f => f.RelativePath).ToList(),
            ExtensionSummary: extSummary
        );

        progress?.Report($"Scan completed. Total: {scannedFiles.Count} files ({totalSize / 1024.0 / 1024.0:F2} MB)");
        return report;
    }

    public async Task SaveReportAsync(
        GameScanReport report,
        string outputJsonPath,
        string outputTextPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        // 1. JSON Export
        var jsonDir = Path.GetDirectoryName(outputJsonPath);
        if (!string.IsNullOrEmpty(jsonDir)) Directory.CreateDirectory(jsonDir);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var jsonContent = JsonSerializer.Serialize(report, jsonOptions);
        await File.WriteAllTextAsync(outputJsonPath, jsonContent, Encoding.UTF8, cancellationToken);

        // 2. Human-readable Text Export
        var textDir = Path.GetDirectoryName(outputTextPath);
        if (!string.IsNullOrEmpty(textDir)) Directory.CreateDirectory(textDir);

        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                         AION 2 LOCALIZATION SCAN REPORT                        ");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Scan Timestamp (UTC) : {report.ScanTimestampUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Game Root Directory  : {report.GameRootPath}");
        sb.AppendLine($"Total Files Scanned  : {report.TotalFilesScanned}");
        sb.AppendLine($"Total Size           : {report.TotalSizeBytes:N0} bytes ({report.TotalSizeBytes / 1024.0 / 1024.0:F2} MB)");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("EXTENSION SUMMARY:");
        foreach (var (ext, count) in report.ExtensionSummary)
        {
            sb.AppendLine($"  {ext,-8} : {count,4} file(s)");
        }
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("FILE DETAILS:");
        sb.AppendLine(string.Format("{0,-60} | {1,12} | {2,-64}", "Relative Path", "Size (bytes)", "SHA-256"));
        sb.AppendLine(new string('-', 142));

        foreach (var f in report.Files)
        {
            sb.AppendLine(string.Format("{0,-60} | {1,12:N0} | {2}", f.RelativePath, f.SizeBytes, f.Sha256Hash));
        }

        sb.AppendLine("================================================================================");
        await File.WriteAllTextAsync(outputTextPath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }
}
