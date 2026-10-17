using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeCredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeCredentialStatusOutcome
{
    internal static async Task<StoredOutcome> RequireAsync(NativeCredentialStatusState state, Guid id, PrincipalRecord principal,
        ApiKeyRecord credential, long expectedIndex, bool denied)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadRetainedEntry(id);
        var operation = entry.Operation ?? throw new InvalidOperationException(P.Failure);
        var payload = JsonSerializer.Serialize(new ConfigureApiKeyRequest(credential), JsonDefaults.Options);
        await Assert.That(entry.Index).IsEqualTo(expectedIndex);
        await Assert.That(database.ReadJournalCut().CommittedIndex).IsEqualTo(expectedIndex);
        await Assert.That(operation.Id).IsEqualTo(id);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.ConfigureApiKey);
        await Assert.That(operation.PrincipalId).IsEqualTo(principal.Id);
        var native = global::KeyLoad.Core.Features.InternalSerialization.NativeCommandPayload.Read<ConfigureApiKeyRequest>(operation);
        await Assert.That(native).IsEqualTo(new ConfigureApiKeyRequest(credential));
        var fingerprint = JsonData.Fingerprint(new
        {
            Id = id,
            Kind = OperationKind.ConfigureApiKey,
            PrincipalId = principal.Id,
            PayloadJson = payload
        });
        var result = denied ? new OperationResult(null, ErrorCode.PermissionDenied, P.DeniedDetail)
            : new OperationResult(null)
            {
                NativeValue = true
            };
        var expected = new StoredOutcome(fingerprint, database.Store.Identity.Incarnation, principal.PolicyEpoch, result)
        {
            ScopeKind = CommandOutcomeScopeKind.Global
        };
        var actual = database.Store.Read(view => view.GetRecord<StoredOutcome>(OutcomeStoreOracle.GlobalKey(principal.Id, id)))
            ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        return expected;
    }
}
