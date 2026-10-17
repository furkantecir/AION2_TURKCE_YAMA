using System;
using System.Text.RegularExpressions;

namespace Aion2TR.Core.Services;

public record ProtectionEvaluation(bool IsProtected, string? Reason);

public interface ITranslationProtectionService
{
    ProtectionEvaluation Evaluate(string key, string text);
}

public class TranslationProtectionService : ITranslationProtectionService
{
    private static readonly Regex GuidRegex = new(
        @"^[{(]?[0-9a-fA-F]{8}[-]?([0-9a-fA-F]{4}[-]?){3}[0-9a-fA-F]{12}[)}]?$",
        RegexOptions.Compiled);

    private static readonly Regex HexOrHashRegex = new(
        @"^(0x[0-9a-fA-F]+|[0-9a-fA-F]{32}|[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$",
        RegexOptions.Compiled);

    private static readonly Regex UrlRegex = new(
        @"^(https?:\/\/|ftp:\/\/|www\.)[^\s/$.?#].[^\s]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AssetPathRegex = new(
        @"^(\/Game\/|[A-Za-z]:\\|\/Engine\/|[a-zA-Z0-9_\-\.\/]+\.(uasset|uexp|ubulk|ini|json|dat|xml|txt|png|tga|dds|pak))$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UnrealInternalRegex = new(
        @"^(None|Default__|BP_|WBP_|SK_|SM_|MI_|M_|FX_|SoundCue_|AnimSeq_)[a-zA-Z0-9_]*$",
        RegexOptions.Compiled);

    private static readonly Regex FormatOnlyRegex = new(
        @"^([%\{\<\[][^\w\s]*[\w\d_\-\.\:]+[\}\>\]%][\s\p{P}]*)+$",
        RegexOptions.Compiled);

    public ProtectionEvaluation Evaluate(string key, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ProtectionEvaluation(true, "Empty or whitespace");
        }

        string trimmed = text.Trim();

        // 1. Pure numbers or percentages (e.g. "100", "+5", "3.14", "50%")
        if (IsNumericOnly(trimmed))
        {
            return new ProtectionEvaluation(true, "Numeric-only string");
        }

        // 2. Pure punctuation or symbols without any letters
        if (IsPunctuationOrSymbolOnly(trimmed))
        {
            return new ProtectionEvaluation(true, "Punctuation/symbol-only string");
        }

        // 3. GUID
        if (GuidRegex.IsMatch(trimmed))
        {
            return new ProtectionEvaluation(true, "GUID identifier");
        }

        // 4. Hex or Hash
        if (HexOrHashRegex.IsMatch(trimmed))
        {
            return new ProtectionEvaluation(true, "Hex or Hash");
        }

        // 5. URL
        if (UrlRegex.IsMatch(trimmed))
        {
            return new ProtectionEvaluation(true, "URL address");
        }

        // 6. Asset / File path
        if (AssetPathRegex.IsMatch(trimmed))
        {
            return new ProtectionEvaluation(true, "Asset or filesystem path");
        }

        // 7. Unreal internal object identifier
        if (UnrealInternalRegex.IsMatch(trimmed))
        {
            return new ProtectionEvaluation(true, "Unreal internal identifier");
        }

        // 8. Format or markup only (e.g. "{0}", "%s", "<br>", "</color>", "{PlayerName}")
        if (IsFormatOrMarkupOnly(trimmed))
        {
            return new ProtectionEvaluation(true, "Format/markup-only string");
        }

        return new ProtectionEvaluation(false, null);
    }

    private static bool IsNumericOnly(string s)
    {
        bool hasDigit = false;
        foreach (char c in s)
        {
            if (char.IsDigit(c)) hasDigit = true;
            else if (c is not ('+' or '-' or '.' or ',' or '%' or ' ' or '\t' or '/' or ':'))
                return false;
        }
        return hasDigit;
    }

    private static bool IsPunctuationOrSymbolOnly(string s)
    {
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
                return false;
        }
        return true;
    }

    private static bool IsFormatOrMarkupOnly(string s)
    {
        // Remove all format tokens and tags and see if any letters/words remain
        var stripped = Regex.Replace(s, @"(%[-+]?[0-9]*\.?[0-9]*[a-zA-Z%])|(\{[\w\d_\-]+\})|(</?[\w\d_\-=#:;,\. ]+>)|(\[[A-Z0-9_]+\])", "");
        stripped = stripped.Trim();
        if (string.IsNullOrEmpty(stripped))
            return true;

        // If what remains has no letters, it was effectively just markup/punctuation
        return IsPunctuationOrSymbolOnly(stripped);
    }
}
