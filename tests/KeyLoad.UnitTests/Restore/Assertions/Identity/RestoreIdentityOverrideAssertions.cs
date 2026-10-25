using System.Text.Json;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.ResourceExecution;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class RestoreIdentityOverrideAssertions
{
    internal static async Task SourceAsync(RestoreIdentityOverrideFixture fixture, RestoreIdentityOverrideState state)
    {
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(state.SourceCut);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Source.Store))
            .IsEquivalentTo(state.SourceImage, CollectionOrdering.Matching);
        await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(fixture.Source.Store,
            fixture.Partition, RestoreIdentityOverrideState.SeedId, state.SeedOutcome);
        await BackupIndexedDocumentDedupAssertions.AssertArchiveUnchanged(fixture.BackupDirectory, state.Archive);
        await ReplayResultAssertions.Same<CommitReceipt>(fixture.Source.Database.Apply(state.Seed), state.SeedResult);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Source.Store))
            .IsEquivalentTo(state.SourceImage, CollectionOrdering.Matching);
    }

    internal static async Task<Result<object>> FenceAsync(RestoreIdentityOverrideFixture fixture,
        RestoreIdentityOverrideState state, DatabaseEngine database, ZoneTreeStore store)
    {
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var refused = database.Apply(state.Seed);
        await Assert.That(Errors.Code(refused.Problem).Value).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(refused.Value).IsNull();

        await Assert.That(refused.Problem?.Detail).IsNotNull();
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store))
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(store,
            fixture.Partition, RestoreIdentityOverrideState.SeedId, state.SeedOutcome);
        return refused;
    }

    internal static async Task ContentAsync(RestoreIdentityOverrideFixture fixture, DatabaseEngine database, bool healthy)
    {
        await DocumentAsync(database, fixture.Source, RestoreIdentityOverrideState.FirstId,
            RestoreIdentityOverrideState.FirstJson);
        if (healthy)
        {
            await DocumentAsync(database, fixture.Source, RestoreIdentityOverrideState.HealthyId,
                RestoreIdentityOverrideState.HealthyJson);
        }
        else
        {
            await BackupIndexedDocumentDedupAssertions.AssertMissing(database, fixture.Partition, RestoreIdentityOverrideState.HealthyId);
        }
    }

    private static async Task DocumentAsync(DatabaseEngine database, TestDatabase original, string id, string json)
    {
        var reference = original.ExpectedReference(new(original.Partition, RestoreIdentityOverrideState.Collection, id));
        var expected = new DocumentResult(reference, 1L, json, false, []);
        var actual = database.GetDocument(RestoreIdentityOverrideState.Principal, expected.Reference);
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
    }

    internal static async Task ReceiptAsync(Result<object> result, Guid id, string documentId)
    {
        await Assert.That(Errors.Code(result.Problem).Value).IsNull();
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        var expected = new MutationReceipt(RestoreIdentityOverrideState.PutDocumentKind, RestoreIdentityOverrideState.Collection, documentId, 1L);
        await Assert.That(JsonSerializer.Serialize(receipt.Mutations, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(new[] { expected }, JsonDefaults.Options));
        await Assert.That(receipt.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
    }

    internal static async Task ReplayAsync(RestoreIdentityOverrideFixture fixture, DatabaseEngine database,
        ZoneTreeStore store, RestoreIdentityOverrideCurrent saved)
    {
        await ReplayResultAssertions.Same<CommitReceipt>(database.Apply(saved.Operation), saved.Result);
        await Assert.That(store.Position).IsEqualTo(saved.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store))
            .IsEquivalentTo(saved.Image, CollectionOrdering.Matching);
        await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(store, fixture.Partition,
            RestoreIdentityOverrideState.HealthyCommandId, saved.Outcome);
    }
}
