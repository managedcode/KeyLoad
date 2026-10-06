using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Replication;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-004: trusted benchmark topology never changes the production default or quorum.</summary>
internal sealed class BenchmarkTopologyConfigurationTests
{
    private const string BenchmarkSetting = "KeyLoad:BenchmarkTopology";
    private const string ConfigurationSection = "KeyLoad";
    private const string Enabled = "true";

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task AcIso004ProductionDefaultsRejectSmallVoterSets(int nodes)
    {
        var configuration = BenchmarkTopologyConfigurationFixture.Replica(nodes);
        var options = BenchmarkTopologyConfigurationFixture.Node(nodes);
        await Assert.That(configuration.BenchmarkTopology).IsFalse();
        await Assert.That(options.BenchmarkTopology).IsFalse();
        Assert.ThrowsExactly<InvalidOperationException>(configuration.Validate);
        Assert.ThrowsExactly<InvalidOperationException>(options.Validate);
    }

    [Test]
    [Arguments(1, 1)]
    [Arguments(2, 2)]
    [Arguments(3, 2)]
    public async Task AcIso004ExplicitBenchmarkUsesTheSameComputedMajority(int nodes, int majority)
    {
        var configuration = BenchmarkTopologyConfigurationFixture.Replica(nodes) with { BenchmarkTopology = true };
        var options = BenchmarkTopologyConfigurationFixture.Node(nodes) with { BenchmarkTopology = true };
        configuration.Validate();
        options.Validate();
        var startupConfiguration = options.CreateReplicaConfiguration(options.DataDirectory);
        startupConfiguration.Validate();
        await Assert.That(configuration.Majority).IsEqualTo(majority);
        await Assert.That(startupConfiguration.Majority).IsEqualTo(majority);
        await Assert.That(startupConfiguration.BenchmarkTopology).IsTrue();
        await Assert.That(startupConfiguration.VoterIds.Length).IsEqualTo(nodes);
    }

    [Test]
    [Arguments(3, 2)]
    [Arguments(5, 3)]
    public async Task AcIso004ProductionOddGroupsRemainSupported(int nodes, int majority)
    {
        var configuration = BenchmarkTopologyConfigurationFixture.Replica(nodes);
        var options = BenchmarkTopologyConfigurationFixture.Node(nodes);
        configuration.Validate();
        options.Validate();
        await Assert.That(configuration.Majority).IsEqualTo(majority);
        await Assert.That(options.CreateReplicaConfiguration(options.DataDirectory).BenchmarkTopology).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(4)]
    [Arguments(5)]
    public void AcIso004BenchmarkOptInDoesNotAdmitOtherCounts(int nodes)
    {
        var configuration = BenchmarkTopologyConfigurationFixture.Replica(nodes) with { BenchmarkTopology = true };
        var options = BenchmarkTopologyConfigurationFixture.Node(nodes) with { BenchmarkTopology = true };
        Assert.ThrowsExactly<InvalidOperationException>(configuration.Validate);
        Assert.ThrowsExactly<InvalidOperationException>(options.Validate);
    }

    [Test]
    public void AcIso004BenchmarkOptInDoesNotAdmitMalformedAuthorities()
    {
        var configuration = BenchmarkTopologyConfigurationFixture.Replica(2) with { BenchmarkTopology = true };
        ReplicaConfiguration[] invalid =
        [
            configuration with { VoterIds = default },
            configuration with { VoterIds = [configuration.LocalId, configuration.LocalId] },
            configuration with { VoterIds = [configuration.LocalId, string.Empty] },
            configuration with { LocalId = string.Empty },
            configuration with { LocalId = BenchmarkTopologyConfigurationFixture.OutsideVoter },
            configuration with { Incarnation = Guid.Empty },
            configuration with { RpcTimeout = TimeSpan.Zero }
        ];
        foreach (var item in invalid)
        {
            Assert.ThrowsExactly<InvalidOperationException>(item.Validate);
        }
    }

    [Test]
    public void AcIso004BenchmarkOptInPreservesTransportAndCredentialValidation()
    {
        var options = BenchmarkTopologyConfigurationFixture.Node(2) with { BenchmarkTopology = true };
        NodeOptions[] invalid =
        [
            options with { Peers = [options.PublicEndpoint, options.PublicEndpoint] },
            options with { PublicEndpoint = BenchmarkTopologyConfigurationFixture.OutsideVoter },
            options with { PhysicalShardId = Guid.Empty },
            options with { Incarnation = Guid.Empty },
            options with { SigningKey = string.Empty },
            options with { PeerSecret = string.Empty },
            options with { AdminKey = string.Empty },
            options with { SiloPort = 0 }
        ];
        foreach (var item in invalid)
        {
            Assert.ThrowsExactly<InvalidOperationException>(item.Validate);
        }
    }

    [Test]
    public async Task AcIso004OptInIsBoundFromTheTrustedStartupConfiguration()
    {
        var production = new ConfigurationBuilder().Build();
        var benchmark = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [BenchmarkSetting] = Enabled }).Build();
        using var productionLifetime = production as IDisposable;
        using var benchmarkLifetime = benchmark as IDisposable;
        var ordinary = production.GetSection(ConfigurationSection).Get<NodeOptions>() ?? new();
        var selected = benchmark.GetSection(ConfigurationSection).Get<NodeOptions>()!;
        await Assert.That(ordinary.BenchmarkTopology).IsFalse();
        await Assert.That(selected.BenchmarkTopology).IsTrue();
    }
}

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
