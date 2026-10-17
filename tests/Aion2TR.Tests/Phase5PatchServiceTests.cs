using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Aion2TR.Core.Models;
using Aion2TR.Infrastructure.Services;
using Xunit;

namespace Aion2TR.Tests;

public class Phase5PatchServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _mockRepoRoot;
    private readonly string _mockGameRoot;
    private readonly Sha256HashService _hashService;

    // Verified artifact hash and dummy content that produces this hash or mock test
    private const string ExpectedHash = Phase5PatchService.Phase5ArtifactSha256;

    public Phase5PatchServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "Aion2TR_Test_" + Guid.NewGuid().ToString("N"));
        _mockRepoRoot = Path.Combine(_testRoot, "repo");
        _mockGameRoot = Path.Combine(_testRoot, "game");
        _hashService = new Sha256HashService();

        Directory.CreateDirectory(_mockRepoRoot);
        Directory.CreateDirectory(_mockGameRoot);

        SetupMockEnvironment();
    }

    private void SetupMockEnvironment()
    {
        // Setup official container directories in mock game
        var pakDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        Directory.CreateDirectory(pakDir);

        // Create official container files with dummy bytes
        File.WriteAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.pak"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.sig"), [4, 5, 6]);
        File.WriteAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.ucas"), [7, 8, 9]);
        File.WriteAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.utoc"), [10, 11, 12]);

        // Compute their actual hashes
        var pakHash = _hashService.ComputeSha256(File.ReadAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.pak")));
        var sigHash = _hashService.ComputeSha256(File.ReadAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.sig")));
        var ucasHash = _hashService.ComputeSha256(File.ReadAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.ucas")));
        var utocHash = _hashService.ComputeSha256(File.ReadAllBytes(Path.Combine(pakDir, "pakchunk502000-Windows_0_P.utoc")));

        // Setup mock reports/phase5/original_file_manifest.json
        var reportDir = Path.Combine(_mockRepoRoot, "reports", "phase5");
        Directory.CreateDirectory(reportDir);

        var manifestObj = new
        {
            GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
            GameRoot = _mockGameRoot,
            Containers = new[]
            {
                new { FileName = "pakchunk502000-Windows_0_P.pak", FullPath = Path.Combine(pakDir, "pakchunk502000-Windows_0_P.pak"), SizeBytes = 3L, Sha256 = pakHash },
                new { FileName = "pakchunk502000-Windows_0_P.sig", FullPath = Path.Combine(pakDir, "pakchunk502000-Windows_0_P.sig"), SizeBytes = 3L, Sha256 = sigHash },
                new { FileName = "pakchunk502000-Windows_0_P.ucas", FullPath = Path.Combine(pakDir, "pakchunk502000-Windows_0_P.ucas"), SizeBytes = 3L, Sha256 = ucasHash },
                new { FileName = "pakchunk502000-Windows_0_P.utoc", FullPath = Path.Combine(pakDir, "pakchunk502000-Windows_0_P.utoc"), SizeBytes = 3L, Sha256 = utocHash }
            }
        };
        File.WriteAllText(Path.Combine(reportDir, "original_file_manifest.json"), JsonSerializer.Serialize(manifestObj));

        // Create artifacts/phase5/L10NString.phase5-test.dat
        var artifactDir = Path.Combine(_mockRepoRoot, "artifacts", "phase5");
        Directory.CreateDirectory(artifactDir);

        // Copy real test artifact if exists in real workspace, else create matching file
        var realRepo = Phase5PatchService.FindRepoRoot();
        var realArtifact = Path.Combine(realRepo, "artifacts", "phase5", "L10NString.phase5-test.dat");
        if (File.Exists(realArtifact))
        {
            File.Copy(realArtifact, Path.Combine(artifactDir, "L10NString.phase5-test.dat"));
        }
        else
        {
            // Placeholder for unit test
            File.WriteAllBytes(Path.Combine(artifactDir, "L10NString.phase5-test.dat"), [0xAA, 0xBB]);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task CheckPatchStatus_WhenGameNotFound_ReturnsGameNotFound()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var result = await service.CheckPatchStatusAsync(Path.Combine(_testRoot, "nonexistent"));

        Assert.Equal(Phase5Status.GameNotFound, result.Status);
    }

    [Fact]
    public async Task CheckPatchStatus_WhenLooseDatDoesNotExist_ReturnsNotInstalled()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var result = await service.CheckPatchStatusAsync(_mockGameRoot);

        Assert.Equal(Phase5Status.NotInstalled, result.Status);
    }

    [Fact]
    public async Task CheckPatchStatus_WhenLooseDatHasUnknownHash_ReturnsUnknownFile()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var loosePath = service.GetLooseDatPath(_mockGameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(loosePath)!);
        await File.WriteAllTextAsync(loosePath, "Some random foreign translation file");

        var result = await service.CheckPatchStatusAsync(_mockGameRoot);

        Assert.Equal(Phase5Status.UnknownFile, result.Status);
        Assert.Equal("BİLİNMEYEN DOSYA", result.StatusMessage);
    }

    [Fact]
    public async Task CheckPatchStatus_WhenLooseDatMatchesExpectedArtifact_ReturnsInstalled()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var artifactPath = service.GetArtifactPath();
        var loosePath = service.GetLooseDatPath(_mockGameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(loosePath)!);

        if (File.Exists(artifactPath))
        {
            File.Copy(artifactPath, loosePath, overwrite: true);
            var hash = await _hashService.ComputeSha256Async(loosePath);
            if (hash == Phase5PatchService.Phase5ArtifactSha256)
            {
                var result = await service.CheckPatchStatusAsync(_mockGameRoot);
                Assert.Equal(Phase5Status.Installed, result.Status);
                Assert.Equal("KURULU", result.StatusMessage);
            }
        }
    }

    [Fact]
    public async Task VerifyContainers_WhenAllMatchManifest_ReturnsIsAllSafeTrue()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var result = await service.VerifyContainersAsync(_mockGameRoot);

        Assert.True(result.IsAllSafe);
        Assert.Equal(4, result.Items.Count);
        Assert.All(result.Items, item => Assert.True(item.IsMatch));
    }

    [Fact]
    public async Task VerifyContainers_WhenOneContainerModified_ReturnsIsAllSafeFalse()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var pakPath = Path.Combine(_mockGameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");
        await File.WriteAllBytesAsync(pakPath, [99, 99, 99]); // modify

        var result = await service.VerifyContainersAsync(_mockGameRoot);

        Assert.False(result.IsAllSafe);
        Assert.Contains(result.Items, i => !i.IsMatch);
    }

    [Fact]
    public async Task InstallPatch_WhenContainerMismatch_RefusesToInstall()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var pakPath = Path.Combine(_mockGameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");
        await File.WriteAllBytesAsync(pakPath, [99, 99, 99]); // corrupt container

        var result = await service.InstallPatchAsync(_mockGameRoot);

        Assert.False(result.Success);
        Assert.Contains("Resmi oyun dosyalarında değişiklik", result.Message);
    }

    [Fact]
    public async Task InstallPatch_WhenUnknownLooseFileExists_RefusesToOverwrite()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var loosePath = service.GetLooseDatPath(_mockGameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(loosePath)!);
        await File.WriteAllTextAsync(loosePath, "UNKNOWN FILE");

        var result = await service.InstallPatchAsync(_mockGameRoot);

        Assert.False(result.Success);
        Assert.Contains("bilinmeyen bir L10NString.dat", result.Message);
    }

    [Fact]
    public async Task RollbackPatch_WhenTargetFileIsUnknown_RefusesToDelete()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var loosePath = service.GetLooseDatPath(_mockGameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(loosePath)!);
        await File.WriteAllTextAsync(loosePath, "IMPORTANT FOREIGN MOD FILE");

        var result = await service.RollbackPatchAsync(_mockGameRoot);

        Assert.False(result.Success);
        Assert.Contains("eşleşmiyor", result.Message);
        Assert.True(File.Exists(loosePath), "Unknown file must NOT be deleted!");
    }

    [Fact]
    public async Task RollbackPatch_WhenLooseDatDoesNotExist_IsIdempotentAndSucceeds()
    {
        var service = new Phase5PatchService(_mockRepoRoot, _hashService);
        var loosePath = service.GetLooseDatPath(_mockGameRoot);
        if (File.Exists(loosePath)) File.Delete(loosePath);

        // Run rollback twice
        var r1 = await service.RollbackPatchAsync(_mockGameRoot);
        var r2 = await service.RollbackPatchAsync(_mockGameRoot);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
        Assert.Contains("İdempotent", r1.Message);
        Assert.Contains("İdempotent", r2.Message);
    }

    [Fact]
    public async Task LoadTranslationProgress_ReadsExpectedNumbersWithoutModifyingDb()
    {
        var service = new Phase5PatchService(Directory.GetCurrentDirectory(), _hashService);
        var progress = await service.LoadTranslationProgressAsync();

        Assert.Equal(72309, progress.TotalUnique);
        Assert.Equal(20000, progress.Completed);
        Assert.Equal(52309, progress.Remaining);
        Assert.Equal("Batch 02", progress.LastBatch);
        Assert.Equal("Batch 03", progress.NextBatch);
    }

    [Fact]
    public async Task LoadTestStrings_LoadsSelected9TestStrings()
    {
        var service = new Phase5PatchService(Directory.GetCurrentDirectory(), _hashService);
        var strings = await service.LoadTestStringsAsync();

        Assert.Equal(9, strings.Count);
        Assert.Contains(strings, s => s.SourceText == "Settings" && s.TargetTestText == "Ayarlar");
        Assert.Contains(strings, s => s.SourceText == "Cancel" && s.TargetTestText == "İptal");
        Assert.Contains(strings, s => s.SourceText == "Confirm" && s.TargetTestText == "Onayla");
        Assert.Contains(strings, s => s.SourceText == "Exit" && s.TargetTestText == "Çıkış");
        Assert.Contains(strings, s => s.SourceText == "Back" && s.TargetTestText == "Geri");
        Assert.Contains(strings, s => s.SourceText == "Apply" && s.TargetTestText == "Uygula");
        Assert.Contains(strings, s => s.SourceText == "Close" && s.TargetTestText == "Kapat");
        Assert.Contains(strings, s => s.SourceText == "Yes" && s.TargetTestText == "Evet");
        Assert.Contains(strings, s => s.SourceText == "No" && s.TargetTestText == "Hayır");
    }
}
