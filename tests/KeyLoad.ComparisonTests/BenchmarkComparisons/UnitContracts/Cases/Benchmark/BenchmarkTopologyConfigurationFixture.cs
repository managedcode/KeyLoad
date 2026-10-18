using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class BenchmarkTopologyConfigurationFixture
{
    internal const string OutsideVoter = "https://127.0.0.1:5599";
    private const string VoterPrefix = "https://127.0.0.1:550";
    private const string PrimaryVoter = VoterPrefix + "1";
    private const string AdministratorPrefix = "root.";
    private const int SecretBytes = 32;

    internal static ReplicaConfiguration Replica(int nodes)
        => new(PrimaryVoter, Voters(nodes), Path.GetTempPath(), Guid.NewGuid());

    internal static NodeOptions Node(int nodes) => new()
    {
        DataDirectory = Path.GetTempPath(),
        PublicEndpoint = PrimaryVoter,
        Peers = Voters(nodes),
        PhysicalShardId = Guid.NewGuid(),
        Incarnation = Guid.NewGuid(),
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
        PeerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
        AdminKey = AdministratorPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretBytes))
    };

    private static ImmutableArray<string> Voters(int nodes)
        => Enumerable.Range(1, nodes).Select(number => VoterPrefix + number.ToString(CultureInfo.InvariantCulture)).ToImmutableArray();
}
