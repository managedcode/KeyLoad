using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

/// <summary>Settles the actual recovered source journal at A and publishes the genuine next control phase.</summary>
internal static class ControlledPartitionMovementProcessHealthy
{
    private const long AcknowledgeIndex = 14;
    private const long AcceptIndex = 15;
    private const long AcceptedPosition = 16;
    private const long FenceIndex = 13;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNativeNode source,
        ControlledPartitionMovementProcessOwners owners, ControlledPartitionMovementProcessPrepared prepared,
        CancellationToken cancellationToken)
    {
        var outcome = await ControlledPartitionMovementProcessFiles.ReadAsync<OperationResult>(source.Root,
            ControlledPartitionMovementProcessProtocol.VerifiedFile, cancellationToken);
        await ControlledPartitionMovementProcessPhaseAssertions.CompleteAsync(outcome, owners);
        var actualFence = outcome.Get<PartitionMovePhaseResult>();
        var runtime = prepared.Runtime;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var admission = new PartitionMovementPeerAdmission(runtime.Node, source.Database,
                runtime.ReplicaConfiguration, runtime.GrainRouting, runtime.Membership, runtime.PartitionMovement,
                source.Database.EvaluationClock);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var acknowledge = ControlledPartitionMovementProcessFenceSettlementRequest.Acknowledge(prepared.Prepared,
                    actualFence, owners, prepared.OriginalCallerAddress, prepared.OriginalExpiry);
                var ack = await SubmitAsync(source, runtime, admission, acknowledge, cancellationToken);
                var grant = prepared.Authorization.Grant
                    ?? throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
                var expectedAck = new PartitionMovePhaseResult(actualFence.MoveId,
                    PartitionMovePeerStage.ControlAcknowledge, new(acknowledge.CommandId, owners.Control.Owner,
                        AcknowledgeIndex, actualFence.Journal.ControlIntentDigest), null, null, null, null,
                    grant with { Settlement = actualFence.Journal });
                await Assert.That(JsonDefaults.Serialize(ack).SequenceEqual(JsonDefaults.Serialize(expectedAck))).IsTrue();
                var accept = ControlledPartitionMovementProcessFenceSettlementRequest.Accept(prepared.Prepared,
                    actualFence, owners, prepared.OriginalCallerAddress, prepared.OriginalExpiry);
                var accepted = await SubmitAsync(source, runtime, admission, accept, cancellationToken);
                var expectedFence = ControlledPartitionMovementProcessPhaseAssertions.Fence(owners);
                var control = expectedFence.Control
                    ?? throw new InvalidOperationException(ControlledPartitionMovementProcessProtocol.Invalid);
                var expected = new PartitionMovePhaseResult(control.MoveId, PartitionMovePeerStage.ControlAcceptFence,
                    new(accept.CommandId, owners.Control.Owner, AcceptIndex, expectedFence.Journal.ControlIntentDigest),
                    control with { Phase = PartitionMovePhase.Fenced, SourceCut = FenceIndex }, expectedFence.Fence, null, null);
                await Assert.That(JsonDefaults.Serialize(accepted).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
                await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(AcceptIndex);
                await Assert.That(source.Store.Position).IsEqualTo(AcceptedPosition);
                await ControlledPartitionMovementProcessModelAssertions.ReadAsync(source, prepared.RecordedAt,
                    AcceptedPosition, cancellationToken);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<PartitionMovePhaseResult> SubmitAsync(ControlledPartitionMovementNativeNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest request, CancellationToken cancellationToken)
    {
        var operation = await ControlledPartitionMovementProcessIssuer.IssueAsync(source, runtime, admission,
            request, cancellationToken);
        var result = source.Journal.Submit(operation, cancellationToken);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        return result.Get<PartitionMovePhaseResult>();
    }
}
