using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface IPhase7PatchService
{
    string RepoRoot { get; }
    
    Task<PreflightGameManifest> GeneratePreflightManifestAsync(string gameRoot, CancellationToken ct = default);
    Task<bool> ValidateFinalDatAsync(CancellationToken ct = default);
    Task<string> GenerateTestDatAsync(CancellationToken ct = default);
    Task<string> GenerateTestPakAsync(string testDatPath, CancellationToken ct = default);
    Task<TestContainerValidationReport> ValidateContainerOfflineAsync(string pakPath, string expectedDatPath, CancellationToken ct = default);
    Task<Phase7OperationResult> InstallTestPakAsync(string gameRoot, CancellationToken ct = default);
    Task<Phase7OperationResult> RollbackTestPakAsync(string gameRoot, CancellationToken ct = default);
    Task<Phase7PatchStatusResult> CheckStatusAsync(string gameRoot, CancellationToken ct = default);
    Task<IReadOnlyList<Phase7TestKeyInfo>> GetTestKeysAsync(CancellationToken ct = default);
    Task<ContainerIntegrityResult> VerifyOfficialIntegrityAsync(string gameRoot, CancellationToken ct = default);
}
