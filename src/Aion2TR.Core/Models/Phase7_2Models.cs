using System;
using System.Collections.Generic;

namespace Aion2TR.Core.Models;

public enum RuntimePatchState
{
    NotInstalled,
    Preparing,
    BackupCreated,
    StubInstalled,
    LooseDatInstalled,
    Installed,
    RollingBack,
    RolledBack,
    Failed
}

public class RuntimeBackupManifest
{
    public string OriginalPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string OriginalSHA256 { get; set; } = string.Empty;
    public string BackupSHA256 { get; set; } = string.Empty;
    public long OriginalSize { get; set; }
    public long BackupSize { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string GameVersionFingerprint { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = "Aion2TR";
}

public class RuntimePatchOwnershipManifest
{
    public string PatchVersion { get; set; } = "1.0.0-phase7.2-test";
    public DateTime InstalledAtUtc { get; set; } = DateTime.UtcNow;
    public string GameFingerprint { get; set; } = string.Empty;
    public string OfficialPakPath { get; set; } = string.Empty;
    public string OriginalPakSHA256 { get; set; } = string.Empty;
    public long OriginalPakSize { get; set; }
    public string BackupPath { get; set; } = string.Empty;
    public string BackupSHA256 { get; set; } = string.Empty;
    public long BackupSize { get; set; }
    public string StubSHA256 { get; set; } = string.Empty;
    public string LooseDatPath { get; set; } = string.Empty;
    public string LooseDatSHA256 { get; set; } = string.Empty;
    public long LooseDatSize { get; set; }
    public string PatchMode { get; set; } = "LEGACY_EMPTY_STUB_PLUS_LOOSE_DAT";
    public RuntimePatchState State { get; set; } = RuntimePatchState.NotInstalled;
    public string CreatedBy { get; set; } = "Aion2TR";
}

public class RuntimePatchStatus
{
    public string Method { get; set; } = "Empty Stub + Loose DAT";
    public string BackupState { get; set; } = "Missing"; // Ready / Missing
    public string OfficialLocalizationState { get; set; } = "Unknown"; // Original / Aion2TR Stub / Unknown
    public string LooseDatState { get; set; } = "Not Installed"; // Installed / Not Installed
    public string RollbackState { get; set; } = "Unsafe"; // Ready / Unsafe
    public string GameState { get; set; } = "Closed"; // Running / Closed
    public bool IsGameRunning { get; set; }
    public bool IsInstalled { get; set; }
    public bool RequiresCrashRecovery { get; set; }
    public string? CrashRecoveryMessage { get; set; }
    public bool GameFingerprintChanged { get; set; }
    public string Details { get; set; } = string.Empty;
    public string OfficialPakSha256 { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
}

public class RuntimePatchOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public RuntimePatchState State { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
