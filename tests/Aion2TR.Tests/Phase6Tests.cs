using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Aion2TR.Infrastructure.Services;
using Xunit;

namespace Aion2TR.Tests;

public class Phase6Tests
{
    private readonly string _repoRoot;
    private readonly Sha256HashService _hashService;

    public Phase6Tests()
    {
        _repoRoot = Phase5PatchService.FindRepoRoot();
        _hashService = new Sha256HashService();
    }

    [Fact]
    public async Task FinalTranslationProgress_ReflectsFullCompletion()
    {
        var service = new Phase5PatchService(_repoRoot, _hashService);
        var progress = await service.LoadTranslationProgressAsync();

        Assert.Equal(72309, progress.TotalUnique);
        Assert.Equal(72309, progress.Completed);
        Assert.Equal(0, progress.Remaining);
        Assert.Equal(100.0, progress.Percentage);
        Assert.Equal("Batch 08", progress.LastBatch);
    }

    [Fact]
    public async Task FinalDatValidationReport_PassesAllChecks()
    {
        var valReportPath = Path.Combine(_repoRoot, "reports", "final_dat_validation.json");
        Assert.True(File.Exists(valReportPath), "final_dat_validation.json must exist!");

        var json = await File.ReadAllTextAsync(valReportPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ValidationPassed").GetBoolean());
        Assert.Equal(153042, root.GetProperty("TotalEntries").GetInt32());
        Assert.True(root.GetProperty("TurkishUnicodePreserved").GetBoolean());
        Assert.True(root.GetProperty("EntryOrderPreserved").GetBoolean());

        var datPath = root.GetProperty("ArtifactPath").GetString()!;
        Assert.True(File.Exists(datPath), "Final DAT file must exist on disk!");

        var actualHash = await _hashService.ComputeSha256Async(datPath);
        var expectedHash = root.GetProperty("ArtifactSha256").GetString()!;
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public async Task FinalExportFiles_ExistAndAreNonEmpty()
    {
        var jsonPath = Path.Combine(_repoRoot, "translations", "tr-TR.final.json");
        var csvPath = Path.Combine(_repoRoot, "translations", "tr-TR.final.csv");

        Assert.True(File.Exists(jsonPath), "tr-TR.final.json must exist!");
        Assert.True(File.Exists(csvPath), "tr-TR.final.csv must exist!");

        var jsonInfo = new FileInfo(jsonPath);
        var csvInfo = new FileInfo(csvPath);

        Assert.True(jsonInfo.Length > 10_000_000, "tr-TR.final.json should contain 130k+ translations!");
        Assert.True(csvInfo.Length > 5_000_000, "tr-TR.final.csv should contain 130k+ translations!");
    }

    [Fact]
    public void All8Batches_HaveEnAndTrJsonFiles()
    {
        var batchesDir = Path.Combine(_repoRoot, "translations", "batches");
        for (int b = 1; b <= 8; b++)
        {
            var enPath = Path.Combine(batchesDir, $"batch_{b:D2}_en.json");
            var trPath = Path.Combine(batchesDir, $"batch_{b:D2}_tr.json");

            Assert.True(File.Exists(enPath), $"batch_{b:D2}_en.json must exist!");
            Assert.True(File.Exists(trPath), $"batch_{b:D2}_tr.json must exist!");
        }
    }

    [Fact]
    public void All8QualityReports_ExistAndContain20Samples()
    {
        var qualityDir = Path.Combine(_repoRoot, "reports", "translation_quality");
        for (int b = 1; b <= 8; b++)
        {
            var rPath = Path.Combine(qualityDir, $"batch_{b:D2}.md");
            Assert.True(File.Exists(rPath), $"batch_{b:D2}.md quality report must exist!");

            var text = File.ReadAllText(rPath);
            Assert.Contains("20 Random Semantic Samples", text);
            Assert.Contains("Excellent", text);
        }
    }

    [Fact]
    public async Task BackupsList_LoadsSuccessfully()
    {
        var service = new Phase5PatchService(_repoRoot, _hashService);
        var backups = await service.GetBackupsAsync();

        Assert.NotNull(backups);
        Assert.NotEmpty(backups);
    }

    [Fact]
    public void BackupInfoItem_Properties_AreConsistent()
    {
        var item = new BackupInfoItem
        {
            DirectoryName = "20261016_062649",
            FullPath = @"C:\backups\20261016_062649",
            FormattedDate = "2026-10-16 06:26",
            Status = "Güvenli / Doğrulandı",
            OriginalLooseFileExisted = false
        };

        Assert.Equal("20261016_062649", item.DirectoryName);
        Assert.Equal("Güvenli / Doğrulandı", item.Status);
        Assert.False(item.OriginalLooseFileExisted);
    }

    [Fact]
    public async Task OfficialContainers_AreVerifiedUntouched()
    {
        var service = new Phase5PatchService(_repoRoot, _hashService);
        var gameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";

        if (Directory.Exists(gameRoot))
        {
            var res = await service.VerifyContainersAsync(gameRoot);
            Assert.True(res.IsAllSafe, "Official containers must remain 100% untouched!");
            Assert.Equal(4, res.Items.Count);
            Assert.All(res.Items, item => Assert.True(item.IsMatch));
        }
    }
}
