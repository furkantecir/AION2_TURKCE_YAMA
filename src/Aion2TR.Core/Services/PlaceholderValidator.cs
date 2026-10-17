using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Aion2TR.Core.Services;

public record PlaceholderValidationResult(
    bool IsValid,
    IReadOnlyList<string> MissingInTarget,
    IReadOnlyList<string> ExtraInTarget)
{
    public static PlaceholderValidationResult Success() => new(true, [], []);
    public static PlaceholderValidationResult Failure(List<string> missing, List<string> extra) => new(false, missing, extra);
}

public interface IPlaceholderValidator
{
    PlaceholderValidationResult Validate(string sourceText, string targetText);
    IReadOnlyList<string> ExtractPlaceholders(string text);
}

public class PlaceholderValidator : IPlaceholderValidator
{
    // Regex for:
    // 1. C-style format strings: %s, %d, %f, %02d, %i, etc.
    // 2. .NET / Unreal curly braces: {0}, {1}, {PlayerName}, {ItemName}
    // 3. XML / HTML / Unreal rich text tags: <br>, <color>, </color>, etc.
    // 4. Custom game tokens: [Token], [A-Z_]
    // 5. Escaped sequences: \r\n, \n, \t
    private static readonly Regex TokenRegex = new(
        @"(%[-+]?[0-9]*\.?[0-9]*[a-zA-Z%])|(\{[\w\d_\-]+\})|(</?[\w\d_\-=#:;,\. ]+>)|(\[[A-Z0-9_]+\])|(\r?\n)|(\\r?\\n)|(\\t)",
        RegexOptions.Compiled);

    public IReadOnlyList<string> ExtractPlaceholders(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var matches = TokenRegex.Matches(text);
        var list = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            // Normalize newlines in tokens if necessary
            string val = m.Value;
            if (val == "\r\n") val = "\n";
            else if (val == "\\r\\n") val = "\\n";
            list.Add(val);
        }
        return list;
    }

    public PlaceholderValidationResult Validate(string sourceText, string targetText)
    {
        var sourceTokens = ExtractPlaceholders(sourceText);
        var targetTokens = ExtractPlaceholders(targetText);

        var sourceCounts = sourceTokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());
        var targetCounts = targetTokens.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count());

        var missing = new List<string>();
        var extra = new List<string>();

        foreach (var (token, srcCount) in sourceCounts)
        {
            targetCounts.TryGetValue(token, out int tgtCount);
            if (tgtCount < srcCount)
            {
                missing.Add($"{token} (expected {srcCount}, found {tgtCount})");
            }
        }

        foreach (var (token, tgtCount) in targetCounts)
        {
            sourceCounts.TryGetValue(token, out int srcCount);
            if (tgtCount > srcCount)
            {
                extra.Add($"{token} (unexpected extra {tgtCount - srcCount})");
            }
        }

        if (missing.Count == 0 && extra.Count == 0)
            return PlaceholderValidationResult.Success();

        return PlaceholderValidationResult.Failure(missing, extra);
    }
}
