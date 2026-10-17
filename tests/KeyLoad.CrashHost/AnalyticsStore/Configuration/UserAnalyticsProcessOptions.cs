using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.AnalyticsStore;

/// <summary>Owns the validated fixed process-fixture node configuration; no running-quorum claim.</summary>
[ConfigurationBinding]
internal static class UserAnalyticsProcessOptions
{
    private const string FirstVoter = "https://127.0.0.1:5511";
    private const string SecondVoter = "https://127.0.0.1:5512";
    private const string ThirdVoter = "https://127.0.0.1:5513";
    private const int TestSecretBytes = 32;
    private const byte TestKeyByte = 42;
    private const string IncarnationText = "b48b95ce-971b-4de0-bbca-4823deec6db3";
    private const string ShardText = "6088f23d-d45f-4d7b-8a5b-3e201a979ec9";
    private static readonly Guid Incarnation = new(IncarnationText);
    private static readonly Guid Shard = new(ShardText);

    internal static IOptions<NodeOptions> Create(string root) => CrashExecutionOptions.Node(new()
    {
        DataDirectory = Path.Combine(root, UserAnalyticsProcessProtocol.PhysicalDirectory),
        PublicEndpoint = FirstVoter,
        Peers = [FirstVoter, SecondVoter, ThirdVoter],
        Incarnation = Incarnation,
        PhysicalShardId = Shard,
        SigningKey = Convert.ToBase64String(Enumerable.Repeat(TestKeyByte, TestSecretBytes).ToArray()),
        PeerSecret = Convert.ToBase64String(Enumerable.Repeat(TestKeyByte, TestSecretBytes).ToArray()),
        AdminKey = CrashFixtureValues.Credential
    });
}
