using KeyLoad.Core;
using KeyLoad.Storage;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaRetainedReplayAuthority
{
    private const string ChangedJson = "{\"value\":\"changed-fingerprint\"}";
    private const string RepairAdministrator = "retained-replay-repair";
    private const int NextEpoch = 1;

    internal static async Task ChangedAsync(RequestCqrsCohortRuntimeFixture fixture,
        RequestCqrsCohortNativeNode leader, ReplicaRetainedReplayCommand original, CancellationToken token)
    {
        var changed = leader.Journal.Database.CreateNativeOperation(OperationKind.Batch, original.Operation.Id,
            P.Root, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(new CommandRequest(original.Operation.Id,
                P.Partition, [new PutDocument(P.Collection, P.DocumentId, ChangedJson)])));
        var cuts = ReplicaRetainedReplayAssertions.Capture(fixture);
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => leader.Journal.Consensus.SubmitAsync(changed, token));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Conflict);
        await ReplicaRetainedReplayAssertions.UnchangedAsync(fixture, cuts, sameTerm: true);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(original.Result,
            await leader.Journal.Consensus.SubmitAsync(original.Operation, token));
        await ReplicaRetainedReplayAssertions.UnchangedAsync(fixture, cuts, sameTerm: true);
    }

    internal static async Task RevokeAndRepairAsync(RequestCqrsCohortRuntimeFixture fixture,
        RequestCqrsCohortNativeNode leader, ReplicaRetainedReplayCommand original, CancellationToken token)
    {
        var root = leader.Journal.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(P.Root)))!;
        await ReplicaRetainedReplayProducer.SubmitAsync(leader, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(root with
            {
                Id = RepairAdministrator
            }), token);
        var revoked = root with
        {
            Revoked = true,
            PolicyEpoch = checked(root.PolicyEpoch + NextEpoch)
        };
        await ReplicaRetainedReplayProducer.SubmitAsync(leader, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(revoked), token, RepairAdministrator);
        await ReplicaRetainedReplayFlow.SyncAsync(fixture, token);
        var cuts = ReplicaRetainedReplayAssertions.Capture(fixture);
        var denied = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => leader.Journal.Consensus.SubmitAsync(original.Operation, token));
        await Assert.That(denied!.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await ReplicaRetainedReplayAssertions.UnchangedAsync(fixture, cuts, sameTerm: true);
        await ReplicaRetainedReplayProducer.SubmitAsync(leader, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(root with
            {
                PolicyEpoch = checked(revoked.PolicyEpoch + NextEpoch)
            }), token, RepairAdministrator);
        await ReplicaRetainedReplayFlow.SyncAsync(fixture, token);
        cuts = ReplicaRetainedReplayAssertions.Capture(fixture);
        var stale = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => leader.Journal.Consensus.SubmitAsync(original.Operation, token));
        await Assert.That(stale!.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await ReplicaRetainedReplayAssertions.UnchangedAsync(fixture, cuts, sameTerm: true);
    }
}
