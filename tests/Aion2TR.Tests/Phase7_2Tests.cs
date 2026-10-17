using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;
using Aion2TR.Core.Models;
using Aion2TR.Infrastructure.Services;
using Xunit;

namespace Aion2TR.Tests;

public class Phase7_2Tests
{
    private readonly string _repoRoot;
    private readonly LocalizationRuntimePatchService _patchService;
    private readonly LocalizationRuntimeRollbackService _rollbackService;

    public Phase7_2Tests()
    {
        _repoRoot = FindRepoRoot();
        _patchService = new LocalizationRuntimePatchService(_repoRoot);
        _rollbackService = new LocalizationRuntimeRollbackService(_patchService);
    }

    [Fact]
    public void Phase7_2_LegacyZipDiscovery_Exists()
    {
        var legacyZip = Path.Combine(_repoRoot, "reference", "legacy_patch", "Türkçe Yama.zip");
        Assert.True(File.Exists(legacyZip), "reference/legacy_patch/Türkçe Yama.zip must exist.");
    }

    [Fact]
    public async Task Phase7_2_LegacyStubHash_MatchesExactExpected()
    {
        var stubPath = Path.Combine(_repoRoot, "artifacts", "phase7_2", "english_empty_stub.pak");
        Assert.True(File.Exists(stubPath), "english_empty_stub.pak must exist.");

        var fi = new FileInfo(stubPath);
        Assert.Equal(LocalizationRuntimePatchService.ExpectedStubSize, fi.Length);

        var hashService = new Sha256HashService();
        var sha = await hashService.ComputeSha256Async(stubPath);
        Assert.Equal(LocalizationRuntimePatchService.ExpectedStubSha256, sha);
    }

    [Fact]
    public void Phase7_2_StubPakParsing_ValidEmptyPakHeaderAndFooter()
    {
        var stubPath = Path.Combine(_repoRoot, "artifacts", "phase7_2", "english_empty_stub.pak");
        var bytes = File.ReadAllBytes(stubPath);
        Assert.Equal(413, bytes.Length);

        // PAK Magic is at offset 209 (0x0D1) = 0x5A6F12E1
        uint magic = BitConverter.ToUInt32(bytes, 209);
        Assert.Equal(0x5A6F12E1u, magic);

        // Version is at offset 213 (0x0D5) = 11 (UE 5.3)
        int version = BitConverter.ToInt32(bytes, 213);
        Assert.Equal(11, version);

        // Encrypted index flag is at offset 208 (0x0D0) = 1
        byte encryptedFlag = bytes[208];
        Assert.Equal(1, encryptedFlag); // Encrypted Index
    }

    [Fact]
    public void Phase7_2_CurrentClientCompatibility_ReportsExistAndConfirmCompatibility()
    {
        var compatReport = Path.Combine(_repoRoot, "reports", "phase7_2", "CURRENT_CLIENT_COMPATIBILITY.md");
        var stubReport = Path.Combine(_repoRoot, "reports", "phase7_2", "STUB_ANALYSIS.md");
        var installReport = Path.Combine(_repoRoot, "reports", "phase7_2", "LEGACY_INSTALL_ANALYSIS.md");

        Assert.True(File.Exists(compatReport), "CURRENT_CLIENT_COMPATIBILITY.md must exist.");
        Assert.True(File.Exists(stubReport), "STUB_ANALYSIS.md must exist.");
        Assert.True(File.Exists(installReport), "LEGACY_INSTALL_ANALYSIS.md must exist.");

        var compatText = File.ReadAllText(compatReport);
        Assert.Contains("LEGACY_STUB_COMPATIBILITY = PASS", compatText);
    }

    [Fact]
    public async Task Phase7_2_TestDatValidation_Verified()
    {
        var testDatPath = Path.Combine(_repoRoot, "artifacts", "phase7_2", "test", "L10NString.phase7_2-test.dat");
        Assert.True(File.Exists(testDatPath), "Test DAT must exist.");

        var hashService = new Sha256HashService();
        var sha = await hashService.ComputeSha256Async(testDatPath);
        Assert.Equal(LocalizationRuntimePatchService.ExpectedTestDatSha256, sha);

        var valReport = Path.Combine(_repoRoot, "reports", "phase7_2", "test_dat_validation.json");
        Assert.True(File.Exists(valReport), "test_dat_validation.json must exist.");

        var json = File.ReadAllText(valReport);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ValidationPassed").GetBoolean());
        Assert.Equal(153042, doc.RootElement.GetProperty("TotalEntries").GetInt32());
        Assert.Equal(66, doc.RootElement.GetProperty("ModifiedEntries").GetInt32());
    }

    [Fact]
    public async Task Phase7_2_TransactionalInstall_InSandbox_SucceedsAndCanRollback()
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "Aion2TR_Sandbox_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mockOfficialDir = Path.Combine(sandboxRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
            Directory.CreateDirectory(mockOfficialDir);

            var mockOfficialPak = Path.Combine(mockOfficialDir, "pakchunk502000-Windows_0_P.pak");
            // Write a distinct dummy official file (e.g. 5000 bytes)
            var dummyData = new byte[5000];
            Random.Shared.NextBytes(dummyData);
            await File.WriteAllBytesAsync(mockOfficialPak, dummyData);

            var hashService = new Sha256HashService();
            var originalSha = await hashService.ComputeSha256Async(mockOfficialPak);

            // 1. Check status before install
            var statusBefore = await _patchService.CheckStatusAsync(sandboxRoot);
            Assert.Equal("Closed", statusBefore.GameState);
            Assert.Equal("Original", statusBefore.OfficialLocalizationState);
            Assert.Equal("Not Installed", statusBefore.LooseDatState);

            // 2. Perform Transactional Install
            var installResult = await _patchService.InstallTestPatchAsync(sandboxRoot);
            Assert.True(installResult.Success, installResult.Message);
            Assert.Equal(RuntimePatchState.Installed, installResult.State);

            // 3. Verify installed state
            var statusAfter = await _patchService.CheckStatusAsync(sandboxRoot);
            Assert.Equal("Aion2TR Stub", statusAfter.OfficialLocalizationState);
            Assert.Equal("Installed", statusAfter.LooseDatState);
            Assert.Equal("Ready", statusAfter.BackupState);
            Assert.Equal("Ready", statusAfter.RollbackState);
            Assert.True(statusAfter.IsInstalled);

            // Verify files on disk in sandbox
            var installedPakSha = await hashService.ComputeSha256Async(mockOfficialPak);
            Assert.Equal(LocalizationRuntimePatchService.ExpectedStubSha256, installedPakSha);

            var mockLooseDat = Path.Combine(sandboxRoot, LocalizationRuntimePatchService.RelativeLooseDatPath);
            Assert.True(File.Exists(mockLooseDat));
            var installedDatSha = await hashService.ComputeSha256Async(mockLooseDat);
            Assert.Equal(LocalizationRuntimePatchService.ExpectedTestDatSha256, installedDatSha);

            // 4. Perform Safe Rollback
            var rollbackResult = await _rollbackService.RollbackTestPatchAsync(sandboxRoot);
            Assert.True(rollbackResult.Success, rollbackResult.Message);
            Assert.Equal(RuntimePatchState.RolledBack, rollbackResult.State);

            // Verify rollback on disk
            Assert.False(File.Exists(mockLooseDat), "Loose DAT should be removed after rollback.");
            var restoredPakSha = await hashService.ComputeSha256Async(mockOfficialPak);
            Assert.Equal(originalSha, restoredPakSha);

            // 5. Verify Rollback Idempotency
            var secondRollback = await _rollbackService.RollbackTestPatchAsync(sandboxRoot);
            Assert.True(secondRollback.Success);
        }
        finally
        {
            if (Directory.Exists(sandboxRoot))
            {
                try { Directory.Delete(sandboxRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Phase7_2_UnknownBackupProtection_ForeignBackupNotTouched()
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "Aion2TR_Sandbox_Foreign_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mockOfficialDir = Path.Combine(sandboxRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
            Directory.CreateDirectory(mockOfficialDir);

            var mockOfficialPak = Path.Combine(mockOfficialDir, "pakchunk502000-Windows_0_P.pak");
            await File.WriteAllBytesAsync(mockOfficialPak, new byte[2000]);

            // Place a foreign backup file .aion2turk-original
            var foreignBackup = Path.Combine(mockOfficialDir, ".aion2turk-original");
            await File.WriteAllTextAsync(foreignBackup, "FOREIGN_BACKUP_DO_NOT_TOUCH");

            var result = await _patchService.InstallTestPatchAsync(sandboxRoot);
            Assert.True(result.Success);

            // Verify foreign backup still exists and is untouched
            Assert.True(File.Exists(foreignBackup));
            var content = await File.ReadAllTextAsync(foreignBackup);
            Assert.Equal("FOREIGN_BACKUP_DO_NOT_TOUCH", content);

            // Cleanup rollback
            await _rollbackService.RollbackTestPatchAsync(sandboxRoot);
            Assert.True(File.Exists(foreignBackup));
        }
        finally
        {
            if (Directory.Exists(sandboxRoot))
            {
                try { Directory.Delete(sandboxRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Phase7_2_UnknownOfficialPakProtection_DoesNotOverwriteForeignPak()
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "Aion2TR_Sandbox_UnknownPak_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mockOfficialDir = Path.Combine(sandboxRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
            Directory.CreateDirectory(mockOfficialDir);

            var mockOfficialPak = Path.Combine(mockOfficialDir, "pakchunk502000-Windows_0_P.pak");
            await File.WriteAllBytesAsync(mockOfficialPak, new byte[2000]);

            var installResult = await _patchService.InstallTestPatchAsync(sandboxRoot);
            Assert.True(installResult.Success);

            // Now simulate an unknown third-party replaced the official PAK with their own file
            await File.WriteAllBytesAsync(mockOfficialPak, new byte[] { 1, 2, 3, 4, 5 });

            // Attempt rollback
            var rollbackResult = await _rollbackService.RollbackTestPatchAsync(sandboxRoot);
            Assert.False(rollbackResult.Success, "Rollback must refuse to overwrite an unknown foreign PAK.");
            Assert.Contains("Resmi PAK dosyası tanınmayan", rollbackResult.Message);
        }
        finally
        {
            if (Directory.Exists(sandboxRoot))
            {
                try { Directory.Delete(sandboxRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Phase7_2_CrashRecovery_DetectsAndRecoversIncompleteState()
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "Aion2TR_Sandbox_Crash_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mockOfficialDir = Path.Combine(sandboxRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
            Directory.CreateDirectory(mockOfficialDir);

            var mockOfficialPak = Path.Combine(mockOfficialDir, "pakchunk502000-Windows_0_P.pak");
            var originalBytes = new byte[3000];
            Random.Shared.NextBytes(originalBytes);
            await File.WriteAllBytesAsync(mockOfficialPak, originalBytes);

            var installResult = await _patchService.InstallTestPatchAsync(sandboxRoot);
            Assert.True(installResult.Success);

            // Simulate crash state: write manifest with State = StubInstalled
            var manifestPath = _patchService.ManifestFilePath;
            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<RuntimePatchOwnershipManifest>(manifestJson)!;
            manifest.State = RuntimePatchState.StubInstalled;
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

            // Check status detects crash recovery needed
            var status = await _patchService.CheckStatusAsync(sandboxRoot);
            Assert.True(status.RequiresCrashRecovery);
            Assert.NotNull(status.CrashRecoveryMessage);

            // Recover from crash
            var recoveryResult = await _rollbackService.RecoverFromCrashAsync(sandboxRoot);
            Assert.True(recoveryResult.Success);
            Assert.Equal(RuntimePatchState.RolledBack, recoveryResult.State);

            // Verify original file is restored
            var hashService = new Sha256HashService();
            var restoredSha = await hashService.ComputeSha256Async(mockOfficialPak);
            Assert.Equal(manifest.OriginalPakSHA256, restoredSha);
        }
        finally
        {
            if (Directory.Exists(sandboxRoot))
            {
                try { Directory.Delete(sandboxRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Phase7_2_GameUpdateFingerprintHandling_DetectsNewGameVersion()
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "Aion2TR_Sandbox_SteamUpdate_" + Guid.NewGuid().ToString("N"));
        try
        {
            var mockOfficialDir = Path.Combine(sandboxRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
            Directory.CreateDirectory(mockOfficialDir);

            var mockOfficialPak = Path.Combine(mockOfficialDir, "pakchunk502000-Windows_0_P.pak");
            await File.WriteAllBytesAsync(mockOfficialPak, new byte[4000]);

            var installResult = await _patchService.InstallTestPatchAsync(sandboxRoot);
            Assert.True(installResult.Success);

            // Simulate Steam update by overwriting the official PAK with new official update
            await File.WriteAllBytesAsync(mockOfficialPak, new byte[9999]);

            var status = await _patchService.CheckStatusAsync(sandboxRoot);
            Assert.True(status.GameFingerprintChanged);
            Assert.Contains("Yeni oyun sürümü algılandı", status.Details);
        }
        finally
        {
            if (Directory.Exists(sandboxRoot))
            {
                try { Directory.Delete(sandboxRoot, recursive: true); } catch { }
            }
        }
    }

    private static string FindRepoRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Aion2TR.sln")))
                return current;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }
}
