namespace KeyLoad.Comparisons.Targets;

internal static class KurrentClusterMembers
{
    internal static bool IsReady(KurrentGossipView[] views, ComparisonTopology topology)
    {
        if (views.Length != ExpectedCount(topology))
        {
            return false;
        }

        var canonical = CanonicalMembers(views[0].Members);
        return MembershipIsValid(canonical, views, topology) && ViewsAgree(canonical, views);
    }

    private static int ExpectedCount(ComparisonTopology topology)
        => topology == ComparisonTopology.Replicated ? KurrentConstants.ExpectedReplicatedNodes : KurrentConstants.ExpectedSingleNode;

    private static KurrentGossipMember[] CanonicalMembers(KurrentGossipMember[] members)
        => members.OrderBy(member => member.Id, StringComparer.Ordinal).ToArray();

    private static bool MembershipIsValid(KurrentGossipMember[] members, KurrentGossipView[] views,
        ComparisonTopology topology)
    {
        var expectedCount = ExpectedCount(topology);
        var localIds = views.Select(view => view.LocalMember.Id).ToArray();
        return members.Length == expectedCount && members.All(IsHealthyMember) &&
            members.Select(member => member.Id).Distinct(StringComparer.Ordinal).Count() == expectedCount &&
            localIds.Distinct(StringComparer.Ordinal).Count() == expectedCount && RolesMatch(members, views, topology);
    }

    private static bool IsHealthyMember(KurrentGossipMember member)
        => member.Id.Length > KurrentConstants.EmptyTextLength && member.Version == KurrentConstants.ExpectedServerVersion && member.IsAlive &&
            !member.IsReadOnly && member.HttpEndpointIp.Length > KurrentConstants.EmptyTextLength && member.HttpEndpointPort > KurrentConstants.EmptyTextLength &&
            member.InternalHttpEndpointIp.Length > KurrentConstants.EmptyTextLength && member.InternalHttpEndpointPort > KurrentConstants.EmptyTextLength;

    private static bool RolesMatch(KurrentGossipMember[] members, KurrentGossipView[] views, ComparisonTopology topology)
    {
        if (topology != ComparisonTopology.Replicated)
        {
            return members.Length == KurrentConstants.ExpectedSingleNode &&
                members.Count(member => member.State == KurrentConstants.LeaderState) == KurrentConstants.ExpectedLeader;
        }

        var leaders = members.Where(member => member.State == KurrentConstants.LeaderState).ToArray();
        return leaders.Length == KurrentConstants.ExpectedLeader &&
            members.Count(member => member.State == KurrentConstants.FollowerState) == KurrentConstants.ExpectedFollowers &&
            views.All(view => view.Members.Count(member => member.State == KurrentConstants.LeaderState) == KurrentConstants.ExpectedLeader &&
                              view.Members.Single(member => member.State == KurrentConstants.LeaderState).Id == leaders[0].Id &&
                              view.Members.Count(member => member.State == KurrentConstants.FollowerState) == KurrentConstants.ExpectedFollowers);
    }

    private static bool ViewsAgree(KurrentGossipMember[] expected, KurrentGossipView[] views)
        => views.All(view => CanonicalMembers(view.Members).All(IsHealthyMember) &&
            expected.Select(member => member.Id).SequenceEqual(
                CanonicalMembers(view.Members).Select(member => member.Id), StringComparer.Ordinal));
}
