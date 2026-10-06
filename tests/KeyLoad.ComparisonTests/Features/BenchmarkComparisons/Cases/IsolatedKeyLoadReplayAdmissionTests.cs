using KeyLoad.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKeyLoadReplayAdmissionTests
{
    private const string KeyLoad = "KeyLoad";
    private const string Prefix = "KeyLoad__ReplayAdmission__";
    private const string Critical = Prefix + "CriticalPerVoter";
    private const string Forward = Prefix + "ForwardPerVoter";
    private const string Read = Prefix + "ReadBarrierPerVoter";
    private const string Data = Prefix + "DataAppendPerVoter";
    private const long Rf3Bound = 835_584;
    private const long Maximum = 1_048_576;

    /// <summary>AC-ISO-003/005 and AC-REP-006: every actual benchmark resource receives the fixed bounded profile.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task BenchmarkReplayPoolsAreExplicitAndFitEveryActualVoterGroup(int count)
    {
        await using var model = new IsolatedResourceTopologyApplication();
        var resources = await model.BuildAsync(KeyLoad, count);
        using var configuration = new ConfigurationManager();
        var expected = IsolatedKeyLoadReplayOptionsRegistration.Bind(configuration).Value;
        foreach (var node in resources.Where(resource => resource.Name != IsolatedResourceTopologyFixture.RunnerName))
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(long.Parse(environment[Critical], System.Globalization.CultureInfo.InvariantCulture))
                .IsEqualTo((long)expected.CriticalPerVoter);
            await Assert.That(long.Parse(environment[Forward], System.Globalization.CultureInfo.InvariantCulture))
                .IsEqualTo((long)expected.ForwardPerVoter);
            await Assert.That(long.Parse(environment[Read], System.Globalization.CultureInfo.InvariantCulture))
                .IsEqualTo((long)expected.ReadBarrierPerVoter);
            await Assert.That(long.Parse(environment[Data], System.Globalization.CultureInfo.InvariantCulture))
                .IsEqualTo((long)expected.DataAppendPerVoter);
        }
        var total = (long)expected.CriticalPerVoter + expected.ForwardPerVoter
            + expected.ReadBarrierPerVoter + expected.DataAppendPerVoter;
        await Assert.That(total * count).IsLessThan(Maximum);
        await Assert.That(total * 3).IsEqualTo(Rf3Bound);
    }

    /// <summary>The production RF3 composition retains original replay defaults.</summary>
    [Test]
    public async Task ProductionResourcesDoNotReceiveBenchmarkReplayOverrides()
    {
        await using var model = new IsolatedResourceTopologyApplication();
        foreach (var node in await model.BuildAsync(null, 3))
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(long.Parse(environment[Critical], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(16_384);
            await Assert.That(long.Parse(environment[Forward], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(32_768);
            await Assert.That(long.Parse(environment[Read], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(16_384);
            await Assert.That(long.Parse(environment[Data], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(32_768);
        }
    }

    [Test]
    public async Task ConfiguredReadBarrierQuotaReachesEveryNativeNode()
    {
        await using var model = new IsolatedResourceTopologyApplication();
        var resources = await model.BuildAsync(KeyLoad, 3,
            IsolatedKeyLoadReplayOptionsRegistration.SectionName + ":ReadBarrierPerVoter", "512");
        foreach (var node in resources.Where(resource => resource.Name != IsolatedResourceTopologyFixture.RunnerName))
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(long.Parse(environment[Read], System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(512);
        }
    }
}
