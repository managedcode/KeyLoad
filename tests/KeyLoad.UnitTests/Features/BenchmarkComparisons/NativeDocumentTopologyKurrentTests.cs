using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003 requires real-count gossip role, identity and copy contracts.</summary>
internal sealed class NativeDocumentTopologyKurrentTests
{
    private const string MemberPrefix = "member-";
    private const string HostPrefix = "kurrent-";
    private const int Port = 2113;

    [Test]
    public async Task AC_ISO_003_AcceptsOneLeaderAndExactNativeFollowersAtEachCount()
    {
        foreach (var count in new[] { 1, 2, 3 })
        {
            var topology = ComparisonTopologies.FromNodeCount(count);
            var views = Views(count);
            await Assert.That(KurrentClusterMembers.IsReady(views, topology)).IsTrue();
            var evidence = KurrentClusterVerifier.CreateEvidence(views, topology);
            await Assert.That(evidence.Nodes).IsEqualTo(count);
            await Assert.That(evidence.DataCopies).IsEqualTo(count);
        }
    }

    [Test]
    public async Task AC_ISO_003_RejectsExtraMembersForeignLocalIdsAndDisagreeingNativeEndpoints()
    {
        await Assert.That(KurrentClusterMembers.IsReady(Views(3), ComparisonTopology.TwoNode)).IsFalse();
        var views = Views(2);
        views[1] = views[1] with { LocalMember = views[0].LocalMember };
        await Assert.That(KurrentClusterMembers.IsReady(views, ComparisonTopology.TwoNode)).IsFalse();
        views = Views(2);
        var members = views[1].Members.ToArray();
        members[1] = members[1] with { HttpEndpointIp = HostPrefix + 9 };
        views[1] = views[1] with { Members = members };
        await Assert.That(KurrentClusterMembers.IsReady(views, ComparisonTopology.TwoNode)).IsFalse();
    }

    private static KurrentGossipView[] Views(int count) => Views(count, KurrentConstants.ExpectedGossipVersion);

    internal static KurrentGossipView[] Views(int count, string version)
    {
        var members = Enumerable.Range(0, count).Select(index => new KurrentGossipMember(MemberPrefix + index,
            index == 0 ? KurrentConstants.LeaderState : KurrentConstants.FollowerState, version,
            HostPrefix + index, Port, HostPrefix + index, Port, true, false, 100, 101, 101)).ToArray();
        return members.Select(member => new KurrentGossipView(member.HttpEndpointIp, Port, members.ToArray(), member)).ToArray();
    }
}
