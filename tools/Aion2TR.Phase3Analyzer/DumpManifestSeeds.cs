using System;
using System.IO;
using System.Linq;
using Blake3;
using System.Security.Cryptography;

namespace Aion2TR.Phase3Analyzer;

internal class DumpManifestSeeds
{
    private static readonly byte[] ManifestHashKeyMaterial =
    [
        0xeb, 0x0b, 0xc0, 0x97, 0x28, 0x47, 0x5a, 0xb5,
        0x19, 0x6a, 0xf8, 0xce, 0x78, 0x5d, 0x8a, 0x79,
        0xdf, 0x18, 0xd2, 0x14, 0x8f, 0x51, 0xcb, 0xef,
        0x8b, 0x39, 0xe4, 0x3b, 0x2a, 0x1d, 0x40, 0x56
    ];

    public static void Run(byte[] data)
    {
        Console.WriteLine("\n=== MANIFEST SEEDLERİNİN ANALİZİ ===");
        const int payloadOffset = 8;
        const int entrySize = 0x30;

        int count = (data.Length - payloadOffset) / entrySize;
        Console.WriteLine($"Kayıt sayısı: {count}");

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
            decryptor.TransformBlock(data, payloadOffset, payloadLen, decrypted, 0);
        }

        Console.WriteLine("\nİlk 10 Decrypted Manifest Bloğu Hex Dump (0x30 = 48 bytes):");
        for (int i = 0; i < Math.Min(10, count); i++)
        {
            int off = i * entrySize;
            ulong seed = BitConverter.ToUInt64(decrypted, off);
            uint sHigh = (uint)(seed >> 32);
            uint sLow = (uint)(seed & 0xFFFFFFFF);
            string hexKey = BitConverter.ToString(decrypted, off + 8, 32);
            string hexPad = BitConverter.ToString(decrypted, off + 40, 8);
            Console.WriteLine($"Entry {i,3}: Seed=0x{seed:X16} (Low=0x{sLow:X8}, High=0x{sHigh:X8}) | Key={hexKey[..23]}... | Pad={hexPad}");
        }

        // Search for any seed that contains 21EA7DBB or similar
        Console.WriteLine("\nSeed arama: '21EA7DBB' veya '52D80C50' içeren tohumlar...");
        for (int i = 0; i < count; i++)
        {
            int off = i * entrySize;
            ulong seed = BitConverter.ToUInt64(decrypted, off);
            if (seed.ToString("X16").Contains("7DBB", StringComparison.OrdinalIgnoreCase) ||
                seed.ToString("X16").Contains("52D8", StringComparison.OrdinalIgnoreCase) ||
                seed.ToString("X16").Contains("BB7D", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  -> Eşleşme bulundu! Entry {i}: Seed=0x{seed:X16}");
            }
        }
    }
}
