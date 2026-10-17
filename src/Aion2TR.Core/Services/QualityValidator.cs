using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aion2TR.Core.Services;

public record QualityIssue(string Rule, string Description);

public record QualityValidationResult(bool IsValid, IReadOnlyList<QualityIssue> Issues)
{
    public static QualityValidationResult Success() => new(true, []);
    public static QualityValidationResult Failure(List<QualityIssue> issues) => new(false, issues);
}

public interface IQualityValidator
{
    QualityValidationResult Validate(string sourceText, string? targetText);
}

public class QualityValidator : IQualityValidator
{
    private readonly IPlaceholderValidator _placeholderValidator;

    public QualityValidator(IPlaceholderValidator? placeholderValidator = null)
    {
        _placeholderValidator = placeholderValidator ?? new PlaceholderValidator();
    }

    public QualityValidationResult Validate(string sourceText, string? targetText)
    {
        var issues = new List<QualityIssue>();

        // 1. Empty translation
        if (string.IsNullOrWhiteSpace(targetText))
        {
            issues.Add(new QualityIssue("EmptyTranslation", "Translated text is empty or whitespace."));
            return QualityValidationResult.Failure(issues);
        }

        // 2. Control characters (allow \r, \n, \t)
        foreach (char c in targetText)
        {
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
            {
                issues.Add(new QualityIssue("ControlCharacters", $"Contains forbidden control character (0x{(int)c:X2})."));
                break;
            }
        }

        // 3. Placeholder / token mismatch
        var placeholderResult = _placeholderValidator.Validate(sourceText, targetText);
        if (!placeholderResult.IsValid)
        {
            foreach (var m in placeholderResult.MissingInTarget)
                issues.Add(new QualityIssue("PlaceholderMissing", $"Missing placeholder in target: {m}"));
            foreach (var e in placeholderResult.ExtraInTarget)
                issues.Add(new QualityIssue("PlaceholderExtra", $"Unexpected placeholder in target: {e}"));
        }

        // 4. Broken markup tags (unmatched angle brackets)
        int srcOpen = sourceText.Count(c => c == '<');
        int srcClose = sourceText.Count(c => c == '>');
        int tgtOpen = targetText.Count(c => c == '<');
        int tgtClose = targetText.Count(c => c == '>');

        if (srcOpen == srcClose && tgtOpen != tgtClose)
        {
            issues.Add(new QualityIssue("BrokenMarkup", $"Angle brackets do not match: {tgtOpen} '<' vs {tgtClose} '>'"));
        }
        else if (srcOpen != srcClose && (tgtOpen - tgtClose) != (srcOpen - srcClose))
        {
            issues.Add(new QualityIssue("BrokenMarkup", $"Angle bracket balance altered relative to source: source has {srcOpen} '<' and {srcClose} '>', target has {tgtOpen} '<' and {tgtClose} '>'"));
        }

        // 5. Length ratio checks
        int srcLen = sourceText.Trim().Length;
        int tgtLen = targetText.Trim().Length;

        if (srcLen > 25 && tgtLen < srcLen * 0.2)
        {
            issues.Add(new QualityIssue("SuspiciouslyShort", $"Target is suspiciously short ({tgtLen} chars vs source {srcLen} chars)."));
        }
        else if (srcLen > 5 && tgtLen > Math.Max(srcLen * 3.5, 60))
        {
            issues.Add(new QualityIssue("SuspiciouslyLong", $"Target is suspiciously long ({tgtLen} chars vs source {srcLen} chars)."));
        }

        // 6. Translation identical to source (warning / issue if source is natural language with words)
        if (string.Equals(sourceText.Trim(), targetText.Trim(), StringComparison.Ordinal) && HasWords(sourceText))
        {
            issues.Add(new QualityIssue("IdenticalToSource", "Target is identical to source text."));
        }

        return issues.Count == 0 ? QualityValidationResult.Success() : QualityValidationResult.Failure(issues);
    }

    private static bool HasWords(string s)
    {
        return s.Any(char.IsLetter) && s.Contains(' ');
    }
}
