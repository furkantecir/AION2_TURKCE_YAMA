using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Aion2TR.Core.Services;

public record MemoryLookupResult(bool Found, string? TranslatedText);

public interface ITranslationMemoryService
{
    void Store(string sourceHash, string translatedText);
    MemoryLookupResult Lookup(string sourceHash);
    int Count { get; }
    void LoadExisting(IEnumerable<(string SourceHash, string TranslatedText)> entries);
}

public class TranslationMemoryService : ITranslationMemoryService
{
    private readonly ConcurrentDictionary<string, string> _memory = new(StringComparer.Ordinal);

    public int Count => _memory.Count;

    public void Store(string sourceHash, string translatedText)
    {
        if (string.IsNullOrEmpty(sourceHash) || string.IsNullOrEmpty(translatedText))
            return;

        _memory[sourceHash] = translatedText;
    }

    public MemoryLookupResult Lookup(string sourceHash)
    {
        if (_memory.TryGetValue(sourceHash, out var text))
        {
            return new MemoryLookupResult(true, text);
        }
        return new MemoryLookupResult(false, null);
    }

    public void LoadExisting(IEnumerable<(string SourceHash, string TranslatedText)> entries)
    {
        foreach (var (hash, text) in entries)
        {
            if (!string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(text))
            {
                _memory[hash] = text;
            }
        }
    }
}
