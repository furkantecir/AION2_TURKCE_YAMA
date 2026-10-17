using System;
using System.IO;
using System.Security.Cryptography;
using Blake3;
using K4os.Compression.LZ4;

namespace Aion2TR.Phase3Analyzer;

internal class TestDataTable
{
    private static readonly byte[] ManifestHashKeyMaterial =
    [
        0xeb, 0x0b, 0xc0, 0x97, 0x28, 0x47, 0x5a, 0xb5,
        0x19, 0x6a, 0xf8, 0xce, 0x78, 0x5d, 0x8a, 0x79,
        0xdf, 0x18, 0xd2, 0x14, 0x8f, 0x51, 0xcb, 0xef,
        0x8b, 0x39, 0xe4, 0x3b, 0x2a, 0x1d, 0x40, 0x56
    ];

    public static void Run(byte[] manifestData, byte[] strStartupData)
    {
        Console.WriteLine("\n=== String_StartUp.dat DEŞİFRE TESTİ ===");
        // 1. Get Manifest Keys
        var manifestKeys = ParseKeyManifest(manifestData);

        // 2. Parse String_StartUp.dat
        using var ms = new MemoryStream(strStartupData);
        using var reader = new BinaryReader(ms);

        int magic = reader.ReadInt32(); // 13
        int version = reader.ReadInt32();
        ulong seed = reader.ReadUInt64();

        Console.WriteLine($"String_StartUp: Magic={magic}, Version={version}, Seed=0x{seed:X16}");

        if (manifestKeys.TryGetValue(seed, out var key))
        {
            Console.WriteLine($"[BÜYÜK BAŞARI!] Seed 0x{seed:X16} MANIFEST İÇİNDE BULUNDU!");
            Console.WriteLine($"AES Anahtarı: {Convert.ToHexString(key)}");

            if (version == 2)
            {
                int rawSize = reader.ReadInt32();
                int packedSize = reader.ReadInt32();
                int alignedSize = reader.ReadInt32();
                Console.WriteLine($"RawSize={rawSize:N0}, PackedSize={packedSize:N0}, AlignedSize={alignedSize:N0}");

                // Decrypt
                byte[] dec = new byte[alignedSize];
                using (var aes = Aes.Create())
                {
                    aes.Mode = CipherMode.ECB;
                    aes.Padding = PaddingMode.None;
                    aes.Key = key;
                    using var decr = aes.CreateDecryptor();
                    decr.TransformBlock(strStartupData, 0x1C, alignedSize, dec, 0);
                }

                // LZ4 decode
                byte[] raw = new byte[rawSize];
                int written = LZ4Codec.Decode(dec.AsSpan(0x10, packedSize), raw);
                Console.WriteLine($"LZ4 Decode: written {written} / {rawSize} bytes!");
                Console.WriteLine($"Raw Entropisi: {CalculateEntropy(raw):F4}");
                Console.WriteLine($"Açılan metin (ilk 200 bayt): {System.Text.Encoding.UTF8.GetString(raw, 0, Math.Min(200, raw.Length))}");
            }
        }
        else
        {
            Console.WriteLine($"Seed 0x{seed:X16} manifestte bulunamadı.");
        }
    }

    private static System.Collections.Generic.Dictionary<ulong, byte[]> ParseKeyManifest(byte[] data)
    {
        const int payloadOffset = 8;
        const int entrySize = 0x30;
        int count = (data.Length - payloadOffset) / entrySize;

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

        var result = new System.Collections.Generic.Dictionary<ulong, byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            int off = i * entrySize;
            ulong seed = BitConverter.ToUInt64(decrypted, off);
            byte[] key = new byte[32];
            Array.Copy(decrypted, off + 8, key, 0, 32);
            result[seed] = key;
        }
        return result;
    }

    private static double CalculateEntropy(byte[] data)
    {
        if (data.Length == 0) return 0;
        var counts = new int[256];
        foreach (var b in data) counts[b]++;
        double entropy = 0.0;
        double len = data.Length;
        foreach (var count in counts)
        {
            if (count == 0) continue;
            double p = count / len;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}
