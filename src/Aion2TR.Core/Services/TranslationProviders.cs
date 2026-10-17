using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;

namespace Aion2TR.Core.Services;

public class ManualTranslationProvider : ITranslationProvider
{
    private readonly Dictionary<string, string> _memory = new(StringComparer.Ordinal);

    public string Name => "Manual";
    public int DefaultBatchSize => 50;

    public void SetTranslation(string sourceHash, string translatedText)
    {
        _memory[sourceHash] = translatedText;
    }

    public Task<IReadOnlyList<TranslationBatchResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationBatchItem> items,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TranslationBatchResultItem>(items.Count);
        foreach (var item in items)
        {
            if (_memory.TryGetValue(item.SourceHash, out var text))
            {
                results.Add(new TranslationBatchResultItem(item.SourceHash, text, true, null));
            }
            else
            {
                results.Add(new TranslationBatchResultItem(item.SourceHash, null, false, "Not found in manual dictionary"));
            }
        }

        return Task.FromResult<IReadOnlyList<TranslationBatchResultItem>>(results);
    }
}

public class ImportTranslationProvider : ITranslationProvider
{
    private readonly Dictionary<string, string> _importedTranslations;

    public ImportTranslationProvider(Dictionary<string, string> importedTranslations)
    {
        _importedTranslations = importedTranslations ?? new Dictionary<string, string>();
    }

    public string Name => "Import";
    public int DefaultBatchSize => 100;

    public Task<IReadOnlyList<TranslationBatchResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationBatchItem> items,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TranslationBatchResultItem>(items.Count);
        foreach (var item in items)
        {
            if (_importedTranslations.TryGetValue(item.SourceHash, out var text))
            {
                results.Add(new TranslationBatchResultItem(item.SourceHash, text, true, null));
            }
            else
            {
                results.Add(new TranslationBatchResultItem(item.SourceHash, null, false, "Translation not in import set"));
            }
        }

        return Task.FromResult<IReadOnlyList<TranslationBatchResultItem>>(results);
    }
}

public class PseudoTranslationProvider : ITranslationProvider
{
    public string Name => "Pseudo";
    public int DefaultBatchSize => 100;

    public string Prefix { get; set; } = "[TR-TEST] ";

    public Task<IReadOnlyList<TranslationBatchResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationBatchItem> items,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TranslationBatchResultItem>(items.Count);
        foreach (var item in items)
        {
            string pseudo = PseudoTranslate(item.SourceText);
            results.Add(new TranslationBatchResultItem(item.SourceHash, pseudo, true, null));
        }

        return Task.FromResult<IReadOnlyList<TranslationBatchResultItem>>(results);
    }

    public string PseudoTranslate(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        // If string starts with a tag like <color=...>, place [TR-TEST] inside or after the leading tag so tags aren't broken
        if (source.StartsWith('<'))
        {
            int tagClose = source.IndexOf('>');
            if (tagClose >= 0 && tagClose + 1 < source.Length)
            {
                return source[..(tagClose + 1)] + Prefix + source[(tagClose + 1)..];
            }
        }

        return Prefix + source;
    }
}
