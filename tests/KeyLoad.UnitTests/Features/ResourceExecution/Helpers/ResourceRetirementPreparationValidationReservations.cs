using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Actual local journals admit source attempt5 and target persisted original6 before reservation8.</summary>
internal static class ResourceRetirementPreparationValidationReservations
{
    internal static async Task<ImmutableArray<ResourceRetirementOwnerReservationV1>> CreateAsync(
        IReadOnlyList<ControlledPartitionMovementNode> nodes, ResourceRetirementPreparationControlV1 control,
        DateTimeOffset expiry, CancellationToken token)
    {
        var output = ImmutableArray.CreateBuilder<ResourceRetirementOwnerReservationV1>();
        foreach (var (receiver, offset, attemptId) in new[]
        {
            (Source, SourceIndex, SourceAttemptId), (Target, TargetIndex, TargetAttemptId)
        })
        {
            var context = new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.OwnerReservation,
                null, control, receiver);
            var attempt = await PrepareAsync(nodes, context, attemptId, expiry, token);
            ResourceRetirementOwnerReservationV1? reservation = null;
            for (var index = Zero; index < Voters; index++)
            {
                var actual = await ResourceRetirementPreparationValidationPersist.ReserveAsync(nodes[index + offset],
                    context, attempt, token);
                reservation ??= actual;
                await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual, reservation);
                ResourceRetirementPreparationProof.RequireReservation(actual, control, receiver);
            }
            output.Add(reservation!);
        }
        return output.ToImmutable();
    }

    private static async Task<ResourceRetirementAttemptV1> PrepareAsync(IReadOnlyList<ControlledPartitionMovementNode> nodes,
        ResourceRetirementAttemptContextV1 context, Guid attemptId, DateTimeOffset expiry, CancellationToken token)
    {
        ResourceRetirementAttemptV1? attempt = null;
        var commandId = ResourceRetirementIdentity.AttemptCommandId(context, ResourceRetirementPhase.PrepareAttempt, attemptId);
        for (var index = Zero; index < Voters; index++)
        {
            var result = await ResourceRetirementPreparationValidationSeed.PhaseAsync(nodes[index], context.Preparation!.Request,
                ResourceRetirementPhase.PrepareAttempt, new ResourceRetirementPrepareAttemptBodyV1(context, attemptId, Zero, expiry),
                commandId, token);
            var actual = result.PreparationState?.OwnerAttempts.Single(value => value.AttemptId == attemptId)
                ?? throw new InvalidOperationException(Invalid);
            attempt ??= actual;
            await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual, attempt);
        }
        return attempt!;
    }
}
