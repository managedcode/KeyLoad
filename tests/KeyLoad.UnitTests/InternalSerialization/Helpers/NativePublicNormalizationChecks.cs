using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativePublicNormalizationChecks
{
    internal static bool HasNullElement(ReplicatedOperation operation, NativePublicShape shape) => shape switch
    {
        NativePublicShape.NullMutation => NativeCommandPayload.Read<CommandRequest>(operation).Mutations[0] is null,
        NativePublicShape.NullPatch => ((PatchDocument)NativeCommandPayload.Read<CommandRequest>(operation).Mutations[0]).Patches[0] is null,
        NativePublicShape.NullGrant => NativeCommandPayload.Read<ConfigurePrincipalRequest>(operation).Principal.Grants[0] is null,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static async Task DurableFailureAsync(TestDatabase database, ReplicatedOperation operation,
        ErrorCode code, string detail)
    {
        var normalized = database.Database.NormalizeOperation(operation);
        var payload = NativeAuthorityFixture.Read(normalized);
        await Assert.That(payload.Error).IsNull();
        await Assert.That(payload.Value.IsEmpty).IsFalse();
        await Assert.That(normalized.PayloadJson).IsEqualTo(operation.PayloadJson);
        var result = database.Database.Apply(normalized);
        await Assert.That(result.Error).IsEqualTo(code);
        await Assert.That(result.SafeDetail).IsEqualTo(detail);
        var stored = NativePublicNormalizationFixture.Stored(database, operation);
        await Assert.That(stored.Result).IsEqualTo(result);
        await Assert.That(stored.Fingerprint).IsEqualTo(NativePublicNormalizationFixture.Fingerprint(operation));
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
    }
}
