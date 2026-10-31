using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Storage;
using ManagedCode.Communication;
using P = KeyLoad.FunctionalTests.ClientApi.CredentialStatusProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal static class CredentialStatusOutcome
{
    internal static async Task<StoredOutcome> RequireAsync(CredentialStatusState state, Guid id, PrincipalRecord principal,
        ApiKeyRecord credential, long expectedIndex, Problem? refusal = null)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadStoredOutcome(id);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(expectedIndex);
        await Assert.That(database.ReadCanonicalCut().AppliedPosition).IsEqualTo(expectedIndex);
        await Assert.That(operation.CommandId).IsEqualTo(id);
        await Assert.That(operation.Value!.Kind).IsEqualTo(OperationKind.ConfigureApiKey);
        await Assert.That(operation.UserId!).IsEqualTo(principal.Id);
        var execution = ((ConfigureApiKeyRequest)(operation).Value!.Input);
        await Assert.That(execution).IsEqualTo(new ConfigureApiKeyRequest(credential));
        var result = refusal is null ? Result<object>.Succeed(true) : Result<object>.Fail(refusal);
        var expectedOperation = database.Database.OwnCommand(operation);
        var expected = new StoredOutcome(database.Store.Identity.Incarnation, principal.PolicyEpoch, result, expectedOperation)
        {
            ScopeKind = CommandOutcomeScopeKind.Global,
            CanonicalSequence = expectedIndex
        };
        var actual = database.Store.Read(view => view.GetRecord<StoredOutcome>(OutcomeStoreOracle.GlobalKey(principal.Id, id)))
            ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(OrleansSerialization.Serialize(actual).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
        return expected;
    }
}
