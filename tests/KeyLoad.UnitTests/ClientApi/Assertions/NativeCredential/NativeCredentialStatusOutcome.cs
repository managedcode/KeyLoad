using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.Features.ClientApi.NativeCredentialStatusProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class NativeCredentialStatusOutcome
{
    internal static async Task<StoredOutcome> RequireAsync(NativeCredentialStatusState state, Guid id, PrincipalRecord principal,
        ApiKeyRecord credential, long expectedIndex, bool denied)
    {
        var database = state.Scenario.Fixture.Database;
        var entry = database.ReadStoredOutcome(id);
        var operation = entry.PreparedCommand ?? throw new InvalidOperationException(P.Failure);
        var payload = JsonSerializer.Serialize(new ConfigureApiKeyRequest(credential), JsonDefaults.Options);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(expectedIndex);
        await Assert.That(database.ReadCanonicalCut().AppliedPosition).IsEqualTo(expectedIndex);
        await Assert.That(operation.CommandId).IsEqualTo(id);
        await Assert.That(operation.Value!.Kind).IsEqualTo(OperationKind.ConfigureApiKey);
        await Assert.That(operation.UserId!).IsEqualTo(principal.Id);
        var native = NativeSerialization.Deserialize<ConfigureApiKeyRequest>((operation).Value!.Input.Span);
        await Assert.That(native).IsEqualTo(new ConfigureApiKeyRequest(credential));
        var fingerprint = JsonData.Fingerprint(new
        {
            Id = id,
            Kind = OperationKind.ConfigureApiKey,
            PrincipalId = principal.Id,
            PayloadJson = payload
        });
        var result = denied ? Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, P.DeniedDetail))
            : Result<object>.Succeed(true);
        var expected = new StoredOutcome(fingerprint, database.Store.Identity.Incarnation, principal.PolicyEpoch, result, operation)
        {
            ScopeKind = CommandOutcomeScopeKind.Global,
            CanonicalSequence = expectedIndex
        };
        var actual = database.Store.Read(view => view.GetRecord<StoredOutcome>(OutcomeStoreOracle.GlobalKey(principal.Id, id)))
            ?? throw new InvalidOperationException(P.Failure);
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        return expected;
    }
}
