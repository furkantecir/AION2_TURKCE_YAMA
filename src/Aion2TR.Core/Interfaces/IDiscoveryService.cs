using System;
using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface IDiscoveryService
{
    Task<GameScanReport> ScanGameLocalizationAsync(
        string gameRootPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task SaveReportAsync(GameScanReport report, string outputJsonPath, string outputTextPath, CancellationToken cancellationToken = default);
}
