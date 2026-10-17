namespace Aion2TR.Core.Models;

public record L10NEntry(
    string Id,
    string? Key,
    string Text,
    long Offset,
    int RawLength,
    string Encoding,
    bool IsDecrypted = true,
    bool IsTextUtf16 = false,
    bool IsKeyUtf16 = false
)
{
    public L10NEntry(string key, string text, long offset = 0, bool isKeyUtf16 = false, bool isTextUtf16 = false)
        : this(
            Id: key,
            Key: key,
            Text: text,
            Offset: offset,
            RawLength: 0,
            Encoding: isTextUtf16 ? "UTF-16" : "UTF-8",
            IsDecrypted: true,
            IsTextUtf16: isTextUtf16,
            IsKeyUtf16: isKeyUtf16)
    {
    }
}
