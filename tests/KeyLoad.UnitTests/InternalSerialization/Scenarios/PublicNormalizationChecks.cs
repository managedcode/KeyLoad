using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.InternalSerialization;

internal static class PublicNormalizationChecks
{
    internal static bool HasNullElement(Command<DatabaseCommand> operation, PublicShape shape) => shape switch
    {
        PublicShape.NullMutation => OrleansSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span, ValidationProfile.PublicInputElements).Mutations[0] is null,
        PublicShape.NullPatch => ((PatchDocument)OrleansSerialization.Deserialize<CommandRequest>((operation).Value!.Input.Span, ValidationProfile.PublicInputElements).Mutations[0]).Patches[0] is null,
        PublicShape.NullGrant => OrleansSerialization.Deserialize<ConfigurePrincipalRequest>((operation).Value!.Input.Span, ValidationProfile.PublicInputElements).Principal.Grants[0] is null,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static async Task DurableFailureAsync(TestDatabase database, Command<DatabaseCommand> operation,
        ErrorCode code, string detail)
    {
        var normalized = database.Database.OwnCommand(operation);
        await Assert.That(normalized.Value!.Input.IsEmpty).IsFalse();
        await Assert.That(normalized.Value.Input.Span.SequenceEqual(operation.Value!.Input.Span)).IsTrue();
        await Assert.That(normalized.Value.Fingerprint).IsEqualTo(PublicNormalizationFixture.Fingerprint(operation));
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(code);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);
        var stored = PublicNormalizationFixture.Stored(database, operation);
        var originalOutcome = PublicNormalizationFixture.StoredBytes(database, operation);
        await Assert.That(stored.Result).IsEqualTo(result);
        await Assert.That(stored.Fingerprint).IsEqualTo(PublicNormalizationFixture.Fingerprint(operation));
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(PublicNormalizationFixture.StoredBytes(database, operation).SequenceEqual(originalOutcome)).IsTrue();
    }
}
