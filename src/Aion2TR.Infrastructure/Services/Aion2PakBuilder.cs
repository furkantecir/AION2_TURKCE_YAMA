using System;
using System.IO;
using System.Security.Cryptography;

namespace Aion2TR.Infrastructure.Services;

public class Aion2PakBuilder
{
    public const string DefaultAesKeyHex = "0x06038EF544B6007614F8574F1B7C2A3F0D565F74CDCC1B366B4EA1A17B97CBFF";
    public const uint PakMagic = 0x5A6F12E1;
    public const int PakVersion = 11; // PakFile_Version_Fnv64BugFix

    // 160-byte decrypted index template captured from official pakchunk502000-Windows_0_P.pak
    private static readonly byte[] OfficialIndexTemplate = Convert.FromHexString(
        "280000002E2E2F2E2E2F2E2E2F41696F6E322F436F6E74656E742F4C31304E2F546578742F656E2D55532F00" + // 44 bytes MountPoint ("../../../Aion2/Content/L10N/Text/en-US/\0")
        "01000000" +                                                                                 // 4 bytes NumEntries (1)
        "DF0EBCDE00000000" +                                                                         // 8 bytes f1 (Path/FNV64 hash)
        "01000000" +                                                                                 // 4 bytes f2
        "9527390000000000" +                                                                         // 8 bytes f3 (Index offset + 160)
        "2000000000000000" +                                                                         // 8 bytes f4
        "8774844FD963B204DE98CE8251C311AE419E98F8" +                                                 // 20 bytes f5 (Hash)
        "01000000" +                                                                                 // 4 bytes f6
        "B527390000000000" +                                                                         // 8 bytes f7 (f3 + 32)
        "3000000000000000" +                                                                         // 8 bytes f8
        "C20F6C962819F4792217A0F53C2464992E9C2508" +                                                 // 20 bytes f9 (Hash)
        "0C000000" +                                                                                 // 4 bytes f10
        "000040E000000000" +                                                                         // 8 bytes f11 (Flags)
        "B426390000000000" +                                                                         // 8 bytes f12 (Payload Size: 3,745,460)
        "28000000"                                                                                   // 4 bytes f13 (40)
    );

    // 80-byte encrypted secondary filename index captured from official pakchunk502000-Windows_0_P.pak
    private static readonly byte[] SecondaryIndexTemplate = Convert.FromHexString(
        "8A42C1670BF4E83AED238D2B0B4F13283268D0A89A5AFBFD3FDA81A2BE5F2FEC370CBBFE2674BDE48650B0200421F9846692A35FB1B598F31A4A4095336456F1E567FD3B85A8B6F8AFF042AB3AF9E31F"
    );

    private readonly byte[] _aesKey;

    public Aion2PakBuilder(string? aesKeyHex = null)
    {
        var hex = aesKeyHex ?? DefaultAesKeyHex;
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        _aesKey = Convert.FromHexString(hex);
    }

    public byte[] BuildSingleFilePak(byte[] fileData)
    {
        ArgumentNullException.ThrowIfNull(fileData);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 1. Calculate file payload properties
        long entryOffset = 0;
        long uncompressedSize = fileData.Length;
        byte[] fileSha1;
        using (var sha1 = SHA1.Create())
        {
            fileSha1 = sha1.ComputeHash(fileData);
        }

        // Align payload for AES-ECB (block size 16)
        int padLen = (16 - (fileData.Length % 16)) % 16;
        byte[] paddedData = new byte[fileData.Length + padLen];
        Buffer.BlockCopy(fileData, 0, paddedData, 0, fileData.Length);

        byte[] encryptedPayload;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = _aesKey;
            using var encryptor = aes.CreateEncryptor();
            encryptedPayload = encryptor.TransformFinalBlock(paddedData, 0, paddedData.Length);
        }
        long compressedSize = encryptedPayload.Length;

        // Write FPakEntry Header at offset 0 (Exact 53 bytes for uncompressed UE5.3 entry)
        writer.Write(entryOffset);           // 8 bytes: Offset (0)
        writer.Write(uncompressedSize);      // 8 bytes: Size (uncompressed size, UE5.3 aligns to 16 on decryption)
        writer.Write(uncompressedSize);      // 8 bytes: UncompressedSize
        writer.Write(0);                     // 4 bytes: CompressionMethod (0 = None)
        writer.Write(fileSha1);              // 20 bytes: SHA-1 Hash
        writer.Write((byte)1);               // 1 byte: bEncrypted (1)
        writer.Write(0);                     // 4 bytes: CompressionBlockSize (0)
        // Total: 8 + 8 + 8 + 4 + 20 + 1 + 4 = 53 bytes

        // Write Encrypted Payload (aligned to 16 bytes for AES-ECB)
        writer.Write(encryptedPayload);

        // 2. Build Primary Index (160 bytes)
        long indexOffset = ms.Position;
        byte[] indexTemplate = (byte[])OfficialIndexTemplate.Clone();

        long f3Val = indexOffset + 160;
        long f7Val = f3Val + 32;
        long f12Val = uncompressedSize;

        // Patch f3 at offset 60 (44 MountPoint + 4 NumEntries + 8 f1 + 4 f2 = 60)
        Buffer.BlockCopy(BitConverter.GetBytes(f3Val), 0, indexTemplate, 60, 8);

        // Patch f7 at offset 100 (60 + 8 f3 + 8 f4 + 20 f5Hash + 4 f6 = 100)
        Buffer.BlockCopy(BitConverter.GetBytes(f7Val), 0, indexTemplate, 100, 8);

        // Patch f12 at offset 148 (100 + 8 f7 + 8 f8 + 20 f9Hash + 4 f10 + 8 f11 = 148)
        Buffer.BlockCopy(BitConverter.GetBytes(f12Val), 0, indexTemplate, 148, 8);

        // Encrypt Primary Index (160 bytes is already divisible by 16)
        byte[] encryptedIndex;
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = _aesKey;
            using var encryptor = aes.CreateEncryptor();
            encryptedIndex = encryptor.TransformFinalBlock(indexTemplate, 0, indexTemplate.Length);
        }

        byte[] indexSha1;
        using (var sha1 = SHA1.Create())
        {
            indexSha1 = sha1.ComputeHash(encryptedIndex);
        }

        writer.Write(encryptedIndex);

        // 3. Write Secondary Filename Index (80 bytes)
        writer.Write(SecondaryIndexTemplate);

        // 4. Write FPakInfo Footer (221 bytes)
        byte[] guidZeros = new byte[16];
        writer.Write(guidZeros);         // 16 bytes Encryption Key GUID
        writer.Write((byte)1);           // 1 byte EncryptedIndex
        writer.Write(PakMagic);          // 4 bytes Magic (0x5A6F12E1)
        writer.Write(PakVersion);        // 4 bytes Version (11)
        writer.Write(indexOffset);       // 8 bytes IndexOffset
        writer.Write((long)160);         // 8 bytes IndexSize
        writer.Write(indexSha1);         // 20 bytes IndexHash
        writer.Write((byte)0);           // 1 byte bFrozenIndex

        byte[] compressionMethods = new byte[159]; // 159 bytes padding/compression slots
        writer.Write(compressionMethods);

        return ms.ToArray();
    }
}
