using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Interfaces;
using Aion2TR.Core.Models;

namespace Aion2TR.Infrastructure.Services;

public class LocalizationRuntimeRollbackService : ILocalizationRuntimeRollbackService
{
    private readonly LocalizationRuntimePatchService _innerService;

    public string RepoRoot => _innerService.RepoRoot;

    public LocalizationRuntimeRollbackService(string? repoRoot = null, IHashService? hashService = null)
    {
        _innerService = new LocalizationRuntimePatchService(repoRoot, hashService);
    }

    public LocalizationRuntimeRollbackService(LocalizationRuntimePatchService innerService)
    {
        _innerService = innerService;
    }

    public Task<RuntimePatchOperationResult> RollbackTestPatchAsync(string gameRoot, CancellationToken ct = default)
    {
        return _innerService.RollbackTestPatchAsync(gameRoot, ct);
    }

    public Task<RuntimePatchOperationResult> RestoreOriginalAsync(string gameRoot, CancellationToken ct = default)
    {
        return _innerService.RestoreOriginalAsync(gameRoot, ct);
    }

    public Task<RuntimePatchOperationResult> RecoverFromCrashAsync(string gameRoot, CancellationToken ct = default)
    {
        return _innerService.RecoverFromCrashAsync(gameRoot, ct);
    }
}
