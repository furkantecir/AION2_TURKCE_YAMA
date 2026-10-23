using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace Aion2TR.V2.RuntimeInstaller;

public enum RuntimeStatusCode
{
    NOT_INSTALLED,      // Clean official game state (official PAK + companions, no loose DAT)
    INSTALLED_VALID,    // Active patch correctly installed (stub pak, valid candidate DAT, no companions)
    INSTALLED_MODIFIED, // Stub pak + loose DAT present, but loose DAT doesn't match expected patch, no companions
    PARTIAL_INSTALL,    // Missing files or broken structure (e.g. no pak at all, or loose DAT missing with stub)
    MIXED_STATE,        // Inconsistent combination (e.g. stub pak with companions, or official pak with loose DAT)
    UNKNOWN
}

public class ArtifactDescriptor
{
    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; set; } = "AION2TR_V2_STEP8_FULL";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "AION2TR V2 Step 8 Full Candidate";

    [JsonPropertyName("relativeDatPath")]
    public string RelativeDatPath { get; set; } = Path.Combine("v2", "artifacts", "step8", "L10NString.tr-TR.full.dat");

    [JsonPropertyName("expectedDatHash")]
    public string ExpectedDatHash { get; set; } = "5d6dcd1afcddbf6ce7acb2d257ec5d5c7ec69482802a2f74e0b9f9fae12601e5";

    [JsonPropertyName("expectedDatSize")]
    public long ExpectedDatSize { get; set; } = 7058052;

    [JsonPropertyName("expectedStubHash")]
    public string ExpectedStubHash { get; set; } = "105a71d61c9a0683500007bb8dab1cf3f12acfd28f17181408ceda426fc3d5b2";

    [JsonPropertyName("expectedStubSize")]
    public long ExpectedStubSize { get; set; } = 413;

    // Official game baseline references
    [JsonPropertyName("expectedOfficialPakHash")]
    public string ExpectedOfficialPakHash { get; set; } = "4565d9d817f6ca8a7a11bbd0d25d0d91c6306c234be5f279ecfb33d5fba47080";

    [JsonPropertyName("expectedOfficialPakSize")]
    public long ExpectedOfficialPakSize { get; set; } = 3749083;

    [JsonPropertyName("expectedOfficialSigHash")]
    public string ExpectedOfficialSigHash { get; set; } = "13cbc2d9e4282de7b8c0917dd80c7822ae31aa2f869dcc8bd513fd0d1ee1381d";

    [JsonPropertyName("expectedOfficialUcasHash")]
    public string ExpectedOfficialUcasHash { get; set; } = "c8e7146240adc7718e7e7d0b65468a8c71f4c7dd1f8a96e60d9a8daddc30be4f";

    [JsonPropertyName("expectedOfficialUtocHash")]
    public string ExpectedOfficialUtocHash { get; set; } = "588dd500e88bd7cc6170831e78c3409c275018ae13a9c1a8fc21633e43612c7e";

    public static ArtifactDescriptor Step8Full => new()
    {
        ArtifactId = "AION2TR_V2_STEP8_FULL",
        DisplayName = "AION2TR V2 Step 8 (Full 100% Translation)",
        RelativeDatPath = Path.Combine("v2", "artifacts", "step8", "L10NString.tr-TR.full.dat"),
        ExpectedDatHash = "5d6dcd1afcddbf6ce7acb2d257ec5d5c7ec69482802a2f74e0b9f9fae12601e5",
        ExpectedDatSize = 7058052,
        ExpectedOfficialPakHash = "4565d9d817f6ca8a7a11bbd0d25d0d91c6306c234be5f279ecfb33d5fba47080",
        ExpectedOfficialPakSize = 3749083,
        ExpectedOfficialSigHash = "13cbc2d9e4282de7b8c0917dd80c7822ae31aa2f869dcc8bd513fd0d1ee1381d"
    };

    public static ArtifactDescriptor Step3Exact => new()
    {
        ArtifactId = "AION2TR_V2_STEP3_EXACT",
        DisplayName = "AION2TR V2 Step 3 (Exact Legacy Only)",
        RelativeDatPath = Path.Combine("v2", "artifacts", "step3", "L10NString.tr-TR.exact-only.dat"),
        ExpectedDatHash = "598174d6c260ca17a998c30936552ec7ed2e0c0e3975896a07bfd3a8ddf98e96",
        ExpectedDatSize = 7032836
    };
}

public class RuntimeStatusReport
{
    [JsonPropertyName("code")]
    public RuntimeStatusCode Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("gameRoot")]
    public string GameRoot { get; set; } = string.Empty;

    [JsonPropertyName("looseDatPresent")]
    public bool LooseDatPresent { get; set; }

    [JsonPropertyName("looseDatHash")]
    public string? LooseDatHash { get; set; }

    [JsonPropertyName("pakPresent")]
    public bool PakPresent { get; set; }

    [JsonPropertyName("pakHash")]
    public string? PakHash { get; set; }

    [JsonPropertyName("isPakStub")]
    public bool IsPakStub { get; set; }

    [JsonPropertyName("isOfficialPak")]
    public bool IsOfficialPak { get; set; }

    [JsonPropertyName("sigPresent")]
    public bool SigPresent { get; set; }

    [JsonPropertyName("ucasPresent")]
    public bool UcasPresent { get; set; }

    [JsonPropertyName("utocPresent")]
    public bool UtocPresent { get; set; }

    [JsonPropertyName("activeArtifactId")]
    public string? ActiveArtifactId { get; set; }

    [JsonPropertyName("details")]
    public Dictionary<string, string> Details { get; set; } = new();
}

public class PreflightReport
{
    [JsonPropertyName("gameRoot")]
    public string GameRoot { get; set; } = string.Empty;

    [JsonPropertyName("isGameRunning")]
    public bool IsGameRunning { get; set; }

    [JsonPropertyName("officialPakFound")]
    public bool OfficialPakFound { get; set; }

    [JsonPropertyName("officialPakSize")]
    public long OfficialPakSize { get; set; }

    [JsonPropertyName("officialPakHash")]
    public string OfficialPakHash { get; set; } = string.Empty;

    [JsonPropertyName("isPakOfficialVerified")]
    public bool IsPakOfficialVerified { get; set; }

    [JsonPropertyName("isPakStubVerified")]
    public bool IsPakStubVerified { get; set; }

    [JsonPropertyName("sigFound")]
    public bool SigFound { get; set; }

    [JsonPropertyName("sigSize")]
    public long SigSize { get; set; }

    [JsonPropertyName("sigHash")]
    public string SigHash { get; set; } = string.Empty;

    [JsonPropertyName("ucasFound")]
    public bool UcasFound { get; set; }

    [JsonPropertyName("ucasSize")]
    public long UcasSize { get; set; }

    [JsonPropertyName("ucasHash")]
    public string UcasHash { get; set; } = string.Empty;

    [JsonPropertyName("utocFound")]
    public bool UtocFound { get; set; }

    [JsonPropertyName("utocSize")]
    public long UtocSize { get; set; }

    [JsonPropertyName("utocHash")]
    public string UtocHash { get; set; } = string.Empty;

    [JsonPropertyName("existingLooseDatFound")]
    public bool ExistingLooseDatFound { get; set; }

    [JsonPropertyName("existingLooseDatSize")]
    public long ExistingLooseDatSize { get; set; }

    [JsonPropertyName("existingLooseDatHash")]
    public string ExistingLooseDatHash { get; set; } = string.Empty;

    [JsonPropertyName("datArtifactId")]
    public string DatArtifactId { get; set; } = "AION2TR_V2_STEP8_FULL";

    [JsonPropertyName("datFound")]
    public bool DatFound { get; set; }

    [JsonPropertyName("datHashValid")]
    public bool DatHashValid { get; set; }

    [JsonPropertyName("datHash")]
    public string DatHash { get; set; } = string.Empty;

    [JsonPropertyName("datSize")]
    public long DatSize { get; set; }

    // Backward-compatible properties
    [JsonIgnore]
    public bool Step3DatFound { get => DatFound; set => DatFound = value; }

    [JsonIgnore]
    public bool Step3DatHashValid { get => DatHashValid; set => DatHashValid = value; }

    [JsonIgnore]
    public string Step3DatHash { get => DatHash; set => DatHash = value; }

    [JsonPropertyName("stubFound")]
    public bool StubFound { get; set; }

    [JsonPropertyName("stubHashValid")]
    public bool StubHashValid { get; set; }

    [JsonPropertyName("stubHash")]
    public string StubHash { get; set; } = string.Empty;

    [JsonPropertyName("freeDiskSpaceBytes")]
    public long FreeDiskSpaceBytes { get; set; }

    [JsonPropertyName("canInstall")]
    public bool CanInstall { get; set; }

    [JsonPropertyName("issues")]
    public List<string> Issues { get; set; } = new();

    [JsonPropertyName("targetLooseDatPath")]
    public string TargetLooseDatPath { get; set; } = string.Empty;

    [JsonPropertyName("targetPakPath")]
    public string TargetPakPath { get; set; } = string.Empty;
}

public class BackupFileEntry
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    [JsonPropertyName("originalPath")]
    public string OriginalPath { get; set; } = string.Empty;

    [JsonPropertyName("backupRelativePath")]
    public string BackupRelativePath { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("existedBeforeInstall")]
    public bool ExistedBeforeInstall { get; set; }
}

public class BackupManifest
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("backupId")]
    public string BackupId { get; set; } = string.Empty;

    [JsonPropertyName("gameRoot")]
    public string GameRoot { get; set; } = string.Empty;

    [JsonPropertyName("backupType")]
    public string BackupType { get; set; } = "TRANSACTION_SNAPSHOT"; // ORIGINAL_GAME_BACKUP, TRANSACTION_SNAPSHOT, PATCH_GENERATION

    [JsonPropertyName("files")]
    public List<BackupFileEntry> Files { get; set; } = new();
}

public class InstallState
{
    [JsonPropertyName("gameRoot")]
    public string GameRoot { get; set; } = string.Empty;

    [JsonPropertyName("installTimestamp")]
    public string InstallTimestamp { get; set; } = string.Empty;

    [JsonPropertyName("artifactId")]
    public string ArtifactId { get; set; } = "AION2TR_V2_STEP8_FULL";

    [JsonPropertyName("datHash")]
    public string DatHash { get; set; } = string.Empty;

    [JsonPropertyName("stubHash")]
    public string StubHash { get; set; } = string.Empty;

    // Backward compatibility
    [JsonPropertyName("step3DatHash")]
    public string Step3DatHash { get => DatHash; set => DatHash = value; }

    [JsonPropertyName("backupDirectory")]
    public string BackupDirectory { get; set; } = string.Empty;

    [JsonPropertyName("filesOriginallyPresent")]
    public List<string> FilesOriginallyPresent { get; set; } = new();

    [JsonPropertyName("filesModified")]
    public List<string> FilesModified { get; set; } = new();

    [JsonPropertyName("filesRemovedOrDisabled")]
    public List<string> FilesRemovedOrDisabled { get; set; } = new();

    [JsonPropertyName("installedFileHashes")]
    public Dictionary<string, string> InstalledFileHashes { get; set; } = new();

    [JsonPropertyName("installerVersion")]
    public string InstallerVersion { get; set; } = "2.1.0";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "INSTALLED_VALID";
}
