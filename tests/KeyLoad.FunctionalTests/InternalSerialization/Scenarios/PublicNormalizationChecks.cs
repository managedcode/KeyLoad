using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.InternalSerialization;

internal static class PublicNormalizationChecks
{
    internal static bool HasNullElement(Command<DatabaseCommand> operation, PublicShape shape) => shape switch
    {
        PublicShape.NullMutation => ((CommandRequest)(operation).Value!.Input).Mutations[0] is null,
        PublicShape.NullPatch => ((PatchDocument)((CommandRequest)(operation).Value!.Input).Mutations[0]).Patches[0] is null,
        PublicShape.NullGrant => ((ConfigurePrincipalRequest)(operation).Value!.Input).Principal.Grants[0] is null,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static async Task DurableFailureAsync(TestDatabase database, Command<DatabaseCommand> operation,
        ErrorCode code, string detail)
    {
        var normalized = database.Database.OwnCommand(operation);
        await Assert.That((normalized.Value!.Input is null)).IsFalse();
        await Assert.That(OrleansSerialization.Serialize(normalized.Value.Input, ValidationProfile.PublicInputElements).AsSpan().SequenceEqual(OrleansSerialization.Serialize(operation.Value!.Input, ValidationProfile.PublicInputElements).AsSpan())).IsTrue();
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem).Value).IsEqualTo(code);
        await Assert.That(result.Problem?.Detail).IsEqualTo(detail);
        var stored = PublicNormalizationFixture.Stored(database, operation);
        var originalOutcome = PublicNormalizationFixture.StoredBytes(database, operation);
        await Assert.That(stored.Result).IsEqualTo(result);
        await Assert.That(stored.PreparedCommand.CommandId).IsEqualTo(operation.CommandId);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(PublicNormalizationFixture.StoredBytes(database, operation).SequenceEqual(originalOutcome)).IsTrue();
    }
}
