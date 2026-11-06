using P = KeyLoad.FunctionalTests.ClientApi.CredentialStatusProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class CredentialStatusDelta
{
    internal static async Task RequireAsync(CredentialStatusState state, CredentialStatusCut before,
        Guid id, PrincipalRecord principal, ApiKeyRecord credential, StoredOutcome expectedOutcome, bool stored = true)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadStoredOutcome(id);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        var rows = before.RowHashes.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        Put(rows, OutcomeStoreOracle.GlobalKey(principal.Id, id), OrleansSerialization.Serialize(expectedOutcome));
        Put(rows, StorageCommandKeys.Applied(null), OrleansSerialization.Serialize(new StoragePartitionPosition(null, entry.CanonicalSequence)));
        Put(rows, StorageCommandKeys.Clock(null), OrleansSerialization.Serialize(
            new DateTimeOffset(operation.Timestamp) >= before.Clock ? new DateTimeOffset(operation.Timestamp) : before.Clock));
        if (stored)
        {
            Put(rows, KeySpace.ApiKey(credential.Id), OrleansSerialization.Serialize(credential));
        }
        var retained = await RequireBatchAsync(state, before, rows, expectedOutcome);
        Put(rows, StorageCommandKeys.ReplicaBatch(null), retained);
        var actual = CredentialStatusAssertions.Capture(state);
        await CredentialStatusAssertions.RequireRowsAsync(rows, actual.RowHashes);
        await Assert.That(actual.Cut.AppliedPosition).IsEqualTo(checked(before.Cut.AppliedPosition + P.First));
        await Assert.That(actual.Cut.StorePosition).IsGreaterThan(before.Cut.StorePosition);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(actual.Cut.AppliedPosition);
    }

    private static async Task<byte[]> RequireBatchAsync(CredentialStatusState state, CredentialStatusCut before,
        Dictionary<string, string> expectedRows, StoredOutcome outcome)
    {
        var store = state.Scenario.Fixture.Database.Store;
        var raw = store.Read(view => view.ReadOwnedValue(StorageCommandKeys.ReplicaBatch(null)))
            ?? throw new InvalidOperationException(P.Failure);
        var batch = OrleansSerialization.Deserialize<StorageReplicaBatch>(raw);
        await Assert.That(batch.BatchId).IsNotEqualTo(Guid.Empty);
        await Assert.That(batch.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(batch.Partition).IsNull();
        await Assert.That(batch.PreviousPosition).IsEqualTo(before.Cut.AppliedPosition);
        await Assert.That(batch.Position).IsEqualTo(outcome.CanonicalSequence);
        await Assert.That(batch.Applied.Single()).IsEqualTo(new StorageCopyCommandAcknowledgement(outcome.CanonicalSequence,
            outcome.Result.IsSuccess, Errors.Code(outcome.Result.Problem).Value));
        foreach (var change in batch.Changes)
        {
            await Assert.That(change.Value).IsNotNull();
            var key = CredentialStatusAssertions.Hash(change.Key.Span);
            await Assert.That(expectedRows.TryGetValue(key, out var expected)
                && expected == CredentialStatusAssertions.Hash(change.Value!.Value.Span)).IsTrue();
        }
        var dependencies = before.Rows.Where(row => row.Key.AsSpan().StartsWith(StorageCommandKeys.AppliedPrefix))
            .Select(row => OrleansSerialization.Deserialize<StoragePartitionPosition>(row.Value))
            .Where(cut => cut.Partition is not null && cut.Position > 0).ToArray();
        await Assert.That(batch.Dependencies).IsEquivalentTo(dependencies, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        return raw;
    }

    private static void Put(Dictionary<string, string> rows, byte[] key, byte[] value)
        => rows[CredentialStatusAssertions.Hash(key)] = CredentialStatusAssertions.Hash(value);
}
