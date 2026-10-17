using KeyLoad.UnitTests.Features.ClusterReplication;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeConservation
{
    internal static async Task VerifyAsync(RequestCqrsCohortRuntimeFixture fixture,
        NativeLocalChildLifetimeSeeded state, CancellationToken token)
    {
        foreach (var voter in K.Voters)
        {
            var node = fixture.Node(voter);
            await NativeLocalChildLifetimeAssertions.RowsAsync(node, token);
            await ReplicaBarrierDeadlineAssertions.UnchangedAsync(node, state.Images[voter]);
        }
        var leader = state.Leader;
        var before = ReplicaBarrierDeadlineAssertions.Image(leader);
        var index = leader.Journal.Log.State.LastIndex;
        var term = leader.Journal.Log.State.Term;
        var replay = (await leader.Journal.Consensus.SubmitAsync(state.Write, token)).Get<CommitReceipt>();
        await NativeLocalChildLifetimeAssertions.EqualAsync(state.Receipt, replay);
        await ReplicaBarrierDeadlineAssertions.ReplayStateAsync(leader, before, state.Write.Id, index, term);
        foreach (var voter in K.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            await NativeLocalChildLifetimeAssertions.RowsAsync(node, token);
        }
    }
}
