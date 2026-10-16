using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Obtains the original source fence and its actual control acknowledgement through native signed phases.</summary>
internal static class ControlledPartitionMovementNativeCaptureFence
{
    internal static async Task<PartitionMovePhaseResult> ExecuteAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovePhaseResult prepared, ControlledPartitionMovementLoopbackCorpus corpus,
        DateTimeOffset expiry, CancellationToken token)
    {
        var caller = owners.Source.CallerSiloAddress;
        var authorization = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovePhaseResult>(owners,
            ControlledPartitionMovementFenceRequest.Authorize(prepared, corpus, caller, expiry), token);
        var fence = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovePhaseResult>(owners,
            ControlledPartitionMovementFenceRequest.Fence(prepared, authorization, corpus, caller, expiry), token);
        var acknowledgement = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovePhaseResult>(owners,
            ControlledPartitionMovementFenceSettlementRequest.Acknowledge(prepared, fence, corpus, caller, expiry), token);
        await Assert.That(acknowledgement.Journal.CommandId)
            .IsEqualTo(ControlledPartitionMovementFenceSettlementRequest.AcknowledgeId);
        var accepted = await ControlledPartitionMovementNativeOuterExchange.ExecuteAsync<PartitionMovePhaseResult>(owners,
            ControlledPartitionMovementFenceSettlementRequest.Accept(prepared, fence, corpus, caller, expiry), token);
        await Assert.That(accepted.Fence).IsNotNull();
        await Assert.That(JsonDefaults.Serialize(accepted.Fence).AsSpan().SequenceEqual(JsonDefaults.Serialize(fence.Fence))).IsTrue();
        return accepted;
    }
}
