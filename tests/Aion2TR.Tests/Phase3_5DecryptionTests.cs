using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Aion2TR.Core.Models;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using K4os.Compression.LZ4;
using Xunit;

namespace Aion2TR.Tests;

public class Phase3_5DecryptionTests
{
    private const string GameRoot = @"d:\uygulamalar\steam\steamapps\common\AION2";
    private const string Aion2AesKeyHex = "0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF";

    private static bool IsGameAvailable => Directory.Exists(GameRoot);
    private static (byte[] Manifest, byte[] L10nContainer)? _cachedFiles;
    private static bool _extractionAttempted;

    private static (byte[] Manifest, byte[] L10nContainer)? ExtractGameFiles()
    {
        if (_extractionAttempted) return _cachedFiles;
        _extractionAttempted = true;

        if (!IsGameAvailable) return null;

        var generalPaks = Path.Combine(GameRoot, "Aion2", "Content", "Paks");
        var l10nPaks = Path.Combine(GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");

        var provider = new DefaultFileProvider(new DirectoryInfo(generalPaks), SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_3));
        provider.Initialize();
        var aesKey = new FAesKey(Aion2AesKeyHex);
        foreach (var vfs in provider.UnloadedVfs)
        {
            try { provider.SubmitKey(vfs.EncryptionKeyGuid, aesKey); } catch { }
        }

        var l10nProvider = new DefaultFileProvider(new DirectoryInfo(l10nPaks), SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_3));
        l10nProvider.Initialize();
        foreach (var vfs in l10nProvider.UnloadedVfs)
        {
            try { l10nProvider.SubmitKey(vfs.EncryptionKeyGuid, aesKey); } catch { }
        }

        if (provider.Files.TryGetValue("Aion2/Content/StartUpData/Table/key_manifest.dat", out var manifestFile) &&
            l10nProvider.Files.TryGetValue("Aion2/Content/L10N/Text/en-US/L10NString.dat", out var l10nFile))
        {
            _cachedFiles = (provider.SaveAsset(manifestFile.Path), l10nProvider.SaveAsset(l10nFile.Path));
        }

        return _cachedFiles;
    }

    [Fact]
    public void Pak_MountDetails()
    {
        if (!IsGameAvailable) return;
        var l10nPaks = Path.Combine(GameRoot, "Aion2", "Content", "Paks", "L10N", "Text", "en-US");
        var l10nProvider = new DefaultFileProvider(new DirectoryInfo(l10nPaks), SearchOption.AllDirectories, false, new VersionContainer(EGame.GAME_UE5_3));
        l10nProvider.Initialize();

        Console.WriteLine($"Total UnloadedVfs: {l10nProvider.UnloadedVfs.Count}");
        foreach (var vfs in l10nProvider.UnloadedVfs)
        {
            Console.WriteLine($"VFS Type: {vfs.GetType().Name}, Name: {vfs.Name}, MountPoint: '{vfs.MountPoint}', IsEncrypted: {vfs.IsEncrypted}");
        }

        var aesKey = new FAesKey(Aion2AesKeyHex);
        foreach (var vfs in l10nProvider.UnloadedVfs)
        {
            try { l10nProvider.SubmitKey(vfs.EncryptionKeyGuid, aesKey); } catch (Exception ex) { Console.WriteLine($"SubmitKey failed: {ex.Message}"); }
        }

        Console.WriteLine($"Total MountedVfs: {l10nProvider.MountedVfs.Count}");
        var pakPath = Path.Combine(l10nPaks, "pakchunk502000-Windows_0_P.pak");
        Assert.True(File.Exists(pakPath));
        Assert.Equal(2, l10nProvider.MountedVfs.Count);
        Assert.True(l10nProvider.Files.ContainsKey("Aion2/Content/L10N/Text/en-US/L10NString.dat"));
    }

    [Fact]
    public void KeyManifest_DeriveL10NSeed_ReturnsExpectedConstant()
    {
        ulong seed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");
        // 0xA875363B0593FEA3 verified via CUE4Parse Blake3Hash("L10NString_en-US")
        Assert.Equal(0xA875363B0593FEA3UL, seed);
    }

    [Fact]
    public void KeyManifest_DecryptsSuccessfully()
    {
        var files = ExtractGameFiles();
        if (files == null) return; // Skip if environment lacks game files

        var manifestData = files.Value.Manifest;
        var keys = Aion2KeyManifestReader.ReadManifest(manifestData);

        Assert.NotNull(keys);
        Assert.True(keys.Count >= 700, $"Expected at least 700 keys, got {keys.Count}");
    }

    [Fact]
    public void KeyManifest_ContainsL10NSeed()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        ulong l10nSeed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");

        Assert.True(keys.ContainsKey(l10nSeed), $"Seed 0x{l10nSeed:X16} was not found in key_manifest.dat!");
        Assert.Equal(32, keys[l10nSeed].Length);
    }

    [Fact]
    public void L10N_Header_IsValid()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        ulong l10nSeed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");
        var header = Aion2L10NDecryptor.ParseHeader(files.Value.L10nContainer, l10nSeed);

        Assert.True(header.IsValid);
        Assert.Equal(2, header.Version);
        Assert.Equal(2, header.EncryptionType); // CompressedAES
        Assert.True(header.PackedSize > 0);
        Assert.True(header.RawSize > 0);
        Assert.True(header.AlignedSize >= header.PackedSize);
        Assert.Equal(3745400, header.PackedSize);
        Assert.Equal(17429693, header.RawSize);
    }

    [Fact]
    public void L10N_Seed_ResolvesToAESKey()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        ulong l10nSeed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");

        Assert.True(keys.TryGetValue(l10nSeed, out byte[]? aesKey));
        Assert.NotNull(aesKey);
        Assert.Equal(32, aesKey.Length);
        Assert.Equal("0CFDAB7BCB727E404A20577C83BE3399207F02AB68C18F300D36FD3C66CB218E", Convert.ToHexString(aesKey));
    }

    [Fact]
    public void L10N_AESDecrypt_Succeeds()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        ulong l10nSeed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");
        var header = Aion2L10NDecryptor.ParseHeader(files.Value.L10nContainer, l10nSeed);

        // AES decrypt payload
        byte[] aesKey = keys[l10nSeed];
        byte[] decrypted = new byte[header.AlignedSize];
        using (var aes = System.Security.Cryptography.Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = aesKey;
            using var decryptor = aes.CreateDecryptor();
            int written = decryptor.TransformBlock(files.Value.L10nContainer, Aion2L10NDecryptor.PayloadOffset, header.AlignedSize, decrypted, 0);
            Assert.Equal(header.AlignedSize, written);
        }

        Assert.NotEmpty(decrypted);
    }

    [Fact]
    public void L10N_LZ4Decode_ReturnsRawSize()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (header, rawBuffer) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        Assert.Equal(header.RawSize, rawBuffer.Length);
        Assert.Equal(17429693, rawBuffer.Length);

        // Verify known SHA-256 of decompressed buffer
        using var sha = SHA256.Create();
        string rawHash = Convert.ToHexString(sha.ComputeHash(rawBuffer)).ToLowerInvariant();
        Assert.Equal("098b3b7099cd8bf24fc302a65ea32e2fff0f3276142a7dcf54da73c67f85bb7c", rawHash);
    }

    [Fact]
    public void L10N_SourceSHA256_RemainsUnchanged()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        using var sha = SHA256.Create();
        string beforeHash = Convert.ToHexString(sha.ComputeHash(files.Value.L10nContainer)).ToLowerInvariant();

        // Perform decryption
        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (header, raw) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        string afterHash = Convert.ToHexString(sha.ComputeHash(files.Value.L10nContainer)).ToLowerInvariant();
        Assert.Equal(beforeHash, afterHash);
        Assert.Equal("694a7d39e6bbe27c85dafa127d3feaca2a4a7a3c502ccf7385078b9c74dc6a3f", afterHash);
    }

    [Fact]
    public void Parser_DoesNotModifyGameFiles()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var parser = new L10NStringParser();
        var originalCopy = (byte[])files.Value.L10nContainer.Clone();

        var result = parser.Parse(files.Value.L10nContainer, "L10NString.dat");

        Assert.Equal(originalCopy, files.Value.L10nContainer);
        Assert.Equal(2, result.Version);
    }

    [Fact]
    public void L10N_RawTable_ParsesCorrectly()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (_, rawBuffer) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        var parser = new L10NStringParser();
        var parseResult = parser.ParseRawTable(rawBuffer, "MainTable");

        Assert.True(parseResult.Success);
        Assert.Equal(1, parseResult.Version);
        Assert.Equal(153042, parseResult.EntryCount);
        Assert.Equal(153042, parseResult.Entries.Count);
        Assert.Equal("AION2", parseResult.Metadata["Namespace"]);

        // Verify first entry has valid text
        var first = parseResult.Entries[0];
        Assert.False(string.IsNullOrWhiteSpace(first.Key));
        Assert.False(string.IsNullOrWhiteSpace(first.Text));
    }
}
