using System.Globalization;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>AC-NATIVE-004..006: canonical peer transport options are admitted before physical ownership.</summary>
internal sealed class CurrentPeerOptionsOwnerTests
{
    private const int CredentialBytes = 32;
    private const int RpcDeadlineMilliseconds = 2_000;
    private const int ValidNativeAdmissions = 4;
    private const int MinimumAdminSuffixCharacters = 32;
    private const string AdministratorPrefix = "root.";
    private const string VoterOne = "https://127.0.0.1:5501";
    private const string VoterTwo = "https://127.0.0.1:5502";
    private const string VoterThree = "https://127.0.0.1:5503";
    private const string TestDataDirectoryPrefix = "keyload-peer-options-owner-";

    [Test]
    public async Task AcNativeDiscoveryAdmissionConfigurationReachesNativeOwnerBeforePhysicalOwnership()
    {
        var dataDirectory = NewDataDirectory();
        using var configuration = Configuration(dataDirectory, ValidNativeAdmissions);
        using var provider = Provider(configuration);
        var runtime = provider.GetRequiredService<ServerRuntimeOptions>();

        runtime.ValidateBeforePhysicalOwnership();
        await Assert.That(runtime.Membership.Value.MaximumAdmissions).IsEqualTo(ValidNativeAdmissions);
        runtime.Membership.Value.Validate();
        await Assert.That(runtime.ReplicaConfiguration.Value.RpcTimeout)
            .IsEqualTo(TimeSpan.FromMilliseconds(RpcDeadlineMilliseconds));
        await Assert.That(Directory.Exists(dataDirectory)).IsFalse();
    }

    [Test]
    public async Task AcNativeDiscoveryZeroAdmissionRejectsBeforePhysicalOwnership()
    {
        var dataDirectory = NewDataDirectory();
        using var configuration = Configuration(dataDirectory, 0);
        using var provider = Provider(configuration);
        var runtime = provider.GetRequiredService<ServerRuntimeOptions>();

        Assert.ThrowsExactly<OptionsValidationException>(runtime.ValidateBeforePhysicalOwnership);
        await Assert.That(Directory.Exists(dataDirectory)).IsFalse();
    }

    private static ConfigurationManager Configuration(string dataDirectory, int nativeAdmissions)
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
            [NativeMembershipKey(nameof(OrleansMembershipOptions.MaximumAdmissions))]
                = nativeAdmissions.ToString(CultureInfo.InvariantCulture)
        };
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    private static string NodeKey(string property)
        => ServerProtocol.ConfigurationSection + ConfigurationPath.KeyDelimiter + property;

    private static string NativeMembershipKey(string property)
        => OrleansMembershipOptions.SectionName + ConfigurationPath.KeyDelimiter + property;

    private static ServiceProvider Provider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddRuntimeOptions(configuration);
        return services.BuildServiceProvider();
    }

    private static string NewDataDirectory()
        => Path.Combine(Path.GetTempPath(), TestDataDirectoryPrefix + Guid.NewGuid().ToString("N"));
}
