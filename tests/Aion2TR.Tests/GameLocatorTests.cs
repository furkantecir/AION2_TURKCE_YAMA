using System.IO;
using Aion2TR.Infrastructure.Services;
using Xunit;

namespace Aion2TR.Tests;

public class GameLocatorTests
{
    private readonly WindowsSteamGameLocator _locator = new();

    [Fact]
    public void ValidateGameDirectory_EmptyOrMissing_ReturnsFalse()
    {
        Assert.False(_locator.ValidateGameDirectory(""));
        Assert.False(_locator.ValidateGameDirectory(@"C:\non_existent_folder_xyz_123"));
    }

    [Fact]
    public void ValidateGameDirectory_ValidMockStructure_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Aion2_Mock_" + Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "Aion2", "Content", "Paks"));
            Directory.CreateDirectory(Path.Combine(tempDir, "Engine"));
            File.WriteAllText(Path.Combine(tempDir, "Manifest_UFSFiles_Win64.txt"), "mock manifest");

            var isValid = _locator.ValidateGameDirectory(tempDir);
            Assert.True(isValid);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
