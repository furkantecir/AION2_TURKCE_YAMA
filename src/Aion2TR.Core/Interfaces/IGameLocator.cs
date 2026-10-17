using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface IGameLocator
{
    Task<GameLocationResult> LocateGameAsync(CancellationToken cancellationToken = default);
    bool ValidateGameDirectory(string path);
}
