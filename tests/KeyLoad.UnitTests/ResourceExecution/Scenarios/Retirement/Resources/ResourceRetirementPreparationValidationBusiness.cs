using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparationValidationBusiness
{
    internal static async Task<ImmutableArray<ResourceRetirementOwnerStatusV1>> AdvanceAsync(
        ResourceRetirementPreparationValidationState state, CancellationToken token)
    {
        var source = state.Nodes[SourceIndex];
        var partition = ControlledPartitionMovementCorpus.Partition;
        var commandId = Guid.NewGuid();
        var index = source.Journal.Log.State.LastIndex + One;
        var request = new CommandRequest(commandId, partition,
            [new PutDocument(ControlledPartitionMovementCorpus.Collection, BusinessId, BusinessJson)]);
        var operation = source.Database.CreateNativeOperation(OperationKind.Batch, commandId,
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(request));
        var outcome = source.Journal.Submit(operation, token);
        await Assert.That(outcome.Error).IsNull();
        var expected = new CommitReceipt(commandId, new(Source.Owner.Incarnation, partition.AtomicPartitionId,
            index, Source.Owner.PlacementEpoch), [new(PutKind, ControlledPartitionMovementCorpus.Collection, BusinessId, One)],
            DurabilityProfile.ProcessDurable);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(outcome.Get<CommitReceipt>(), expected);
        var reference = new EntityRef(partition, ControlledPartitionMovementCorpus.Collection, BusinessId);
        var expectedReference = (await ControlledPartitionMovementExpectedReferences.DocumentAsync(source)) with
        {
            Id = BusinessId
        };
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(source.Database.GetDocument(
            PhysicalShardCatalogFixture.RootPrincipalId, reference, cancellationToken: token),
            new DocumentResult(expectedReference, One, BusinessJson, false, []));
        var actual = ResourceRetirementPreparationValidationSeed.ReadStatuses(state.Nodes, state.Preparation.Control, token);
        await Assert.That(actual[SourceIndex].Cut.StorePosition).IsGreaterThan(state.Statuses[SourceIndex].Cut.StorePosition);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual[SourceIndex].Reservation,
            state.Statuses[SourceIndex].Reservation);
        return actual;
    }
}
