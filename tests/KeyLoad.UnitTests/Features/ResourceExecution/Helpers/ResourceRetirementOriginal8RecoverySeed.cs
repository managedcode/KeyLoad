using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementOriginal8RecoverySeed
{
    internal static async Task<(ResourceRetirementAttemptContextV1 Context, ResourceRetirementAttemptV1 Attempt)> CreateAsync(
        IReadOnlyList<ControlledPartitionMovementNode> nodes, CancellationToken token)
    {
        for (var index = Zero; index < Voters; index++)
        {
            await Assert.That(ControlledPartitionMovementSetup.Seed(nodes[index], nodes[index + Voters], out _, token).Error).IsNull();
        }
        var source = nodes[SourceIndex];
        var partition = ControlledPartitionMovementCorpus.Partition;
        var definition = source.Store.Read(view => view.GetRecord<ResourceDefinition>(KeyLoad.Core.KeySpace.Resource(
            partition.TenantId, partition.DatabaseId, ControlledPartitionMovementCorpus.Collection)))
            ?? throw new InvalidOperationException(Invalid);
        var lifetime = source.Store.Read(view => ResourceLifetimeCatalog.Read(view, partition.TenantId,
            partition.DatabaseId, definition.Name, definition)) ?? throw new InvalidOperationException(Invalid);
        var request = new ResourceRetirementRequestV1(Version, Guid.NewGuid(), partition.TenantId,
            partition.DatabaseId, definition.Name, lifetime.Incarnation);
        ResourceRetirementPreparationStateV1? preparation = null;
        for (var index = Zero; index < Voters; index++)
        {
            var result = await ResourceRetirementPreparationValidationSeed.PhaseAsync(nodes[index], request,
                ResourceRetirementPhase.PrepareRetirement, new ResourceRetirementPrepareBodyV1(request),
                ResourceRetirementIdentity.PreparationCommandId(request), token);
            preparation ??= result.PreparationState ?? throw new InvalidOperationException(Invalid);
            await ResourceRetirementPreparationValidationAssertions.EqualAsync(result.PreparationState, preparation);
        }
        var context = new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.OwnerReservation,
            null, preparation!.Control, Target);
        var attemptId = Guid.NewGuid();
        var expiry = source.Database.EvaluationClock.GetUtcNow().AddSeconds(source.Database.Limits.QueryDeadlineSeconds);
        var commandId = ResourceRetirementIdentity.AttemptCommandId(context, ResourceRetirementPhase.PrepareAttempt, attemptId);
        ResourceRetirementAttemptV1? attempt = null;
        for (var index = Zero; index < Voters; index++)
        {
            var result = await ResourceRetirementPreparationValidationSeed.PhaseAsync(nodes[index], request,
                ResourceRetirementPhase.PrepareAttempt, new ResourceRetirementPrepareAttemptBodyV1(context, attemptId, Zero, expiry),
                commandId, token);
            var actual = result.PreparationState?.OwnerAttempts.Single(value => value.AttemptId == attemptId)
                ?? throw new InvalidOperationException(Invalid);
            attempt ??= actual;
            await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual, attempt);
        }
        return (context, attempt!);
    }
}
