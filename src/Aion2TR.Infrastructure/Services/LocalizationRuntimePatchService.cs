using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;
using Aion2TR.Core.Models;

namespace Aion2TR.Infrastructure.Services;

public class LocalizationRuntimePatchService : ILocalizationRuntimePatchService, ILocalizationRuntimeRollbackService
{
    public const string ExpectedStubSha256 = "105a71d61c9a0683500007bb8dab1cf3f12acfd28f17181408ceda426fc3d5b2";
    public const long ExpectedStubSize = 413;
    public const string ExpectedFullDatSha256 = "903c0bdfdb4a6a52881a903500b0d3a9efb86812ccf1f6e284e18eb803bea7d7";
    public const string ExpectedTestDatSha256 = "81e8d9a779e9a9fb9d123c92d72766144a8b2addcd5bc9570057cdc6e696c998";
    public const string LegacyPatchMode = "LEGACY_EMPTY_STUB_PLUS_LOOSE_DAT";

    private readonly string _repoRoot;
    private readonly IHashService _hashService;

    public string RepoRoot => _repoRoot;

    public LocalizationRuntimePatchService(string? repoRoot = null, IHashService? hashService = null)
    {
        _repoRoot = repoRoot ?? FindRepoRoot();
        _hashService = hashService ?? new Sha256HashService();
    }

    public static string RelativeOfficialPakPath => Path.Combine("Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");
    public static string RelativeLooseDatPath => Path.Combine("Aion2", "Content", "L10N", "Text", "en-US", "L10NString.dat");
    public static string LegacyWrongLooseDatPath => Path.Combine("Aion2", "Content", "Paks", "L10N", "Text", "en-US", "L10NString.dat");
    public string ManifestFilePath => Path.Combine(_repoRoot, "data", "runtime_patch_manifest.json");

    public bool IsGameProcessRunning()
    {
        string[] targetNames = { "AION2", "AION2-Win64-Shipping", "Aion2", "Aion2-Win64-Shipping" };
        foreach (var name in targetNames)
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0)
                return true;
        }
        return false;
    }

    public async Task<RuntimePatchStatus> CheckStatusAsync(string gameRoot, CancellationToken ct = default)
    {
        var status = new RuntimePatchStatus
        {
            Method = "Empty Stub + Loose DAT"
        };

        status.IsGameRunning = IsGameProcessRunning();
        status.GameState = status.IsGameRunning ? "Running" : "Closed";

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            status.Details = "Oyun dizini geçersiz veya bulunamadı.";
            return status;
        }

        var officialPakPath = Path.Combine(gameRoot, RelativeOfficialPakPath);
        var looseDatPath = Path.Combine(gameRoot, RelativeLooseDatPath);
        var manifest = await LoadManifestAsync(ct);

        // Check official PAK
        if (File.Exists(officialPakPath))
        {
            var currentPakSha = await _hashService.ComputeSha256Async(officialPakPath, ct);
            status.OfficialPakSha256 = currentPakSha;

            if (string.Equals(currentPakSha, ExpectedStubSha256, StringComparison.OrdinalIgnoreCase))
            {
                status.OfficialLocalizationState = "Aion2TR Stub";
            }
            else if (manifest != null && string.Equals(currentPakSha, manifest.OriginalPakSHA256, StringComparison.OrdinalIgnoreCase))
            {
                status.OfficialLocalizationState = "Original";
            }
            else
            {
                // Check if manifest existed and current PAK changed unexpectedly
                if (manifest != null && manifest.State == RuntimePatchState.Installed && !string.Equals(currentPakSha, manifest.StubSHA256, StringComparison.OrdinalIgnoreCase))
                {
                    status.GameFingerprintChanged = true;
                    status.OfficialLocalizationState = "Unknown (Oyun Güncellenmiş Olabilir)";
                }
                else
                {
                    status.OfficialLocalizationState = "Original";
                }
            }
        }
        else
        {
            status.OfficialLocalizationState = "Missing";
        }

        // Check loose DAT
        if (File.Exists(looseDatPath))
        {
            var looseDatSha = await _hashService.ComputeSha256Async(looseDatPath, ct);
            if (string.Equals(looseDatSha, ExpectedFullDatSha256, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(looseDatSha, ExpectedTestDatSha256, StringComparison.OrdinalIgnoreCase) ||
                (manifest != null && string.Equals(looseDatSha, manifest.LooseDatSHA256, StringComparison.OrdinalIgnoreCase)))
            {
                status.LooseDatState = "Installed";
            }
            else
            {
                status.LooseDatState = "Foreign / Unknown";
            }
        }
        else
        {
            status.LooseDatState = "Not Installed";
        }

        // Check backup
        string? verifiedBackupPath = null;
        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.BackupPath) && File.Exists(manifest.BackupPath))
        {
            var backupSha = await _hashService.ComputeSha256Async(manifest.BackupPath, ct);
            if (string.Equals(backupSha, manifest.BackupSHA256, StringComparison.OrdinalIgnoreCase))
            {
                verifiedBackupPath = manifest.BackupPath;
            }
        }

        if (verifiedBackupPath == null)
        {
            // Discover from backups directory
            verifiedBackupPath = await DiscoverValidBackupPathAsync(ct);
        }

        if (verifiedBackupPath != null)
        {
            status.BackupState = "Ready";
            status.BackupPath = verifiedBackupPath;
            status.RollbackState = "Ready";
        }
        else
        {
            status.BackupState = "Missing";
            status.RollbackState = "Unsafe";
        }

        // Crash recovery check
        if (manifest != null)
        {
            if (manifest.State == RuntimePatchState.Preparing ||
                manifest.State == RuntimePatchState.BackupCreated ||
                manifest.State == RuntimePatchState.StubInstalled ||
                manifest.State == RuntimePatchState.LooseDatInstalled ||
                manifest.State == RuntimePatchState.RollingBack ||
                manifest.State == RuntimePatchState.Failed)
            {
                status.RequiresCrashRecovery = true;
                status.CrashRecoveryMessage = "Önceki yama işlemi tamamlanmamış. Güvenli geri yükleme gerekiyor.";
            }
            else if (manifest.State == RuntimePatchState.Installed)
            {
                status.IsInstalled = true;
            }
        }

        if (status.GameFingerprintChanged)
        {
            status.Details = "Yeni oyun sürümü algılandı. Resmi dosyalar Steam tarafından güncellenmiş olabilir.";
        }

        return status;
    }

    public Task<RuntimePatchOperationResult> InstallTestPatchAsync(string gameRoot, CancellationToken ct = default)
    {
        return InstallPatchInternalAsync(gameRoot, useCandidateFullDat: false, ct);
    }

    public Task<RuntimePatchOperationResult> InstallFullPatchAsync(string gameRoot, CancellationToken ct = default)
    {
        return InstallPatchInternalAsync(gameRoot, useCandidateFullDat: true, ct);
    }

    private async Task<RuntimePatchOperationResult> InstallPatchInternalAsync(string gameRoot, bool useCandidateFullDat, CancellationToken ct)
    {
        var result = new RuntimePatchOperationResult();

        // 1. Game Process Check
        if (IsGameProcessRunning())
        {
            result.Success = false;
            result.Message = "AION 2 açık. Yama dosyalarını değiştirmeden önce oyunu kapatın.";
            result.Errors.Add("Game process is active.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            result.Success = false;
            result.Message = "Oyun dizini bulunamadı.";
            return result;
        }

        var officialPakPath = Path.Combine(gameRoot, RelativeOfficialPakPath);
        var looseDatPath = Path.Combine(gameRoot, RelativeLooseDatPath);
        var officialDir = Path.GetDirectoryName(officialPakPath)!;

        // 2. Locate official localization PAK
        if (!File.Exists(officialPakPath))
        {
            result.Success = false;
            result.Message = $"Resmi localization PAK bulunamadı: {officialPakPath}";
            result.Errors.Add("Official PAK not found.");
            return result;
        }

        // Check for foreign backup protection: .aion2turk-original
        var foreignBackup = Path.Combine(officialDir, ".aion2turk-original");
        var foreignBackup2 = officialPakPath + ".aion2turk-original";
        if (File.Exists(foreignBackup) || File.Exists(foreignBackup2))
        {
            result.Warnings.Add("Yabancı yedek dosyası (.aion2turk-original) tespit edildi. Dokunulmadan korunuyor.");
        }

        // 3. Locate & Validate Stub Source
        var stubSourcePath = ResolveStubSourcePath();
        if (!File.Exists(stubSourcePath))
        {
            result.Success = false;
            result.Message = $"Doğrulanmış boş stub PAK bulunamadı: {stubSourcePath}";
            result.Errors.Add("Stub source not found.");
            return result;
        }

        var stubFileInfo = new FileInfo(stubSourcePath);
        if (stubFileInfo.Length != ExpectedStubSize)
        {
            result.Success = false;
            result.Message = $"Stub dosya boyutu hatalı: {stubFileInfo.Length} (Beklenen: {ExpectedStubSize})";
            result.Errors.Add("Stub size mismatch.");
            return result;
        }

        var stubSha = await _hashService.ComputeSha256Async(stubSourcePath, ct);
        if (!string.Equals(stubSha, ExpectedStubSha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Success = false;
            result.Message = "LEGACY_REFERENCE_HASH_MISMATCH: Boş stub SHA-256 imzası eşleşmedi.";
            result.Errors.Add($"Expected {ExpectedStubSha256}, got {stubSha}");
            return result;
        }

        // 4. Locate & Validate DAT Source
        var candidateDatSourcePath = Path.Combine(_repoRoot, "artifacts", "phase7_3", "L10NString.tr-TR.candidate.dat");
        var fullDatSourcePath = Path.Combine(_repoRoot, "artifacts", "final", "L10NString.tr-TR.dat");
        var legacyTestDatPath = Path.Combine(_repoRoot, "artifacts", "phase7_2", "test", "L10NString.phase7_2-test.dat");

        string testDatSourcePath;
        string expectedDatSha;

        if (useCandidateFullDat)
        {
            if (File.Exists(candidateDatSourcePath))
            {
                testDatSourcePath = candidateDatSourcePath;
                expectedDatSha = ExpectedFullDatSha256;
            }
            else if (File.Exists(fullDatSourcePath))
            {
                testDatSourcePath = fullDatSourcePath;
                expectedDatSha = ExpectedFullDatSha256;
            }
            else
            {
                result.Success = false;
                result.Message = "Doğrulanmış tam Türkçe DAT dosyası bulunamadı.";
                result.Errors.Add("Candidate or full DAT source not found.");
                return result;
            }
        }
        else
        {
            if (File.Exists(legacyTestDatPath))
            {
                testDatSourcePath = legacyTestDatPath;
                expectedDatSha = ExpectedTestDatSha256;
            }
            else
            {
                result.Success = false;
                result.Message = "Doğrulanmış test Türkçe DAT dosyası bulunamadı.";
                result.Errors.Add("Test DAT source not found.");
                return result;
            }
        }

        var testDatSha = await _hashService.ComputeSha256Async(testDatSourcePath, ct);
        var testDatSize = new FileInfo(testDatSourcePath).Length;
        if (!string.Equals(testDatSha, expectedDatSha, StringComparison.OrdinalIgnoreCase))
        {
            result.Success = false;
            result.Message = "Türkçe DAT SHA-256 eşleşmedi. Dosya bozulmuş veya değiştirilmiş.";
            result.Errors.Add($"Expected {expectedDatSha}, got {testDatSha}");
            return result;
        }

        // 5. Calculate official PAK hash & size
        var originalPakSize = new FileInfo(officialPakPath).Length;
        var originalPakSha = await _hashService.ComputeSha256Async(officialPakPath, ct);

        if (string.Equals(originalPakSha, ExpectedStubSha256, StringComparison.OrdinalIgnoreCase))
        {
            result.Success = false;
            result.Message = "Resmi PAK dosyasının yerinde zaten boş stub var. Yama zaten kurulmuş veya yarım kalmış.";
            return result;
        }

        // 6. Transaction State: Preparing
        var manifest = new RuntimePatchOwnershipManifest
        {
            PatchVersion = "1.0.0-phase7.2-test",
            InstalledAtUtc = DateTime.UtcNow,
            GameFingerprint = $"{originalPakSize}:{originalPakSha}",
            OfficialPakPath = officialPakPath,
            OriginalPakSHA256 = originalPakSha,
            OriginalPakSize = originalPakSize,
            StubSHA256 = stubSha,
            LooseDatPath = looseDatPath,
            LooseDatSHA256 = testDatSha,
            LooseDatSize = testDatSize,
            PatchMode = LegacyPatchMode,
            State = RuntimePatchState.Preparing,
            CreatedBy = "Aion2TR"
        };
        await SaveManifestAsync(manifest, ct);

        // 7. Create Aion2TR-owned backup
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(_repoRoot, "backups", "runtime_patch", timestamp);
        Directory.CreateDirectory(backupDir);

        var backupPakPath = Path.Combine(backupDir, "pakchunk502000-Windows_0_P.pak");
        var backupManifestPath = Path.Combine(backupDir, "backup_manifest.json");

        // Copy official file to backup
        File.Copy(officialPakPath, backupPakPath, overwrite: true);

        // Verify backup
        var backupSize = new FileInfo(backupPakPath).Length;
        var backupSha = await _hashService.ComputeSha256Async(backupPakPath, ct);

        if (backupSize != originalPakSize || !string.Equals(backupSha, originalPakSha, StringComparison.OrdinalIgnoreCase))
        {
            // Backup verification failed! Clean up and abort!
            if (File.Exists(backupPakPath)) File.Delete(backupPakPath);
            manifest.State = RuntimePatchState.Failed;
            await SaveManifestAsync(manifest, ct);

            result.Success = false;
            result.Message = "Yedek doğrulama başarısız oldu. Orijinal dosyaya dokunulmadı.";
            result.Errors.Add("Backup size or hash mismatch.");
            return result;
        }

        var backupManifest = new RuntimeBackupManifest
        {
            OriginalPath = officialPakPath,
            BackupPath = backupPakPath,
            OriginalSHA256 = originalPakSha,
            BackupSHA256 = backupSha,
            OriginalSize = originalPakSize,
            BackupSize = backupSize,
            TimestampUtc = DateTime.UtcNow,
            GameVersionFingerprint = $"{originalPakSize}:{originalPakSha}",
            CreatedBy = "Aion2TR"
        };
        await File.WriteAllTextAsync(backupManifestPath, JsonSerializer.Serialize(backupManifest, new JsonSerializerOptions { WriteIndented = true }), ct);

        manifest.BackupPath = backupPakPath;
        manifest.BackupSHA256 = backupSha;
        manifest.BackupSize = backupSize;
        manifest.State = RuntimePatchState.BackupCreated;
        await SaveManifestAsync(manifest, ct);

        // 8. Safely replace official PAK with stub (via temporary file)
        try
        {
            var tempStubPath = officialPakPath + ".aion2tr_tmp";
            File.Copy(stubSourcePath, tempStubPath, overwrite: true);

            var tempStubSha = await _hashService.ComputeSha256Async(tempStubPath, ct);
            if (!string.Equals(tempStubSha, ExpectedStubSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempStubPath);
                throw new InvalidOperationException("Geçici stub dosyasının hash doğrulaması başarısız oldu.");
            }

            // Atomic replace
            File.Move(tempStubPath, officialPakPath, overwrite: true);

            manifest.State = RuntimePatchState.StubInstalled;
            await SaveManifestAsync(manifest, ct);

            // 9. Write loose test DAT (via temporary file)
            var looseDir = Path.GetDirectoryName(looseDatPath);
            if (!string.IsNullOrEmpty(looseDir))
            {
                Directory.CreateDirectory(looseDir);
            }

            var tempDatPath = looseDatPath + ".aion2tr_tmp";
            File.Copy(testDatSourcePath, tempDatPath, overwrite: true);

            var tempDatSha = await _hashService.ComputeSha256Async(tempDatPath, ct);
            if (!string.Equals(tempDatSha, expectedDatSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempDatPath);
                throw new InvalidOperationException("Geçici DAT dosyasının hash doğrulaması başarısız oldu.");
            }

            File.Move(tempDatPath, looseDatPath, overwrite: true);

            manifest.State = RuntimePatchState.LooseDatInstalled;
            await SaveManifestAsync(manifest, ct);

            // 10. Final Verification
            var installedStubSha = await _hashService.ComputeSha256Async(officialPakPath, ct);
            var installedDatSha = await _hashService.ComputeSha256Async(looseDatPath, ct);

            if (!string.Equals(installedStubSha, ExpectedStubSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(installedDatSha, expectedDatSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Kurulum sonrası son doğrulama başarısız oldu. Geri alma başlatılıyor.");
            }

            manifest.State = RuntimePatchState.Installed;
            await SaveManifestAsync(manifest, ct);

            result.Success = true;
            result.State = RuntimePatchState.Installed;
            result.Message = "Phase 7.2 Legacy Test Yaması başarıyla kuruldu. Orijinal dosya güvenle yedeklendi.";
            return result;
        }
        catch (Exception ex)
        {
            // Immediate Rollback on any failure!
            result.Errors.Add($"Hata oluştu: {ex.Message}. Acil geri alma uygulanıyor...");
            await ExecuteInternalRollbackAsync(manifest, ct);

            result.Success = false;
            result.State = RuntimePatchState.Failed;
            result.Message = $"Kurulum başarısız oldu ve otomatik geri alındı: {ex.Message}";
            return result;
        }
    }

    public async Task<RuntimePatchOperationResult> RollbackTestPatchAsync(string gameRoot, CancellationToken ct = default)
    {
        var result = new RuntimePatchOperationResult();

        if (IsGameProcessRunning())
        {
            result.Success = false;
            result.Message = "AION 2 açık. Geri yükleme yapmadan önce oyunu kapatın.";
            result.Errors.Add("Game process is active.");
            return result;
        }

        var manifest = await LoadManifestAsync(ct);
        if (manifest == null)
        {
            // Try to recover using backup discovered on disk
            var discoveredBackup = await DiscoverValidBackupPathAsync(ct);
            if (discoveredBackup == null)
            {
                result.Success = false;
                result.Message = "Yama manifesti ve geçerli yedek bulunamadı. Geri alma yapılamaz.";
                result.Errors.Add("Manifest and backup not found.");
                return result;
            }

            manifest = new RuntimePatchOwnershipManifest
            {
                BackupPath = discoveredBackup,
                OfficialPakPath = Path.Combine(gameRoot, RelativeOfficialPakPath),
                LooseDatPath = Path.Combine(gameRoot, RelativeLooseDatPath)
            };
        }

        manifest.State = RuntimePatchState.RollingBack;
        await SaveManifestAsync(manifest, ct);

        // 1. Verify backup exists and is valid
        if (!File.Exists(manifest.BackupPath))
        {
            result.Success = false;
            result.Message = $"Yedek dosyası bulunamadı: {manifest.BackupPath}";
            result.Errors.Add("Backup file not found.");
            return result;
        }

        var backupSha = await _hashService.ComputeSha256Async(manifest.BackupPath, ct);
        if (!string.IsNullOrWhiteSpace(manifest.BackupSHA256) &&
            !string.Equals(backupSha, manifest.BackupSHA256, StringComparison.OrdinalIgnoreCase))
        {
            result.Success = false;
            result.Message = "Yedek dosyasının hash değeri bozulmuş! Güvenlik nedeniyle geri yükleme iptal edildi.";
            result.Errors.Add("Corrupt backup file.");
            return result;
        }

        // 2. Check current official PAK ownership
        var officialPakPath = string.IsNullOrWhiteSpace(manifest.OfficialPakPath)
            ? Path.Combine(gameRoot, RelativeOfficialPakPath)
            : manifest.OfficialPakPath;

        if (File.Exists(officialPakPath))
        {
            var currentPakSha = await _hashService.ComputeSha256Async(officialPakPath, ct);
            bool isOurStub = string.Equals(currentPakSha, ExpectedStubSha256, StringComparison.OrdinalIgnoreCase);
            bool isAlreadyOriginal = string.Equals(currentPakSha, backupSha, StringComparison.OrdinalIgnoreCase);

            if (!isOurStub && !isAlreadyOriginal)
            {
                result.Success = false;
                result.Message = "Resmi PAK dosyası tanınmayan üçüncü parti veya güncellenmiş içeriğe sahip. Güvenlik nedeniyle üzerine yazılmadı.";
                result.Errors.Add("Official PAK is neither Aion2TR stub nor original.");
                return result;
            }
        }

        // 3. Remove only Aion2TR-owned loose DAT
        var looseDatPath = string.IsNullOrWhiteSpace(manifest.LooseDatPath)
            ? Path.Combine(gameRoot, RelativeLooseDatPath)
            : manifest.LooseDatPath;

        if (File.Exists(looseDatPath))
        {
            var looseDatSha = await _hashService.ComputeSha256Async(looseDatPath, ct);
            if (string.Equals(looseDatSha, ExpectedFullDatSha256, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(looseDatSha, ExpectedTestDatSha256, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(looseDatSha, manifest.LooseDatSHA256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(looseDatPath);
            }
            else
            {
                result.Warnings.Add("Mevcut loose DAT dosyası Aion2TR test DAT'ı ile eşleşmediği için silinmedi (yabancı dosya koruması).");
            }
        }

        // Also clean up any legacy mislocated loose DAT if present
        var legacyWrongLoosePath = Path.Combine(gameRoot, LegacyWrongLooseDatPath);
        if (File.Exists(legacyWrongLoosePath))
        {
            try { File.Delete(legacyWrongLoosePath); } catch { }
        }

        // 4. Restore original PAK from backup
        var tempRestorePath = officialPakPath + ".aion2tr_restore_tmp";
        File.Copy(manifest.BackupPath, tempRestorePath, overwrite: true);

        var tempRestoreSha = await _hashService.ComputeSha256Async(tempRestorePath, ct);
        if (!string.Equals(tempRestoreSha, backupSha, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempRestorePath);
            result.Success = false;
            result.Message = "Geri yükleme kopyası doğrulanamadı.";
            result.Errors.Add("Restored temp copy hash mismatch.");
            return result;
        }

        File.Move(tempRestorePath, officialPakPath, overwrite: true);

        // Verify restored file
        var restoredSha = await _hashService.ComputeSha256Async(officialPakPath, ct);
        if (!string.Equals(restoredSha, backupSha, StringComparison.OrdinalIgnoreCase))
        {
            result.Success = false;
            result.Message = "Geri yüklenen dosya doğrulaması başarısız oldu!";
            result.Errors.Add("Restored file hash mismatch.");
            return result;
        }

        manifest.State = RuntimePatchState.RolledBack;
        await SaveManifestAsync(manifest, ct);

        result.Success = true;
        result.State = RuntimePatchState.RolledBack;
        result.Message = "Orijinal oyun dosyası yedekten başarıyla geri yüklendi ve test dosyaları temizlendi.";
        return result;
    }

    public Task<RuntimePatchOperationResult> RestoreOriginalAsync(string gameRoot, CancellationToken ct = default)
    {
        return RollbackTestPatchAsync(gameRoot, ct);
    }

    public async Task<RuntimePatchOperationResult> RecoverFromCrashAsync(string gameRoot, CancellationToken ct = default)
    {
        return await RollbackTestPatchAsync(gameRoot, ct);
    }

    public async Task<RuntimePatchOperationResult> VerifyFilesAsync(string gameRoot, CancellationToken ct = default)
    {
        var result = new RuntimePatchOperationResult();
        var status = await CheckStatusAsync(gameRoot, ct);

        result.Success = (status.OfficialLocalizationState == "Original" || status.OfficialLocalizationState == "Aion2TR Stub") &&
                         !status.RequiresCrashRecovery;
        result.Message = $"Doğrulama tamamlandı: Resmi PAK={status.OfficialLocalizationState}, Loose DAT={status.LooseDatState}, Yedek={status.BackupState}, Oyun={status.GameState}";
        return result;
    }

    private async Task ExecuteInternalRollbackAsync(RuntimePatchOwnershipManifest manifest, CancellationToken ct)
    {
        try
        {
            if (File.Exists(manifest.LooseDatPath))
            {
                var looseDatSha = await _hashService.ComputeSha256Async(manifest.LooseDatPath, ct);
                if (string.Equals(looseDatSha, ExpectedFullDatSha256, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(looseDatSha, ExpectedTestDatSha256, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(looseDatSha, manifest.LooseDatSHA256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(manifest.LooseDatPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(manifest.BackupPath) && File.Exists(manifest.BackupPath))
            {
                File.Copy(manifest.BackupPath, manifest.OfficialPakPath, overwrite: true);
            }

            manifest.State = RuntimePatchState.RolledBack;
            await SaveManifestAsync(manifest, ct);
        }
        catch
        {
            manifest.State = RuntimePatchState.Failed;
            await SaveManifestAsync(manifest, ct);
        }
    }

    private string ResolveStubSourcePath()
    {
        var candidate1 = Path.Combine(_repoRoot, "artifacts", "phase7_2", "english_empty_stub.pak");
        if (File.Exists(candidate1)) return candidate1;

        var candidate2 = Path.Combine(_repoRoot, "artifacts", "phase7_2", "legacy_extracted", "payload", "_internal", "english_empty_stub.pak");
        if (File.Exists(candidate2)) return candidate2;

        return candidate1;
    }

    private async Task<string?> DiscoverValidBackupPathAsync(CancellationToken ct)
    {
        var backupsRoot = Path.Combine(_repoRoot, "backups", "runtime_patch");
        if (!Directory.Exists(backupsRoot)) return null;

        var subdirs = Directory.GetDirectories(backupsRoot);
        Array.Sort(subdirs);
        Array.Reverse(subdirs); // newest first

        foreach (var dir in subdirs)
        {
            var manifestFile = Path.Combine(dir, "backup_manifest.json");
            var pakFile = Path.Combine(dir, "pakchunk502000-Windows_0_P.pak");

            if (File.Exists(manifestFile) && File.Exists(pakFile))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(manifestFile, ct);
                    var bManifest = JsonSerializer.Deserialize<RuntimeBackupManifest>(text);
                    if (bManifest != null)
                    {
                        var sha = await _hashService.ComputeSha256Async(pakFile, ct);
                        if (string.Equals(sha, bManifest.BackupSHA256, StringComparison.OrdinalIgnoreCase))
                        {
                            return pakFile;
                        }
                    }
                }
                catch
                {
                    // Continue searching
                }
            }
        }

        return null;
    }

    private async Task<RuntimePatchOwnershipManifest?> LoadManifestAsync(CancellationToken ct)
    {
        if (!File.Exists(ManifestFilePath)) return null;
        try
        {
            var text = await File.ReadAllTextAsync(ManifestFilePath, ct);
            return JsonSerializer.Deserialize<RuntimePatchOwnershipManifest>(text);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveManifestAsync(RuntimePatchOwnershipManifest manifest, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(ManifestFilePath)!;
        Directory.CreateDirectory(dir);
        var text = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(ManifestFilePath, text, ct);
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
