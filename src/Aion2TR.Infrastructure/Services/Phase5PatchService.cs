using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;
using Aion2TR.Core.Models;

namespace Aion2TR.Infrastructure.Services;

public class Phase5PatchService : IPhase5PatchService
{
    private readonly string _repoRoot;
    private readonly IHashService _hashService;

    public const string Phase5ArtifactSha256 = "56693611c2e71503c3108866b84bc1125c3b87e92b131ec2b18e0a84e0b67a09";
    public const long Phase5ArtifactSize = 5776484;

    public string RepoRoot => _repoRoot;
    public string ExpectedArtifactSha256 => Phase5ArtifactSha256;

    public Phase5PatchService(string? repoRoot = null, IHashService? hashService = null)
    {
        _repoRoot = repoRoot ?? FindRepoRoot();
        _hashService = hashService ?? new Sha256HashService();
    }

    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Aion2TR.sln")) ||
                Directory.Exists(Path.Combine(dir, "artifacts", "phase5")) ||
                File.Exists(Path.Combine(dir, "TRANSLATION_PROGRESS.md")))
            {
                return dir;
            }
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    public string GetLooseDatPath(string gameRoot) =>
        Path.Combine(gameRoot, "Aion2", "Content", "L10N", "Text", "en-US", "L10NString.dat");

    public string GetArtifactPath() =>
        Path.Combine(_repoRoot, "artifacts", "phase5", "L10NString.phase5-test.dat");

    public async Task<Phase5PatchStatusResult> CheckPatchStatusAsync(string? gameRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return new Phase5PatchStatusResult
            {
                Status = Phase5Status.GameNotFound,
                StatusMessage = "AION 2 oyun dizini bulunamadı.",
                Details = gameRoot
            };
        }

        var loosePath = GetLooseDatPath(gameRoot);
        if (!File.Exists(loosePath))
        {
            return new Phase5PatchStatusResult
            {
                Status = Phase5Status.NotInstalled,
                LoosePath = loosePath,
                StatusMessage = "KURULU DEĞİL",
                Details = "Hedef loose override dizininde L10NString.dat bulunmuyor."
            };
        }

        try
        {
            var hash = await _hashService.ComputeSha256Async(loosePath, ct);
            if (string.Equals(hash, Phase5ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new Phase5PatchStatusResult
                {
                    Status = Phase5Status.Installed,
                    LoosePath = loosePath,
                    LooseSha256 = hash,
                    StatusMessage = "KURULU",
                    Details = "Doğrulanmış Phase 5 test dosyası etkin."
                };
            }
            else
            {
                return new Phase5PatchStatusResult
                {
                    Status = Phase5Status.UnknownFile,
                    LoosePath = loosePath,
                    LooseSha256 = hash,
                    StatusMessage = "BİLİNMEYEN DOSYA",
                    Details = $"Hedef dosya Phase 5 artifact ile eşleşmiyor (Hash: {hash[..16]}...)."
                };
            }
        }
        catch (Exception ex)
        {
            return new Phase5PatchStatusResult
            {
                Status = Phase5Status.Error,
                LoosePath = loosePath,
                StatusMessage = "HATA",
                Details = ex.Message
            };
        }
    }

    public async Task<ContainerIntegrityResult> VerifyContainersAsync(string? gameRoot, CancellationToken ct = default)
    {
        var result = new ContainerIntegrityResult();

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            result.IsAllSafe = false;
            result.SummaryMessage = "Oyun kök dizini geçerli değil.";
            return result;
        }

        var manifestPath = Path.Combine(_repoRoot, "reports", "phase5", "original_file_manifest.json");
        List<(string Name, string ExpectedHash, long ExpectedSize)> expectedContainers = [];

        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
                if (doc.RootElement.TryGetProperty("Containers", out var containersEl))
                {
                    foreach (var c in containersEl.EnumerateArray())
                    {
                        var name = c.GetProperty("FileName").GetString() ?? "";
                        var hash = c.GetProperty("Sha256").GetString() ?? "";
                        var size = c.GetProperty("SizeBytes").GetInt64();
                        expectedContainers.Add((name, hash, size));
                    }
                }
            }
            catch
            {
                // Fallback to hardcoded verified manifest if JSON read fails
            }
        }

        if (expectedContainers.Count == 0)
        {
            expectedContainers =
            [
                ("pakchunk502000-Windows_0_P.pak", "07d61d63c131ce86a5674be28e14d9d93a11631a8704eb9c85e310123f11088c", 3745986),
                ("pakchunk502000-Windows_0_P.sig", "f8e5b7f4b1e750d22ace1e9319b226210dff4888729edf40774d21d883178325", 760),
                ("pakchunk502000-Windows_0_P.ucas", "c8e7146240adc7718e7e7d0b65468a8c71f4c7dd1f8a96e60d9a8daddc30be4f", 48),
                ("pakchunk502000-Windows_0_P.utoc", "588dd500e88bd7cc6170831e78c3409c275018ae13a9c1a8fc21633e43612c7e", 1297)
            ];
        }

        var containerDir = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        var allMatch = true;

        foreach (var (name, expHash, expSize) in expectedContainers)
        {
            var fullPath = Path.Combine(containerDir, name);
            var item = new ContainerIntegrityItem
            {
                FileName = name,
                FullPath = fullPath,
                ExpectedSha256 = expHash,
                SizeBytes = expSize,
                Exists = File.Exists(fullPath)
            };

            if (item.Exists)
            {
                try
                {
                    item.ActualSha256 = await _hashService.ComputeSha256Async(fullPath, ct);
                    if (!item.IsMatch) allMatch = false;
                }
                catch
                {
                    allMatch = false;
                }
            }
            else
            {
                allMatch = false;
            }

            result.Items.Add(item);
        }

        result.IsAllSafe = allMatch;
        result.SummaryMessage = allMatch
            ? "Tüm resmi oyun containerları orijinal ve güvenli."
            : "Resmi oyun dosyasında değişiklik algılandı.";

        return result;
    }

    public async Task<Phase5OperationResult> InstallPatchAsync(string? gameRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return Phase5OperationResult.Fail("AION 2 oyun dizini bulunamadı.");

        // 1. Check official containers integrity
        var integrity = await VerifyContainersAsync(gameRoot, ct);
        if (!integrity.IsAllSafe)
            return Phase5OperationResult.Fail("Resmi oyun dosyalarında değişiklik veya eksiklik algılandı. Güvenlik nedeniyle kurulum iptal edildi.");

        // 2. Validate artifact
        var artifactPath = GetArtifactPath();
        if (!File.Exists(artifactPath))
            return Phase5OperationResult.Fail($"Phase 5 test artifact dosyası bulunamadı: {artifactPath}");

        var artifactHash = await _hashService.ComputeSha256Async(artifactPath, ct);
        if (!string.Equals(artifactHash, Phase5ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            return Phase5OperationResult.Fail("Phase 5 artifact SHA-256 doğrulaması başarısız (Beklenen dosya hash uyuşmuyor).");

        // 3. Check target loose file status
        var targetLooseDat = GetLooseDatPath(gameRoot);
        var targetLooseDir = Path.GetDirectoryName(targetLooseDat)!;

        bool targetExisted = File.Exists(targetLooseDat);
        string? targetExistingHash = null;

        if (targetExisted)
        {
            targetExistingHash = await _hashService.ComputeSha256Async(targetLooseDat, ct);
            if (string.Equals(targetExistingHash, Phase5ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Phase5OperationResult.Ok("Phase 5 test yaması zaten kurulu.");
            }
            // Unknown loose file safety check
            return Phase5OperationResult.Fail("Hedef konumda yabancı veya bilinmeyen bir L10NString.dat dosyası mevcut. Güvenlik nedeniyle üzerine yazma iptal edildi.");
        }

        // 4. Create Pre-install Backup
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(_repoRoot, "backups", "phase5", timestamp);
        Directory.CreateDirectory(backupDir);

        var backupManifestPath = Path.Combine(backupDir, "backup_manifest.json");
        var backupManifest = new
        {
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            BackupDir = Path.GetRelativePath(_repoRoot, backupDir),
            TargetLoosePath = targetLooseDat,
            OriginalLooseFileExisted = false,
            OriginalLooseFileSizeBytes = (long?)null,
            OriginalLooseFileSha256 = (string?)null,
            OriginalLooseFileLastWriteTimeUtc = (string?)null,
            BackupFilePath = (string?)null
        };

        var manifestJson = JsonSerializer.Serialize(backupManifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(backupManifestPath, manifestJson, ct);

        // 5. Atomic installation
        if (!Directory.Exists(targetLooseDir))
        {
            Directory.CreateDirectory(targetLooseDir);
        }

        var tempDat = targetLooseDat + ".tmp";
        try
        {
            File.Copy(artifactPath, tempDat, overwrite: true);

            var tempHash = await _hashService.ComputeSha256Async(tempDat, ct);
            if (!string.Equals(tempHash, Phase5ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempDat);
                return Phase5OperationResult.Fail("Kopyalanan geçici dosya hash doğrulaması başarısız oldu.");
            }

            File.Move(tempDat, targetLooseDat, overwrite: true);

            var installedHash = await _hashService.ComputeSha256Async(targetLooseDat, ct);
            if (!string.Equals(installedHash, Phase5ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(targetLooseDat);
                return Phase5OperationResult.Fail("Kurulan dosyanın SHA-256 değeri beklenen hash ile uyuşmuyor. Otomatik geri alma uygulandı.");
            }

            return Phase5OperationResult.Ok("Phase 5 test yaması başarıyla kuruldu. Oyun içi teste hazır.");
        }
        catch (Exception ex)
        {
            if (File.Exists(tempDat))
            {
                try { File.Delete(tempDat); } catch { }
            }
            return Phase5OperationResult.Fail($"Kurulum sırasında hata oluştu: {ex.Message}");
        }
    }

    public async Task<Phase5OperationResult> RollbackPatchAsync(string? gameRoot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return Phase5OperationResult.Fail("Oyun kök dizini belirtilmedi.");

        var targetLooseDat = GetLooseDatPath(gameRoot);
        var targetLooseDir = Path.GetDirectoryName(targetLooseDat)!;

        if (!File.Exists(targetLooseDat))
        {
            return Phase5OperationResult.Ok("Hedef loose dosya zaten mevcut değil. Geri alma işlemi başarılı (İdempotent).");
        }

        // 1. CRITICAL OWNERSHIP CHECK (Requirement 8)
        var currentHash = await _hashService.ComputeSha256Async(targetLooseDat, ct);
        if (!string.Equals(currentHash, Phase5ArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Phase5OperationResult.Fail("Hedef dosya Phase 5 tarafından kurulan test dosyasıyla eşleşmiyor. Güvenlik nedeniyle otomatik kaldırma iptal edildi.");
        }

        // 2. Read latest backup manifest if available
        var backupsBase = Path.Combine(_repoRoot, "backups", "phase5");
        bool originalExisted = false;
        string? backupFilePath = null;
        string? originalHash = null;

        if (Directory.Exists(backupsBase))
        {
            var latestBackup = Directory.GetDirectories(backupsBase)
                .OrderByDescending(d => Path.GetFileName(d))
                .FirstOrDefault();

            if (latestBackup != null)
            {
                var manifestPath = Path.Combine(latestBackup, "backup_manifest.json");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
                        if (doc.RootElement.TryGetProperty("OriginalLooseFileExisted", out var existedEl) && existedEl.GetBoolean())
                        {
                            originalExisted = true;
                            if (doc.RootElement.TryGetProperty("BackupFilePath", out var bakEl))
                                backupFilePath = bakEl.GetString();
                            if (doc.RootElement.TryGetProperty("OriginalLooseFileSha256", out var hashEl))
                                originalHash = hashEl.GetString();
                        }
                    }
                    catch { }
                }
            }
        }

        // 3. Perform Rollback
        if (originalExisted && !string.IsNullOrEmpty(backupFilePath) && File.Exists(backupFilePath))
        {
            File.Copy(backupFilePath, targetLooseDat, overwrite: true);
            var restoredHash = await _hashService.ComputeSha256Async(targetLooseDat, ct);
            if (!string.IsNullOrEmpty(originalHash) && !string.Equals(restoredHash, originalHash, StringComparison.OrdinalIgnoreCase))
            {
                return Phase5OperationResult.Fail("Orijinal dosya geri yüklendi fakat SHA-256 doğrulaması uyuşmadı.");
            }
        }
        else
        {
            File.Delete(targetLooseDat);

            if (Directory.Exists(targetLooseDir) && !Directory.EnumerateFileSystemEntries(targetLooseDir).Any())
            {
                try { Directory.Delete(targetLooseDir, recursive: false); } catch { }
            }
        }

        // 4. Verify official containers are 100% untouched
        var containerCheck = await VerifyContainersAsync(gameRoot, ct);
        if (!containerCheck.IsAllSafe)
        {
            return Phase5OperationResult.Fail("Rollback tamamlandı ancak resmi dosyalarda tutarsızlık var!");
        }

        return Phase5OperationResult.Ok("Phase 5 test yaması başarıyla kaldırıldı ve orijinal durum geri yüklendi.");
    }

    public async Task<IReadOnlyList<Phase5TestStringItem>> LoadTestStringsAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_repoRoot, "reports", "phase5", "selected_test_strings.json");
        if (!File.Exists(path))
        {
            return
            [
                new Phase5TestStringItem { Key = "InputKeyMapping_UI_Setting_desc", SourceText = "Settings", TargetTestText = "Ayarlar" },
                new Phase5TestStringItem { Key = "String_UI_MONSTERCUBE_GETOUT_PARTY_POPUP_BTN_CANCEL_body", SourceText = "Cancel", TargetTestText = "İptal" },
                new Phase5TestStringItem { Key = "String_UI_PACKAGEDOWNLOAD_POPUP_NETWORK_CHANGED_CONFIRM_body", SourceText = "Confirm", TargetTestText = "Onayla" },
                new Phase5TestStringItem { Key = "String_UI_SNAPSHOT_MENU_EXIT_body", SourceText = "Exit", TargetTestText = "Çıkış" },
                new Phase5TestStringItem { Key = "String_UI_HUD_KEYGUIDE_DESC_BACKWARD_body", SourceText = "Back", TargetTestText = "Geri" },
                new Phase5TestStringItem { Key = "String_UI_SKIN_DECO_PRESET_APPLY_APPLY_body", SourceText = "Apply", TargetTestText = "Uygula" },
                new Phase5TestStringItem { Key = "String_UI_UPDATE_DOWNLOAD_CLOSE_body", SourceText = "Close", TargetTestText = "Kapat" },
                new Phase5TestStringItem { Key = "String_UI_RIFT_USE_NOTICE_POPUP_YES_body", SourceText = "Yes", TargetTestText = "Evet" },
                new Phase5TestStringItem { Key = "String_UI_CRAFT_RECIPE_HIT_NOTICE_CRAFT_body", SourceText = "No", TargetTestText = "Hayır" }
            ];
        }

        var json = await File.ReadAllTextAsync(path, ct);
        var items = JsonSerializer.Deserialize<List<Phase5TestStringItem>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return items ?? [];
    }

    public async Task<TranslationProgressInfo> LoadTranslationProgressAsync(CancellationToken ct = default)
    {
        var info = new TranslationProgressInfo();
        var progressMd = Path.Combine(_repoRoot, "TRANSLATION_PROGRESS.md");

        if (File.Exists(progressMd))
        {
            var lines = await File.ReadAllLinesAsync(progressMd, ct);
            foreach (var line in lines)
            {
                if (line.StartsWith("Total translatable unique strings:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = ExtractNumber(line);
                    if (val > 0) info.TotalUnique = val;
                }
                else if (line.StartsWith("Completed:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = ExtractNumber(line);
                    if (val > 0) info.Completed = val;
                }
                else if (line.StartsWith("Remaining:", StringComparison.OrdinalIgnoreCase))
                {
                    var val = ExtractNumber(line);
                    if (val >= 0) info.Remaining = val;
                }
                else if (line.StartsWith("Last completed batch:", StringComparison.OrdinalIgnoreCase))
                {
                    info.LastBatch = line.Split(':', 2)[1].Trim();
                }
                else if (line.StartsWith("Next batch:", StringComparison.OrdinalIgnoreCase))
                {
                    info.NextBatch = line.Split(':', 2)[1].Trim();
                }
                else if (line.StartsWith("Next range:", StringComparison.OrdinalIgnoreCase))
                {
                    info.NextRange = line.Split(':', 2)[1].Trim();
                }
            }
        }

        return info;
    }

    public async Task<IReadOnlyList<BackupInfoItem>> GetBackupsAsync(CancellationToken ct = default)
    {
        var list = new List<BackupInfoItem>();
        var backupsBase = Path.Combine(_repoRoot, "backups", "phase5");
        if (!Directory.Exists(backupsBase))
            return list;

        var dirs = Directory.GetDirectories(backupsBase).OrderByDescending(d => Path.GetFileName(d));
        foreach (var d in dirs)
        {
            var manifestPath = Path.Combine(d, "backup_manifest.json");
            var dirName = Path.GetFileName(d);
            var item = new BackupInfoItem
            {
                DirectoryName = dirName,
                FullPath = d,
                FormattedDate = dirName.Length == 15 
                    ? $"{dirName[0..4]}-{dirName[4..6]}-{dirName[6..8]} {dirName[9..11]}:{dirName[11..13]}" 
                    : dirName
            };

            if (File.Exists(manifestPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
                    if (doc.RootElement.TryGetProperty("OriginalLooseFileExisted", out var existedEl))
                    {
                        item.OriginalLooseFileExisted = existedEl.GetBoolean();
                    }
                    if (doc.RootElement.TryGetProperty("TargetLoosePath", out var targetEl))
                    {
                        item.TargetLoosePath = targetEl.GetString() ?? string.Empty;
                    }
                }
                catch { }
            }

            list.Add(item);
        }

        return list;
    }

    private static int ExtractNumber(string line)
    {
        var parts = line.Split(':', 2);
        if (parts.Length < 2) return 0;
        var digitsOnly = Regex.Replace(parts[1], @"[^\d]", "");
        return int.TryParse(digitsOnly, out var n) ? n : 0;
    }
}
