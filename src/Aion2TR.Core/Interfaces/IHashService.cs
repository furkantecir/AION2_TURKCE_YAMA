using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Aion2TR.Core.Interfaces;

public interface IHashService
{
    string ComputeSha256(byte[] data);
    Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default);
    Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default);
}
