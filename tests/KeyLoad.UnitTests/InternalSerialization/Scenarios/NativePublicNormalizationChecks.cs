using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativePublicNormalizationChecks
{
    internal static bool HasNullElement(Command<DatabaseCommand> operation, NativePublicShape shape) => shape switch
    {
        NativePublicShape.NullMutation => NativeSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span, NativeValidationProfile.PublicInputElements).Mutations[0] is null,
        NativePublicShape.NullPatch => ((PatchDocument)NativeSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span, NativeValidationProfile.PublicInputElements).Mutations[0]).Patches[0] is null,
        NativePublicShape.NullGrant => NativeSerialization.Deserialize<ConfigurePrincipalRequest>((operation).Value!.Input.Span, NativeValidationProfile.PublicInputElements).Principal.Grants[0] is null,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static async Task DurableFailureAsync(TestDatabase database, Command<DatabaseCommand> operation,
        ErrorCode code, string detail)
    {
        var normalized = database.Database.OwnCommand(operation);
        await Assert.That(normalized.Value!.Input.IsEmpty).IsFalse();
        await Assert.That(normalized.Value.Input.Span.SequenceEqual(operation.Value!.Input.Span)).IsTrue();
        await Assert.That(normalized.Value.Fingerprint).IsEqualTo(NativePublicNormalizationFixture.Fingerprint(operation));
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(code);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);
        var stored = NativePublicNormalizationFixture.Stored(database, operation);
        await Assert.That(stored.Result).IsEqualTo(result);
        await Assert.That(stored.Fingerprint).IsEqualTo(NativePublicNormalizationFixture.Fingerprint(operation));
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
    }
}
