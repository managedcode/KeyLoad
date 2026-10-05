using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeAppHostAdmissionTests
{
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";
    private const string SuiteKey = "KeyLoadTests:Suite";
    private const string BenchmarkKey = "Benchmarks:Enabled";
    private const string BenchmarkProfileKey = "Benchmarks:Profile";
    private const string ComparisonTargetKey = "Benchmarks:Target";
    private const string ComparisonNodesKey = "Benchmarks:NodeCount";
    private const string CohortEnabledKey = "KeyLoadTests:ProtocolCohort:Enabled";
    private const string CohortNode1Key = "KeyLoadTests:ProtocolCohort:Voters:node1";
    private const string CohortNode2Key = "KeyLoadTests:ProtocolCohort:Voters:node2";
    private const string CohortNode3Key = "KeyLoadTests:ProtocolCohort:Voters:node3";
    private const string TestImageNode1Key = "TestImages:node1";
    private const string TestImageNode2Key = "TestImages:node2";
    private const string TestImageNode3Key = "TestImages:node3";
    private const string Node1 = "node1";
    private const string Node2 = "node2";
    private const string Node3 = "node3";

    [Test]
    public async Task ExactEphemeralThreeVoterEqualDigestProfileIsAdmitted()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(fixture =>
        {
            var builder = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture);
            var images = RequestCqrsProbeAppHostBuilder.ReadThreeImages(builder);
            var profile = RequestCqrsProbeProfile.Read(builder, fixture.DataRoot, true, null, images);
            return AssertAdmittedAsync(profile, images, fixture);
        });
    }

    [Test]
    public async Task DisabledProfileRequiresNeitherFilesNorImageSettings()
    {
        var absent = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        await Assert.That(RequestCqrsProbeProfile.Read(absent, "/missing/data", false, null, null!)).IsNull();
        var disabled = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        disabled.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { [RequestCqrsProbeAppHostBuilder.EnabledKey] = "false" });
        var profile = RequestCqrsProbeProfile.Read(disabled, "/missing/data", false, null, null!);
        await Assert.That(profile).IsNull();
    }

    [Test]
    public async Task IncompatibleSuiteBenchmarkAndCohortModesAreRejected()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var validBuilder = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture);
            var images = RequestCqrsProbeAppHostBuilder.ReadThreeImages(validBuilder);
            foreach (var setting in new[] { SuiteKey, BenchmarkKey, BenchmarkProfileKey,
                         ComparisonTargetKey, ComparisonNodesKey })
            {
                var value = setting == SuiteKey ? "unit" : setting == BenchmarkProfileKey ? "micro" : "true";
                var builder = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture, new KeyValuePair<string, string?>(setting, value));
                var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                    RequestCqrsProbeProfile.Read(builder, fixture.DataRoot, true, null, images));
                await Assert.That(error.Message).IsEqualTo(InvalidConfiguration);
            }
            var cohort = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture,
                new KeyValuePair<string, string?>(CohortEnabledKey, "true"),
                new KeyValuePair<string, string?>(CohortNode1Key, "old"),
                new KeyValuePair<string, string?>(CohortNode2Key, "old"),
                new KeyValuePair<string, string?>(CohortNode3Key, "old"));
            var cohortError = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfile.Read(cohort, fixture.DataRoot, true, null, images));
            await Assert.That(cohortError.Message).IsEqualTo(InvalidConfiguration);
            var nonEphemeral = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture);
            var modeError = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfile.Read(nonEphemeral, fixture.DataRoot, false, null, images));
            await Assert.That(modeError.Message).IsEqualTo(InvalidConfiguration);
            var benchmarkNodes = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture);
            var benchmarkError = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfile.Read(benchmarkNodes, fixture.DataRoot, true, 3, images));
            await Assert.That(benchmarkError.Message).IsEqualTo(InvalidConfiguration);
        });
    }

    [Test]
    public async Task UnequalOrMissingImageMembersAreRejected()
    {
        await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            var builder = RequestCqrsProbeAppHostBuilder.CreateBuilder(fixture,
                new KeyValuePair<string, string?>(TestImageNode1Key, RequestCqrsProbeAppHostBuilder.ValidImage),
                new KeyValuePair<string, string?>(TestImageNode2Key, RequestCqrsProbeAppHostBuilder.ValidImage),
                new KeyValuePair<string, string?>(TestImageNode3Key, "ghcr.io/managedcode/keyload:other@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"));
            var unequal = new Dictionary<string, RuntimeContainerImage>(StringComparer.Ordinal)
            {
                [Node1] = RuntimeContainerImage.Read(builder, TestImageNode1Key),
                [Node2] = RuntimeContainerImage.Read(builder, TestImageNode2Key),
                [Node3] = RuntimeContainerImage.Read(builder, TestImageNode3Key)
            };
            var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfile.Read(builder, fixture.DataRoot, true, null, unequal));
            await Assert.That(error.Message).IsEqualTo(InvalidConfiguration);
            var missing = new Dictionary<string, RuntimeContainerImage>(unequal, StringComparer.Ordinal);
            missing.Remove(Node3);
            var missingError = Assert.ThrowsExactly<InvalidOperationException>(() =>
                RequestCqrsProbeProfile.Read(builder, fixture.DataRoot, true, null, missing));
            await Assert.That(missingError.Message).IsEqualTo(InvalidConfiguration);
        });
    }

    private static async Task AssertAdmittedAsync(RequestCqrsProbeProfile? profile,
        IReadOnlyDictionary<string, RuntimeContainerImage> images, RequestCqrsProbeAppHostFileFixture fixture)
    {
        await Assert.That(profile).IsNotNull();
        await Assert.That(profile!.SessionId).IsEqualTo(fixture.SessionId);
        await Assert.That(images.Count).IsEqualTo(3);
        await Assert.That(images.Values.Select(image => image.Reference).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }
}
