using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface IPhase7RollbackService
{
    Task<Phase7OperationResult> RollbackAsync(string gameRoot, CancellationToken ct = default);
}
