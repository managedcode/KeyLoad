using System.Globalization;
using System.Net;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons.Targets;
using KurrentDB.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>REQ-BC-FAIL-007: inspect the actual pinned SDK configuration produced by each native Aspire cell.</summary>
internal sealed class IsolatedKurrentDiscoverySettingsTests
{
    private const string Target = "KurrentDB";
    private const string Prefix = "isolated-kurrent-";
    private const string HostSuffix = ".dev.internal";
    private const int Port = 2113;
    private const string MissingAddress = "Native single-node address is missing.";
    private const string MissingSeeds = "Native cluster DNS seeds are missing.";

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ActualNativeConnectionBindsEverySeedAndLeaderPreference(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedKurrentResources.Add(fixture.Context);
        _ = fixture.Build();
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        var settings = KurrentNativeSettings.CreateWriter(environment[IsolatedDocumentResourceAssertions.Connection]);
        var connectivity = settings.ConnectivitySettings;
        await Assert.That(connectivity.NodePreference).IsEqualTo(NodePreference.Leader);
        await Assert.That(connectivity.Insecure).IsTrue();
        await Assert.That(connectivity.TlsVerifyCert).IsTrue();
        await Assert.That(connectivity.IsSingleNode).IsEqualTo(count == 1);
        var expected = Enumerable.Range(1, count)
            .Select(index => Prefix + index.ToString(CultureInfo.InvariantCulture) + HostSuffix).ToArray();
        if (count == 1)
        {
            var address = connectivity.Address ?? throw new InvalidOperationException(MissingAddress);
            await Assert.That(address.Host).IsEqualTo(expected[0]);
            await Assert.That(address.Port).IsEqualTo(Port);
            await Assert.That(connectivity.GossipSeeds).IsEmpty();
        }
        else
        {
            await Assert.That(connectivity.Address).IsNull();
            await Assert.That(connectivity.GossipSeeds.All(seed => seed is DnsEndPoint)).IsTrue();
            var seeds = connectivity.DnsGossipSeeds ?? throw new InvalidOperationException(MissingSeeds);
            await Assert.That(seeds.Select(seed => seed.Host)).IsEquivalentTo(expected);
            await Assert.That(seeds.All(seed => seed.Port == Port)).IsTrue();
            await Assert.That(connectivity.IpGossipSeeds ?? []).IsEmpty();
        }
    }
}
