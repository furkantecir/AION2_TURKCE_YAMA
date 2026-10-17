using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Aion2TR.Infrastructure.Services;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using GenericReader;
using Xunit;

using Xunit.Abstractions;

namespace Aion2TR.Tests;

public class Phase7PakBuilderTests
{
    private readonly ITestOutputHelper _output;

    public Phase7PakBuilderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PakBuilder_CreatesValidPak_ExtractableByCUE4Parse()
    {
        var builder = new Aion2PakBuilder();
        var sampleData = Encoding.UTF8.GetBytes("Aion2TR Phase 7 In-Game Mod Pak Validation Payload 1234567890!");

        var pakBytes = builder.BuildSingleFilePak(sampleData);

        Assert.NotNull(pakBytes);
        Assert.True(pakBytes.Length > sampleData.Length);

        // Test with CUE4Parse
        var tempDir = Path.Combine(Path.GetTempPath(), "Aion2TR_PakTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var testPakPath = Path.Combine(tempDir, "pakchunk502000-Windows_99_P.pak");

        try
        {
            File.WriteAllBytes(testPakPath, pakBytes);

            var officialPakPath = Path.Combine(@"d:\uygulamalar\steam\steamapps\common\AION2\Aion2\Content\Paks\L10N\Text\en-US", "pakchunk502000-Windows_0_P.pak");
            using var officialReader = new CUE4Parse.UE4.Pak.PakFileReader(officialPakPath, new VersionContainer(EGame.GAME_UE5_3));
            var aesKey = new FAesKey(Aion2PakBuilder.DefaultAesKeyHex);
            officialReader.AesKey = aesKey;
            officialReader.Mount(StringComparer.OrdinalIgnoreCase);

            _output.WriteLine($"Official Info: Magic=0x{officialReader.Info.Magic:X8}, Version={officialReader.Info.Version}, IndexOffset={officialReader.Info.IndexOffset}, IndexSize={officialReader.Info.IndexSize}, EncryptedIndex={officialReader.Info.EncryptedIndex}");
            _output.WriteLine($"Official IndexHash: {officialReader.Info.IndexHash}");
            _output.WriteLine($"Official EncryptionKeyGuid: {officialReader.Info.EncryptionKeyGuid}");
            _output.WriteLine($"Official Files count: {officialReader.Files.Count}");
            var officialEntry = officialReader.Files["Aion2/Content/L10N/Text/en-US/L10NString.dat"];
            var officialExtracted = officialReader.Extract((CUE4Parse.UE4.VirtualFileSystem.VfsEntry)officialEntry);
            _output.WriteLine($"Official Extracted Size: {officialExtracted.Length}, first 16 bytes: {Convert.ToHexString(officialExtracted.AsSpan(0, 16))}");

            var offPakBytes = File.ReadAllBytes(officialPakPath);
            _output.WriteLine($"Official First 53 bytes hex:\n{Convert.ToHexString(offPakBytes, 0, 53)}");
            var offEntry = (CUE4Parse.UE4.Pak.Objects.FPakEntry)officialReader.Files["Aion2/Content/L10N/Text/en-US/L10NString.dat"];
            _output.WriteLine($"Official Entry: Offset={offEntry.Offset}, Size={offEntry.Size}, UncompressedSize={offEntry.UncompressedSize}, CompressionMethod={offEntry.CompressionMethod}, bEncrypted={offEntry.IsEncrypted}, StructSize={offEntry.StructSize}, CompressionBlocks={offEntry.CompressionBlocks?.Length ?? 0}, CompressionBlockSize={offEntry.CompressionBlockSize}");

            var offIndexEnc = new byte[officialReader.Info.IndexSize];
            Array.Copy(offPakBytes, officialReader.Info.IndexOffset, offIndexEnc, 0, offIndexEnc.Length);
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = Convert.FromHexString(Aion2PakBuilder.DefaultAesKeyHex[2..]);
                using var dec = aes.CreateDecryptor();
                var offIndexDec = dec.TransformFinalBlock(offIndexEnc, 0, offIndexEnc.Length);
                using var idxMs = new MemoryStream(offIndexDec);
                using var idxReader = new BinaryReader(idxMs);
                int mpLen = idxReader.ReadInt32();
                byte[] mpBytes = idxReader.ReadBytes(mpLen);
                _output.WriteLine($"MountPoint: {Encoding.ASCII.GetString(mpBytes)}");
                int numEntries = idxReader.ReadInt32();
                _output.WriteLine($"NumEntries: {numEntries}");

                long f1 = idxReader.ReadInt64(); // 8 bytes: DF0EBCDE00000000
                int f2 = idxReader.ReadInt32();  // 4 bytes: 01000000
                long f3 = idxReader.ReadInt64(); // 8 bytes: 9527390000000000 (3,745,685 = Index offset + 160!)
                long f4 = idxReader.ReadInt64(); // 8 bytes: 2000000000000000 (32)
                byte[] f5Hash = idxReader.ReadBytes(20); // 20 bytes hash
                int f6 = idxReader.ReadInt32();  // 4 bytes: 01000000
                long f7 = idxReader.ReadInt64(); // 8 bytes: B527390000000000 (3,745,717)
                long f8 = idxReader.ReadInt64(); // 8 bytes: 3000000000000000 (48)
                _output.WriteLine($"Official File Length: {offPakBytes.Length}");
                _output.WriteLine($"Official IndexOffset: {officialReader.Info.IndexOffset}, IndexSize: {officialReader.Info.IndexSize}");
                _output.WriteLine($"f3 offset in pak: {f3}, bytes remaining from f3: {offPakBytes.Length - f3}");
                var block80 = new byte[80];
                Array.Copy(offPakBytes, 3745685, block80, 0, 80);
                using (var aes80 = System.Security.Cryptography.Aes.Create())
                {
                    aes80.Mode = CipherMode.ECB;
                    aes80.Padding = PaddingMode.None;
                    aes80.Key = Convert.FromHexString(Aion2PakBuilder.DefaultAesKeyHex[2..]);
                    using var dec80Obj = aes80.CreateDecryptor();
                    var dec80 = dec80Obj.TransformFinalBlock(block80, 0, 80);
                    _output.WriteLine($"Decrypted 80 bytes:\n{Convert.ToHexString(dec80)}");
                }

                // Decrypt our index from testPakPath
                var ourPakBytes = File.ReadAllBytes(testPakPath);
                int ourIndexOffset = ourPakBytes.Length - 221 - 80 - 160;
                var ourIndexEnc = new byte[160];
                Array.Copy(ourPakBytes, ourIndexOffset, ourIndexEnc, 0, 160);
                byte[] ourIndexDec;
                using (var aesIdx = System.Security.Cryptography.Aes.Create())
                {
                    aesIdx.Mode = CipherMode.ECB;
                    aesIdx.Padding = PaddingMode.None;
                    aesIdx.Key = Convert.FromHexString(Aion2PakBuilder.DefaultAesKeyHex[2..]);
                    using var decIdx = aesIdx.CreateDecryptor();
                    ourIndexDec = decIdx.TransformFinalBlock(ourIndexEnc, 0, 160);
                }

                _output.WriteLine($"Off Dec Index Hex:\n{Convert.ToHexString(offIndexDec)}");
                _output.WriteLine($"Our Dec Index Hex:\n{Convert.ToHexString(ourIndexDec)}");

                // Decrypt our 80 bytes at offset (ourIndexOffset + 160) from testPakPath
                var our80Enc = new byte[80];
                Array.Copy(ourPakBytes, ourIndexOffset + 160, our80Enc, 0, 80);
                byte[] our80Dec;
                using (var aes80Test = System.Security.Cryptography.Aes.Create())
                {
                    aes80Test.Mode = CipherMode.ECB;
                    aes80Test.Padding = PaddingMode.None;
                    aes80Test.Key = Convert.FromHexString(Aion2PakBuilder.DefaultAesKeyHex[2..]);
                    using var dec80Test = aes80Test.CreateDecryptor();
                    our80Dec = dec80Test.TransformFinalBlock(our80Enc, 0, 80);
                }
                var offBytes48 = new byte[48];
                Array.Copy(offPakBytes, 3745717, offBytes48, 0, 48);
                byte[] offDec48;
                using (var aesOff = System.Security.Cryptography.Aes.Create())
                {
                    aesOff.Mode = CipherMode.ECB;
                    aesOff.Padding = PaddingMode.None;
                    aesOff.Key = Convert.FromHexString(Aion2PakBuilder.DefaultAesKeyHex[2..]);
                    using var decOff = aesOff.CreateDecryptor();
                    offDec48 = decOff.TransformFinalBlock(offBytes48, 0, 48);
                }
                _output.WriteLine($"offDec48 hex:\n{Convert.ToHexString(offDec48)}");

                void TraceDir(byte[] decBytes, string label)
                {
                    _output.WriteLine($"--- Tracing {label} (length {decBytes.Length}) ---");
                    using var gbr = new GenericBufferReader(decBytes);
                    int num5 = gbr.Read<int>();
                    _output.WriteLine($"num5 = {num5}");
                    for (int i = 0; i < num5; i++)
                    {
                        var s1 = gbr.ReadFStringMemory();
                        _output.WriteLine($"Dir {i}: '{s1.ToString()}' (pos={gbr.Position})");
                        int num8 = gbr.Read<int>();
                        _output.WriteLine($"num8 = {num8} (pos={gbr.Position})");
                        for (int j = 0; j < num8; j++)
                        {
                            var s2 = gbr.ReadFStringMemory();
                            _output.WriteLine($"File {j}: '{s2.ToString()}' (pos={gbr.Position})");
                            int num10 = gbr.Read<int>();
                            _output.WriteLine($"num10 = {num10} (pos={gbr.Position})");
                        }
                    }
                    _output.WriteLine($"Finished {label} successfully at pos={gbr.Position}!");
                }

                using (var msTest = new MemoryStream(ourIndexDec))
                using (var brTest = new BinaryReader(msTest))
                {
                    int mpL = brTest.ReadInt32();
                    string mpStr = Encoding.ASCII.GetString(brTest.ReadBytes(mpL));
                    int cap = brTest.ReadInt32();
                    msTest.Position += 8;
                    bool hasPathHash = brTest.ReadBoolean();
                    msTest.Position += 3; // padding for bool
                    msTest.Position += 36;
                    bool hasDirIndex = brTest.ReadBoolean();
                    msTest.Position += 3; // padding for bool
                    long readNum = brTest.ReadInt64();
                    long readNum2 = brTest.ReadInt64();
                    _output.WriteLine($"ourIndexDec parse: mp='{mpStr}', cap={cap}, hasPathHash={hasPathHash}, hasDirIndex={hasDirIndex}, num={readNum}, num2={readNum2}");
                }
            }

            using var ourReader = new CUE4Parse.UE4.Pak.PakFileReader(testPakPath, new VersionContainer(EGame.GAME_UE5_3));
            _output.WriteLine($"Our Info: Magic=0x{ourReader.Info.Magic:X8}, Version={ourReader.Info.Version}, IndexOffset={ourReader.Info.IndexOffset}, IndexSize={ourReader.Info.IndexSize}, EncryptedIndex={ourReader.Info.EncryptedIndex}");
            _output.WriteLine($"Our IndexHash: {ourReader.Info.IndexHash}");
            ourReader.AesKey = aesKey;
            try
            {
                ourReader.Mount(StringComparer.OrdinalIgnoreCase);
                _output.WriteLine($"ourReader mounted! Files count: {ourReader.Files.Count}");
                foreach (var kv in ourReader.Files)
                {
                    _output.WriteLine($"ourReader File: {kv.Key}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"ourReader.Mount EXCEPTION: {ex.GetType().FullName}: {ex.Message}");
                _output.WriteLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    _output.WriteLine($"Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                }
                throw;
            }

            Assert.True(ourReader.Files.ContainsKey("Aion2/Content/L10N/Text/en-US/L10NString.dat"));
            var entry = (CUE4Parse.UE4.Pak.Objects.FPakEntry)ourReader.Files["Aion2/Content/L10N/Text/en-US/L10NString.dat"];
            _output.WriteLine($"Entry: Offset={entry.Offset}, Size={entry.Size}, UncompressedSize={entry.UncompressedSize}, CompressionMethod={entry.CompressionMethod}, bEncrypted={entry.IsEncrypted}, StructSize={entry.StructSize}");
            var extracted = ourReader.Extract((CUE4Parse.UE4.VirtualFileSystem.VfsEntry)entry);
            _output.WriteLine($"Extracted length: {extracted.Length}");
            _output.WriteLine($"Extracted hex:\n{Convert.ToHexString(extracted)}");
            _output.WriteLine($"Sample hex:\n{Convert.ToHexString(sampleData)}");
            Assert.Equal(sampleData, extracted);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
