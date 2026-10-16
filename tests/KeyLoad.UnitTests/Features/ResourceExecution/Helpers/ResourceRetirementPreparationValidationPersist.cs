using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparationValidationPersist
{
    internal static async Task<ResourceRetirementOwnerReservationV1> ReserveAsync(ControlledPartitionMovementNode node,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, CancellationToken token)
    {
        var request = context.Preparation!.Request;
        var body = new ResourceRetirementReserveOwnerBodyV1(context, attempt);
        var command = new ResourceRetirementPhaseCommandV1(Version, request.RetirementId,
            ResourceRetirementPhase.ReserveOwner, NativeSerialization.Serialize(body));
        var original = node.Database.CreateResourceRetirementOperation(attempt.OriginalCommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, node.Database.EvaluationClock.GetUtcNow(), command);
        var persistId = ResourceRetirementIdentity.PreparedOperationCommandId(context, attempt.AttemptId,
            ResourceRetirementPreparedOperationAction.Persist);
        var persisted = await ResourceRetirementPreparationValidationSeed.PhaseAsync(node, request,
            ResourceRetirementPhase.PreparedOperation, new ResourceRetirementPreparedOperationBodyV1(context, attempt,
                ResourceRetirementPreparedOperationAction.Persist, original), persistId, token);
        var prepared = persisted.PreparedOperation ?? throw new InvalidOperationException(Invalid);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(prepared.Original, original);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(prepared.Receipt, persisted.Receipt);
        var actual = node.Database.ReadPreparedResourceRetirementOperation(PhysicalShardCatalogFixture.RootPrincipalId,
            context, attempt, ResourceRetirementPreparationValidationFixture.Work(node, token));
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual, prepared);
        var reserved = await ResourceRetirementPreparationValidationSeed.PhaseAsync(node, request,
            ResourceRetirementPhase.ReserveOwner, body, original.Id, token, original);
        return reserved.OwnerReservation ?? throw new InvalidOperationException(Invalid);
    }
}
