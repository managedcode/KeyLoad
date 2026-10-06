using System.Globalization;
using KeyLoad.Replication;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>AC-NATIVE-004..006: canonical peer transport options are admitted before physical ownership.</summary>
internal sealed class CurrentPeerOptionsOwnerTests
{
    private const int CredentialBytes = 32;
    private const int RpcDeadlineMilliseconds = 2_000;
    private const int ValidDiscoveryDeadlineMilliseconds = 250;
    private const int MinimumAdminSuffixCharacters = 32;
    private const string AdministratorPrefix = "root.";
    private const string VoterOne = "https://127.0.0.1:5501";
    private const string VoterTwo = "https://127.0.0.1:5502";
    private const string VoterThree = "https://127.0.0.1:5503";
    private const string TestDataDirectoryPrefix = "keyload-peer-options-owner-";
    private const string TimeSpanFormat = "c";

    [Test]
    public async Task AcNativePeerDiscoveryConfigurationReachesSocketBeforePhysicalOwnership()
    {
        var dataDirectory = NewDataDirectory();
        using var configuration = Configuration(dataDirectory, TimeSpan.FromMilliseconds(ValidDiscoveryDeadlineMilliseconds));
        using var provider = Provider(configuration);
        var runtime = provider.GetRequiredService<ServerRuntimeOptions>();

        runtime.ValidateBeforePhysicalOwnership();
        using var security = new PeerSecurity(runtime.Peer.Value.Secret, TimeProvider.System, runtime.PeerDiscovery);
        using var handler = (DelegatingHandler)security.CreateHandler();
        var sockets = (SocketsHttpHandler)handler.InnerHandler!;

        await Assert.That(sockets.ConnectTimeout).IsEqualTo(TimeSpan.FromMilliseconds(ValidDiscoveryDeadlineMilliseconds));
        await Assert.That(runtime.ReplicaConfiguration.Value.RpcTimeout)
            .IsEqualTo(TimeSpan.FromMilliseconds(RpcDeadlineMilliseconds));
        await Assert.That(Directory.Exists(dataDirectory)).IsFalse();
    }

    [Test]
    public async Task AcNativeDiscoveryDeadlineAtRpcBoundaryRejectsBeforePhysicalOwnership()
    {
        var dataDirectory = NewDataDirectory();
        using var configuration = Configuration(dataDirectory, TimeSpan.FromMilliseconds(RpcDeadlineMilliseconds));
        using var provider = Provider(configuration);
        var runtime = provider.GetRequiredService<ServerRuntimeOptions>();

        Assert.ThrowsExactly<InvalidOperationException>(runtime.ValidateBeforePhysicalOwnership);
        await Assert.That(Directory.Exists(dataDirectory)).IsFalse();
    }

    private static ConfigurationManager Configuration(string dataDirectory, TimeSpan connectTimeout)
    {
        var values = new Dictionary<string, string?>
        {
            [NodeKey(nameof(NodeOptions.DataDirectory))] = dataDirectory,
            [NodeKey(nameof(NodeOptions.PublicEndpoint))] = VoterOne,
            [NodeKey(nameof(NodeOptions.Peers)) + ConfigurationPath.KeyDelimiter + "0"] = VoterOne,
            [NodeKey(nameof(NodeOptions.Peers)) + ConfigurationPath.KeyDelimiter + "1"] = VoterTwo,
            [NodeKey(nameof(NodeOptions.Peers)) + ConfigurationPath.KeyDelimiter + "2"] = VoterThree,
            [NodeKey(nameof(NodeOptions.PhysicalShardId))] = Guid.NewGuid().ToString(),
            [NodeKey(nameof(NodeOptions.Incarnation))] = Guid.NewGuid().ToString(),
            [NodeKey(nameof(NodeOptions.SigningKey))] = Convert.ToBase64String(new byte[CredentialBytes]),
            [NodeKey(nameof(NodeOptions.PeerSecret))] = Convert.ToBase64String(new byte[CredentialBytes]),
            [NodeKey(nameof(NodeOptions.AdminKey))] = AdministratorPrefix + new string('x', MinimumAdminSuffixCharacters),
            [PeerDiscoveryKey(nameof(PeerDiscoveryOptions.ConnectTimeout))]
                = connectTimeout.ToString(TimeSpanFormat, CultureInfo.InvariantCulture)
        };
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    private static string NodeKey(string property)
        => ServerProtocol.ConfigurationSection + ConfigurationPath.KeyDelimiter + property;

    private static string PeerDiscoveryKey(string property)
        => PeerDiscoveryOptions.SectionName + ConfigurationPath.KeyDelimiter + property;

    private static ServiceProvider Provider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddRuntimeOptions(configuration);
        return services.BuildServiceProvider();
    }

    private static string NewDataDirectory()
        => Path.Combine(Path.GetTempPath(), TestDataDirectoryPrefix + Guid.NewGuid().ToString("N"));
}
