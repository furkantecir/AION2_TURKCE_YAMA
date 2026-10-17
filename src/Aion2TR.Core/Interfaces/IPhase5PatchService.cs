using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface IPhase5PatchService
{
    string RepoRoot { get; }
    string ExpectedArtifactSha256 { get; }
    
    Task<Phase5PatchStatusResult> CheckPatchStatusAsync(string? gameRoot, CancellationToken ct = default);
    Task<ContainerIntegrityResult> VerifyContainersAsync(string? gameRoot, CancellationToken ct = default);
    Task<Phase5OperationResult> InstallPatchAsync(string? gameRoot, CancellationToken ct = default);
    Task<Phase5OperationResult> RollbackPatchAsync(string? gameRoot, CancellationToken ct = default);
    Task<IReadOnlyList<Phase5TestStringItem>> LoadTestStringsAsync(CancellationToken ct = default);
    Task<TranslationProgressInfo> LoadTranslationProgressAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BackupInfoItem>> GetBackupsAsync(CancellationToken ct = default);
}
