using System.Security.Cryptography;
using KeyLoad.Server;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class BenchmarkTopologyConfigurationFixture
{
    private const string PublicEndpoint = "https://127.0.0.1:5501";
    private const string AdministratorPrefix = "root.";
    private const int SecretBytes = 32;

    internal static NodeOptions Node(int silos, int copies) => new()
    {
        DataDirectory = Path.GetTempPath(),
        PublicEndpoint = PublicEndpoint,
        SiloCount = silos,
        ReplicaCount = copies,
        Incarnation = Guid.NewGuid(),
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
        AdminKey = AdministratorPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes))
    };
}
