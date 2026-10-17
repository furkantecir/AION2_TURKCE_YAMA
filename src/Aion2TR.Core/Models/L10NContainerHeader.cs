namespace Aion2TR.Core.Models;

/// <summary>
/// Verified container header for AION 2 L10NString.dat (Version 2).
/// Format:
/// - int32 Version (Offset 0x00, value = 2)
/// - 16 bytes encrypted header block (Offset 0x04) XORed with DeriveHeaderKey(seed, EHeaderType.Localization)
///   -> int32 PackedSize (Bytes 0x00..0x03)
///   -> int32 EncryptionType (Bytes 0x04..0x07, 2 = CompressedAES)
///   -> int32 AlignedSize (Bytes 0x08..0x0B)
///   -> int32 RawSize (Bytes 0x0C..0x0F)
/// - Encrypted Payload starts at Offset 0x14 with length AlignedSize
/// </summary>
public record L10NContainerHeader(
    int Version,
    ulong Seed,
    int RawSize,
    int PackedSize,
    int AlignedSize,
    int EncryptionType,
    bool IsValid,
    string? ErrorMessage = null
);
