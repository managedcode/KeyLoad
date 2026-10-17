using KeyLoad.Server.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparationValidationAssertions
{
    internal static async Task RejectAsync(ResourceRetirementPreparationValidationState state)
    {
        var statuses = state.Statuses;
        var first = statuses[SourceIndex];
        var duplicate = statuses[One] with
        {
            Cut = statuses[One].Cut with
            {
                NodeId = first.Cut.NodeId
            }
        };
        var remaining = first with
        {
            Remaining = [new(ControlledPartitionMovementCorpus.Partition, RemainingMoveId)]
        };
        var badControl = first.Reservation with
        {
            Control = first.Reservation.Control with
            {
                IntentDigest = OtherDigest
            }
        };
        var badReceipt = first.Reservation with
        {
            Receipt = first.Reservation.Receipt with
            {
                CommandId = OtherCommandId
            }
        };
        await RefusedAsync(() => ResourceRetirementPreparationStatuses.RequireReady(statuses.RemoveAt(statuses.Length - One),
            state.Preparation), ErrorCode.RecoveryRequired);
        await RefusedAsync(() => ResourceRetirementPreparationStatuses.RequireReady(statuses.SetItem(One, duplicate),
            state.Preparation), ErrorCode.RecoveryRequired);
        await RefusedAsync(() => ResourceRetirementPreparationStatuses.RequireReady(statuses.SetItem(SourceIndex, remaining),
            state.Preparation), ErrorCode.RecoveryRequired);
        await RefusedAsync(() => ResourceRetirementPreparationStatuses.RequireReady(statuses.SetItem(SourceIndex,
            first with
            {
                Reservation = badControl
            }), state.Preparation), ErrorCode.RecoveryRequired);
        await RefusedAsync(() => ResourceRetirementPreparationProof.RequireReservation(badControl, state.Preparation.Control,
            Source), ErrorCode.Corruption);
        await RefusedAsync(() => ResourceRetirementPreparationProof.RequireReservation(badReceipt, state.Preparation.Control,
            Source), ErrorCode.Corruption);
        var wrongRequest = state.Preparation.Control.Request with
        {
            ResourceName = OtherResource
        };
        await RefusedAsync(() => ResourceRetirementPreparationProof.RequireControl(state.Preparation.Control,
            wrongRequest, Source), ErrorCode.TokenInvalidated);
    }

    private static async Task RefusedAsync(Action candidate, ErrorCode expected)
    {
        var refused = Assert.ThrowsExactly<KeyLoadException>(candidate);
        await Assert.That(refused.Code).IsEqualTo(expected);
        await Assert.That(refused.Message).IsEqualTo(ResourceRetirementNativeProtocol.Unavailable);
    }

    // Every native field of this DTO is public JSON, including complete nested values and ordered arrays.
    // Compare values without treating Orleans object-reference aliases as control identity.
    internal static async Task EqualAsync(ResourceRetirementControlV1 actual, ResourceRetirementControlV1 expected)
        => await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
}
