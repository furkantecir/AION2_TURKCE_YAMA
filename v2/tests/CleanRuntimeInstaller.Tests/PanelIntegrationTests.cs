using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Aion2TR.App.ViewModels;
using Aion2TR.V2.RuntimeInstaller;
using Xunit;

namespace CleanRuntimeInstaller.Tests;

public class PanelIntegrationTests : IDisposable
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

    public PanelIntegrationTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "Aion2_PanelTests_" + Guid.NewGuid().ToString("N"));
        string mockSteamapps = Path.Combine(_testBaseDir, "steamapps");
        _mockGameRoot = Path.Combine(mockSteamapps, "common", "AION2");
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

        // Mock Steam official AION 2 manifest
        File.WriteAllText(Path.Combine(mockSteamapps, "appmanifest_3393110.acf"),
@"""AppState""
{
	""appid""		""3393110""
	""name""		""AION 2""
	""installdir""		""AION2""
}");

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

    private MainViewModel CreateViewModel(RuntimeInstallerEngine? engine = null)
    {
        var vm = new MainViewModel(engine ?? CreateEngine(), _processDetector)
        {
            GamePath = _mockGameRoot,
            GameFound = true,
            ShowMessage = (msg, title, btn, img) => MessageBoxResult.Yes
        };
        return vm;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBaseDir))
        {
            try { Directory.Delete(_testBaseDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Test01_ViewModel_NotInstalled_State_MapsToInstallButton()
    {
        var vm = CreateViewModel();
        await vm.RefreshStatusAsync();

        Assert.True(vm.GameFound);
        Assert.False(vm.IsGameRunning);
        Assert.True(vm.CanInstall);
        Assert.False(vm.CanRemove);
        Assert.False(vm.CanUpdate);
        Assert.False(vm.CanRepair);
        Assert.False(vm.CanLaunch);
        Assert.Equal("Türkçe Yamayı Kur", vm.PrimaryButtonText);
        Assert.Equal("Türkçe Yama Kurulu Değil", vm.StatusTitle);
    }

    [Fact]
    public async Task Test02_ViewModel_Install_Executes_AndTransitionsToInstalled()
    {
        var vm = CreateViewModel();
        await vm.RefreshStatusAsync();

        Assert.True(vm.CanInstall);
        await vm.InstallPatchAsync();

        Assert.False(vm.CanInstall);
        Assert.True(vm.CanRemove);
        Assert.True(vm.CanLaunch);
        Assert.Equal("Oyunu Başlat", vm.PrimaryButtonText);
        Assert.Equal("Türkçe Yama Kurulu", vm.StatusTitle);
    }

    [Fact]
    public async Task Test03_ViewModel_Remove_Executes_AndTransitionsToNotInstalled()
    {
        var vm = CreateViewModel();
        await vm.RefreshStatusAsync();
        await vm.InstallPatchAsync();
        Assert.True(vm.CanRemove);

        // Click Remove (confirmation defaults to Yes)
        await vm.RemovePatchAsync();

        Assert.True(vm.CanInstall);
        Assert.False(vm.CanRemove);
        Assert.Equal("Türkçe Yamayı Kur", vm.PrimaryButtonText);
        Assert.Equal("Türkçe Yama Kurulu Değil", vm.StatusTitle);
    }

    [Fact]
    public async Task Test04_ViewModel_RemoveCanceled_KeepsInstalledState()
    {
        var vm = CreateViewModel();
        await vm.RefreshStatusAsync();
        await vm.InstallPatchAsync();

        // User cancels confirmation
        vm.ShowMessage = (msg, title, btn, img) => MessageBoxResult.No;
        await vm.RemovePatchAsync();

        Assert.True(vm.CanRemove);
        Assert.Equal("Türkçe Yama Kurulu", vm.StatusTitle);
    }

    [Fact]
    public async Task Test05_ViewModel_UpdateAvailable_MapsToUpdateButton()
    {
        // 1. Install Step 3 (older patch)
        var step3Engine = CreateEngine(_testStep3Descriptor);
        step3Engine.ExecuteInstall(step3Engine.RunPreflight(_mockGameRoot));

        // 2. Open ViewModel with Step 8 engine
        var vm = CreateViewModel(CreateEngine(_testStep8Descriptor));
        await vm.RefreshStatusAsync();

        Assert.True(vm.CanUpdate);
        Assert.True(vm.CanRemove);
        Assert.False(vm.CanInstall);
        Assert.Equal("Yamayı Güncelle", vm.PrimaryButtonText);
        Assert.Equal("Yama Güncellemesi Mevcut", vm.StatusTitle);

        // Execute Update
        await vm.UpdatePatchAsync();

        Assert.False(vm.CanUpdate);
        Assert.True(vm.CanRemove);
        Assert.True(vm.CanLaunch);
        Assert.Equal("Oyunu Başlat", vm.PrimaryButtonText);
        Assert.Equal("Türkçe Yama Kurulu", vm.StatusTitle);
    }

    [Fact]
    public async Task Test06_ViewModel_MixedState_MapsToRepairButton()
    {
        var engine = CreateEngine();
        engine.EnsureOriginalCleanBackup(_mockGameRoot);

        // Corrupt environment to mixed state
        string paksDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        string l10nDir = Path.Combine(_mockGameRoot, "Aion2", "Content", "L10N", "Text", "en-US");
        File.Copy(_mockStubPakPath, Path.Combine(paksDir, "pakchunk502000-Windows_0_P.pak"), overwrite: true);
        File.WriteAllBytes(Path.Combine(l10nDir, "L10NString.dat"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(paksDir, "pakchunk502000-Windows_0_P.sig"), _mockOfficialSigBytes);

        var vm = CreateViewModel(engine);
        await vm.RefreshStatusAsync();

        Assert.True(vm.CanRepair);
        Assert.False(vm.CanInstall);
        Assert.False(vm.CanRemove);
        Assert.Equal("Dosyaları Onar", vm.PrimaryButtonText);
        Assert.Equal("Yama Dosyalarında Onarım Gerekiyor", vm.StatusTitle);

        // Execute Repair
        await vm.RepairPatchAsync();

        Assert.True(vm.CanInstall);
        Assert.False(vm.CanRepair);
        Assert.Equal("Türkçe Yamayı Kur", vm.PrimaryButtonText);
        Assert.Equal("Türkçe Yama Kurulu Değil", vm.StatusTitle);
    }

    [Fact]
    public async Task Test07_ViewModel_GameRunning_DisablesActions()
    {
        _processDetector.IsRunning = true;
        var vm = CreateViewModel();
        await vm.RefreshStatusAsync();

        Assert.True(vm.IsGameRunning);
        Assert.False(vm.CanInstall);
        Assert.False(vm.CanUpdate);
        Assert.False(vm.CanRemove);
        Assert.False(vm.CanRepair);
        Assert.Equal("Tekrar Kontrol Et", vm.PrimaryButtonText);
        Assert.Equal("AION2 Şu Anda Çalışıyor", vm.StatusTitle);
    }

    [Fact]
    public async Task Test08_ViewModel_GameNotFound_PromptsFolderSelect()
    {
        var vm = CreateViewModel();
        vm.GameFound = false;
        vm.GamePath = "Geçersiz Yol";
        await vm.RefreshStatusAsync();

        Assert.False(vm.GameFound);
        Assert.False(vm.CanInstall);
        Assert.Equal("Oyun Klasörünü Seç", vm.PrimaryButtonText);
        Assert.Equal("AION2 Bulunamadı", vm.StatusTitle);
    }

    [Fact]
    public void Test09_ViewModel_BusyState_BlocksConcurrentExecution()
    {
        var vm = CreateViewModel();
        vm.IsBusy = true;
        Assert.False(vm.CanExecuteActions);
        Assert.False(vm.PrimaryActionCommand.CanExecute(null));
        Assert.False(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Test10_AppRestart_ReflectsActualPhysicalState()
    {
        // Instance 1: Install patch
        var vm1 = CreateViewModel();
        await vm1.RefreshStatusAsync();
        await vm1.InstallPatchAsync();
        Assert.Equal("Türkçe Yama Kurulu", vm1.StatusTitle);

        // Instance 2 (simulating app restart)
        var vm2 = CreateViewModel();
        await vm2.RefreshStatusAsync();
        Assert.Equal("Türkçe Yama Kurulu", vm2.StatusTitle);
        Assert.True(vm2.CanRemove);
        Assert.True(vm2.CanLaunch);
    }

    [Fact]
    public async Task Test11_Regression_CannotLaunchUnrelatedAppId_WhenWrongManifestPresent()
    {
        // Setup isolated mock Steam library with unrelated game manifest (e.g. 3041280 Prison of Lies)
        string fakeSteamapps = Path.Combine(_testBaseDir, "FakeSteamLibrary", "steamapps");
        string fakeGameRoot = Path.Combine(fakeSteamapps, "common", "OtherGame");
        Directory.CreateDirectory(fakeGameRoot);
        Directory.CreateDirectory(Path.Combine(fakeGameRoot, "Aion2", "Content", "Paks"));

        File.WriteAllText(Path.Combine(fakeSteamapps, "appmanifest_3041280.acf"),
@"""AppState""
{
	""appid""		""3041280""
	""name""		""Prison of Lies""
	""installdir""		""Prison of Lies""
}");

        var engine = CreateEngine();
        string? detectedAppId = engine.DetectSteamAppId(fakeGameRoot);

        // Must reject unrelated AppId and return null
        Assert.Null(detectedAppId);
        Assert.Null(engine.GetSteamLaunchUri(fakeGameRoot));

        var vm = CreateViewModel(engine);
        vm.GamePath = fakeGameRoot;
        await vm.RefreshStatusAsync();

        Assert.Null(vm.DetectedSteamAppId);
        Assert.Null(vm.CurrentLaunchUri);
        Assert.False(vm.CanLaunch);
        Assert.NotEqual("steam://rungameid/3041280", vm.CurrentLaunchUri);
        Assert.NotEqual("steam://rungameid/3142020", vm.CurrentLaunchUri);
    }

    [Fact]
    public async Task Test12_Regression_DynamicallyDerivesCorrectAion2AppId()
    {
        var engine = CreateEngine();
        string? detectedAppId = engine.DetectSteamAppId(_mockGameRoot);

        // Must dynamically detect 3393110 from appmanifest
        Assert.Equal("3393110", detectedAppId);
        Assert.Equal("steam://rungameid/3393110", engine.GetSteamLaunchUri(_mockGameRoot));

        var vm = CreateViewModel(engine);
        await vm.RefreshStatusAsync();
        await vm.InstallPatchAsync();

        Assert.Equal("3393110", vm.DetectedSteamAppId);
        Assert.Equal("steam://rungameid/3393110", vm.CurrentLaunchUri);
        Assert.True(vm.CanLaunch);
        Assert.Equal("Oyunu Başlat", vm.PrimaryButtonText);

        // Strictly verify that old bogus AppIDs are NEVER launched
        Assert.NotEqual("steam://rungameid/3041280", vm.CurrentLaunchUri);
        Assert.NotEqual("steam://rungameid/3142020", vm.CurrentLaunchUri);
    }

    [Fact]
    public async Task Test13_Regression_NonSteamDirectory_DisablesPlayButtonRatherThanGuessing()
    {
        // Standalone directory outside Steam with no appmanifest
        string standaloneGameRoot = Path.Combine(_testBaseDir, "StandaloneGames", "AION2");
        Directory.CreateDirectory(Path.Combine(standaloneGameRoot, "Aion2", "Content", "Paks"));

        var engine = CreateEngine();
        string? detectedAppId = engine.DetectSteamAppId(standaloneGameRoot);

        Assert.Null(detectedAppId);
        Assert.Null(engine.GetSteamLaunchUri(standaloneGameRoot));

        var vm = CreateViewModel(engine);
        vm.GamePath = standaloneGameRoot;
        await vm.RefreshStatusAsync();

        // When AppID cannot be derived, CanLaunch MUST be false and Play button must not guess
        Assert.False(vm.CanLaunch);
        Assert.Null(vm.DetectedSteamAppId);
        Assert.Null(vm.CurrentLaunchUri);
    }
}
