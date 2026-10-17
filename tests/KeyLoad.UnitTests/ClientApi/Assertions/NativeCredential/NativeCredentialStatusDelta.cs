using KeyLoad.Core;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeCredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeCredentialStatusDelta
{
    internal static async Task RequireAsync(NativeCredentialStatusState state, NativeCredentialStatusCut before,
        Guid id, PrincipalRecord principal, ApiKeyRecord credential, StoredOutcome expectedOutcome, bool denied)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadRetainedEntry(id);
        var operation = entry.Operation ?? throw new InvalidOperationException(P.Failure);
        var rows = before.RowHashes.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        Put(rows, OutcomeStoreOracle.GlobalKey(principal.Id, id), NativeSerialization.Serialize(expectedOutcome));
        Put(rows, KeySpace.AppliedBytes, NativeSerialization.Serialize(entry.Index));
        Put(rows, KeySpace.ClockBytes, NativeSerialization.Serialize(
            operation.EvaluatedAt >= before.Clock ? operation.EvaluatedAt : before.Clock));
        if (!denied)
        {
            Put(rows, KeySpace.ApiKey(credential.Id), NativeSerialization.Serialize(credential));
        }
        var actual = NativeCredentialStatusAssertions.Capture(state);
        await NativeCredentialStatusAssertions.RequireRowsAsync(rows, actual.RowHashes);
        await Assert.That(actual.Journal.LastIndex).IsEqualTo(checked(before.Journal.LastIndex + P.First));
        await Assert.That(actual.Journal.CommittedIndex).IsEqualTo(actual.Journal.LastIndex);
        await Assert.That(actual.Journal.Term).IsEqualTo(before.Journal.Term);
    }

    private static void Put(Dictionary<string, string> rows, byte[] key, byte[] value)
        => rows[NativeCredentialStatusAssertions.Hash(key)] = NativeCredentialStatusAssertions.Hash(value);
}
