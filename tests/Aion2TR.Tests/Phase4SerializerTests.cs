using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Aion2TR.Core.Models;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Xunit;

namespace Aion2TR.Tests;

public class Phase4SerializerTests
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
    public void Serializer_PreservesTurkishUnicode()
    {
        var parser = new L10NStringParser();

        const string testNamespace = "AION2";
        const string turkishSample = "Çığ şoförü, öğle öğününde üzüm yedi. İSTANBUL 123 !?";

        var entries = new List<L10NEntry>
        {
            new("TEST_KEY_1", turkishSample, 0, false, true),
            new("TEST_KEY_2", "Ayarlar", 1, false, false),
            new("TEST_KEY_3", "İptal", 2, false, true),
            new("TEST_KEY_4", "Onayla", 3, false, false)
        };

        byte[] rawBytes = L10NStringSerializer.Serialize(entries, testNamespace, 1);
        Assert.NotEmpty(rawBytes);

        var parseResult = parser.ParseRawTable(rawBytes, "TurkishTest");
        Assert.True(parseResult.Success);
        Assert.Equal(4, parseResult.EntryCount);
        Assert.Equal(turkishSample, parseResult.Entries[0].Text);
        Assert.Equal("Ayarlar", parseResult.Entries[1].Text);
        Assert.Equal("İptal", parseResult.Entries[2].Text);
        Assert.Equal("Onayla", parseResult.Entries[3].Text);
    }

    [Fact]
    public void Serializer_PreservesDuplicates()
    {
        var parser = new L10NStringParser();

        var entries = new List<L10NEntry>
        {
            new("CONFIRM_BTN_1", "Confirm", 0, false, false),
            new("CONFIRM_BTN_2", "Confirm", 1, false, false),
            new("CONFIRM_BTN_3", "Confirm", 2, false, false)
        };

        byte[] raw = L10NStringSerializer.Serialize(entries, "AION2", 1);
        var parsed = parser.ParseRawTable(raw, "DupTest");

        Assert.Equal(3, parsed.EntryCount);
        Assert.Equal("CONFIRM_BTN_1", parsed.Entries[0].Key);
        Assert.Equal("CONFIRM_BTN_2", parsed.Entries[1].Key);
        Assert.Equal("CONFIRM_BTN_3", parsed.Entries[2].Key);
        Assert.All(parsed.Entries, e => Assert.Equal("Confirm", e.Text));
    }

    [Fact]
    public void RawTable_ParseSerializeParse_PreservesAllEntries()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (_, rawOriginal) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        var parser = new L10NStringParser();
        var originalParsed = parser.ParseRawTable(rawOriginal, "Original");
        Assert.Equal(153042, originalParsed.EntryCount);

        string ns = originalParsed.Metadata.TryGetValue("Namespace", out var nsObj) ? nsObj.ToString()! : "AION2";
        byte[] serializedRaw = L10NStringSerializer.Serialize(
            originalParsed.Entries,
            ns,
            originalParsed.Version);

        // Byte-identical round trip test on raw data
        Assert.Equal(rawOriginal.Length, serializedRaw.Length);
        Assert.Equal(rawOriginal, serializedRaw);

        // Parse serialized data again
        var reParsed = parser.ParseRawTable(serializedRaw, "ReParsed");
        Assert.Equal(originalParsed.EntryCount, reParsed.EntryCount);
        Assert.Equal(originalParsed.Version, reParsed.Version);
    }

    [Fact]
    public void Serializer_Preserves153042Entries()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (_, rawOriginal) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        var parser = new L10NStringParser();
        var originalParsed = parser.ParseRawTable(rawOriginal, "Original");

        byte[] serializedRaw = L10NStringSerializer.Serialize(originalParsed.Entries, "AION2", originalParsed.Version);

        var reParsed = parser.ParseRawTable(serializedRaw, "CheckCount");
        Assert.Equal(153042, reParsed.EntryCount);
    }

    [Fact]
    public void Serializer_PreservesKeys()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (_, rawOriginal) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        var parser = new L10NStringParser();
        var originalParsed = parser.ParseRawTable(rawOriginal, "Original");

        byte[] serializedRaw = L10NStringSerializer.Serialize(originalParsed.Entries, "AION2", originalParsed.Version);

        var reParsed = parser.ParseRawTable(serializedRaw, "CheckKeys");

        for (int i = 0; i < 1000; i++) // Sample first 1000
        {
            Assert.Equal(originalParsed.Entries[i].Key, reParsed.Entries[i].Key);
            Assert.Equal(originalParsed.Entries[i].Text, reParsed.Entries[i].Text);
        }
    }

    [Fact]
    public void Serializer_PreservesOrdering()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (_, rawOriginal) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        var parser = new L10NStringParser();
        var originalParsed = parser.ParseRawTable(rawOriginal, "Original");

        byte[] serializedRaw = L10NStringSerializer.Serialize(originalParsed.Entries, "AION2", originalParsed.Version);
        var reParsed = parser.ParseRawTable(serializedRaw, "CheckOrder");

        Assert.Equal(originalParsed.Entries[0].Key, reParsed.Entries[0].Key);
        Assert.Equal(originalParsed.Entries[50000].Key, reParsed.Entries[50000].Key);
        Assert.Equal(originalParsed.Entries[100000].Key, reParsed.Entries[100000].Key);
        Assert.Equal(originalParsed.Entries[^1].Key, reParsed.Entries[^1].Key);
    }

    [Fact]
    public void Container_HeaderSizesAreValid()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (header, rawOriginal) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        byte[] rebuiltContainer = Aion2L10NBuilder.BuildContainer(rawOriginal, "en-US", keys);

        ulong seed = Aion2KeyManifestReader.DeriveL10NSeed("en-US");
        var rebuiltHeader = Aion2L10NDecryptor.ParseHeader(rebuiltContainer, seed);

        Assert.True(rebuiltHeader.IsValid);
        Assert.Equal(2, rebuiltHeader.Version);
        Assert.Equal(rawOriginal.Length, rebuiltHeader.RawSize);
        Assert.True(rebuiltHeader.PackedSize > 0);
        Assert.True(rebuiltHeader.AlignedSize >= rebuiltHeader.PackedSize);
        Assert.Equal(0, rebuiltHeader.AlignedSize % 16);
    }

    [Fact]
    public void Container_DecryptsAfterRebuild()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (_, rawOriginal) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        byte[] rebuiltContainer = Aion2L10NBuilder.BuildContainer(rawOriginal, "en-US", keys);

        var (rebuiltHeader, decryptedRaw) = Aion2L10NDecryptor.Decrypt(rebuiltContainer, "en-US", keys);
        Assert.Equal(rawOriginal.Length, decryptedRaw.Length);
        Assert.Equal(rawOriginal, decryptedRaw);
    }

    [Fact]
    public void Container_RebuildDecryptParse_PreservesAllEntries()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        var keys = Aion2KeyManifestReader.ReadManifest(files.Value.Manifest);
        var (_, rawOriginal) = Aion2L10NDecryptor.Decrypt(files.Value.L10nContainer, "en-US", keys);

        var parser = new L10NStringParser();
        var originalParsed = parser.ParseRawTable(rawOriginal, "Original");

        string ns = originalParsed.Metadata.TryGetValue("Namespace", out var nsObj) ? nsObj.ToString()! : "AION2";
        byte[] serializedRaw = L10NStringSerializer.Serialize(originalParsed.Entries, ns, originalParsed.Version);

        byte[] rebuiltContainer = Aion2L10NBuilder.BuildContainer(serializedRaw, "en-US", keys);

        var (_, decryptedRaw) = Aion2L10NDecryptor.Decrypt(rebuiltContainer, "en-US", keys);
        var finalParsed = parser.ParseRawTable(decryptedRaw, "FinalRebuilt");

        Assert.Equal(153042, finalParsed.EntryCount);
        Assert.Equal(originalParsed.Entries[0].Text, finalParsed.Entries[0].Text);
        Assert.Equal(originalParsed.Entries[^1].Text, finalParsed.Entries[^1].Text);
    }

    [Fact]
    public void SourceGameFiles_SHA256_Unchanged()
    {
        var files = ExtractGameFiles();
        if (files == null) return;

        using var sha = SHA256.Create();
        string manifestSha = Convert.ToHexString(sha.ComputeHash(files.Value.Manifest)).ToLowerInvariant();
        string l10nSha = Convert.ToHexString(sha.ComputeHash(files.Value.L10nContainer)).ToLowerInvariant();

        Assert.Equal("694a7d39e6bbe27c85dafa127d3feaca2a4a7a3c502ccf7385078b9c74dc6a3f", l10nSha);
        Assert.Equal("d44e94bec85f16d41089cd104f0143cb74ef86eeb5e141d3c180fdab29616963", manifestSha);
    }
}
