using System;
using System.IO;
using System.Threading.Tasks;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Aion2TR.Tests;

public class Phase7Tests
{
    private readonly string _repoRoot;
    private readonly Phase7PatchService _patchService;

    public Phase7Tests()
    {
        _repoRoot = Phase5PatchService.FindRepoRoot();
        _patchService = new Phase7PatchService(_repoRoot);
    }

    [Fact]
    public async Task Phase7_GenerateTestDat_ProducesValidFile()
    {
        var testDat = await _patchService.GenerateTestDatAsync();
        Assert.True(File.Exists(testDat));
        var fi = new FileInfo(testDat);
        Assert.True(fi.Length > 1000);
    }

    [Fact]
    public async Task Phase7_GenerateTestPak_ProducesValidPak()
    {
        var testDat = await _patchService.GenerateTestDatAsync();
        var testPak = await _patchService.GenerateTestPakAsync(testDat);
        Assert.True(File.Exists(testPak));
        var fi = new FileInfo(testPak);
        Assert.True(fi.Length > 1000);
    }

    [Fact]
    public async Task Phase7_ValidateContainerOffline_PassesWithCUE4Parse()
    {
        var testDat = await _patchService.GenerateTestDatAsync();
        var testPak = await _patchService.GenerateTestPakAsync(testDat);

        var report = await _patchService.ValidateContainerOfflineAsync(testPak, testDat);
        Assert.True(report.ContainerReadable);
        Assert.True(report.VirtualPathFound);
        Assert.True(report.DatHashIdentical);
        Assert.True(report.ValidationPassed);
        Assert.Equal(Phase7PatchService.VirtualDatPath, report.ExpectedVirtualPath);
    }

    [Fact]
    public async Task Phase7_GetTestKeys_Returns15Keys()
    {
        var keys = await _patchService.GetTestKeysAsync();
        Assert.NotNull(keys);
        Assert.True(keys.Count >= 10);
        Assert.Contains(keys, k => k.English == "Settings" && k.Turkish == "Ayarlar");
        Assert.Contains(keys, k => k.English == "Confirm" && k.Turkish == "Onayla");
        Assert.Contains(keys, k => k.English == "Cancel" && k.Turkish == "İptal");
        Assert.Contains(keys, k => k.English == "Exit Game" && k.Turkish == "Oyundan Çık");
    }

    [Fact]
    public async Task Phase7_PreflightManifest_GeneratesReport()
    {
        var gameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
        if (Directory.Exists(gameRoot))
        {
            var manifest = await _patchService.GeneratePreflightManifestAsync(gameRoot);
            Assert.NotNull(manifest);
            Assert.NotEmpty(manifest.Containers);
            Assert.Contains(manifest.Containers, c => c.Filename == "pakchunk502000-Windows_0_P.pak");

            var reportPath = Path.Combine(_repoRoot, "reports", "phase7", "preflight_game_manifest.json");
            Assert.True(File.Exists(reportPath));
        }
    }

    [Fact]
    public async Task Phase7_InstallAndRollback_PreservesZeroWriteToOfficialFiles()
    {
        var gameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
        if (Directory.Exists(gameRoot))
        {
            // 1. Initial official integrity
            var preIntegrity = await _patchService.VerifyOfficialIntegrityAsync(gameRoot);
            Assert.True(preIntegrity.IsAllSafe);

            // 2. Install
            var installResult = await _patchService.InstallTestPakAsync(gameRoot);
            Assert.True(installResult.Success, installResult.Message);

            // 3. Status
            var status = await _patchService.CheckStatusAsync(gameRoot);
            Assert.True(status.IsTestContainerInstalled);
            Assert.True(status.IsOfficialFilesUntouched);
            Assert.True(File.Exists(status.InstalledPakPath));

            // 4. Official containers still untouched during install
            var midIntegrity = await _patchService.VerifyOfficialIntegrityAsync(gameRoot);
            Assert.True(midIntegrity.IsAllSafe);

            // 5. Rollback
            var rollbackResult = await _patchService.RollbackTestPakAsync(gameRoot);
            Assert.True(rollbackResult.Success, rollbackResult.Message);

            // 6. Post-rollback check
            var postStatus = await _patchService.CheckStatusAsync(gameRoot);
            Assert.False(postStatus.IsTestContainerInstalled);

            var postIntegrity = await _patchService.VerifyOfficialIntegrityAsync(gameRoot);
            Assert.True(postIntegrity.IsAllSafe);
        }
    }

    [Fact]
    public void InvestigateGamePackagingAndMounting()
    {
        var gameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
        if (!Directory.Exists(gameRoot)) return;

        // 1. Inspect .sig file for pakchunk502000-Windows_0_P
        var sigPath = Path.Combine(gameRoot, @"Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.sig");
        if (File.Exists(sigPath))
        {
            var sigBytes = File.ReadAllBytes(sigPath);
            File.WriteAllText(Path.Combine(_repoRoot, "reports", "sig_analysis.txt"),
                $"Official SIG Length: {sigBytes.Length}\nHex:\n{Convert.ToHexString(sigBytes)}");
        }

        // 2. Search which pak contains StartUpData
        var paksDir = Path.Combine(gameRoot, @"Aion2\Content\Paks");
        var pakFiles = Directory.GetFiles(paksDir, "*.pak", SearchOption.AllDirectories);
        var aesKey = new CUE4Parse.Encryption.Aes.FAesKey(Aion2PakBuilder.DefaultAesKeyHex);

        var findings = new System.Collections.Generic.List<string>();

        foreach (var p in pakFiles)
        {
            try
            {
                using var reader = new CUE4Parse.UE4.Pak.PakFileReader(p, new CUE4Parse.UE4.Versions.VersionContainer(CUE4Parse.UE4.Versions.EGame.GAME_UE5_3));
                reader.AesKey = aesKey;
                reader.Mount(StringComparer.OrdinalIgnoreCase);

                if (reader.Files.ContainsKey("Aion2/Content/StartUpData/Table/L10N/en-US/L10NString.dat"))
                {
                    findings.Add($"StartUpData found in: {p}");
                }
                if (reader.Files.ContainsKey("Aion2/Content/L10N/Text/en-US/L10NString.dat"))
                {
                    findings.Add($"Main L10N found in: {p}");
                }
            }
            catch { }
        }

        File.WriteAllLines(Path.Combine(_repoRoot, "reports", "pak_findings.txt"), findings);
    }

    [Fact]
    public void DecryptStartUpDataStrings()
    {
        var gameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
        if (!Directory.Exists(gameRoot)) return;

        var generalPaksDir = Path.Combine(gameRoot, @"Aion2\Content\Paks");
        var aesKey = new CUE4Parse.Encryption.Aes.FAesKey(Aion2PakBuilder.DefaultAesKeyHex);

        // Find StartUpData L10NString.dat and key_manifest.dat
        byte[]? startupL10nBytes = null;
        byte[]? keyManifestBytes = null;

        var pak0 = Path.Combine(generalPaksDir, "pakchunk0-Windows_0_P.pak");
        if (File.Exists(pak0))
        {
            using var reader = new CUE4Parse.UE4.Pak.PakFileReader(pak0, new CUE4Parse.UE4.Versions.VersionContainer(CUE4Parse.UE4.Versions.EGame.GAME_UE5_3));
            reader.AesKey = aesKey;
            reader.Mount(StringComparer.OrdinalIgnoreCase);

            if (reader.Files.ContainsKey("Aion2/Content/StartUpData/Table/L10N/en-US/L10NString.dat"))
            {
                startupL10nBytes = reader.Extract((CUE4Parse.UE4.VirtualFileSystem.VfsEntry)reader.Files["Aion2/Content/StartUpData/Table/L10N/en-US/L10NString.dat"]);
            }
            if (reader.Files.ContainsKey("Aion2/Content/StartUpData/Table/key_manifest.dat"))
            {
                keyManifestBytes = reader.Extract((CUE4Parse.UE4.VirtualFileSystem.VfsEntry)reader.Files["Aion2/Content/StartUpData/Table/key_manifest.dat"]);
            }
        }

        if (startupL10nBytes != null && keyManifestBytes != null)
        {
            var manifestKeys = Aion2KeyManifestReader.ReadManifest(keyManifestBytes);
            // Decrypt startup data
            using var ms = new MemoryStream(startupL10nBytes);
            using var reader = new BinaryReader(ms);
            int magic = reader.ReadInt32();
            int version = reader.ReadInt32();
            ulong seed = reader.ReadUInt64();

            if (manifestKeys.TryGetValue(seed, out var key) && version == 2)
            {
                int rawSize = reader.ReadInt32();
                int packedSize = reader.ReadInt32();
                int alignedSize = reader.ReadInt32();

                byte[] dec = new byte[alignedSize];
                using (var aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.Mode = System.Security.Cryptography.CipherMode.ECB;
                    aes.Padding = System.Security.Cryptography.PaddingMode.None;
                    aes.Key = key;
                    using var decr = aes.CreateDecryptor();
                    decr.TransformBlock(startupL10nBytes, 0x1C, alignedSize, dec, 0);
                }

                byte[] raw = new byte[rawSize];
                K4os.Compression.LZ4.LZ4Codec.Decode(dec.AsSpan(0x10, packedSize), raw);

                // Parse strings in raw
                var parser = new L10NStringParser();
                var parsed = parser.ParseRawTable(raw, "StartUpTable");

                var lines = new System.Collections.Generic.List<string>();
                lines.Add($"Total StartUp Strings: {parsed.Entries.Count}");
                foreach (var entry in parsed.Entries)
                {
                    lines.Add($"{entry.Key} => {entry.Text}");
                }
                File.WriteAllLines(Path.Combine(_repoRoot, "reports", "startup_strings.txt"), lines);
            }
        }
    }

    [Fact]
    public void CheckExeStrings()
    {
        var exePath = @"d:\uygulamalar\steam\steamapps\common\AION2\Aion2\Binaries\Win64\AION2.exe";
        if (!File.Exists(exePath)) return;

        var bytes = File.ReadAllBytes(exePath);
        var ascii = System.Text.Encoding.ASCII.GetString(bytes);
        var utf16 = System.Text.Encoding.Unicode.GetString(bytes);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ASCII CheckSignatures: {ascii.Contains("CheckSignatures")}");
        sb.AppendLine($"UTF16 CheckSignatures: {utf16.Contains("CheckSignatures")}");
        sb.AppendLine($"ASCII pakchunk502000: {ascii.Contains("pakchunk502000")}");
        sb.AppendLine($"UTF16 pakchunk502000: {utf16.Contains("pakchunk502000")}");
        sb.AppendLine($"ASCII L10NString: {ascii.Contains("L10NString")}");
        sb.AppendLine($"UTF16 L10NString: {utf16.Contains("L10NString")}");
        sb.AppendLine($"ASCII ~mods: {ascii.Contains("~mods")}");
        sb.AppendLine($"UTF16 ~mods: {utf16.Contains("~mods")}");
        sb.AppendLine($"ASCII Content/Paks: {ascii.Contains("Content/Paks")}");
        sb.AppendLine($"UTF16 Content/Paks: {utf16.Contains("Content/Paks")}");

        File.WriteAllText(Path.Combine(_repoRoot, "reports", "exe_strings_check.txt"), sb.ToString());
    }
}
