using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementFinalOutcomeCapacityRf3Boundary
{
    internal const int OneByte = 1;
    private const int MidpointDivisor = 2;

    internal static async Task<int> RequireAsync(PartitionMovementFinalInstallFramePrecut precut,
        string cluster, CancellationToken cancellationToken)
    {
        var state = precut.State;
        await Assert.That(state.Pending).IsNull();
        var descriptor = state.Selected!.OriginalDescriptor!;
        var phase = PartitionMovementParentNativePhases.Install(state.Header!, state.Control!,
            state.Selected.OriginalFence!, descriptor, precut.FinalOrdinal);
        await Assert.That(PartitionMovementParentCapacityFinalInstall.IsFinal(state, phase)).IsTrue();
        var lower = (long)OneByte;
        var upper = (long)int.MaxValue;
        while (lower < upper)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var middle = lower + (upper - lower) / MidpointDivisor;
            if (Accepts(state, phase, cluster, middle))
            { upper = middle; }
            else
            { lower = checked(middle + OneByte); }
        }
        Require(state, phase, cluster, lower);
        var error = await Assert.ThrowsAsync<KeyLoadException>(() =>
        {
            Require(state, phase, cluster, lower - OneByte);
            return Task.CompletedTask;
        });
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(error.Message).IsEqualTo(PartitionMovementFinalOutcomeCapacityProtocol.Exceeded);
        new DatabaseLimits { MaxBatchBytes = checked((int)lower) }.Validate();
        new DatabaseLimits { MaxBatchBytes = checked((int)(lower - OneByte)) }.Validate();
        await Assert.That(lower).IsLessThanOrEqualTo(new DatabaseLimits().MaxBatchBytes);
        return checked((int)lower);
    }

    private static void Require(PartitionMoveParentState state, PartitionMovePhaseCommand phase,
        string cluster, long maximum)
        => PartitionMovementParentCaptureLimits.RequirePlannedStageCapacity(state, phase,
            state.Header!.DestinationOwner, state.Selected!.OriginalDescriptor!.Resources, cluster, maximum);

    private static bool Accepts(PartitionMoveParentState state, PartitionMovePhaseCommand phase,
        string cluster, long maximum)
    {
        try
        { Require(state, phase, cluster, maximum); return true; }
        catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded
            && error.Message == PartitionMovementFinalOutcomeCapacityProtocol.Exceeded)
        { return false; }
    }
}
