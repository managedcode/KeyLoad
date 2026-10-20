using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.OrleansExecution;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.Features.ClientApi.CredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class CredentialStatusAssertions
{
    internal static async Task CurrentAsync(CredentialStatusState state, bool original, bool healthy)
    {
        var database = state.Scenario.Fixture.Database;
        await EqualAsync(state.Administrator, database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(state.Administrator.Id))));
        await EqualAsync(state.Ordinary, database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(state.Ordinary.Id))));
        await KeyAsync(state, state.Caller, exists: true);
        await KeyAsync(state, state.Original, original);
        await KeyAsync(state, state.Denied, exists: false);
        await KeyAsync(state, state.Healthy, healthy);
        await EqualAsync(new OutboxStatus(new(P.Empty, P.First, P.Empty, P.Empty), []),
            database.Database.GetOutboxStatus(ConnectionProtocol.Root, database.Partition));
        await Assert.That(state.Scenario.Stored(P.AbsentDocument)).IsNull();
        if (original)
        {
            await Assert.That(database.Database.Authenticate(P.OriginalKey, state.Scenario.Fixture.Clock.GetUtcNow()) == state.Ordinary.Id).IsTrue();
        }
        if (healthy)
        {
            await Assert.That(database.Database.Authenticate(P.HealthyKey, state.Scenario.Fixture.Clock.GetUtcNow()) == state.Ordinary.Id).IsTrue();
        }
    }

    private static async Task KeyAsync(CredentialStatusState state, ApiKeyRecord expected, bool exists)
    {
        var actual = state.Scenario.Fixture.Database.Store.Read(view => view.GetRecord<ApiKeyRecord>(KeySpace.ApiKey(expected.Id)));
        await EqualAsync(exists ? expected : null, actual);
    }

    internal static CredentialStatusCut Capture(CredentialStatusState state)
    {
        var database = state.Scenario.Fixture.Database;
        var rows = database.Store.Read(view =>
        {
            var page = view.Scan([], database.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(P.Failure);
            }
            return page.Records.Select(row => new KeyValuePair<byte[], byte[]>(row.Key.ToArray(), row.Value.ToArray())).ToArray();
        });
        var hashes = rows.ToDictionary(row => Hash(row.Key), row => Hash(row.Value), StringComparer.Ordinal);
        var clock = database.Store.Read(view => OrleansSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.ClockBytes) ?? throw new InvalidOperationException(P.Failure)));
        return new(hashes, rows, database.Store.Position, database.ReadCanonicalCut(), clock,
            OrleansSerialization.Serialize(database.Store.Identity));
    }

    internal static async Task UnchangedAsync(CredentialStatusState state, CredentialStatusCut original,
        bool healthy)
    {
        await CurrentAsync(state, original: true, healthy);
        var actual = Capture(state);
        await RequireRowsAsync(original.RowHashes, actual.RowHashes);
        await Assert.That(actual.Rows.Length == original.Rows.Length && actual.Rows.Zip(original.Rows).All(pair =>
            pair.First.Key.AsSpan().SequenceEqual(pair.Second.Key)
            && pair.First.Value.AsSpan().SequenceEqual(pair.Second.Value))).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(original.Position);
        await Assert.That(actual.Cut).IsEqualTo(original.Cut);
        await Assert.That(actual.Clock).IsEqualTo(original.Clock);
        await Assert.That(actual.Identity.AsSpan().SequenceEqual(original.Identity)).IsTrue();
    }

    internal static async Task AbsentDeniedOutcomeAsync(CredentialStatusState state)
    {
        var database = state.Scenario.Fixture.Database;
        var principal = state.Ordinary.Id;
        var id = state.DeniedId;
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.GlobalKey(principal, id)))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.UnknownKey(principal, id)))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            OutcomeStoreOracle.PartitionKey(database.Partition, principal, id)))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.OutcomeLocator(database.Partition, principal, id)))).IsNull();
    }

    internal static async Task RequireRowsAsync(IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
        => await Assert.That(expected.Count == actual.Count && expected.All(row =>
            actual.TryGetValue(row.Key, out var value) && string.Equals(row.Value, value, StringComparison.Ordinal))).IsTrue();

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static Task ProblemAsync(Problem? actual, ErrorCode code, string detail)
        => EqualAsync(Errors.Problem(code, detail), actual);

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(expected).SequenceEqual(JsonDefaults.Serialize(actual))).IsTrue();
}
