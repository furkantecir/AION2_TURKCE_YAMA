using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Aion2TR.V2.RuntimeInstaller;
using Xunit;

namespace CleanRuntimeInstaller.Tests;

public class MockProcessDetector : IProcessDetector
{
    public bool IsRunning { get; set; }
    public MockProcessDetector(bool isRunning = false) => IsRunning = isRunning;
    public bool IsGameRunning() => IsRunning;
}

public class RuntimeInstallerTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly string _mockGameRoot;
    private readonly string _mockRepoRoot;
    private readonly string _mockStep8DatPath;
    private readonly string _mockStep3DatPath;
    private readonly string _mockStubPakPath;
    private readonly string _mockBackupRoot;
    private readonly string _mockStateFilePath;
    private readonly MockProcessDetector _processDetector;

    private readonly byte[] _mockOfficialPakBytes = Encoding.UTF8.GetBytes("MOCK_OFFICIAL_PAK_PAYLOAD_12345");
    private readonly byte[] _mockOfficialSigBytes = Encoding.UTF8.GetBytes("MOCK_OFFICIAL_SIG_PAYLOAD");
    private readonly byte[] _mockOfficialUcasBytes = Encoding.UTF8.GetBytes("MOCK_OFFICIAL_UCAS_PAYLOAD");
    private readonly byte[] _mockOfficialUtocBytes = Encoding.UTF8.GetBytes("MOCK_OFFICIAL_UTOC_PAYLOAD");

    private readonly byte[] _mockStubBytes = Encoding.UTF8.GetBytes("MOCK_STUB_PAK_413_BYTES");
    private readonly byte[] _mockStep8DatBytes = Encoding.UTF8.GetBytes("MOCK_STEP8_DAT_7049892_BYTES");
    private readonly byte[] _mockStep3DatBytes = Encoding.UTF8.GetBytes("MOCK_STEP3_DAT_7032836_BYTES");

    private readonly ArtifactDescriptor _testStep8Descriptor;
    private readonly ArtifactDescriptor _testStep3Descriptor;

    public RuntimeInstallerTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "Aion2_Tests_" + Guid.NewGuid().ToString("N"));
        _mockGameRoot = Path.Combine(_testBaseDir, "GameRoot");
        _mockRepoRoot = Path.Combine(_testBaseDir, "RepoRoot");

        _mockStep8DatPath = Path.Combine(_mockRepoRoot, "v2", "artifacts", "step8", "L10NString.tr-TR.full.dat");
        _mockStep3DatPath = Path.Combine(_mockRepoRoot, "v2", "artifacts", "step3", "L10NString.tr-TR.exact-only.dat");
        _mockStubPakPath = Path.Combine(_mockRepoRoot, "v2", "artifacts", "runtime", "english_empty_stub.pak");
        _mockBackupRoot = Path.Combine(_mockRepoRoot, "v2", "runtime_backups");
        _mockStateFilePath = Path.Combine(_mockRepoRoot, "v2", "runtime_state", "install_state.json");

        _processDetector = new MockProcessDetector(false);

        Directory.CreateDirectory(_mockGameRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(_mockStep8DatPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_mockStep3DatPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_mockStubPakPath)!);
        Directory.CreateDirectory(_mockBackupRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(_mockStateFilePath)!);

        File.WriteAllBytes(_mockStep8DatPath, _mockStep8DatBytes);
        File.WriteAllBytes(_mockStep3DatPath, _mockStep3DatBytes);
        File.WriteAllBytes(_mockStubPakPath, _mockStubBytes);

        _testStep8Descriptor = new ArtifactDescriptor
        {
            ArtifactId = "TEST_STEP8",
            DisplayName = "Test Step 8 Full Candidate",
            RelativeDatPath = Path.Combine("v2", "artifacts", "step8", "L10NString.tr-TR.full.dat"),
            ExpectedDatHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockStep8DatBytes),
            ExpectedDatSize = _mockStep8DatBytes.Length,
            ExpectedStubHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockStubBytes),
            ExpectedStubSize = _mockStubBytes.Length,
            ExpectedOfficialPakHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockOfficialPakBytes),
            ExpectedOfficialPakSize = _mockOfficialPakBytes.Length,
            ExpectedOfficialSigHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockOfficialSigBytes),
            ExpectedOfficialUcasHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockOfficialUcasBytes),
            ExpectedOfficialUtocHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockOfficialUtocBytes)
        };

        _testStep3Descriptor = new ArtifactDescriptor
        {
            ArtifactId = "TEST_STEP3",
            DisplayName = "Test Step 3 Exact Candidate",
            RelativeDatPath = Path.Combine("v2", "artifacts", "step3", "L10NString.tr-TR.exact-only.dat"),
            ExpectedDatHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockStep3DatBytes),
            ExpectedDatSize = _mockStep3DatBytes.Length,
            ExpectedStubHash = RuntimeInstallerEngine.ComputeSha256Hex(_mockStubBytes),
            ExpectedStubSize = _mockStubBytes.Length,
            ExpectedOfficialPakHash = _testStep8Descriptor.ExpectedOfficialPakHash,
            ExpectedOfficialPakSize = _testStep8Descriptor.ExpectedOfficialPakSize,
            ExpectedOfficialSigHash = _testStep8Descriptor.ExpectedOfficialSigHash,
            ExpectedOfficialUcasHash = _testStep8Descriptor.ExpectedOfficialUcasHash,
            ExpectedOfficialUtocHash = _testStep8Descriptor.ExpectedOfficialUtocHash
        };

        SetupCleanOfficialGameDirectory();
    }

    private void SetupCleanOfficialGameDirectory()
    {
        string paksDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        string l10nDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "L10N", "Text", "en-US");

        Directory.CreateDirectory(paksDir);
        Directory.CreateDirectory(l10nDir);

        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.pak"), _mockOfficialPakBytes);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.sig"), _mockOfficialSigBytes);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.ucas"), _mockOfficialUcasBytes);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.utoc"), _mockOfficialUtocBytes);

        string looseDat = Path.Combine(l10nDir, "L10NString.dat");
        if (File.Exists(looseDat)) File.Delete(looseDat);
    }

    private RuntimeInstallerEngine CreateEngine(ArtifactDescriptor? descriptor = null)
    {
        return new RuntimeInstallerEngine(
            processDetector: _processDetector,
            artifact: descriptor ?? _testStep8Descriptor,
            customRepoRoot: _mockRepoRoot,
            customDatPath: (descriptor ?? _testStep8Descriptor).ArtifactId == "TEST_STEP3" ? _mockStep3DatPath : _mockStep8DatPath,
            customStubPakPath: _mockStubPakPath,
            customBackupRoot: _mockBackupRoot,
            customStateFilePath: _mockStateFilePath
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBaseDir))
        {
            try { Directory.Delete(_testBaseDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Test01_Clean_InstallOldPatch_Succeeds()
    {
        var engine = CreateEngine(_testStep3Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        Assert.True(preflight.CanInstall);

        var state = engine.ExecuteInstall(preflight);
        Assert.Equal("INSTALLED_VALID", state.Status);

        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.INSTALLED_VALID, status.Code);
        Assert.True(status.LooseDatPresent);
        Assert.True(status.IsPakStub);
        Assert.False(status.SigPresent);
    }

    [Fact]
    public void Test02_OldPatch_Status_ReportsInstalledValid()
    {
        var engine = CreateEngine(_testStep3Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        engine.ExecuteInstall(preflight);

        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.INSTALLED_VALID, status.Code);
        Assert.Equal(_testStep3Descriptor.ExpectedDatHash, status.LooseDatHash);
    }

    [Fact]
    public void Test03_OldPatch_Rollback_RestoresCleanOriginal()
    {
        var engine = CreateEngine(_testStep3Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        var state = engine.ExecuteInstall(preflight);

        bool rolledBack = engine.ExecuteRollback(_mockGameRoot, state.BackupDirectory);
        Assert.True(rolledBack);

        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.NOT_INSTALLED, status.Code);
        Assert.False(status.LooseDatPresent);
        Assert.True(status.IsOfficialPak);
        Assert.True(status.SigPresent);
        Assert.True(status.UcasPresent);
        Assert.True(status.UtocPresent);
    }

    [Fact]
    public void Test04_Clean_InstallStep8_Succeeds()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        Assert.True(preflight.CanInstall);

        var state = engine.ExecuteInstall(preflight);
        Assert.Equal("INSTALLED_VALID", state.Status);
        Assert.Equal(_testStep8Descriptor.ArtifactId, state.ArtifactId);

        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.INSTALLED_VALID, status.Code);
        Assert.Equal(_testStep8Descriptor.ExpectedDatHash, status.LooseDatHash);
        Assert.True(status.IsPakStub);
        Assert.False(status.SigPresent);
    }

    [Fact]
    public void Test05_Step8_Rollback_RestoresCleanOriginal()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        var state = engine.ExecuteInstall(preflight);

        bool rolledBack = engine.ExecuteRollback(_mockGameRoot, state.BackupDirectory);
        Assert.True(rolledBack);

        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.NOT_INSTALLED, status.Code);
        Assert.False(status.LooseDatPresent);
        Assert.True(status.IsOfficialPak);
    }

    [Fact]
    public void Test06_OldPatch_UpgradeStep8_Succeeds()
    {
        // 1. Install Step 3
        var step3Engine = CreateEngine(_testStep3Descriptor);
        step3Engine.ExecuteInstall(step3Engine.RunPreflight(_mockGameRoot));
        Assert.Equal(RuntimeStatusCode.INSTALLED_VALID, step3Engine.GetStatus(_mockGameRoot).Code);

        // 2. Upgrade to Step 8
        var step8Engine = CreateEngine(_testStep8Descriptor);
        var upgradeState = step8Engine.ExecuteUpgrade(_mockGameRoot);
        Assert.Equal("INSTALLED_VALID", upgradeState.Status);
        Assert.Equal(_testStep8Descriptor.ArtifactId, upgradeState.ArtifactId);

        var postStatus = step8Engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.INSTALLED_VALID, postStatus.Code);
        Assert.Equal(_testStep8Descriptor.ExpectedDatHash, postStatus.LooseDatHash);
        Assert.True(postStatus.IsPakStub);
        Assert.False(postStatus.SigPresent);
    }

    [Fact]
    public void Test07_UpgradeFailureMidway_TriggersAutomaticRollback()
    {
        // 1. Install Step 3
        var step3Engine = CreateEngine(_testStep3Descriptor);
        step3Engine.ExecuteInstall(step3Engine.RunPreflight(_mockGameRoot));

        // 2. Break Step 8 DAT path right before upgrade
        File.Delete(_mockStep8DatPath);

        var step8Engine = CreateEngine(_testStep8Descriptor);
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            step8Engine.ExecuteUpgrade(_mockGameRoot);
        });

        // 3. Verify previous Step 3 state was restored
        var status = step3Engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.INSTALLED_VALID, status.Code);
        Assert.Equal(_testStep3Descriptor.ExpectedDatHash, status.LooseDatHash);
    }

    [Fact]
    public void Test08_CopyHashFailure_TriggersAutomaticRollback()
    {
        // Write corrupted DAT to artifact path
        File.WriteAllBytes(_mockStep8DatPath, Encoding.UTF8.GetBytes("CORRUPT_BYTES_XYZ"));

        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        Assert.False(preflight.CanInstall);

        Assert.Throws<InvalidOperationException>(() =>
        {
            engine.ExecuteInstall(preflight);
        });

        // Verify clean game root untouched
        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.NOT_INSTALLED, status.Code);
    }

    [Fact]
    public void Test09_StateWriteFailure_TriggersAutomaticRollback()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);

        // Make state file path invalid (directory as file name or locked)
        Directory.CreateDirectory(_mockStateFilePath); // will fail File.WriteAllText

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            engine.ExecuteInstall(preflight);
        });

        Assert.Contains("Automatic rollback was triggered", ex.Message);

        // Clean up directory to allow status check
        Directory.Delete(_mockStateFilePath);

        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.NOT_INSTALLED, status.Code);
    }

    [Fact]
    public void Test10_CompanionRestorationFailure_DetectedFailure()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        var state = engine.ExecuteInstall(preflight);

        // Corrupt one companion in backup
        string backupSig = Path.Combine(state.BackupDirectory, "pakchunk502000-Windows_0_P.sig");
        File.WriteAllBytes(backupSig, [255, 254, 253]);

        Assert.Throws<InvalidOperationException>(() =>
        {
            engine.ExecuteRollback(_mockGameRoot, state.BackupDirectory);
        });
    }

    [Fact]
    public void Test11_RepeatedRollback_SafeAndIdempotent()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        var state = engine.ExecuteInstall(preflight);

        bool rb1 = engine.ExecuteRollback(_mockGameRoot, state.BackupDirectory);
        Assert.True(rb1);

        bool rb2 = engine.ExecuteRollback(_mockGameRoot, state.BackupDirectory);
        Assert.True(rb2);

        var status = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.NOT_INSTALLED, status.Code);
    }

    [Fact]
    public void Test12_RepeatedInstall_RefusedSafely()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);
        engine.ExecuteInstall(preflight);

        // Second install attempt of same artifact
        var preflight2 = engine.RunPreflight(_mockGameRoot);
        Assert.Throws<InvalidOperationException>(() =>
        {
            engine.ExecuteInstall(preflight2);
        });
    }

    [Fact]
    public void Test13_Repair_MixedState_RestoresCleanOriginal()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        // Ensure clean backup exists
        engine.EnsureOriginalCleanBackup(_mockGameRoot);

        // Corrupt game folder into MIXED_STATE:
        // Stub pak + loose dat + companions present
        string paksDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        string l10nDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "L10N", "Text", "en-US");
        File.Copy(_mockStubPakPath, Path.Combine(paksDir, "pakchunk502000-Windows_0_P.pak"), overwrite: true);
        File.WriteAllBytes(Path.Combine(l10nDir, "L10NString.dat"), [10, 20, 30]);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.sig"), _mockOfficialSigBytes);

        var mixedStatus = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.MIXED_STATE, mixedStatus.Code);

        // Execute automated repair
        bool repairOk = engine.ExecuteRepair(_mockGameRoot);
        Assert.True(repairOk);

        var cleanStatus = engine.GetStatus(_mockGameRoot);
        Assert.Equal(RuntimeStatusCode.NOT_INSTALLED, cleanStatus.Code);
        Assert.False(cleanStatus.LooseDatPresent);
        Assert.True(cleanStatus.IsOfficialPak);
        Assert.True(cleanStatus.SigPresent);
    }

    [Fact]
    public void Test14_GameProcessRunning_RefusesInstallAndRollback()
    {
        _processDetector.IsRunning = true;
        var engine = CreateEngine(_testStep8Descriptor);
        var preflight = engine.RunPreflight(_mockGameRoot);

        Assert.False(preflight.CanInstall);
        Assert.True(preflight.IsGameRunning);

        Assert.Throws<InvalidOperationException>(() => engine.ExecuteInstall(preflight));
        Assert.Throws<InvalidOperationException>(() => engine.ExecuteUpgrade(_mockGameRoot));
        Assert.Throws<InvalidOperationException>(() => engine.ExecuteRollback(_mockGameRoot));
        Assert.Throws<InvalidOperationException>(() => engine.ExecuteRepair(_mockGameRoot));
    }

    [Fact]
    public void Test15_MixedState_DetectedCorrectly()
    {
        var engine = CreateEngine(_testStep8Descriptor);
        string paksDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        string l10nDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "L10N", "Text", "en-US");

        // Case A: Stub PAK + companions
        File.Copy(_mockStubPakPath, Path.Combine(paksDir, "pakchunk502000-Windows_0_P.pak"), overwrite: true);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.sig"), _mockOfficialSigBytes);
        Assert.Equal(RuntimeStatusCode.MIXED_STATE, engine.GetStatus(_mockGameRoot).Code);

        // Case B: Official PAK + loose DAT
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.pak"), _mockOfficialPakBytes);
        File.WriteAllBytes(Path.Combine(l10nDir, "L10NString.dat"), [1, 2, 3]);
        Assert.Equal(RuntimeStatusCode.MIXED_STATE, engine.GetStatus(_mockGameRoot).Code);
    }
}
