using System.Text;
using Aion2TR.Infrastructure.Services;
using Xunit;

namespace Aion2TR.Tests;

public class L10NStringAnalyzerTests
{
    private readonly L10NStringAnalyzer _analyzer = new(new Sha256HashService());

    [Fact]
    public void AnalyzeBytes_PlaintextTable_DetectsLowEntropy()
    {
        var sampleText = "ID_1001=Start Game\nID_1002=Settings\nID_1003=Exit Game\n";
        var bytes = Encoding.UTF8.GetBytes(sampleText);

        var result = _analyzer.AnalyzeBytes(bytes, "mock_table.dat");

        Assert.Equal("mock_table.dat", result.FilePath);
        Assert.True(result.Entropy < 5.0, $"Expected low entropy for text, got {result.Entropy}");
        Assert.NotEmpty(result.SampleStrings);
    }

    [Fact]
    public void AnalyzeBytes_HighEntropy_IdentifiesLikelyEncrypted()
    {
        // Generate pseudo-random high entropy bytes
        var randomBytes = new byte[2048];
        new System.Random(42).NextBytes(randomBytes);

        var result = _analyzer.AnalyzeBytes(randomBytes, "encrypted.dat");

        Assert.True(result.Entropy > 7.5, $"Expected high entropy, got {result.Entropy}");
        Assert.Equal("LIKELY_ENCRYPTED_OR_COMPRESSED", result.ConfidenceLevel);
    }
}
