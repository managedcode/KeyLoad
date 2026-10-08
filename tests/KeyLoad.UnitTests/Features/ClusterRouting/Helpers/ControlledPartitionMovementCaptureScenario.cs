using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Produces capture inputs through actual prepared, authorized, settled and accepted native phases.</summary>
internal static class ControlledPartitionMovementCaptureScenario
{
    internal static async Task<(PartitionMovementCaptureHandle Handle, PartitionMovementPageResult[] Pages,
        PartitionMovePhaseResult Settlement)> ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackCorpus corpus,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission, string originalCallerAddress,
        byte[] originalReceipt, long initialPosition, CancellationToken cancellationToken)
    {
        var observed = await ControlledPartitionMovementPrepareFlow.ExecuteAsync(source, target, corpus,
            runtime, admission, originalCallerAddress, cancellationToken);
        var phases = await ControlledPartitionMovementGrantFlow.ExecuteAsync(source, runtime, admission, corpus,
            observed.Prepared, originalCallerAddress, observed.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementFenceStateAssertions.RetainedAsync(source, originalReceipt,
            initialPosition, cancellationToken);
        var accepted = await ControlledPartitionMovementFenceSettlementFlow.ExecuteAsync(source, runtime,
            admission, corpus, observed.Prepared, phases.Authorization, phases.Fence, originalCallerAddress,
            observed.ExpiresAt, cancellationToken);
        var capture = await ControlledPartitionMovementCaptureFlow.ExecuteAsync(source, runtime, admission, corpus,
            accepted, originalCallerAddress, observed.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementCaptureReceiptAssertions.AssertAsync(corpus, accepted,
            capture.Handle, capture.Settlement);
        await ControlledPartitionMovementCaptureAcknowledgement.ExecuteAsync(source, runtime, admission, corpus,
            accepted, capture.Settlement, originalCallerAddress, observed.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementCapturedAdvance.ExecuteAsync(source, runtime, admission, corpus,
            accepted, capture.Handle.Descriptor, originalCallerAddress, observed.ExpiresAt, cancellationToken);
        return capture;
    }
}
