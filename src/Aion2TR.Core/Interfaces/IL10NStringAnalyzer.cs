using System.Threading;
using System.Threading.Tasks;
using Aion2TR.Core.Models;

namespace Aion2TR.Core.Interfaces;

public interface IL10NStringAnalyzer
{
    Task<L10NAnalysisResult> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default);
    L10NAnalysisResult AnalyzeBytes(byte[] data, string sourceName);
}
