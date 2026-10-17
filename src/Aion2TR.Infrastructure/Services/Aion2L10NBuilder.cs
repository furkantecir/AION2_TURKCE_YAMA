using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Aion2TR.Core.Models;
using K4os.Compression.LZ4;

namespace Aion2TR.Infrastructure.Services;

/// <summary>
/// Verified container builder for AION 2 L10NString.dat (Version 2).
/// Compresses raw table data with LZ4, encrypts with AES-256-ECB, and generates valid XOR headers.
/// </summary>
public static class Aion2L10NBuilder
{
    public const int HeaderSize = 0x14; // 20 bytes (4-byte version + 16-byte encrypted block)
    public const int PayloadOffset = 0x14;
    public const int Lz4StreamOffsetInPayload = 0x20; // 32-byte prefix

    /// <summary>
    /// Builds a full L10NString.dat container from raw table bytes.
    /// Resolves the AES-256 encryption key from the provided manifest keys dictionary.
    /// </summary>
    public static byte[] BuildContainer(
        byte[] rawTableBytes,
        string locale,
        Dictionary<ulong, byte[]> manifestKeys,
        byte[]? customPrefix = null,
        LZ4Level compressionLevel = LZ4Level.L00_FAST)
    {
        ArgumentNullException.ThrowIfNull(rawTableBytes);
        ArgumentNullException.ThrowIfNull(manifestKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);

        ulong seed = Aion2KeyManifestReader.DeriveL10NSeed(locale);
        if (!manifestKeys.TryGetValue(seed, out byte[]? aesKey))
        {
            throw new KeyNotFoundException($"No AES key found in manifest for locale '{locale}' (Seed: 0x{seed:X16}).");
        }

        return BuildContainerWithKey(rawTableBytes, seed, aesKey, customPrefix, compressionLevel);
    }

    /// <summary>
    /// Builds an L10NString.dat container using an explicit 64-bit seed and 32-byte AES key.
    /// </summary>
    public static byte[] BuildContainerWithKey(
        byte[] rawTableBytes,
        ulong seed,
        byte[] aesKey,
        byte[]? customPrefix = null,
        LZ4Level compressionLevel = LZ4Level.L00_FAST)
    {
        ArgumentNullException.ThrowIfNull(rawTableBytes);
        ArgumentNullException.ThrowIfNull(aesKey);

        if (aesKey.Length != 32)
        {
            throw new ArgumentException("AES key must be exactly 32 bytes (AES-256).", nameof(aesKey));
        }

        // 1. LZ4 Compression
        int maxCompressedSize = LZ4Codec.MaximumOutputSize(rawTableBytes.Length);
        byte[] compressedBuf = new byte[maxCompressedSize];
        int packedSize = LZ4Codec.Encode(rawTableBytes, compressedBuf, compressionLevel);

        // 2. Prepare payload buffer: 32 bytes prefix + packedSize bytes + 16-byte alignment
        int payloadDataLen = Lz4StreamOffsetInPayload + packedSize;
        int alignedSize = (payloadDataLen + 15) & ~15;

        byte[] payloadToEncrypt = new byte[alignedSize];
        if (customPrefix != null && customPrefix.Length >= Lz4StreamOffsetInPayload)
        {
            Array.Copy(customPrefix, 0, payloadToEncrypt, 0, Lz4StreamOffsetInPayload);
        }
        else
        {
            // Default 32-byte salt/zero prefix
            Array.Clear(payloadToEncrypt, 0, Lz4StreamOffsetInPayload);
        }

        Array.Copy(compressedBuf, 0, payloadToEncrypt, Lz4StreamOffsetInPayload, packedSize);

        // 3. AES-256-ECB Encrypt payload
        byte[] encryptedPayload = new byte[alignedSize];
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = aesKey;
            using var encryptor = aes.CreateEncryptor();
            encryptor.TransformBlock(payloadToEncrypt, 0, alignedSize, encryptedPayload, 0);
        }

        // 4. Construct and XOR-encrypt 16-byte container header block
        byte[] headerBlock = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(headerBlock.AsSpan(0, 4), packedSize);
        BinaryPrimitives.WriteInt32LittleEndian(headerBlock.AsSpan(4, 4), 2); // 2 = CompressedAES
        BinaryPrimitives.WriteInt32LittleEndian(headerBlock.AsSpan(8, 4), alignedSize);
        BinaryPrimitives.WriteInt32LittleEndian(headerBlock.AsSpan(12, 4), rawTableBytes.Length);

        // XOR encrypt header block with Derived Header Key
        byte[] headerKey = DeriveHeaderKey(seed, 3); // 3 = Localization
        for (int i = 0; i < 16; i++)
        {
            headerBlock[i] ^= headerKey[i % headerKey.Length];
        }

        // 5. Assemble final container: Version (4) + HeaderBlock (16) + EncryptedPayload (alignedSize)
        byte[] container = new byte[HeaderSize + alignedSize];
        BinaryPrimitives.WriteInt32LittleEndian(container.AsSpan(0, 4), 2); // Version 2
        Array.Copy(headerBlock, 0, container, 4, 16);
        Array.Copy(encryptedPayload, 0, container, PayloadOffset, alignedSize);

        return container;
    }

    private static byte[] DeriveHeaderKey(ulong seed, int type)
    {
        const ulong HeaderXorConstant = 0xCD02190910CE83F6;
        ulong headerKeyRoot = Blake3HashUlong(HeaderXorConstant);

        Span<byte> input = stackalloc byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(input, headerKeyRoot);
        BinaryPrimitives.WriteUInt64LittleEndian(input[8..], seed);
        BinaryPrimitives.WriteInt32LittleEndian(input[16..], type);
        input[20..].Clear();

        using var hasher = Blake3.Hasher.New();
        hasher.Update(input);
        return hasher.Finalize().AsSpan().ToArray();
    }

    private static ulong Blake3HashUlong(ulong value)
    {
        Span<byte> input = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(input, value);
        using var hasher = Blake3.Hasher.New();
        hasher.Update(input);
        return BinaryPrimitives.ReadUInt64LittleEndian(hasher.Finalize().AsSpan());
    }
}
