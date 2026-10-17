using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface ILocalizationRuntimeRollbackService
{
    string RepoRoot { get; }
    
    Task<RuntimePatchOperationResult> RollbackTestPatchAsync(string gameRoot, CancellationToken ct = default);
    Task<RuntimePatchOperationResult> RestoreOriginalAsync(string gameRoot, CancellationToken ct = default);
    Task<RuntimePatchOperationResult> RecoverFromCrashAsync(string gameRoot, CancellationToken ct = default);
}
