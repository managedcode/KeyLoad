using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementDropParentNativeData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Local native engine and real WAL only; this is not a RF3 or positive D3/D4 retirement receipt.</summary>
internal static class ResourceRetirementDropParentNativeFlow
{
    internal static async Task RunAsync(TestDatabase fixture)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = ResourceRetirementDropParentNativeSeed.Create(fixture);
        try
        {
            var image = ResourceRetirementDropParentNativeImages.Read(fixture.Store);
            var position = fixture.Store.Position;
            var result = state.Database.Apply(state.Original);
            await ResourceRetirementDropParentNativeImages.ResultAsync(state.Result, result);
            await ResourceRetirementDropParentNativeImages.CreatedAsync(fixture, state, image, position);
            state = state with
            {
                Result = result,
                OutcomeBytes = fixture.Store.Read(view =>
                view.ReadOwnedValue(KeySpace.GlobalOutcome(Root, state.Original.Id)))!
            };
            await RetainedAsync(fixture, state.Database, state, token);
            await ResourceRetirementDropParentNativeRefusals.EarlyCompletionAsync(fixture, state, token);
            await ResourceRetirementDropParentNativeRefusals.MissingOutcomeAsync(fixture, state, token);
            await HealthyAsync(fixture, state);
            await ColdAsync(fixture, state, token);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(() => original.Data[StateKey] = state, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static async Task RetainedAsync(TestDatabase fixture, DatabaseEngine database,
        ResourceRetirementDropParentNativeState state, CancellationToken token)
    {
        var image = ResourceRetirementDropParentNativeImages.Read(database.Store as KeyLoad.Storage.ZoneTree.ZoneTreeStore
            ?? throw new InvalidOperationException(ResourceRetirementProtocol.Malformed));
        var position = database.Store.Position;
        var saved = database.ReadResourceRetirementDropParent(Root, state.Lookup, fixture.CreateReadWork(token));
        await ResourceIncarnationAssertions.EqualAsync(state.Expected, saved);
        await Assert.That(saved!.OriginalIntent.NativePayload.ToArray()).IsEquivalentTo(state.Original.NativePayload.ToArray(),
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(saved.OriginalIntent.EvaluatedAt.EqualsExact(state.Original.EvaluatedAt)).IsTrue();
        await ResourceRetirementDropParentNativeImages.ResultAsync(state.Result, database.Apply(state.Original));
        await ResourceRetirementDropParentNativeImages.SameAsync((KeyLoad.Storage.ZoneTree.ZoneTreeStore)database.Store, image, position);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(Root, state.Original.Id))))
            .IsEquivalentTo(state.OutcomeBytes, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task HealthyAsync(TestDatabase fixture, ResourceRetirementDropParentNativeState state)
    {
        var id = Guid.NewGuid();
        var operation = state.Database.CreateNativeOperation(OperationKind.Batch, id, Root,
            state.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new CommandRequest(id,
                fixture.Partition, [new PutDocument(Table, Row, HealthyJson, First)])));
        var position = fixture.Store.Position;
        var receipt = state.Database.Apply(operation).Get<CommitReceipt>();
        await ResourceIncarnationAssertions.EqualAsync(new CommitReceipt(id,
            new(fixture.Store.Identity.Incarnation, fixture.Partition.AtomicPartitionId, position + First,
                state.Expected.PersistReceipt.PhysicalOwner.PlacementEpoch),
            [new MutationReceipt(PutKind, Table, Row, HealthyRevision)], DurabilityProfile.ProcessDurable), receipt);
        var row = state.Database.GetDocument(Root, new(fixture.Partition, Table, Row))!;
        await Assert.That(row.Revision).IsEqualTo((long)HealthyRevision);
        await Assert.That(row.Json).IsEqualTo(HealthyJson);
        await Assert.That(row.Redacted).IsFalse();
        await Assert.That(row.RedactedFields.IsEmpty).IsTrue();
    }

    private static async Task ColdAsync(TestDatabase fixture, ResourceRetirementDropParentNativeState state,
        CancellationToken token)
    {
        var image = ResourceRetirementDropParentNativeImages.Read(fixture.Store);
        var position = fixture.Store.Position;
        var identity = fixture.Store.Identity;
        fixture.JoinOwnedResources();
        for (var wave = Zero; wave < ColdWaves; wave++)
        {
            token.ThrowIfCancellationRequested();
            await ResourceIncarnationCold.OpenAsync(fixture, identity, async store =>
            {
                await ResourceRetirementDropParentNativeImages.SameAsync(store, image, position);
                var database = fixture.CreateConfiguredDatabase(store);
                await RetainedAsync(fixture, database, state, token);
                var row = database.GetDocument(Root, new(fixture.Partition, Table, Row))!;
                await Assert.That(row.Json).IsEqualTo(HealthyJson);
                await Assert.That(row.Revision).IsEqualTo((long)HealthyRevision);
                await ResourceRetirementDropParentNativeImages.SameAsync(store, image, position);
            });
        }
    }
}
