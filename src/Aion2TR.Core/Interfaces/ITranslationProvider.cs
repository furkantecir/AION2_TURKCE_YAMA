using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aion2TR.Core.Interfaces;

public record TranslationBatchItem(string SourceHash, string SourceText, string? Context);

public record TranslationBatchResultItem(string SourceHash, string? TranslatedText, bool Success, string? ErrorMessage);

public interface ITranslationProvider
{
    string Name { get; }
    int DefaultBatchSize { get; }
    Task<IReadOnlyList<TranslationBatchResultItem>> TranslateBatchAsync(
        IReadOnlyList<TranslationBatchItem> items,
        string targetLanguage,
        CancellationToken cancellationToken = default);
}
