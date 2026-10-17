using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Aion2TR.Core.Services;
using Aion2TR.Infrastructure.Services;
using Xunit;

namespace Aion2TR.Tests;

public class Phase3L10NTests
{
    private readonly L10NStringParser _parser = new();
    private readonly Sha256HashService _hashService = new();

    [Fact]
    public void Parser_DoesNotModifySourceFile()
    {
        var originalData = new byte[] { 0x02, 0x00, 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD };
        var copy = (byte[])originalData.Clone();

        var result = _parser.Parse(copy, "test.dat");

        Assert.Equal(originalData, copy);
        Assert.Equal(2, result.Version);
    }

    [Fact]
    public void SourceFile_SHA256_UnchangedAfterAnalysis()
    {
        var data = new byte[1024];
        new Random(12345).NextBytes(data);
        var initialHash = _hashService.ComputeSha256(data);

        // Run parser
        _parser.Parse(data, "sample.dat");

        // Hash again
        var postHash = _hashService.ComputeSha256(data);
        Assert.Equal(initialHash, postHash);
    }

    [Fact]
    public void XorHypothesis_25A87E91_FailsToProduceValidEnglishOnPseudoRandomData()
    {
        // Prove that applying 25 A8 7E 91 to high entropy data does NOT produce readable English
        var highEntropyData = new byte[2048];
        new Random(42).NextBytes(highEntropyData);

        byte[] key = [0x25, 0xA8, 0x7E, 0x91];
        var decoded = new byte[highEntropyData.Length];
        for (int i = 0; i < highEntropyData.Length; i++)
            decoded[i] = (byte)(highEntropyData[i] ^ key[i % key.Length]);

        var text = Encoding.ASCII.GetString(decoded);
        string[] targetWords = ["Character", "Start", "Inventory", "Server", "Settings"];

        foreach (var word in targetWords)
        {
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Parser_RejectsTooShortData()
    {
        var shortData = new byte[] { 0x01, 0x00 };
        var result = _parser.Parse(shortData, "short.dat");

        Assert.False(result.Success);
        Assert.Equal(0, result.EntryCount);
    }
}
