using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Measures only the product's actual earlier state and genuine generated page; no authority is returned.</summary>
internal static class PartitionMovementParentOperationalCapacityRf3Boundary
{
    private const long BoundaryStep = 1;
    private const int MidpointDivisor = 2;
    internal const string ClusterPrefix = "keyload-";

    internal static async Task<int> RequireAsync(PartitionMoveParentState actual,
        PartitionMoveAuthorizeBody observed, string cluster, CancellationToken cancellationToken)
    {
        var header = actual.Header ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var control = actual.Control ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var captured = actual.Selected ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await Assert.That(actual.Pending).IsNull();
        await Assert.That(control.Phase).IsEqualTo(PartitionMovePhase.Captured);
        await Assert.That(observed.Phase.Stage).IsEqualTo(PartitionMovePeerStage.StagePage);
        await Assert.That(observed.Phase.PageOrdinal).IsEqualTo(PartitionMoveProtocol.EmptyCount);
        await SqlRf3Protocol.EqualAsync(header.DestinationOwner, observed.ReceiverOwner);
        var page = NativeSerialization.Deserialize<PartitionMovePageBody>(observed.Phase.Body.Span);
        await SqlRf3Protocol.EqualAsync(control, page.Control);
        await SqlRf3Protocol.EqualAsync(captured.OriginalDescriptor, page.Descriptor);
        await SqlRf3Protocol.EqualAsync(captured.OriginalFence, page.Fence);
        var intended = PartitionMovementParentNativePhases.StagePage(header, control,
            captured.OriginalFence!, captured.OriginalDescriptor!, page.Page);
        await SqlRf3Protocol.EqualAsync(observed.Phase, intended);
        var lower = BoundaryStep;
        var upper = (long)int.MaxValue;
        while (lower < upper)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var middle = lower + (upper - lower) / MidpointDivisor;
            if (Accepts(actual, observed, cluster, middle))
            { upper = middle; }
            else
            { lower = checked(middle + BoundaryStep); }
        }
        await RequireRetainedPageBoundsAsync(captured, page, lower);
        Require(actual, observed, cluster, lower);
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() =>
        {
            Require(actual, observed, cluster, lower - BoundaryStep);
            return Task.CompletedTask;
        });
        await Assert.That(denied!.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        new DatabaseLimits { MaxBatchBytes = checked((int)lower) }.Validate();
        new DatabaseLimits { MaxBatchBytes = checked((int)(lower - BoundaryStep)) }.Validate();
        return checked((int)lower);
    }

    private static async Task RequireRetainedPageBoundsAsync(PartitionMoveParentPhase captured,
        PartitionMovePageBody page, long currentLegalCap)
    {
        var original = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(captured.OriginalPhase!.Body.Span);
        await Assert.That((long)original.MaximumPageBytes).IsGreaterThan(currentLegalCap);
        await Assert.That(NativeSerialization.Measure(page.Page)).IsLessThanOrEqualTo(currentLegalCap - BoundaryStep);
    }

    private static void Require(PartitionMoveParentState state, PartitionMoveAuthorizeBody observed,
        string cluster, long maximum)
        => PartitionMovementParentCaptureLimits.RequirePlannedStageCapacity(state, observed.Phase,
            observed.ReceiverOwner, state.Selected!.OriginalDescriptor!.Resources, cluster, maximum);

    private static bool Accepts(PartitionMoveParentState state, PartitionMoveAuthorizeBody observed,
        string cluster, long maximum)
    {
        try
        { Require(state, observed, cluster, maximum); return true; }
        catch (KeyLoadException error) when (error.Code == ErrorCode.BudgetExceeded) { return false; }
    }
}
