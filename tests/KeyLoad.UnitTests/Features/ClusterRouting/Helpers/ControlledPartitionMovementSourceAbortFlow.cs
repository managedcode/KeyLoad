using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Logs terminal source Abort only after actual source producer/page/image closure and the native marker.</summary>
internal static class ControlledPartitionMovementSourceAbortFlow
{
    private const int Version = 1;
    private const int InitialBatch = 0;
    private const int CompletedBatch = 1;

    internal static async Task<(PartitionMovePhaseResult Terminal, Guid GrantId,
        ReadOnlyMemory<byte> OriginalBody)> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementCaptureRuntime actualOwner, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult aborting, Guid actualTargetTerminalGrantId, string originalCallerAddress,
        DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        await ControlledPartitionMovementSourceAbortClosure.ExecuteAsync(source, actualOwner);
        var terminalOrdinal = PartitionMoveCleanupFamilies.All.Length;
        var stage = PartitionMovePeerStage.Abort;
        var role = PartitionMoveCleanupRole.Source;
        var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            ControlledPartitionMovementAbortRequest.Authorize(aborting, stage, role, terminalOrdinal,
                InitialBatch, actualTargetTerminalGrantId, corpus, originalCallerAddress, originalExpiry),
            cancellationToken);
        await Assert.That(authorization.Error).IsNull();
        var request = ControlledPartitionMovementAbortRequest.Apply(aborting, stage, role, terminalOrdinal,
            InitialBatch, actualTargetTerminalGrantId, authorization.Get<PartitionMovePhaseResult>(), corpus,
            originalCallerAddress, originalExpiry);
        var outcome = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            request, cancellationToken);
        await Assert.That(outcome.Error).IsNull();
        var actual = outcome.Get<PartitionMovePhaseResult>();
        var expected = new PartitionMoveCleanupState(Version,
            ControlledPartitionMovementPrepareRequest.MoveId, ControlledPartitionMovementCorpus.Partition,
            aborting.Journal.ControlIntentDigest, stage, role, terminalOrdinal, actual.Journal, CompletedBatch);
        await Assert.That(JsonDefaults.Serialize(actual.Cleanup)
            .SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        actualOwner.Source.ConfirmMoveClosed(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementPrepareRequest.MoveId);
        await ControlledPartitionMovementAbortAcknowledgement.ExecuteAsync(source, runtime, admission,
            corpus, aborting, actual, role, InitialBatch, originalCallerAddress, originalExpiry,
            cancellationToken);
        return (actual, ControlledPartitionMovementAbortPhaseIds.Grant(stage, role, InitialBatch),
            request.Envelope.Body);
    }
}
