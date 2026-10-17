using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface ILocalizationRuntimePatchService
{
    string RepoRoot { get; }
    
    Task<RuntimePatchStatus> CheckStatusAsync(string gameRoot, CancellationToken ct = default);
    Task<RuntimePatchOperationResult> InstallTestPatchAsync(string gameRoot, CancellationToken ct = default);
    Task<RuntimePatchOperationResult> InstallFullPatchAsync(string gameRoot, CancellationToken ct = default);
    Task<RuntimePatchOperationResult> VerifyFilesAsync(string gameRoot, CancellationToken ct = default);
    bool IsGameProcessRunning();
}
