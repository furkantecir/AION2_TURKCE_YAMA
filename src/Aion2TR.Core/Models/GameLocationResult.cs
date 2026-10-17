namespace Aion2TR.Core.Models;

public record GameLocationResult(
    bool IsFound,
    string? GamePath,
    string? DetectionSource,
    string? ErrorMessage = null)
{
    public static GameLocationResult NotFound(string error) => new(false, null, null, error);
    public static GameLocationResult Found(string path, string source) => new(true, path, source);
}
