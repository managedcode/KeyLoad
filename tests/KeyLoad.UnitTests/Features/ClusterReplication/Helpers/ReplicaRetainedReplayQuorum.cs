using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaRetainedReplayQuorum
{
    private const string UnexpectedSuccess = "A retained local receipt bypassed the actual native quorum.";

    internal static async Task RefuseThenRepairAsync(RequestCqrsCohortRuntimeFixture fixture,
        RequestCqrsCohortNativeNode leader, ReplicaRetainedReplayCommand original, CancellationToken token)
    {
        var stopped = ReplicaBarrierDeadlineFlow.Voters.Where(voter => voter != leader.Journal.Configuration.LocalId).ToArray();
        foreach (var voter in stopped)
        {
            await fixture.Node(voter).Journal.Consensus.StopAsync(token);
        }
        var cut = ReplicaRetainedReplayAssertions.Capture(leader);
        try
        {
            _ = await leader.Journal.Consensus.SubmitAsync(original.Operation, token);
            throw new InvalidOperationException(UnexpectedSuccess);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.OwnershipLost)
        {
        }
        await Assert.That(token.IsCancellationRequested).IsFalse();
        await ReplicaRetainedReplayAssertions.UnchangedAsync(leader, cut, sameTerm: false);
        foreach (var voter in stopped)
        {
            await fixture.RestartVoterAsync(voter, token);
        }
        var current = await ReplicaBarrierDeadlineFlow.LeaderAsync(fixture, token);
        await ReplicaRetainedReplayFlow.SyncAsync(fixture, token);
        var cuts = ReplicaRetainedReplayAssertions.Capture(fixture);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original.Result,
            await current.Journal.Consensus.SubmitAsync(original.Operation, token));
        await ReplicaRetainedReplayAssertions.UnchangedAsync(fixture, cuts, sameTerm: true);
        _ = await ReplicaRetainedReplayProducer.DocumentAsync(current,
            ReplicaRetainedReplayProducer.FollowingDocument, ReplicaRetainedReplayProducer.FollowingJson, token);
        await ReplicaRetainedReplayFlow.SyncAsync(fixture, token);
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            var actual = fixture.Node(voter).Journal.Database.GetDocument(P.Root,
                new(P.Partition, P.Collection, ReplicaRetainedReplayProducer.FollowingDocument), cancellationToken: token);
            var expected = new DocumentResult(new(P.Partition, P.Collection, ReplicaRetainedReplayProducer.FollowingDocument),
                ReplicaRetainedReplayProducer.Single, ReplicaRetainedReplayProducer.FollowingJson, false, []);
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected), NativeSerialization.Serialize(actual));
        }
    }
}
