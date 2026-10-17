using System.Text;
using System.Threading.Tasks;
using Aion2TR.Infrastructure.Services;
using Xunit;

namespace Aion2TR.Tests;

public class HashServiceTests
{
    private readonly Sha256HashService _hashService = new();

    [Fact]
    public void ComputeSha256_KnownData_MatchesStandardHash()
    {
        // "abc" SHA-256 standard test vector: ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
        var bytes = Encoding.UTF8.GetBytes("abc");
        var hash = _hashService.ComputeSha256(bytes);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void ComputeSha256_DifferentData_ProducesDifferentHash()
    {
        var h1 = _hashService.ComputeSha256(Encoding.UTF8.GetBytes("Data1"));
        var h2 = _hashService.ComputeSha256(Encoding.UTF8.GetBytes("Data2"));

        Assert.NotEqual(h1, h2);
    }
}
