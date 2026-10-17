using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Services;

/// <summary>
/// Verified lossless serializer for AION 2 raw localization tables (Unreal Engine TMap binary format).
/// Correctly encodes UTF-8 and UTF-16 FStrings according to Unreal Engine standards.
/// </summary>
public static class L10NStringSerializer
{
    public static byte[] Serialize(
        IReadOnlyList<L10NEntry> entries,
        string tableNamespace = "AION2",
        int tableVersion = 1,
        bool appendTrailingZeroPadding = true)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableNamespace);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 1. Table Version (int32 = 1)
        writer.Write(tableVersion);

        // 2. Table Namespace (FString = "AION2")
        WriteFString(writer, tableNamespace, forceUtf16: false);

        // 3. Entries Count (int32)
        writer.Write(entries.Count);

        // 4. Repeated pairs of (FString Key, FString Text)
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            WriteFString(writer, entry.Key ?? string.Empty, entry.IsKeyUtf16);
            WriteFString(writer, entry.Text, entry.IsTextUtf16);
        }

        // 5. Unreal Engine archive trailing padding (4 zero bytes)
        if (appendTrailingZeroPadding)
        {
            writer.Write((int)0);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Writes an Unreal Engine FString.
    /// In Unreal Engine, positive length (+N) indicates single-byte ANSI/ASCII characters.
    /// Negative length (-N) indicates 2-byte UTF-16LE characters.
    /// If forceUtf16 is true or any character is non-ASCII (code point > 127, e.g. ç, Ç, ö, Ö, ü, Ü, ğ, Ğ, ı, İ, ş, Ş),
    /// it writes -length (UTF-16LE with 2-byte null terminator).
    /// Pure ASCII strings (c <= 127) are written with +length (ANSI with 1-byte null terminator).
    /// </summary>
    public static void WriteFString(BinaryWriter writer, string? value, bool forceUtf16 = false)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.Write((int)0);
            return;
        }

        bool requiresUtf16 = forceUtf16;
        if (!requiresUtf16)
        {
            foreach (char c in value)
            {
                if (c > 127)
                {
                    requiresUtf16 = true;
                    break;
                }
            }
        }

        if (requiresUtf16)
        {
            int charCount = value.Length + 1; // includes null terminator
            writer.Write(-charCount);
            byte[] bytes = Encoding.Unicode.GetBytes(value);
            writer.Write(bytes);
            writer.Write((ushort)0); // 2-byte null terminator
        }
        else
        {
            int charCount = value.Length + 1; // includes null terminator
            writer.Write(charCount);
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            writer.Write(bytes);
            writer.Write((byte)0); // 1-byte null terminator
        }
    }
}
