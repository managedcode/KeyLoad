using KeyLoad.Clustering;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Server.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparingShapeData
{
    internal static T RoundTrip<T>(T value) => NativeSerialization.Deserialize<T>(NativeSerialization.Serialize(value));

    internal static ResourceRetirementPreparingShapeSeed Seed(ResourceRetirementPreparationValidationState state)
    {
        var source = state.Nodes[ResourceRetirementPreparationValidationData.SourceIndex];
        var original = At(source, state.Preparation.Control.PrepareReceipt.AppliedPosition);
        return new(state, original.Phase, original.Result);
    }

    internal static (ResourceRetirementPhaseCommandV1 Phase, ResourceRetirementPhaseResultV1 Result) At(
        ControlledPartitionMovementNode owner, long position)
    {
        var entry = owner.Journal.Log.Read(position, 1, owner.Journal.Configuration.MaxAppendBytes).Single();
        var original = entry.Operation ?? throw new InvalidDataException(ResourceRetirementNativeProtocol.InvalidProof);
        var verified = owner.Database.VerifyOperationAuthority(original);
        return (NativeCommandPayload.Read<ResourceRetirementPhaseCommandV1>(verified),
            RoundTrip(owner.Database.ResolveOutcome(original).Get<ResourceRetirementPhaseResultV1>()));
    }

    internal static ResourceRetirementNativeRequest Outer(ReplicaSecurityFixture native, ResourceRetirementPreparingShapeSeed seed)
    {
        var original = ResourceRetirementNativeEnvelopeData.Request(native, native.Database.EvaluationClock.GetUtcNow());
        return RoundTrip(original with
        {
            Control = seed.Reservation.Receiver,
            Destination = seed.Reservation.Receiver,
            Command = false,
            CommandId = Guid.Empty
        });
    }

    internal static ResourceRetirementReadRequestV1 Read(ResourceRetirementNativeRequest outer,
        ResourceRetirementPreparingShapeSeed seed, ResourceRetirementReadOperation operation)
        => RoundTrip(new ResourceRetirementReadRequestV1(1, operation, seed.Request, null, Guid.Empty, 0,
            outer.ExpiresAt, outer.Destination, outer.DestinationVoter, outer.DestinationSiloAddress, 0,
            outer.MaximumReadBytes, outer.MaximumExaminedRecords, outer.MaximumResultBytes)
        {
            Preparation = operation == ResourceRetirementReadOperation.OwnerStatus ? seed.Control : null,
            Context = operation is ResourceRetirementReadOperation.Attempt or ResourceRetirementReadOperation.PreparedOperation
                or ResourceRetirementReadOperation.IssueOwnerReservation ? seed.Context : null,
            Attempt = operation is ResourceRetirementReadOperation.PreparedOperation or ResourceRetirementReadOperation.IssueOwnerReservation
                ? seed.Attempt : null,
            AttemptId = operation == ResourceRetirementReadOperation.Attempt ? seed.Attempt.AttemptId : Guid.Empty,
            ExpiresAt = operation == ResourceRetirementReadOperation.IssueOwnerReservation ? seed.Attempt.ExpiresAt : outer.ExpiresAt
        });

    internal static async Task RefusalAsync(Action action, ErrorCode code)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(action);
        await Assert.That(error.Code).IsEqualTo(code);
    }
}
