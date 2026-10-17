using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Aion2TR.Core.Models;
using Blake3;
using K4os.Compression.LZ4;

namespace Aion2TR.Infrastructure.Services;

/// <summary>
/// Verified container decryptor and decompressor for AION 2 L10NString.dat.
/// Implements header XOR decryption, AES-256-ECB payload decryption, and LZ4 decompression.
/// </summary>
public static class Aion2L10NDecryptor
{
    public const int HeaderSize = 0x14; // 20 bytes (4-byte version + 16-byte encrypted block)
    public const int PayloadOffset = 0x14;
    public const int Lz4StreamOffsetInPayload = 0x20;

    private const ulong HeaderXorConstant = 0xCD02190910CE83F6;
    private static readonly ulong HeaderKeyRoot = Blake3HashUlong(HeaderXorConstant);

    /// <summary>
    /// Decrypts the 16-byte container header block using DeriveHeaderKey and returns parsed container header metadata.
    /// </summary>
    public static L10NContainerHeader ParseHeader(byte[] data, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < HeaderSize)
        {
            return new L10NContainerHeader(0, seed, 0, 0, 0, 0, false, "Container data is too small for header (minimum 20 bytes required).");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
        if (version != 2)
        {
            return new L10NContainerHeader(version, seed, 0, 0, 0, 0, false, $"Unsupported version {version}. Only version 2 is supported.");
        }

        // Decrypt the 16-byte header block (offsets 0x04..0x14)
        byte[] headerBlock = data[4..20].ToArray();
        byte[] key = DeriveHeaderKey(seed, 3); // 3 = EHeaderType.Localization

        for (int i = 0; i < headerBlock.Length; i++)
        {
            headerBlock[i] ^= key[i % key.Length];
        }

        int packedSize = BinaryPrimitives.ReadInt32LittleEndian(headerBlock.AsSpan(0, 4));
        int encType = BinaryPrimitives.ReadInt32LittleEndian(headerBlock.AsSpan(4, 4));
        int alignedSize = BinaryPrimitives.ReadInt32LittleEndian(headerBlock.AsSpan(8, 4));
        int rawSize = BinaryPrimitives.ReadInt32LittleEndian(headerBlock.AsSpan(12, 4));

        bool isValid = version == 2 &&
                       rawSize > 0 &&
                       packedSize > 0 &&
                       alignedSize >= packedSize &&
                       encType == 2 && // 2 = CompressedAES
                       PayloadOffset + alignedSize <= data.Length;

        string? error = isValid ? null : "Header validation failed (size constraints or invalid encryption type).";
        return new L10NContainerHeader(version, seed, rawSize, packedSize, alignedSize, encType, isValid, error);
    }

    /// <summary>
    /// AES-256-ECB decrypts the container payload and decompresses it with LZ4 into the raw table buffer.
    /// </summary>
    public static byte[] DecryptAndDecompress(byte[] data, L10NContainerHeader header, byte[] aesKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(aesKey);

        if (!header.IsValid)
        {
            throw new InvalidOperationException($"Cannot decrypt invalid container header: {header.ErrorMessage}");
        }

        if (aesKey.Length != 32)
        {
            throw new ArgumentException("AES key must be exactly 32 bytes (AES-256).", nameof(aesKey));
        }

        if (PayloadOffset + header.AlignedSize > data.Length)
        {
            throw new InvalidDataException("Payload aligned size exceeds available data length.");
        }

        // 1. AES-256-ECB Decryption
        byte[] decryptedPayload = new byte[header.AlignedSize];
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = aesKey;
            using var decryptor = aes.CreateDecryptor();
            decryptor.TransformBlock(data, PayloadOffset, header.AlignedSize, decryptedPayload, 0);
        }

        // 2. LZ4 Decompression starting at offset 0x20 of the decrypted payload
        if (decryptedPayload.Length < Lz4StreamOffsetInPayload + header.PackedSize)
        {
            throw new InvalidDataException("Decrypted payload is shorter than required LZ4 stream offset + packed size.");
        }

        byte[] rawOutput = new byte[header.RawSize];
        int written = LZ4Codec.Decode(decryptedPayload.AsSpan(Lz4StreamOffsetInPayload, header.PackedSize), rawOutput);

        if (written != header.RawSize)
        {
            throw new InvalidDataException($"LZ4 decompression failed: decoded {written} bytes, expected {header.RawSize} bytes.");
        }

        return rawOutput;
    }

    /// <summary>
    /// Executes end-to-end decryption using the manifest dictionary and target locale.
    /// </summary>
    public static (L10NContainerHeader Header, byte[] RawBuffer) Decrypt(
        byte[] containerData,
        string locale,
        Dictionary<ulong, byte[]> manifestKeys)
    {
        ArgumentNullException.ThrowIfNull(containerData);
        ArgumentNullException.ThrowIfNull(manifestKeys);

        ulong seed = Aion2KeyManifestReader.DeriveL10NSeed(locale);
        if (!manifestKeys.TryGetValue(seed, out byte[]? aesKey))
        {
            throw new KeyNotFoundException($"No AES key found in manifest for locale '{locale}' (Seed: 0x{seed:X16}).");
        }

        var header = ParseHeader(containerData, seed);
        if (!header.IsValid)
        {
            throw new InvalidDataException($"Invalid L10N container header: {header.ErrorMessage}");
        }

        byte[] raw = DecryptAndDecompress(containerData, header, aesKey);
        return (header, raw);
    }

    private static byte[] DeriveHeaderKey(ulong seed, int type)
    {
        Span<byte> input = stackalloc byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(input, HeaderKeyRoot);
        BinaryPrimitives.WriteUInt64LittleEndian(input[8..], seed);
        BinaryPrimitives.WriteInt32LittleEndian(input[16..], type);
        input[20..].Clear();

        using var hasher = Hasher.New();
        hasher.Update(input);
        return hasher.Finalize().AsSpan().ToArray();
    }

    private static ulong Blake3HashUlong(ulong value)
    {
        Span<byte> input = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(input, value);
        using var hasher = Hasher.New();
        hasher.Update(input);
        return BinaryPrimitives.ReadUInt64LittleEndian(hasher.Finalize().AsSpan());
    }
}
