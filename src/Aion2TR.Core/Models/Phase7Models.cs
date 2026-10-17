using System;
using System.Collections.Generic;

namespace Aion2TR.Core.Models;

public enum MountStrategyType
{
    SAFE_SIDE_BY_SIDE_PAK_SUPPORTED,
    SAFE_SIDE_BY_SIDE_IOSTORE_SUPPORTED,
    OFFICIAL_CONTAINER_MODIFICATION_REQUIRED,
    ANTI_CHEAT_OR_EXECUTABLE_MODIFICATION_REQUIRED,
    INSUFFICIENT_EVIDENCE
}

public enum Phase7RuntimeTestStatus
{
    NOT_INSTALLED,
    WAITING_FOR_PHASE7_USER_TEST,
    PASS,
    CONTAINER_NOT_LOADED,
    CONTAINER_REJECTED,
    INTEGRITY_FAILURE
}

public class Phase7TestKeyInfo
{
    public string Key { get; set; } = string.Empty;
    public string English { get; set; } = string.Empty;
    public string Turkish { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}

public class PreflightGameManifest
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string GameRoot { get; set; } = string.Empty;
    public List<PreflightContainerInfo> Containers { get; set; } = new();
}

public class PreflightContainerInfo
{
    public string Filename { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SHA256 { get; set; } = string.Empty;
    public DateTime LastWriteTimeUtc { get; set; }
    public bool IsLocalizationContainer { get; set; }
}

public class Phase7InstallManifest
{
    public string PatchVersion { get; set; } = "1.0.0-phase7-test";
    public DateTime InstalledAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "Aion2TR";
    public string Purpose { get; set; } = "Phase 7 In-Game Localization Runtime Test";
    public List<Phase7InstalledFile> Files { get; set; } = new();
}

public class Phase7InstalledFile
{
    public string Path { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string SHA256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Purpose { get; set; } = string.Empty;
}

public class TestContainerValidationReport
{
    public DateTime ValidatedAtUtc { get; set; } = DateTime.UtcNow;
    public string ContainerPath { get; set; } = string.Empty;
    public string ContainerFilename { get; set; } = string.Empty;
    public long ContainerSize { get; set; }
    public string ContainerSha256 { get; set; } = string.Empty;
    public string ExpectedVirtualPath { get; set; } = string.Empty;
    public string MountPoint { get; set; } = string.Empty;
    public bool ContainerReadable { get; set; }
    public bool VirtualPathFound { get; set; }
    public bool DatHashIdentical { get; set; }
    public string ExtractedDatSha256 { get; set; } = string.Empty;
    public string ExpectedDatSha256 { get; set; } = string.Empty;
    public int EntryCount { get; set; }
    public bool ValidationPassed { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class Phase7PatchStatusResult
{
    public Phase7RuntimeTestStatus Status { get; set; } = Phase7RuntimeTestStatus.NOT_INSTALLED;
    public string StatusMessage { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public MountStrategyType MountStrategy { get; set; } = MountStrategyType.SAFE_SIDE_BY_SIDE_PAK_SUPPORTED;
    public bool IsOfficialFilesUntouched { get; set; } = true;
    public bool IsTestContainerInstalled { get; set; }
    public string InstalledPakPath { get; set; } = string.Empty;
    public string InstalledPakSha256 { get; set; } = string.Empty;
    public bool CanPublicInstall { get; set; } = false;
}

public class Phase7OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
