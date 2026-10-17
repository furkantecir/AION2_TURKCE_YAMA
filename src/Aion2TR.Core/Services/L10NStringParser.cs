using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Services;

public record L10NParseResult(
    bool Success,
    int Version,
    int EntryCount,
    IReadOnlyList<L10NEntry> Entries,
    string StatusMessage,
    IReadOnlyDictionary<string, object> Metadata
);

public class L10NStringParser
{
    /// <summary>
    /// Parses a raw, decompressed AION 2 localization table buffer (Version 1).
    /// Format:
    /// - int32 tableVersion (1)
    /// - FString namespace ("AION2")
    /// - int32 mapCount (153,042)
    /// - Repeated pairs of (FString Key, FString Text)
    /// </summary>
    public L10NParseResult ParseRawTable(byte[] rawTableData, string sourceName = "RawTable")
    {
        ArgumentNullException.ThrowIfNull(rawTableData);

        if (rawTableData.Length < 4)
        {
            return new L10NParseResult(
                Success: false,
                Version: 0,
                EntryCount: 0,
                Entries: [],
                StatusMessage: "Raw table buffer is too small.",
                Metadata: new Dictionary<string, object> { ["Length"] = rawTableData.Length });
        }

        using var ms = new MemoryStream(rawTableData);
        using var reader = new BinaryReader(ms);

        int version = reader.ReadInt32();
        if (version != 1)
        {
            return new L10NParseResult(
                Success: false,
                Version: version,
                EntryCount: 0,
                Entries: [],
                StatusMessage: $"Unsupported raw table version {version}. Expected 1.",
                Metadata: new Dictionary<string, object> { ["Version"] = version });
        }

        string tableNamespace = ReadFString(reader);
        int count = reader.ReadInt32();

        var entries = new List<L10NEntry>(count);
        for (int i = 0; i < count; i++)
        {
            long offset = ms.Position;
            int keyLen = reader.ReadInt32();
            string key = ReadFStringFromLength(reader, keyLen);

            int valLen = reader.ReadInt32();
            string text = ReadFStringFromLength(reader, valLen);

            entries.Add(new L10NEntry(
                Id: (i + 1).ToString(),
                Key: key,
                Text: text,
                Offset: offset,
                RawLength: (int)(ms.Position - offset),
                Encoding: valLen < 0 ? "UTF-16" : "UTF-8",
                IsDecrypted: true,
                IsTextUtf16: valLen < 0,
                IsKeyUtf16: keyLen < 0
            ));
        }

        var metadata = new Dictionary<string, object>
        {
            ["Source"] = sourceName,
            ["Namespace"] = tableNamespace,
            ["TotalSizeBytes"] = rawTableData.Length,
            ["EntryCount"] = entries.Count,
            ["TableVersion"] = version
        };

        return new L10NParseResult(
            Success: true,
            Version: version,
            EntryCount: entries.Count,
            Entries: entries,
            StatusMessage: $"Successfully parsed {entries.Count} entries in namespace '{tableNamespace}'.",
            Metadata: metadata
        );
    }

    /// <summary>
    /// Checks an input file buffer to determine if it is raw table or encrypted container.
    /// </summary>
    public L10NParseResult Parse(byte[] data, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < 4)
        {
            return new L10NParseResult(
                Success: false,
                Version: 0,
                EntryCount: 0,
                Entries: [],
                StatusMessage: "File is too small to contain valid header.",
                Metadata: new Dictionary<string, object> { ["Length"] = data.Length });
        }

        int version = BitConverter.ToInt32(data, 0);

        // If it's already decompressed raw table (version 1)
        if (version == 1)
        {
            return ParseRawTable(data, sourceName);
        }

        // If it's encrypted container (version 2)
        if (version == 2)
        {
            return new L10NParseResult(
                Success: false,
                Version: version,
                EntryCount: 0,
                Entries: [],
                StatusMessage: "Format is L10N Container Version 2 with AES+LZ4 encryption. Use Aion2L10NDecryptor to extract raw table first.",
                Metadata: new Dictionary<string, object>
                {
                    ["Source"] = sourceName,
                    ["TotalSizeBytes"] = data.Length,
                    ["Version"] = version,
                    ["HeaderHex"] = Convert.ToHexString(data[..Math.Min(16, data.Length)])
                }
            );
        }

        return new L10NParseResult(
            Success: false,
            Version: version,
            EntryCount: 0,
            Entries: [],
            StatusMessage: $"Unsupported L10NString table version: {version}",
            Metadata: new Dictionary<string, object> { ["Version"] = version }
        );
    }

    private static string ReadFStringFromLength(BinaryReader reader, int length)
    {
        if (length == 0) return string.Empty;

        if (length > 0)
        {
            byte[] bytes = reader.ReadBytes(length);
            // In Unreal Engine, positive length is single-byte ANSI/Latin-1 characters
            return Encoding.Latin1.GetString(bytes, 0, length - 1);
        }
        else
        {
            int charCount = -length;
            byte[] bytes = reader.ReadBytes(charCount * 2);
            return Encoding.Unicode.GetString(bytes, 0, (charCount - 1) * 2);
        }
    }

    private static string ReadFString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        return ReadFStringFromLength(reader, length);
    }
}
