using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Joins actual admitted prepare, persisted authorization and source-fence operations under one caller identity.</summary>
internal static class ControlledPartitionMovementGrantScenario
{
    private const long SettledFenceNativeIndex = 15;

    internal static async Task<(string[] Source, string[] Target, long Position, long Index,
        PartitionMovePhaseResult Prepared, DateTimeOffset ExpiresAt)> ExecuteAsync(
        ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ControlledPartitionMovementLoopbackCorpus corpus, ServerRuntimeOptions runtime,
        PartitionMovementPeerAdmission admission, string originalCallerAddress, byte[] originalReceipt,
        long initialPosition, CancellationToken cancellationToken)
    {
        var observed = await ControlledPartitionMovementPrepareFlow.ExecuteAsync(source, target, corpus,
            runtime, admission, originalCallerAddress, cancellationToken);
        var phases = await ControlledPartitionMovementGrantFlow.ExecuteAsync(source, runtime, admission, corpus,
            observed.Prepared, originalCallerAddress, observed.ExpiresAt, cancellationToken);
        await ControlledPartitionMovementFenceStateAssertions.RetainedAsync(source, originalReceipt,
            initialPosition, cancellationToken);
        await ControlledPartitionMovementFenceSettlementFlow.ExecuteAsync(source, runtime, admission, corpus,
            observed.Prepared, phases.Authorization, phases.Fence, originalCallerAddress, observed.ExpiresAt,
            cancellationToken);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(SettledFenceNativeIndex);
        await Assert.That(source.Store.Position).IsEqualTo(checked(initialPosition + SettledFenceNativeIndex));
        observed.Source = ControlledPartitionMovementRawImage.Bytes(source.Store);
        observed.Position = checked(initialPosition + SettledFenceNativeIndex);
        observed.Index = SettledFenceNativeIndex;
        return observed;
    }
}
