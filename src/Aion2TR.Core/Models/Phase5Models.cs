using System;
using System.Collections.Generic;

namespace Aion2TR.Core.Models;

public enum Phase5Status
{
    NotInstalled,
    Installed,
    UnknownFile,
    GameNotFound,
    Error
}

public class ContainerIntegrityItem
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string ExpectedSha256 { get; set; } = string.Empty;
    public string? ActualSha256 { get; set; }
    public long SizeBytes { get; set; }
    public bool Exists { get; set; }
    public bool IsMatch => Exists && string.Equals(ExpectedSha256, ActualSha256, StringComparison.OrdinalIgnoreCase);
}

public class ContainerIntegrityResult
{
    public bool IsAllSafe { get; set; }
    public List<ContainerIntegrityItem> Items { get; set; } = [];
    public string SummaryMessage { get; set; } = string.Empty;
}

public class Phase5PatchStatusResult
{
    public Phase5Status Status { get; set; }
    public string LoosePath { get; set; } = string.Empty;
    public string? LooseSha256 { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class Phase5TestStringItem
{
    public string Key { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string TargetTestText { get; set; } = string.Empty;
    public string? CurrentTranslatedText { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
}

public class TranslationProgressInfo
{
    public int TotalUnique { get; set; } = 72309;
    public int Completed { get; set; } = 20000;
    public int Remaining { get; set; } = 52309;
    public double Percentage => TotalUnique > 0 ? (double)Completed / TotalUnique * 100.0 : 0.0;
    public string LastBatch { get; set; } = "Batch 02";
    public string NextBatch { get; set; } = "Batch 03";
    public string NextRange { get; set; } = "20,001 - 30,000";
}

public class BackupInfoItem
{
    public string DirectoryName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string FormattedDate { get; set; } = string.Empty;
    public string Status { get; set; } = "Güvenli / Doğrulandı";
    public string TargetLoosePath { get; set; } = string.Empty;
    public bool OriginalLooseFileExisted { get; set; }
}

public class Phase5OperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }

    public static Phase5OperationResult Ok(string message) => new() { Success = true, Message = message };
    public static Phase5OperationResult Fail(string message, string? details = null) => new() { Success = false, Message = message, ErrorDetails = details };
}
