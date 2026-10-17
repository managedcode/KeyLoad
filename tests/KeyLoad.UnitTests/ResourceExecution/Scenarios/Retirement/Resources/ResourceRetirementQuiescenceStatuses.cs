using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceStatuses
{
    internal static async Task<ResourceRetirementOwnerStatusV1[]> ReadAsync(ResourceRetirementQuiescenceState state,
        bool settled, CancellationToken token)
    {
        var statuses = new List<ResourceRetirementOwnerStatusV1>();
        foreach (var owner in new[] { state.Source, state.Target })
        {
            var status = owner.Database.ReadResourceRetirementOwnerStatus(Root, state.Control,
                ResourceRetirementQuiescencePhases.Work(owner, token));
            await ResourceRetirementQuiescenceAssertions.EqualAsync(state.Control, status.Reservation.Control);
            await Assert.That(status.Cut.NodeId).IsEqualTo(owner.Store.Identity.NodeId);
            await Assert.That(status.Cut.StoreIncarnation).IsEqualTo(owner.Store.Identity.Incarnation);
            await Assert.That(status.Cut.ReadGeneration).IsEqualTo(owner.Store.Identity.ReadGeneration);
            await Assert.That(status.Cut.StorePosition).IsEqualTo(owner.Store.Position);
            await Assert.That(status.Cut.AppliedIndex).IsEqualTo(owner.Database.LastApplied);
            var expected = !settled && ReferenceEquals(owner, state.Source)
                ? new[] { new ResourceRetirementAcceptedMoveV1(Partition, ControlledPartitionMovementPrepareRequest.MoveId) }
                : Array.Empty<ResourceRetirementAcceptedMoveV1>();
            await Assert.That(status.Remaining.SequenceEqual(expected)).IsTrue();
            statuses.Add(status);
        }
        return statuses.ToArray();
    }

    internal static async Task IncompleteBeginAsync(ResourceRetirementQuiescenceState state, CancellationToken token)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var statuses = await ReadAsync(state, settled: true, token);
        var body = new ResourceRetirementBeginBodyV1(state.Request)
        {
            Preparation = state.Control,
            OwnerStatuses = [.. statuses]
        };
        var phase = new ResourceRetirementPhaseCommandV1(ResourceRetirementQuiescenceData.Version, state.Request.RetirementId,
            ResourceRetirementPhase.Begin, NativeSerialization.Serialize(body));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => state.Source.Database.CreateResourceRetirementOperation(
            state.Request.RetirementId, Root, state.Source.Database.EvaluationClock.GetUtcNow(), phase));
        state.ExpectedFailures.Add(error);
        await Assert.That(error.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
        var preparing = state.Source.Database.ReadResourceRetirementPreparation(Root, state.Request,
            ResourceRetirementQuiescencePhases.Work(state.Source, token));
        await ResourceRetirementQuiescenceAssertions.EqualAsync(state.Control, preparing.Control);
        await Assert.That(preparing.BegunControl).IsNull();
    }
}
