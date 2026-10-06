using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/005/006: pure member inputs preserve exact pinned build identity; native HTTP proof remains a separate GitHub gate.</summary>
internal sealed class KurrentGossipVersionTests
{
    private const string PinnedImageVersion = "26.1.2", PinnedGossipVersion = "26.1.2.3778";
    private const string ImagePrefix = "docker.io/kurrentplatform/kurrentdb:";
    private const string DigestSuffix = "@sha256:ef49a58bab8bc4d7b08cd218f1bb85cf230b03edf5d136e98d6e86a3e5100b6a";
    private const string Connection = "esdb://localhost:2113?tls=false";
    private const string RunId = "b8f60fda56334d1c9480d03b4fb78b37";
    private static readonly string[] RejectedVersions =
    [
        PinnedImageVersion, "26.1.2.3777", "26.1.2.3779", "26.1.3.3778", "26.1.2.3778-rc1", "26.1.2.3778+build",
        "26.1.2.3778 ", " 26.1.2.3778", "unknown_version", string.Empty,
    ];

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ExactNativeBuildIsAcceptedAndRetainedSeparatelyFromImageProfile(int count)
    {
        var topology = ComparisonTopologies.FromNodeCount(count);
        var views = NativeDocumentTopologyKurrentTests.Views(count, PinnedGossipVersion);
        await Assert.That(KurrentConstants.ExpectedServerVersion).IsEqualTo(PinnedImageVersion);
        await Assert.That(KurrentConstants.ExpectedGossipVersion).IsEqualTo(PinnedGossipVersion);
        await Assert.That(KurrentClusterMembers.IsReady(views, topology)).IsTrue();
        var evidence = KurrentClusterVerifier.CreateEvidence(views, topology);
        var versions = evidence.Observations.Where(value => value.StartsWith(KurrentConstants.ObservationVersion, StringComparison.Ordinal)).ToArray();
        await Assert.That(versions.Length).IsEqualTo(count);
        await Assert.That(versions.All(value => value == KurrentConstants.ObservationVersion + PinnedGossipVersion)).IsTrue();
        using var client = new HttpClient();
        await using var target = new KurrentTarget(Connection, [client], RunId, ImagePrefix + PinnedImageVersion + DigestSuffix, ComparisonTopology.Standalone, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Diagnostics());
        await Assert.That(target.Profile.Version).IsEqualTo(PinnedImageVersion);
        await Assert.That(target.Profile.Image).IsEqualTo(ImagePrefix + PinnedImageVersion + DigestSuffix);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ImageTagsNeighboringBuildsAndNormalizedVariantsFailExactNativeIdentity(int count)
    {
        foreach (var version in RejectedVersions)
        {
            var views = NativeDocumentTopologyKurrentTests.Views(count, version);
            await Assert.That(KurrentClusterMembers.IsReady(views, ComparisonTopologies.FromNodeCount(count))).IsFalse();
        }
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task AForeignBuildOnOneOtherwiseMatchingMemberRejectsTheWholeGroup(int count)
    {
        var views = NativeDocumentTopologyKurrentTests.Views(count, PinnedGossipVersion);
        ReplaceMember(views, 1, views[0].Members[1] with { Version = RejectedVersions[1] });
        await Assert.That(KurrentClusterMembers.IsReady(views, ComparisonTopologies.FromNodeCount(count))).IsFalse();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ExactBuildDoesNotOverrideUnhealthyMembersOrEndpointAndRoleRequirements(int count)
    {
        var baseline = NativeDocumentTopologyKurrentTests.Views(count, PinnedGossipVersion);
        var member = baseline[0].Members[0];
        foreach (var invalid in new[]
        {
            member with { Id = string.Empty }, member with { IsAlive = false }, member with { IsReadOnly = true },
            member with { HttpEndpointIp = string.Empty }, member with { HttpEndpointPort = 0 },
            member with { InternalHttpEndpointIp = string.Empty }, member with { InternalHttpEndpointPort = 0 },
            member with { State = KurrentConstants.FollowerState },
        })
        {
            var views = NativeDocumentTopologyKurrentTests.Views(count, PinnedGossipVersion);
            ReplaceMember(views, 0, invalid);
            await Assert.That(KurrentClusterMembers.IsReady(views, ComparisonTopologies.FromNodeCount(count))).IsFalse();
        }
    }

    [Test]
    [Arguments("26.1.2.3778")]
    [Arguments("26.1.2.3779")]
    [Arguments("26.1.3")]
    [Arguments("26.1.2-rc1")]
    public async Task NativeBuildVersionCannotReplaceTheStrictImageTag(string imageVersion)
    {
        using var client = new HttpClient();
        var failure = await Assert.ThrowsExactlyAsync<ComparisonFailureException>(() => CreateProfileAsync(client, imageVersion));
        await Assert.That(failure!.Message).IsEqualTo(KurrentConstants.VersionMismatch);
    }

    private static async Task CreateProfileAsync(HttpClient client, string imageVersion)
    {
        var target = new KurrentTarget(Connection, [client], RunId, ImagePrefix + imageVersion + DigestSuffix, ComparisonTopology.Standalone, UnitBenchmarkOptions.Lifecycle(), UnitBenchmarkOptions.Diagnostics());
        await target.DisposeAsync();
    }

    private static void ReplaceMember(KurrentGossipView[] views, int index, KurrentGossipMember replacement)
    {
        for (var view = 0; view < views.Length; view++)
        {
            var original = views[view].Members[index];
            var members = views[view].Members.ToArray();
            members[index] = replacement;
            views[view] = views[view] with { Members = members, LocalMember = views[view].LocalMember.Id == original.Id ? replacement : views[view].LocalMember };
        }
    }
}
