using System;
using System.IO;
using System.Security.Cryptography;
using Blake3;
using K4os.Compression.LZ4;

namespace Aion2TR.Phase3Analyzer;

internal class BruteForceManifestKeys
{
    private static readonly byte[] ManifestHashKeyMaterial =
    [
        0xeb, 0x0b, 0xc0, 0x97, 0x28, 0x47, 0x5a, 0xb5,
        0x19, 0x6a, 0xf8, 0xce, 0x78, 0x5d, 0x8a, 0x79,
        0xdf, 0x18, 0xd2, 0x14, 0x8f, 0x51, 0xcb, 0xef,
        0x8b, 0x39, 0xe4, 0x3b, 0x2a, 0x1d, 0x40, 0x56
    ];

    public static void Run(byte[] manifestData, byte[] l10nData)
    {
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("    752 MANIFEST ANAHTARININ L10NString.dat ÜZERİNDE KAPSAMLI DENENMESİ          ");
        Console.WriteLine("================================================================================");

        // 1. Decrypt manifest
        const int payloadOffset = 8;
        const int entrySize = 0x30;
        int count = (manifestData.Length - payloadOffset) / entrySize;

        using var hasher = Hasher.New();
        hasher.Update(ManifestHashKeyMaterial);
        byte[] manifestAesKey = hasher.Finalize().AsSpan().ToArray();

        int payloadLen = count * entrySize;
        byte[] decrypted = new byte[payloadLen];
        using (var aes = Aes.Create())
        {
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = manifestAesKey;
            using var decryptor = aes.CreateDecryptor();
            decryptor.TransformBlock(manifestData, payloadOffset, payloadLen, decrypted, 0);
        }

        Console.WriteLine($"Toplam denenecek anahtar: {count}");

        int matchCount = 0;
        using var testAes = Aes.Create();
        testAes.Mode = CipherMode.ECB;
        testAes.Padding = PaddingMode.None;

        // Try candidate offsets in L10NString.dat: 0, 4, 8, 12, 16, 24 (0x18), 28 (0x1C), 32 (0x20)
        int[] testOffsets = [0x18, 0x1C, 0x0, 0x4, 0x8, 0x10, 0x20];

        foreach (var startOffset in testOffsets)
        {
            if (startOffset + 64 > l10nData.Length) continue;

            byte[] cipherSlice = new byte[64];
            Array.Copy(l10nData, startOffset, cipherSlice, 0, 64);

            for (int i = 0; i < count; i++)
            {
                int off = i * entrySize;
                ulong seed = BitConverter.ToUInt64(decrypted, off);
                byte[] key = new byte[32];
                Array.Copy(decrypted, off + 8, key, 0, 32);

                testAes.Key = key;
                using var decr = testAes.CreateDecryptor();
                byte[] plain = decr.TransformFinalBlock(cipherSlice, 0, 64);

                // Check if plain contains nulls or printable ASCII typical of LZ4 header or table
                int score = plain.Count(b => b is 0 or (>= 32 and <= 126));
                if (score > 52)
                {
                    matchCount++;
                    Console.WriteLine($"\n[HİT BULUNDU!] Offset 0x{startOffset:X2} | Entry {i} | Seed=0x{seed:X16} | Skor: {score}/64");
                    Console.WriteLine($"  Key: {Convert.ToHexString(key)}");
                    Console.WriteLine($"  Plaintext (hex): {BitConverter.ToString(plain)}");
                    Console.WriteLine($"  Plaintext (txt): {System.Text.Encoding.ASCII.GetString(plain)}");

                    // Try full LZ4 decompression with this key
                    TryFullLz4(l10nData, startOffset, key);
                }
            }
        }

        Console.WriteLine($"\nTarama tamamlandı. Toplam başarılı aday: {matchCount}");
    }

    private static void TryFullLz4(byte[] l10nData, int startOffset, byte[] key)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            using var decr = aes.CreateDecryptor();

            int decLen = Math.Min(l10nData.Length - startOffset, 65536);
            decLen -= (decLen % 16);
            byte[] dec = decr.TransformFinalBlock(l10nData, startOffset, decLen);

            // Try LZ4 decode from offset 0, 4, 8, 16
            for (int lz4Start = 0; lz4Start <= 32; lz4Start += 4)
            {
                byte[] target = new byte[1024 * 1024];
                int written = LZ4Codec.Decode(dec.AsSpan(lz4Start, dec.Length - lz4Start), target);
                if (written > 1000)
                {
                    Console.WriteLine($"  ===> [TAM BAŞARI!] LZ4 Decompress başarılı oldu! Açılan bayt: {written:N0}!");
                    var text = System.Text.Encoding.UTF8.GetString(target, 0, Math.Min(written, 300));
                    Console.WriteLine($"  ===> Örnek Metin: {text}");
                }
            }
        }
        catch { }
    }
}
