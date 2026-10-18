using KeyLoad.Core;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeCredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeCredentialStatusDelta
{
    internal static async Task RequireAsync(NativeCredentialStatusState state, NativeCredentialStatusCut before,
        Guid id, PrincipalRecord principal, ApiKeyRecord credential, StoredOutcome expectedOutcome)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadStoredOutcome(id);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        var rows = before.RowHashes.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        Put(rows, OutcomeStoreOracle.GlobalKey(principal.Id, id), NativeSerialization.Serialize(expectedOutcome));
        Put(rows, KeySpace.AppliedBytes, NativeSerialization.Serialize(entry.CanonicalSequence));
        Put(rows, KeySpace.ClockBytes, NativeSerialization.Serialize(
            new DateTimeOffset(operation.Timestamp) >= before.Clock ? new DateTimeOffset(operation.Timestamp) : before.Clock));
        Put(rows, KeySpace.ApiKey(credential.Id), NativeSerialization.Serialize(credential));
        var actual = NativeCredentialStatusAssertions.Capture(state);
        await NativeCredentialStatusAssertions.RequireRowsAsync(rows, actual.RowHashes);
        await Assert.That(actual.Cut.AppliedPosition).IsEqualTo(checked(before.Cut.AppliedPosition + P.First));
        await Assert.That(actual.Cut.StorePosition).IsEqualTo(checked(before.Cut.StorePosition + P.First));
        await Assert.That(entry.CanonicalSequence).IsEqualTo(actual.Cut.AppliedPosition);
    }

    private static void Put(Dictionary<string, string> rows, byte[] key, byte[] value)
        => rows[NativeCredentialStatusAssertions.Hash(key)] = NativeCredentialStatusAssertions.Hash(value);
}
