using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Aion2TR.V2.RuntimeInstaller;

public class RuntimeInstallerEngine
{
    private readonly IProcessDetector _processDetector;
    private readonly string _repoRoot;
    private readonly ArtifactDescriptor _artifact;
    private readonly string _datArtifactPath;
    private readonly string _stubPakPath;
    private readonly string _backupRoot;
    private readonly string _stateFilePath;

    public RuntimeInstallerEngine(
        IProcessDetector? processDetector = null,
        ArtifactDescriptor? artifact = null,
        string? customRepoRoot = null,
        string? customDatPath = null,
        string? customStubPakPath = null,
        string? customBackupRoot = null,
        string? customStateFilePath = null)
    {
        _processDetector = processDetector ?? new SystemProcessDetector();
        _repoRoot = customRepoRoot ?? ResolveRepoRoot();
        _artifact = artifact ?? ArtifactDescriptor.Step8Full;

        _datArtifactPath = ResolveDatPath(_repoRoot, _artifact, customDatPath);
        _stubPakPath = ResolveStubPath(_repoRoot, customStubPakPath);
        _backupRoot = ResolveBackupRoot(_repoRoot, customBackupRoot);
        _stateFilePath = ResolveStateFilePath(_repoRoot, customStateFilePath);
    }

    public string RepoRoot => _repoRoot;
    public ArtifactDescriptor Artifact => _artifact;
    public string DatArtifactPath => _datArtifactPath;
    public string StubPakPath => _stubPakPath;
    public string BackupRoot => _backupRoot;
    public string StateFilePath => _stateFilePath;

    // Backward-compatible properties
    public string Step3DatPath => _datArtifactPath;
    public const string ExpectedStep3DatHash = "598174d6c260ca17a998c30936552ec7ed2e0c0e3975896a07bfd3a8ddf98e96";
    public const string ExpectedStep8DatHash = "71778c2cbf51580208761f1c3a4f8abd97cddb2dac54c507292fd55e21a43e7e";
    public const string ExpectedStubHash = "105a71d61c9a0683500007bb8dab1cf3f12acfd28f17181408ceda426fc3d5b2";
    public const string ExpectedOfficialPakHash = "07d61d63c131ce86a5674be28e14d9d93a11631a8704eb9c85e310123f11088c";

    public static string ResolveRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "Aion2TR.sln")))
        {
            string? parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current) break;
            current = parent;
        }
        return string.IsNullOrEmpty(current) ? Directory.GetCurrentDirectory() : current;
    }

    private static string ResolveDatPath(string repoRoot, ArtifactDescriptor artifact, string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath)) return customPath;

        string baseDir = AppContext.BaseDirectory;
        string packagedPath = Path.Combine(baseDir, "patch", "L10NString.dat");
        if (File.Exists(packagedPath)) return packagedPath;

        string adjacentPath = Path.Combine(baseDir, "L10NString.dat");
        if (File.Exists(adjacentPath)) return adjacentPath;

        string repoPath = Path.Combine(repoRoot, artifact.RelativeDatPath);
        if (File.Exists(repoPath)) return repoPath;

        return packagedPath;
    }

    private static string ResolveStubPath(string repoRoot, string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath)) return customPath;

        string baseDir = AppContext.BaseDirectory;
        string packagedPath = Path.Combine(baseDir, "patch", "english_empty_stub.pak");
        if (File.Exists(packagedPath)) return packagedPath;

        string adjacentPath = Path.Combine(baseDir, "english_empty_stub.pak");
        if (File.Exists(adjacentPath)) return adjacentPath;

        string repoPath = Path.Combine(repoRoot, "v2", "artifacts", "runtime", "english_empty_stub.pak");
        if (File.Exists(repoPath)) return repoPath;

        return packagedPath;
    }

    private static string ResolveBackupRoot(string repoRoot, string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath)) return customPath;

        if (File.Exists(Path.Combine(repoRoot, "Aion2TR.sln")))
        {
            return Path.Combine(repoRoot, "v2", "runtime_backups");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2TR", "backups");
    }

    private static string ResolveStateFilePath(string repoRoot, string? customPath)
    {
        if (!string.IsNullOrEmpty(customPath)) return customPath;

        if (File.Exists(Path.Combine(repoRoot, "Aion2TR.sln")))
        {
            return Path.Combine(repoRoot, "v2", "runtime_state", "install_state.json");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2TR", "install_state.json");
    }

    public string? AutoDetectGameRoot()
    {
        // 1. Try Windows Steam Registry and LibraryFolders
        try
        {
            string? steamPath = GetSteamPathFromRegistry();
            if (!string.IsNullOrEmpty(steamPath))
            {
                var candidates = FindGameInSteamLibraries(steamPath);
                foreach (var path in candidates)
                {
                    if (IsValidGameRoot(path)) return path;
                }
            }
        }
        catch { }

        // 2. Try common default drives
        string[] candidatesList =
        [
            @"D:\uygulamalar\steam\steamapps\common\AION2",
            @"C:\Program Files (x86)\Steam\steamapps\common\AION2",
            @"D:\Steam\steamapps\common\AION2",
            @"D:\SteamLibrary\steamapps\common\AION2",
            @"C:\Steam\steamapps\common\AION2",
            @"E:\Steam\steamapps\common\AION2",
            @"E:\SteamLibrary\steamapps\common\AION2"
        ];

        foreach (var c in candidatesList)
        {
            if (IsValidGameRoot(c)) return c;
        }

        return null;
    }

    private static string? GetSteamPathFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var cuKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (cuKey?.GetValue("SteamPath") is string steamPath && !string.IsNullOrEmpty(steamPath))
                return steamPath.Replace('/', '\\');

            using var lmKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam") ??
                              Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            if (lmKey?.GetValue("InstallPath") is string installPath && !string.IsNullOrEmpty(installPath))
                return installPath.Replace('/', '\\');
        }
        catch { }

        return null;
    }

    private static List<string> FindGameInSteamLibraries(string steamPath)
    {
        var paths = new List<string>
        {
            Path.Combine(steamPath, "steamapps", "common", "AION2")
        };

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                var content = File.ReadAllText(vdfPath);
                var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                        paths.Add(Path.Combine(libPath, "steamapps", "common", "AION2"));
                    }
                }
            }
            catch { }
        }

        return paths;
    }

    public static readonly string[] VerifiedAion2AppIds = ["3393110", "4972320"];

    public string? DetectSteamAppId(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return null;

        try
        {
            string gameFolderName = Path.GetFileName(gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Only check the steamapps directory that directly parents this gameRoot!
            // E.g.: <SteamLibrary>/steamapps/common/AION2 -> <SteamLibrary>/steamapps
            string? current = Path.GetFullPath(gameRoot);
            string? steamappsDir = null;

            for (int i = 0; i < 4 && current != null; i++)
            {
                current = Path.GetDirectoryName(current);
                if (current != null && string.Equals(Path.GetFileName(current), "steamapps", StringComparison.OrdinalIgnoreCase))
                {
                    steamappsDir = current;
                    break;
                }
            }

            if (steamappsDir != null && Directory.Exists(steamappsDir))
            {
                return FindAppIdInSteamapps(steamappsDir, gameFolderName);
            }
        }
        catch { }

        return null;
    }

    public string? GetSteamLaunchUri(string? gameRoot)
    {
        var appId = DetectSteamAppId(gameRoot);
        return !string.IsNullOrEmpty(appId) ? $"steam://rungameid/{appId}" : null;
    }

    public static List<string> GetAllSteamappsDirectories(string steamPath)
    {
        var dirs = new List<string>();
        string rootSteamapps = Path.Combine(steamPath, "steamapps");
        if (Directory.Exists(rootSteamapps))
            dirs.Add(rootSteamapps);

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                var content = File.ReadAllText(vdfPath);
                var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                        string sDir = Path.Combine(libPath, "steamapps");
                        if (Directory.Exists(sDir) && !dirs.Contains(sDir, StringComparer.OrdinalIgnoreCase))
                        {
                            dirs.Add(sDir);
                        }
                    }
                }
            }
            catch { }
        }

        return dirs;
    }

    private static string? FindAppIdInSteamapps(string steamappsDir, string gameFolderName)
    {
        if (!Directory.Exists(steamappsDir)) return null;

        // Pass 1: Check known verified AION 2 manifests first (3393110 and 4972320)
        foreach (var verifiedId in VerifiedAion2AppIds)
        {
            string acfPath = Path.Combine(steamappsDir, $"appmanifest_{verifiedId}.acf");
            if (File.Exists(acfPath))
            {
                try
                {
                    string text = File.ReadAllText(acfPath);
                    var (appId, name, installDir) = ParseAcfMetadata(text);
                    if (appId == verifiedId && IsAion2Match(installDir, name, gameFolderName))
                    {
                        return verifiedId;
                    }
                }
                catch { }
            }
        }

        // Pass 2: Check all appmanifest_*.acf files in this steamapps directory
        var acfFiles = Directory.GetFiles(steamappsDir, "appmanifest_*.acf");
        foreach (var file in acfFiles)
        {
            try
            {
                string text = File.ReadAllText(file);
                var (appId, name, installDir) = ParseAcfMetadata(text);
                if (!string.IsNullOrEmpty(appId) && IsAion2Match(installDir, name, gameFolderName))
                {
                    if (VerifiedAion2AppIds.Contains(appId) ||
                        (!string.IsNullOrEmpty(name) && (name.Equals("AION 2", StringComparison.OrdinalIgnoreCase) || name.Equals("AION2", StringComparison.OrdinalIgnoreCase))))
                    {
                        return appId;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    private static bool IsAion2Match(string? installDir, string? name, string gameFolderName)
    {
        bool dirMatch = !string.IsNullOrEmpty(installDir) &&
            string.Equals(installDir, gameFolderName, StringComparison.OrdinalIgnoreCase);

        bool nameMatch = !string.IsNullOrEmpty(name) &&
            (name.Contains("AION 2", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("AION2", StringComparison.OrdinalIgnoreCase));

        return dirMatch && nameMatch;
    }

    private static (string? AppId, string? Name, string? InstallDir) ParseAcfMetadata(string text)
    {
        string? appId = null;
        string? name = null;
        string? installDir = null;

        var appIdMatch = Regex.Match(text, @"""appid""\s+""(\d+)""", RegexOptions.IgnoreCase);
        if (appIdMatch.Success) appId = appIdMatch.Groups[1].Value;

        var nameMatch = Regex.Match(text, @"""name""\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (nameMatch.Success) name = nameMatch.Groups[1].Value;

        var installDirMatch = Regex.Match(text, @"""installdir""\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (installDirMatch.Success) installDir = installDirMatch.Groups[1].Value;

        return (appId, name, installDir);
    }

    public bool IsValidGameRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        string paksDir = Path.Combine(path, "Aion2", "Content", "Paks");
        return Directory.Exists(paksDir);
    }

    public PreflightReport RunPreflight(string? customGameRoot = null)
    {
        string? gameRoot = customGameRoot ?? AutoDetectGameRoot();
        var report = new PreflightReport
        {
            DatArtifactId = _artifact.ArtifactId
        };

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            report.GameRoot = gameRoot ?? string.Empty;
            report.CanInstall = false;
            report.Issues.Add("Game root directory could not be found or does not exist.");
            return report;
        }

        report.GameRoot = Path.GetFullPath(gameRoot);
        report.TargetLooseDatPath = Path.Combine(report.GameRoot, "Aion2", "Content", "L10N", "Text", "en-US", "L10NString.dat");
        report.TargetPakPath = Path.Combine(report.GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");

        string sigPath = Path.Combine(report.GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.sig");
        string ucasPath = Path.Combine(report.GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.ucas");
        string utocPath = Path.Combine(report.GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.utoc");

        // 1. Process check
        report.IsGameRunning = _processDetector.IsGameRunning();
        if (report.IsGameRunning)
        {
            report.Issues.Add("AION 2 game process is currently running! Cannot modify files.");
        }

        // 2. Official PAK check
        if (File.Exists(report.TargetPakPath))
        {
            report.OfficialPakFound = true;
            var fi = new FileInfo(report.TargetPakPath);
            report.OfficialPakSize = fi.Length;
            report.OfficialPakHash = ComputeSha256Hex(report.TargetPakPath);
            report.IsPakOfficialVerified = string.Equals(report.OfficialPakHash, _artifact.ExpectedOfficialPakHash, StringComparison.OrdinalIgnoreCase);
            report.IsPakStubVerified = string.Equals(report.OfficialPakHash, _artifact.ExpectedStubHash, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            report.OfficialPakFound = false;
            report.Issues.Add($"Localization PAK not found: {report.TargetPakPath}");
        }

        // 3. Companion files check
        if (File.Exists(sigPath))
        {
            report.SigFound = true;
            report.SigSize = new FileInfo(sigPath).Length;
            report.SigHash = ComputeSha256Hex(sigPath);
        }

        if (File.Exists(ucasPath))
        {
            report.UcasFound = true;
            report.UcasSize = new FileInfo(ucasPath).Length;
            report.UcasHash = ComputeSha256Hex(ucasPath);
        }

        if (File.Exists(utocPath))
        {
            report.UtocFound = true;
            report.UtocSize = new FileInfo(utocPath).Length;
            report.UtocHash = ComputeSha256Hex(utocPath);
        }

        // 4. Loose DAT check
        if (File.Exists(report.TargetLooseDatPath))
        {
            report.ExistingLooseDatFound = true;
            report.ExistingLooseDatSize = new FileInfo(report.TargetLooseDatPath).Length;
            report.ExistingLooseDatHash = ComputeSha256Hex(report.TargetLooseDatPath);
        }

        // 5. Candidate DAT artifact check
        if (File.Exists(_datArtifactPath))
        {
            report.DatFound = true;
            report.DatSize = new FileInfo(_datArtifactPath).Length;
            report.DatHash = ComputeSha256Hex(_datArtifactPath);
            report.DatHashValid = string.Equals(report.DatHash, _artifact.ExpectedDatHash, StringComparison.OrdinalIgnoreCase);
            if (!report.DatHashValid)
            {
                report.Issues.Add($"Candidate DAT hash mismatch for {_artifact.ArtifactId}! Expected: {_artifact.ExpectedDatHash}, Got: {report.DatHash}");
            }
        }
        else
        {
            report.DatFound = false;
            report.Issues.Add($"Candidate DAT artifact not found: {_datArtifactPath}");
        }

        // 6. Stub PAK check
        if (File.Exists(_stubPakPath))
        {
            report.StubFound = true;
            report.StubHash = ComputeSha256Hex(_stubPakPath);
            report.StubHashValid = string.Equals(report.StubHash, _artifact.ExpectedStubHash, StringComparison.OrdinalIgnoreCase);
            if (!report.StubHashValid)
            {
                report.Issues.Add($"Stub PAK hash mismatch! Expected: {_artifact.ExpectedStubHash}, Got: {report.StubHash}");
            }
        }
        else
        {
            report.StubFound = false;
            report.Issues.Add($"Verified stub PAK artifact not found: {_stubPakPath}");
        }

        // 7. Disk space check
        try
        {
            string rootDrive = Path.GetPathRoot(report.GameRoot) ?? "C:\\";
            var driveInfo = new DriveInfo(rootDrive);
            report.FreeDiskSpaceBytes = driveInfo.AvailableFreeSpace;
            if (report.FreeDiskSpaceBytes < 50 * 1024 * 1024)
            {
                report.Issues.Add($"Insufficient free disk space on drive {rootDrive} ({report.FreeDiskSpaceBytes / 1024 / 1024} MB free, minimum 50 MB required).");
            }
        }
        catch
        {
            report.FreeDiskSpaceBytes = -1;
        }

        report.CanInstall = report.Issues.Count == 0 && !report.IsGameRunning && report.OfficialPakFound && report.DatHashValid && report.StubHashValid;
        return report;
    }

    public RuntimeStatusReport GetStatus(string? customGameRoot = null)
    {
        string? gameRoot = customGameRoot ?? AutoDetectGameRoot();
        var report = new RuntimeStatusReport
        {
            GameRoot = gameRoot ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            report.Code = RuntimeStatusCode.UNKNOWN;
            report.Message = "Game root directory not found or not specified.";
            return report;
        }

        string looseDatPath = Path.Combine(gameRoot, "Aion2", "Content", "L10N", "Text", "en-US", "L10NString.dat");
        string pakPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");
        string sigPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.sig");
        string ucasPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.ucas");
        string utocPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.utoc");

        report.LooseDatPresent = File.Exists(looseDatPath);
        if (report.LooseDatPresent)
        {
            report.LooseDatHash = ComputeSha256Hex(looseDatPath);
        }

        report.PakPresent = File.Exists(pakPath);
        if (report.PakPresent)
        {
            report.PakHash = ComputeSha256Hex(pakPath);
            report.IsPakStub = string.Equals(report.PakHash, _artifact.ExpectedStubHash, StringComparison.OrdinalIgnoreCase);
            report.IsOfficialPak = string.Equals(report.PakHash, _artifact.ExpectedOfficialPakHash, StringComparison.OrdinalIgnoreCase);
        }

        report.SigPresent = File.Exists(sigPath);
        report.UcasPresent = File.Exists(ucasPath);
        report.UtocPresent = File.Exists(utocPath);

        bool companionsPresent = report.SigPresent || report.UcasPresent || report.UtocPresent;
        bool isExpectedArtifactDat = string.Equals(report.LooseDatHash, _artifact.ExpectedDatHash, StringComparison.OrdinalIgnoreCase);
        bool isStep3Dat = string.Equals(report.LooseDatHash, ExpectedStep3DatHash, StringComparison.OrdinalIgnoreCase);
        bool isAnyValidPatchDat = isExpectedArtifactDat || isStep3Dat;

        // Classification
        if (!report.LooseDatPresent && report.PakPresent && report.IsOfficialPak && report.SigPresent && report.UcasPresent && report.UtocPresent)
        {
            report.Code = RuntimeStatusCode.NOT_INSTALLED;
            report.Message = "Clean official game state detected. Patch is not installed.";
        }
        else if (report.LooseDatPresent && isAnyValidPatchDat && report.PakPresent && report.IsPakStub && !companionsPresent)
        {
            report.Code = RuntimeStatusCode.INSTALLED_VALID;
            report.ActiveArtifactId = isExpectedArtifactDat ? _artifact.ArtifactId : "AION2TR_V2_STEP3_EXACT";
            report.Message = $"AION2TR Turkish patch ({report.ActiveArtifactId}) is correctly installed and verified.";
        }
        else if (report.LooseDatPresent && report.IsPakStub && !isAnyValidPatchDat && !companionsPresent)
        {
            report.Code = RuntimeStatusCode.INSTALLED_MODIFIED;
            report.Message = "Patch is installed, but loose DAT content does not match any recognized candidate.";
        }
        else if (report.IsPakStub && companionsPresent)
        {
            report.Code = RuntimeStatusCode.MIXED_STATE;
            report.Message = "Mixed runtime state detected: Stub PAK is present alongside IoStore companion files (.sig/.ucas/.utoc).";
        }
        else if (report.LooseDatPresent && report.IsOfficialPak)
        {
            report.Code = RuntimeStatusCode.MIXED_STATE;
            report.Message = "Mixed runtime state detected: Loose DAT is present while official PAK is still in place.";
        }
        else if (report.LooseDatPresent && companionsPresent)
        {
            report.Code = RuntimeStatusCode.MIXED_STATE;
            report.Message = "Mixed runtime state detected: Loose DAT is present alongside IoStore companion files.";
        }
        else if (!report.PakPresent || (report.IsPakStub && !report.LooseDatPresent))
        {
            report.Code = RuntimeStatusCode.PARTIAL_INSTALL;
            report.Message = "Partial or incomplete patch installation detected.";
        }
        else
        {
            report.Code = RuntimeStatusCode.MIXED_STATE;
            report.Message = "Inconsistent or unrecognized runtime state detected.";
        }

        report.Details["LooseDatPath"] = looseDatPath;
        report.Details["PakPath"] = pakPath;
        report.Details["CompanionsPresent"] = companionsPresent.ToString();

        return report;
    }

    public string CreateTransactionSnapshot(string gameRoot, string operationName)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string snapshotId = $"txn_{operationName}_{timestamp}_{Guid.NewGuid().ToString("N")[..6]}";
        string snapshotDir = Path.Combine(_backupRoot, snapshotId);
        Directory.CreateDirectory(snapshotDir);

        var manifest = new BackupManifest
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            BackupId = snapshotId,
            GameRoot = gameRoot,
            BackupType = "TRANSACTION_SNAPSHOT"
        };

        string paksDir = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        string l10nDir = Path.Combine(gameRoot, "Aion2", "Content", "L10N", "Text", "en-US");

        var filesToBackup = new List<(string Id, string Path)>();
        string pakPath = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.pak");
        string sigPath = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.sig");
        string ucasPath = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.ucas");
        string utocPath = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.utoc");
        string looseDatPath = Path.Combine(l10nDir, "L10NString.dat");

        if (File.Exists(pakPath)) filesToBackup.Add(("pak", pakPath));
        if (File.Exists(sigPath)) filesToBackup.Add(("sig", sigPath));
        if (File.Exists(ucasPath)) filesToBackup.Add(("ucas", ucasPath));
        if (File.Exists(utocPath)) filesToBackup.Add(("utoc", utocPath));
        if (File.Exists(looseDatPath)) filesToBackup.Add(("loose_dat", looseDatPath));

        foreach (var (id, srcPath) in filesToBackup)
        {
            string fileName = Path.GetFileName(srcPath);
            if (id == "loose_dat") fileName = "L10NString.snapshot.dat";

            string destPath = Path.Combine(snapshotDir, fileName);
            string srcSha256 = ComputeSha256Hex(srcPath);
            long size = new FileInfo(srcPath).Length;

            File.Copy(srcPath, destPath, overwrite: true);

            string destSha256 = ComputeSha256Hex(destPath);
            if (!string.Equals(srcSha256, destSha256, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(snapshotDir, recursive: true);
                throw new InvalidOperationException($"Snapshot verification failed for {fileName}!");
            }

            manifest.Files.Add(new BackupFileEntry
            {
                Identifier = id,
                OriginalPath = srcPath,
                BackupRelativePath = fileName,
                Size = size,
                Sha256 = destSha256,
                ExistedBeforeInstall = true
            });
        }

        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(snapshotDir, "manifest.json"), manifestJson, Encoding.UTF8);

        return snapshotDir;
    }

    public string EnsureOriginalCleanBackup(string? gameRoot = null)
    {
        string cleanBackupDir = Path.Combine(_backupRoot, "original_clean_backup");
        string manifestPath = Path.Combine(cleanBackupDir, "manifest.json");

        if (Directory.Exists(cleanBackupDir) && File.Exists(manifestPath))
        {
            try
            {
                string json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<BackupManifest>(json);
                if (manifest != null && manifest.Files.Any(f => f.Identifier == "official_pak" && string.Equals(f.Sha256, _artifact.ExpectedOfficialPakHash, StringComparison.OrdinalIgnoreCase)))
                {
                    string pakPath = Path.Combine(cleanBackupDir, "pakchunk502000-Windows_0_P.pak");
                    if (File.Exists(pakPath) && string.Equals(ComputeSha256Hex(pakPath), _artifact.ExpectedOfficialPakHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return cleanBackupDir;
                    }
                }
            }
            catch { }
        }

        // Search repository historical backups or app distribution folder for official PAK
        string[] searchDirs =
        [
            Path.Combine(AppContext.BaseDirectory, "patch", "official"),
            Path.Combine(AppContext.BaseDirectory, "backup"),
            Path.Combine(_repoRoot, "backups", "runtime_patch"),
            Path.Combine(_repoRoot, "v2", "runtime_backups")
        ];

        string? foundPak = null;
        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;
            var files = Directory.GetFiles(dir, "pakchunk502000-Windows_0_P.pak", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                if (new FileInfo(f).Length == _artifact.ExpectedOfficialPakSize)
                {
                    string h = ComputeSha256Hex(f);
                    if (string.Equals(h, _artifact.ExpectedOfficialPakHash, StringComparison.OrdinalIgnoreCase))
                    {
                        foundPak = f;
                        break;
                    }
                }
            }
            if (foundPak != null) break;
        }

        // Also check if game currently has official PAK
        if (foundPak == null && gameRoot != null)
        {
            string gamePak = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");
            if (File.Exists(gamePak) && string.Equals(ComputeSha256Hex(gamePak), _artifact.ExpectedOfficialPakHash, StringComparison.OrdinalIgnoreCase))
            {
                foundPak = gamePak;
            }
        }

        if (foundPak == null)
        {
            throw new InvalidOperationException("Could not locate verified official game PAK (SHA-256: " + _artifact.ExpectedOfficialPakHash + ") in repository backups or game directory!");
        }

        Directory.CreateDirectory(cleanBackupDir);
        string destPak = Path.Combine(cleanBackupDir, "pakchunk502000-Windows_0_P.pak");
        File.Copy(foundPak, destPak, overwrite: true);

        // Companions
        string destSig = Path.Combine(cleanBackupDir, "pakchunk502000-Windows_0_P.sig");
        string destUcas = Path.Combine(cleanBackupDir, "pakchunk502000-Windows_0_P.ucas");
        string destUtoc = Path.Combine(cleanBackupDir, "pakchunk502000-Windows_0_P.utoc");

        // Try copy companions from game or backups
        string foundDir = Path.GetDirectoryName(foundPak)!;
        CopyCompanionIfExists(foundDir, "pakchunk502000-Windows_0_P.sig", destSig, _artifact.ExpectedOfficialSigHash);
        CopyCompanionIfExists(foundDir, "pakchunk502000-Windows_0_P.ucas", destUcas, _artifact.ExpectedOfficialUcasHash);
        CopyCompanionIfExists(foundDir, "pakchunk502000-Windows_0_P.utoc", destUtoc, _artifact.ExpectedOfficialUtocHash);

        if (gameRoot != null)
        {
            string gamePaks = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
            if (!File.Exists(destSig)) CopyCompanionIfExists(gamePaks, "pakchunk502000-Windows_0_P.sig", destSig, _artifact.ExpectedOfficialSigHash);
            if (!File.Exists(destUcas)) CopyCompanionIfExists(gamePaks, "pakchunk502000-Windows_0_P.ucas", destUcas, _artifact.ExpectedOfficialUcasHash);
            if (!File.Exists(destUtoc)) CopyCompanionIfExists(gamePaks, "pakchunk502000-Windows_0_P.utoc", destUtoc, _artifact.ExpectedOfficialUtocHash);
        }

        var newManifest = new BackupManifest
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            BackupId = "original_clean_backup",
            GameRoot = gameRoot ?? string.Empty,
            BackupType = "ORIGINAL_GAME_BACKUP",
            Files =
            [
                new BackupFileEntry
                {
                    Identifier = "official_pak",
                    OriginalPath = "Aion2\\Content\\Paks\\L10N\\Text\\en-US\\pakchunk502000-Windows_0_P.pak",
                    BackupRelativePath = "pakchunk502000-Windows_0_P.pak",
                    Size = new FileInfo(destPak).Length,
                    Sha256 = ComputeSha256Hex(destPak),
                    ExistedBeforeInstall = true
                }
            ]
        };

        if (File.Exists(destSig))
        {
            newManifest.Files.Add(new BackupFileEntry
            {
                Identifier = "sig",
                OriginalPath = "Aion2\\Content\\Paks\\L10N\\Text\\en-US\\pakchunk502000-Windows_0_P.sig",
                BackupRelativePath = "pakchunk502000-Windows_0_P.sig",
                Size = new FileInfo(destSig).Length,
                Sha256 = ComputeSha256Hex(destSig),
                ExistedBeforeInstall = true
            });
        }

        if (File.Exists(destUcas))
        {
            newManifest.Files.Add(new BackupFileEntry
            {
                Identifier = "ucas",
                OriginalPath = "Aion2\\Content\\Paks\\L10N\\Text\\en-US\\pakchunk502000-Windows_0_P.ucas",
                BackupRelativePath = "pakchunk502000-Windows_0_P.ucas",
                Size = new FileInfo(destUcas).Length,
                Sha256 = ComputeSha256Hex(destUcas),
                ExistedBeforeInstall = true
            });
        }

        if (File.Exists(destUtoc))
        {
            newManifest.Files.Add(new BackupFileEntry
            {
                Identifier = "utoc",
                OriginalPath = "Aion2\\Content\\Paks\\L10N\\Text\\en-US\\pakchunk502000-Windows_0_P.utoc",
                BackupRelativePath = "pakchunk502000-Windows_0_P.utoc",
                Size = new FileInfo(destUtoc).Length,
                Sha256 = ComputeSha256Hex(destUtoc),
                ExistedBeforeInstall = true
            });
        }

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(newManifest, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        return cleanBackupDir;
    }

    private static void CopyCompanionIfExists(string sourceDir, string fileName, string destPath, string expectedHash)
    {
        string src = Path.Combine(sourceDir, fileName);
        if (File.Exists(src))
        {
            string h = ComputeSha256Hex(src);
            if (string.Equals(h, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(src, destPath, overwrite: true);
            }
        }
    }

    public InstallState ExecuteInstall(PreflightReport preflight, IProgress<(string Message, double Progress)>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(preflight);

        if (_processDetector.IsGameRunning())
        {
            throw new InvalidOperationException("INSTALL REFUSED: AION 2 game process is running!");
        }

        if (!preflight.CanInstall)
        {
            throw new InvalidOperationException($"INSTALL REFUSED: Preflight checks failed: {string.Join("; ", preflight.Issues)}");
        }

        var status = GetStatus(preflight.GameRoot);
        if (status.Code == RuntimeStatusCode.INSTALLED_VALID && string.Equals(status.LooseDatHash, _artifact.ExpectedDatHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"INSTALL REFUSED: Artifact {_artifact.ArtifactId} is already installed and verified! Roll back first if you wish to re-install.");
        }

        // If another patch is installed or state is modified, redirect to upgrade
        if (status.Code == RuntimeStatusCode.INSTALLED_VALID || status.Code == RuntimeStatusCode.INSTALLED_MODIFIED)
        {
            return ExecuteUpgrade(preflight.GameRoot, progress);
        }

        progress?.Report(("Hazırlanıyor...", 0.1));

        // Ensure we have a permanent clean backup of official files
        EnsureOriginalCleanBackup(preflight.GameRoot);

        progress?.Report(("Güvenli yedek oluşturuluyor...", 0.3));

        // 1. Transaction Snapshot
        string snapshotDir = CreateTransactionSnapshot(preflight.GameRoot, "install");

        string paksDir = Path.GetDirectoryName(preflight.TargetPakPath)!;
        string sigPath = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.sig");
        string ucasPath = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.ucas");
        string utocPath = Path.Combine(paksDir, "pakchunk502000-Windows_0_P.utoc");

        var modifiedFiles = new List<string>();
        var removedFiles = new List<string>();
        var originalPresent = new List<string> { preflight.TargetPakPath };
        if (File.Exists(sigPath)) originalPresent.Add(sigPath);
        if (File.Exists(ucasPath)) originalPresent.Add(ucasPath);
        if (File.Exists(utocPath)) originalPresent.Add(utocPath);
        if (File.Exists(preflight.TargetLooseDatPath)) originalPresent.Add(preflight.TargetLooseDatPath);

        try
        {
            progress?.Report(("Türkçe yama kuruluyor...", 0.55));

            // 2. Install Candidate DAT atomically
            string looseDir = Path.GetDirectoryName(preflight.TargetLooseDatPath)!;
            Directory.CreateDirectory(looseDir);

            string looseTmp = preflight.TargetLooseDatPath + ".tmp_" + Guid.NewGuid().ToString("N");
            File.Copy(_datArtifactPath, looseTmp, overwrite: true);
            string looseTmpHash = ComputeSha256Hex(looseTmp);
            if (!string.Equals(looseTmpHash, _artifact.ExpectedDatHash, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(looseTmp)) File.Delete(looseTmp);
                throw new InvalidOperationException($"Candidate DAT temporary copy hash verification failed! Expected: {_artifact.ExpectedDatHash}, Got: {looseTmpHash}");
            }

            File.Move(looseTmp, preflight.TargetLooseDatPath, overwrite: true);
            modifiedFiles.Add(preflight.TargetLooseDatPath);

            // 3. Install Stub PAK atomically
            string stubTmp = preflight.TargetPakPath + ".tmp_" + Guid.NewGuid().ToString("N");
            File.Copy(_stubPakPath, stubTmp, overwrite: true);
            string stubTmpHash = ComputeSha256Hex(stubTmp);
            if (!string.Equals(stubTmpHash, _artifact.ExpectedStubHash, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(stubTmp)) File.Delete(stubTmp);
                throw new InvalidOperationException($"Stub PAK temporary copy hash verification failed! Expected: {_artifact.ExpectedStubHash}, Got: {stubTmpHash}");
            }

            File.Move(stubTmp, preflight.TargetPakPath, overwrite: true);
            modifiedFiles.Add(preflight.TargetPakPath);

            // 4. Disable / Remove companion IoStore files
            if (File.Exists(sigPath))
            {
                File.Delete(sigPath);
                removedFiles.Add(sigPath);
            }
            if (File.Exists(ucasPath))
            {
                File.Delete(ucasPath);
                removedFiles.Add(ucasPath);
            }
            if (File.Exists(utocPath))
            {
                File.Delete(utocPath);
                removedFiles.Add(utocPath);
            }

            progress?.Report(("Dosyalar doğrulanıyor...", 0.85));

            // 5. Final verification on disk
            var postStatus = GetStatus(preflight.GameRoot);
            if (postStatus.Code != RuntimeStatusCode.INSTALLED_VALID || !string.Equals(postStatus.LooseDatHash, _artifact.ExpectedDatHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Post-install status validation failed! Code: {postStatus.Code}, Message: {postStatus.Message}");
            }

            // 6. Record State Manifest
            var state = new InstallState
            {
                GameRoot = preflight.GameRoot,
                InstallTimestamp = DateTime.UtcNow.ToString("o"),
                ArtifactId = _artifact.ArtifactId,
                DatHash = _artifact.ExpectedDatHash,
                StubHash = _artifact.ExpectedStubHash,
                BackupDirectory = snapshotDir,
                FilesOriginallyPresent = originalPresent,
                FilesModified = modifiedFiles,
                FilesRemovedOrDisabled = removedFiles,
                InstalledFileHashes = new Dictionary<string, string>
                {
                    [preflight.TargetLooseDatPath] = _artifact.ExpectedDatHash,
                    [preflight.TargetPakPath] = _artifact.ExpectedStubHash
                },
                InstallerVersion = "2.1.0",
                Status = "INSTALLED_VALID"
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            progress?.Report(("Tamamlandı.", 1.0));
            return state;
        }
        catch (Exception ex)
        {
            // Automatic Rollback
            try
            {
                ExecuteRollbackInternal(snapshotDir, preflight.GameRoot);
            }
            catch (Exception rollbackEx)
            {
                throw new AggregateException("Install failed, and automatic rollback also encountered an error!", ex, rollbackEx);
            }

            throw new InvalidOperationException($"Install failed midway! Automatic rollback was triggered and successfully restored original game files. Cause: {ex.Message}", ex);
        }
    }

    public InstallState ExecuteUpgrade(string? customGameRoot = null, IProgress<(string Message, double Progress)>? progress = null)
    {
        string? gameRoot = customGameRoot ?? AutoDetectGameRoot();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            throw new InvalidOperationException("Cannot upgrade: Game root directory not found.");
        }

        if (_processDetector.IsGameRunning())
        {
            throw new InvalidOperationException("UPGRADE REFUSED: AION 2 game process is running!");
        }

        progress?.Report(("Hazırlanıyor...", 0.1));

        var preflight = RunPreflight(gameRoot);
        if (!preflight.DatHashValid)
        {
            throw new InvalidOperationException($"UPGRADE REFUSED: Candidate DAT artifact hash verification failed for {_artifact.ArtifactId}: {preflight.DatHash}");
        }
        if (!preflight.StubHashValid)
        {
            throw new InvalidOperationException($"UPGRADE REFUSED: Stub PAK artifact hash verification failed: {preflight.StubHash}");
        }

        // Ensure permanent clean backup of official files exists
        EnsureOriginalCleanBackup(gameRoot);

        progress?.Report(("Güvenli yedek snapshot oluşturuluyor...", 0.35));

        // 1. Transaction Snapshot
        string snapshotDir = CreateTransactionSnapshot(gameRoot, "upgrade");

        string looseDatPath = Path.Combine(gameRoot, "Aion2", "Content", "L10N", "Text", "en-US", "L10NString.dat");
        string pakPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.pak");
        string sigPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.sig");
        string ucasPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.ucas");
        string utocPath = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US", "pakchunk502000-Windows_0_P.utoc");

        try
        {
            progress?.Report(("Güncel Türkçe yama kuruluyor...", 0.65));

            // 2. Stage and update loose DAT atomically
            Directory.CreateDirectory(Path.GetDirectoryName(looseDatPath)!);
            string looseTmp = looseDatPath + ".tmp_" + Guid.NewGuid().ToString("N");
            File.Copy(_datArtifactPath, looseTmp, overwrite: true);
            string looseTmpHash = ComputeSha256Hex(looseTmp);
            if (!string.Equals(looseTmpHash, _artifact.ExpectedDatHash, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(looseTmp)) File.Delete(looseTmp);
                throw new InvalidOperationException($"Candidate DAT temporary copy hash verification failed! Expected: {_artifact.ExpectedDatHash}, Got: {looseTmpHash}");
            }
            File.Move(looseTmp, looseDatPath, overwrite: true);

            // 3. Ensure Stub PAK is in place
            if (!File.Exists(pakPath) || !string.Equals(ComputeSha256Hex(pakPath), _artifact.ExpectedStubHash, StringComparison.OrdinalIgnoreCase))
            {
                string stubTmp = pakPath + ".tmp_" + Guid.NewGuid().ToString("N");
                File.Copy(_stubPakPath, stubTmp, overwrite: true);
                string stubTmpHash = ComputeSha256Hex(stubTmp);
                if (!string.Equals(stubTmpHash, _artifact.ExpectedStubHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(stubTmp)) File.Delete(stubTmp);
                    throw new InvalidOperationException($"Stub PAK temporary copy hash verification failed!");
                }
                File.Move(stubTmp, pakPath, overwrite: true);
            }

            // 4. Ensure companion files are deleted
            if (File.Exists(sigPath)) File.Delete(sigPath);
            if (File.Exists(ucasPath)) File.Delete(ucasPath);
            if (File.Exists(utocPath)) File.Delete(utocPath);

            progress?.Report(("Dosyalar doğrulanıyor...", 0.85));

            // 5. Verify post-upgrade status
            var postStatus = GetStatus(gameRoot);
            if (postStatus.Code != RuntimeStatusCode.INSTALLED_VALID || !string.Equals(postStatus.LooseDatHash, _artifact.ExpectedDatHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Post-upgrade status validation failed! Code: {postStatus.Code}, Message: {postStatus.Message}");
            }

            // 6. Record State Manifest
            var state = new InstallState
            {
                GameRoot = gameRoot,
                InstallTimestamp = DateTime.UtcNow.ToString("o"),
                ArtifactId = _artifact.ArtifactId,
                DatHash = _artifact.ExpectedDatHash,
                StubHash = _artifact.ExpectedStubHash,
                BackupDirectory = snapshotDir,
                InstalledFileHashes = new Dictionary<string, string>
                {
                    [looseDatPath] = _artifact.ExpectedDatHash,
                    [pakPath] = _artifact.ExpectedStubHash
                },
                InstallerVersion = "2.1.0",
                Status = "INSTALLED_VALID"
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            progress?.Report(("Tamamlandı.", 1.0));
            return state;
        }
        catch (Exception ex)
        {
            // Automatic Rollback to pre-upgrade snapshot
            try
            {
                ExecuteRollbackInternal(snapshotDir, gameRoot);
            }
            catch (Exception rollbackEx)
            {
                throw new AggregateException("Upgrade failed, and automatic rollback also encountered an error!", ex, rollbackEx);
            }

            throw new InvalidOperationException($"Upgrade failed midway! Automatic rollback was triggered and successfully restored previous state. Cause: {ex.Message}", ex);
        }
    }

    public bool ExecuteRepair(string? customGameRoot = null, IProgress<(string Message, double Progress)>? progress = null)
    {
        string? gameRoot = customGameRoot ?? AutoDetectGameRoot();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            throw new InvalidOperationException("Cannot repair: Game root directory not found.");
        }

        if (_processDetector.IsGameRunning())
        {
            throw new InvalidOperationException("REPAIR REFUSED: AION 2 game process is running!");
        }

        progress?.Report(("Hazırlanıyor...", 0.1));

        // 1. Locate trusted original clean backup
        string cleanBackupDir = EnsureOriginalCleanBackup(gameRoot);

        progress?.Report(("Yedek snapshot oluşturuluyor...", 0.3));

        // 2. Take a transaction snapshot before modifying anything
        string snapshotDir = CreateTransactionSnapshot(gameRoot, "repair");

        string paksDir = Path.Combine(gameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        string l10nDir = Path.Combine(gameRoot, "Aion2", "Content", "L10N", "Text", "en-US");
        string looseDatPath = Path.Combine(l10nDir, "L10NString.dat");

        try
        {
            progress?.Report(("Orijinal resmi dosyalar geri yükleniyor...", 0.6));

            // 3. Delete loose DAT
            if (File.Exists(looseDatPath))
            {
                File.Delete(looseDatPath);
            }

            // 4. Restore official PAK and companions from original_clean_backup
            string manifestPath = Path.Combine(cleanBackupDir, "manifest.json");
            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidOperationException("Corrupt original backup manifest.");

            foreach (var entry in manifest.Files)
            {
                string src = Path.Combine(cleanBackupDir, entry.BackupRelativePath);
                if (!File.Exists(src))
                {
                    throw new InvalidOperationException($"Original backup file missing: {src}");
                }

                string actualHash = ComputeSha256Hex(src);
                if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Original backup file corrupted: {src}!");
                }

                string target = Path.Combine(paksDir, entry.BackupRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(src, target, overwrite: true);

                string restoredHash = ComputeSha256Hex(target);
                if (!string.Equals(restoredHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Restored file verification failed for {target}!");
                }
            }

            progress?.Report(("Dosyalar doğrulanıyor...", 0.85));

            // 5. Verify post-repair status
            var status = GetStatus(gameRoot);
            if (status.Code != RuntimeStatusCode.NOT_INSTALLED)
            {
                throw new InvalidOperationException($"Post-repair verification failed! Status: {status.Code}, Message: {status.Message}");
            }

            // 6. Delete install_state.json if present
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
            }

            progress?.Report(("Tamamlandı.", 1.0));
            return true;
        }
        catch (Exception ex)
        {
            // Automatic Rollback to snapshot before repair
            try
            {
                ExecuteRollbackInternal(snapshotDir, gameRoot);
            }
            catch (Exception rbEx)
            {
                throw new AggregateException("Repair failed, and automatic rollback also encountered an error!", ex, rbEx);
            }

            throw new InvalidOperationException($"Repair failed! Automatic rollback restored pre-repair state. Cause: {ex.Message}", ex);
        }
    }

    public bool ExecuteRollback(string? customGameRoot = null, string? customBackupDir = null, IProgress<(string Message, double Progress)>? progress = null)
    {
        if (_processDetector.IsGameRunning())
        {
            throw new InvalidOperationException("ROLLBACK REFUSED: AION 2 game process is running!");
        }

        string? gameRoot = customGameRoot;
        string? backupDir = customBackupDir;

        // Try load state file
        InstallState? state = null;
        if (File.Exists(_stateFilePath))
        {
            try
            {
                string json = File.ReadAllText(_stateFilePath);
                state = JsonSerializer.Deserialize<InstallState>(json);
                if (state != null)
                {
                    gameRoot ??= state.GameRoot;
                    backupDir ??= state.BackupDirectory;
                }
            }
            catch { }
        }

        gameRoot ??= AutoDetectGameRoot();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            throw new InvalidOperationException("Cannot rollback: Game root directory not found.");
        }

        progress?.Report(("Hazırlanıyor...", 0.1));

        // If backupDir not specified, prefer original_clean_backup
        string cleanBackupDir = Path.Combine(_backupRoot, "original_clean_backup");
        if (string.IsNullOrWhiteSpace(backupDir) && Directory.Exists(cleanBackupDir) && File.Exists(Path.Combine(cleanBackupDir, "manifest.json")))
        {
            backupDir = cleanBackupDir;
        }

        // If still not found, check other backup directories
        if (string.IsNullOrWhiteSpace(backupDir) || !Directory.Exists(backupDir))
        {
            if (Directory.Exists(_backupRoot))
            {
                var dirs = Directory.GetDirectories(_backupRoot, "backup_*")
                    .Concat(Directory.GetDirectories(_backupRoot, "txn_*"))
                    .OrderByDescending(d => d)
                    .ToList();

                if (dirs.Count > 0)
                {
                    backupDir = dirs[0];
                }
            }
        }

        if (string.IsNullOrWhiteSpace(backupDir) || !Directory.Exists(backupDir))
        {
            var currentStatus = GetStatus(gameRoot);
            if (currentStatus.Code == RuntimeStatusCode.NOT_INSTALLED)
            {
                return true; // Already clean
            }

            throw new InvalidOperationException("Cannot rollback: No backup directory found to restore from.");
        }

        progress?.Report(("Orijinal oyun dosyaları geri yükleniyor...", 0.5));
        ExecuteRollbackInternal(backupDir, gameRoot);

        progress?.Report(("Dosyalar doğrulanıyor...", 0.85));

        // Verify post-rollback state
        var postStatus = GetStatus(gameRoot);
        if (postStatus.Code != RuntimeStatusCode.NOT_INSTALLED)
        {
            // If backup was not clean, try repair to ensure official state
            if (postStatus.Code == RuntimeStatusCode.MIXED_STATE || postStatus.Code == RuntimeStatusCode.INSTALLED_MODIFIED)
            {
                try
                {
                    ExecuteRepair(gameRoot);
                    postStatus = GetStatus(gameRoot);
                }
                catch { }
            }
        }

        if (postStatus.Code != RuntimeStatusCode.NOT_INSTALLED)
        {
            throw new InvalidOperationException($"Rollback completed file restoration, but post-rollback verification failed! Code: {postStatus.Code}, Message: {postStatus.Message}");
        }

        // Remove state file
        if (File.Exists(_stateFilePath))
        {
            File.Delete(_stateFilePath);
        }

        progress?.Report(("Tamamlandı.", 1.0));
        return true;
    }

    private void ExecuteRollbackInternal(string backupDir, string gameRoot)
    {
        string manifestPath = Path.Combine(backupDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Cannot rollback: Backup manifest not found at {manifestPath}");
        }

        string manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson)
            ?? throw new InvalidOperationException("Corrupt backup manifest file.");

        string looseDatPath = Path.Combine(gameRoot, "Aion2", "Content", "L10N", "Text", "en-US", "L10NString.dat");

        bool looseDatWasBackedUp = false;

        foreach (var entry in manifest.Files)
        {
            string backupFilePath = Path.Combine(backupDir, entry.BackupRelativePath);
            if (!File.Exists(backupFilePath))
            {
                throw new InvalidOperationException($"Backup file missing: {backupFilePath}");
            }

            // Verify backup file hash before restoring
            string backupHash = ComputeSha256Hex(backupFilePath);
            if (!string.Equals(backupHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Corrupt backup file: {backupFilePath}! Expected hash {entry.Sha256}, got {backupHash}");
            }

            if (entry.Identifier == "loose_dat")
            {
                looseDatWasBackedUp = true;
            }

            string targetPath = entry.OriginalPath;
            if (!Path.IsPathRooted(targetPath))
            {
                targetPath = Path.Combine(gameRoot, targetPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(backupFilePath, targetPath, overwrite: true);

            string restoredHash = ComputeSha256Hex(targetPath);
            if (!string.Equals(restoredHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Restored file hash verification failed for {targetPath}");
            }
        }

        // If loose DAT was not backed up (because it didn't exist before install), delete it
        if (!looseDatWasBackedUp && File.Exists(looseDatPath))
        {
            File.Delete(looseDatPath);
        }
    }

    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
