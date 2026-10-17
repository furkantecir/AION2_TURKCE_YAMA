using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Blake3;

namespace Aion2TR.Infrastructure.Services;

/// <summary>
/// Verified reader and decryptor for AION 2 key_manifest.dat.
/// Extracts AES keys indexed by 64-bit BLAKE3 seeds.
/// </summary>
public static class Aion2KeyManifestReader
{
    public const int PayloadOffset = 8;
    public const int EntrySize = 0x30; // 48 bytes (8-byte seed, 32-byte AES key, 8-byte padding)

    private static readonly byte[] ManifestHashKeyMaterial =
    [
        0xeb, 0x0b, 0xc0, 0x97, 0x28, 0x47, 0x5a, 0xb5,
        0x19, 0x6a, 0xf8, 0xce, 0x78, 0x5d, 0x8a, 0x79,
        0xdf, 0x18, 0xd2, 0x14, 0x8f, 0x51, 0xcb, 0xef,
        0x8b, 0x39, 0xe4, 0x3b, 0x2a, 0x1d, 0x40, 0x56
    ];

    /// <summary>
    /// Parses and decrypts key_manifest.dat, returning a mapping of Seed -> AES-256 Key.
    /// </summary>
    public static Dictionary<ulong, byte[]> ReadManifest(byte[] manifestData)
    {
        ArgumentNullException.ThrowIfNull(manifestData);

        if (manifestData.Length < PayloadOffset || (manifestData.Length - PayloadOffset) % EntrySize != 0)
        {
            throw new InvalidDataException(
                $"Invalid key_manifest.dat size ({manifestData.Length} bytes). Expected at least {PayloadOffset} bytes and a multiple of {EntrySize}.");
        }

        int count = (manifestData.Length - PayloadOffset) / EntrySize;

        // Derive manifest AES-256 key from hash key material using BLAKE3
        using var hasher = Hasher.New();
        hasher.Update(ManifestHashKeyMaterial);
        byte[] manifestAesKey = hasher.Finalize().AsSpan().ToArray();

        // AES-256-ECB Decrypt payload
        int payloadLen = count * EntrySize;
        byte[] decrypted = new byte[payloadLen];
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = manifestAesKey;
            using var decryptor = aes.CreateDecryptor();
            decryptor.TransformBlock(manifestData, PayloadOffset, payloadLen, decrypted, 0);
        }

        var keys = new Dictionary<ulong, byte[]>(count);
        using var ms = new MemoryStream(decrypted);
        using var reader = new BinaryReader(ms);

        for (int i = 0; i < count; i++)
        {
            ulong seed = reader.ReadUInt64();
            byte[] key = reader.ReadBytes(32);
            reader.ReadBytes(8); // 8-byte zero padding
            keys[seed] = key;
        }

        return keys;
    }

    /// <summary>
    /// Computes the 64-bit BLAKE3 seed for a given L10N locale (e.g. "en-US" -> Blake3("L10NString_en-US")).
    /// </summary>
    public static ulong DeriveL10NSeed(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        string seedString = $"L10NString_{locale}";
        using var hasher = Hasher.New();
        hasher.Update(Encoding.UTF8.GetBytes(seedString));
        return BinaryPrimitives.ReadUInt64LittleEndian(hasher.Finalize().AsSpan());
    }
}
