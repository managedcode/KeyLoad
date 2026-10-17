using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparationValidationSeed
{
    internal static async Task<ResourceRetirementPreparationValidationState> CreateAsync(
        IReadOnlyList<ControlledPartitionMovementNode> nodes, CancellationToken token)
    {
        for (var index = Zero; index < Voters; index++)
        {
            var seed = ControlledPartitionMovementSetup.Seed(nodes[index], nodes[index + Voters], out _, token);
            await Assert.That(seed.Error).IsNull();
        }
        var source = nodes[SourceIndex];
        var partition = ControlledPartitionMovementCorpus.Partition;
        var definition = source.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            partition.TenantId, partition.DatabaseId, ControlledPartitionMovementCorpus.Collection)))
            ?? throw new InvalidOperationException(Invalid);
        var lifetime = source.Store.Read(view => ResourceLifetimeCatalog.Read(view, partition.TenantId,
            partition.DatabaseId, definition.Name, definition)) ?? throw new InvalidOperationException(Invalid);
        var request = new ResourceRetirementRequestV1(ResourceRetirementPreparationValidationData.Version, RetirementId, partition.TenantId,
            partition.DatabaseId, definition.Name, lifetime.Incarnation);
        ResourceRetirementPreparationStateV1? preparation = null;
        for (var index = Zero; index < Voters; index++)
        {
            var result = await PhaseAsync(nodes[index], request, ResourceRetirementPhase.PrepareRetirement,
                new ResourceRetirementPrepareBodyV1(request), ResourceRetirementIdentity.PreparationCommandId(request), token);
            var actual = result.PreparationState ?? throw new InvalidOperationException(Invalid);
            preparation ??= actual;
            await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual, preparation);
        }
        var control = preparation!.Control;
        var expiry = source.Database.EvaluationClock.GetUtcNow().AddSeconds(source.Database.Limits.QueryDeadlineSeconds);
        var reservations = await ResourceRetirementPreparationValidationReservations.CreateAsync(nodes, control, expiry, token);
        foreach (var reservation in reservations)
        {
            var id = ResourceRetirementIdentity.AttemptCommandId(request, ResourceRetirementPhase.RecordReservation,
                reservation.Attempt!.AttemptId, reservation.Receiver.Owner.PhysicalShardId);
            for (var index = Zero; index < Voters; index++)
            {
                _ = await PhaseAsync(nodes[index], request, ResourceRetirementPhase.RecordReservation,
                    new ResourceRetirementRecordReservationBodyV1(control, reservation), id, token);
            }
        }
        var final = source.Database.ReadResourceRetirementPreparation(PhysicalShardCatalogFixture.RootPrincipalId,
            request, ResourceRetirementPreparationValidationFixture.Work(source, token));
        ResourceRetirementPreparationStatuses.RequireState(final, request, Source);
        return new(nodes, final, ReadStatuses(nodes, control, token));
    }

    internal static ImmutableArray<ResourceRetirementOwnerStatusV1> ReadStatuses(
        IReadOnlyList<ControlledPartitionMovementNode> nodes, ResourceRetirementPreparationControlV1 control,
        CancellationToken token)
        => nodes.Select(node => node.Database.ReadResourceRetirementOwnerStatus(PhysicalShardCatalogFixture.RootPrincipalId,
            control, ResourceRetirementPreparationValidationFixture.Work(node, token))).ToImmutableArray();

    internal static async Task<ResourceRetirementPhaseResultV1> PhaseAsync<T>(ControlledPartitionMovementNode node,
        ResourceRetirementRequestV1 request, ResourceRetirementPhase phase, T body, Guid commandId, CancellationToken token,
        ReplicatedOperation? originalOperation = null)
    {
        var index = node.Journal.Log.State.LastIndex + One;
        var command = new ResourceRetirementPhaseCommandV1(ResourceRetirementPreparationValidationData.Version, request.RetirementId, phase, NativeSerialization.Serialize(body));
        var original = originalOperation ?? node.Database.CreateResourceRetirementOperation(commandId, PhysicalShardCatalogFixture.RootPrincipalId,
            node.Database.EvaluationClock.GetUtcNow(), command);
        var outcome = node.Journal.Submit(original, token);
        await Assert.That(outcome.Error).IsNull();
        var result = outcome.Get<ResourceRetirementPhaseResultV1>();
        await Assert.That(result.Receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(result.Receipt.AppliedPosition).IsEqualTo(index);
        await Assert.That(result.Receipt.Phase).IsEqualTo(phase);
        return result;
    }
}
