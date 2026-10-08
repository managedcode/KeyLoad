using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Prepares an authentic locally signed Fence for actual journal/apply crash recovery.</summary>
internal static class ControlledPartitionMovementProcessPreparation
{
    private const int Version = 1;
    private const long InitialEpoch = 1;
    private const long EmptyRevision = 0;
    private const int PeerKeyBytes = 32;
    private const string Missing = "The original movement process producer did not complete.";

    internal static async Task<ControlledPartitionMovementProcessPrepared> PrepareAsync(
        ControlledPartitionMovementNativeNode source, ControlledPartitionMovementNativeNode target,
        ControlledPartitionMovementLoopbackListeners listeners, ControlledPartitionMovementProcessOwners owners,
        CommitStage cut, CancellationToken cancellationToken)
    {
        var seed = ControlledPartitionMovementProcessSetup.Seed(source, target, owners, cancellationToken);
        ControlledPartitionMovementProcessSetup.Require(seed.Outcome);
        var blob = ControlledPartitionMovementProcessBlobSeed.Publish(source, cancellationToken);
        var runtime = ControlledPartitionMovementProcessOptions.Bind(source, owners,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes)), control: true);
        ControlledPartitionMovementProcessPrepared? captured = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership, runtime.PartitionMovement,
                source.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var placement = new AtomicPartitionPlacementResolution(Version,
                    ControlledPartitionMovementProcessCorpus.Partition, owners.Control.Owner.PhysicalShardId,
                    owners.Control.Owner.Incarnation, owners.Control.Owner.VoterIds, InitialEpoch,
                    EmptyRevision, EmptyRevision, true);
                var caller = ControlledPartitionMovementProcessPrepareRequest.CallerAddress(listeners);
                var prepare = ControlledPartitionMovementProcessPrepareRequest.Create(source, runtime, owners,
                    placement, caller);
                var prepared = await SubmitAsync(source, runtime, admission, prepare, cancellationToken);
                var authorize = ControlledPartitionMovementProcessFenceRequest.Authorize(prepared, owners,
                    caller, prepare.Envelope.ExpiresAt);
                var authorized = await SubmitAsync(source, runtime, admission, authorize, cancellationToken);
                var fence = ControlledPartitionMovementProcessFenceRequest.Fence(prepared, authorized, owners,
                    caller, prepare.Envelope.ExpiresAt);
                var original = await ControlledPartitionMovementProcessIssuer.IssueAsync(source, runtime,
                    admission, fence, cancellationToken);
                captured = new(new(owners.Control.Owner, original, source.Store.Position,
                    source.Journal.Log.State.LastIndex, cut,
                    ControlledPartitionMovementNativeJournal.SupportingHistoryEntries), seed.EvaluatedAt,
                    ControlledPartitionMovementProcessCorpus.SeedOperation(source.Database, seed.EvaluatedAt),
                    NativeSerialization.Serialize(seed.Outcome.Get<CommitReceipt>()), blob.Result, blob.Request, blob.EvaluatedAt,
                    prepared, authorized, runtime, caller, prepare.Envelope.ExpiresAt,
                    ControlledPartitionMovementProcessRawImage.Bytes(target.Store));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return captured ?? throw new InvalidOperationException(Missing);
    }

    private static async Task<PartitionMovePhaseResult> SubmitAsync(ControlledPartitionMovementNativeNode node,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        var operation = await ControlledPartitionMovementProcessIssuer.IssueAsync(node, runtime, admission,
            original, cancellationToken);
        var result = node.Journal.Submit(operation, cancellationToken);
        ControlledPartitionMovementProcessSetup.Require(result);
        return result.Get<PartitionMovePhaseResult>();
    }
}
