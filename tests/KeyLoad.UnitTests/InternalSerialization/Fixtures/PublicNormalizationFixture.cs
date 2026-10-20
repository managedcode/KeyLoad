using System.Text;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class PublicNormalizationFixture
{
    internal const string TargetPrincipal = "execution-null-grants";
    internal const string NonAdministrator = "execution-non-administrator";
    private const string Collection = "orders";
    private const string Entity = "entry";
    private const string Wildcard = "*";
    private const string MissingMutation = "A mutation entry is missing.";
    private const string InvalidNestedMutation = "A nested mutation entry or enum value is invalid.";
    private const string InvalidGrant = "The principal scope exceeds its budget or contains an invalid grant.";

    internal static Command<DatabaseCommand> Create(TestDatabase database, PublicShape shape,
        string principal = AuthorityFixture.Root, DateTimeOffset? time = null,
        string targetPrincipal = TargetPrincipal)
    {
        var id = Guid.NewGuid();
        var payload = shape switch
        {
            PublicShape.NullMutation => JsonDefaults.Serialize(new CommandRequest(id, database.Partition, [null!])),
            PublicShape.NullPatch => JsonDefaults.Serialize(new CommandRequest(id, database.Partition,
                [new PatchDocument(Collection, Entity, [null!], 1)])),
            PublicShape.NullGrant => JsonDefaults.Serialize(new ConfigurePrincipalRequest(
                new PrincipalRecord(targetPrincipal, database.Partition.TenantId, [null!], []))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var kind = shape == PublicShape.NullGrant ? OperationKind.ConfigurePrincipal : OperationKind.Batch;
        // Retain exact authored whitespace as part of the existing command identity.
        var json = " \n" + Encoding.UTF8.GetString(payload) + "\t ";
        return database.Database.CreateCommand(kind, id, principal, time ?? database.Database.EvaluationClock.GetUtcNow(), json);
    }

    internal static string Detail(PublicShape shape) => shape switch
    {
        PublicShape.NullMutation => MissingMutation,
        PublicShape.NullPatch => InvalidNestedMutation,
        PublicShape.NullGrant => InvalidGrant,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static KeyLoadException EncodingFailure(Command<DatabaseCommand> operation)
        => operation.Value!.Kind == OperationKind.Batch
            ? Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(
                OrleansSerialization.Deserialize<CommandRequest>(operation.Value.Input.Span, ValidationProfile.PublicInputElements)))
            : Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(
                OrleansSerialization.Deserialize<ConfigurePrincipalRequest>(operation.Value.Input.Span, ValidationProfile.PublicInputElements)));

    internal static StoredOutcome Stored(TestDatabase database, Command<DatabaseCommand> operation)
        => OutcomeStoreOracle.ReadStored(database.Store, operation)!;

    internal static string PublicJson(Command<DatabaseCommand> operation)
    {
        object input = operation.Value!.Kind == OperationKind.Batch
            ? OrleansSerialization.Deserialize<CommandRequest>(operation.Value.Input.Span, ValidationProfile.PublicInputElements)
            : OrleansSerialization.Deserialize<ConfigurePrincipalRequest>(operation.Value.Input.Span, ValidationProfile.PublicInputElements);
        return " \n" + Encoding.UTF8.GetString(JsonDefaults.Serialize(input)) + "\t ";
    }

    internal static string Fingerprint(Command<DatabaseCommand> operation)
        => JsonData.Fingerprint(new
        {
            Id = operation.CommandId,
            Kind = operation.Value!.Kind,
            PrincipalId = operation.UserId!,
            PayloadJson = PublicJson(operation)
        });

    internal static PrincipalRecord OrdinaryPrincipal(TestDatabase database)
        => new(NonAdministrator, database.Partition.TenantId, [new(Wildcard, Wildcard, Capability.All)], []);
}
