using System;

namespace Aion2TR.Core.Models;

public enum TranslationStatus
{
    Untranslated,
    MachineTranslated,
    Reviewed,
    Protected,
    Skipped,
    Invalid
}

public class TranslationEntry
{
    public string Key { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string? TranslatedText { get; set; }
    public TranslationStatus Status { get; set; } = TranslationStatus.Untranslated;
    public string Category { get; set; } = "Unknown";
    public string FirstSeenVersion { get; set; } = "1.0.0";
    public string LastSeenVersion { get; set; } = "1.0.0";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
