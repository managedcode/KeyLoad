using System.Text;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum NativePublicShape
{
    NullMutation,
    NullPatch,
    NullGrant
}

internal static class NativePublicNormalizationFixture
{
    internal const string TargetPrincipal = "native-null-grants";
    internal const string NonAdministrator = "native-non-administrator";
    private const string Collection = "orders";
    private const string Entity = "entry";
    private const string Wildcard = "*";
    private const string MissingMutation = "A mutation entry is missing.";
    private const string InvalidNestedMutation = "A nested mutation entry or enum value is invalid.";
    private const string InvalidGrant = "The principal scope exceeds its budget or contains an invalid grant.";

    internal static ReplicatedOperation Create(TestDatabase database, NativePublicShape shape,
        string principal = NativeAuthorityFixture.Root, DateTimeOffset? time = null,
        long ownershipEpoch = 1, string targetPrincipal = TargetPrincipal)
    {
        var id = Guid.NewGuid();
        var payload = shape switch
        {
            NativePublicShape.NullMutation => JsonDefaults.Serialize(new CommandRequest(id, database.Partition, [null!], ownershipEpoch)),
            NativePublicShape.NullPatch => JsonDefaults.Serialize(new CommandRequest(id, database.Partition,
                [new PatchDocument(Collection, Entity, [null!], 1)], ownershipEpoch)),
            NativePublicShape.NullGrant => JsonDefaults.Serialize(new ConfigurePrincipalRequest(
                new PrincipalRecord(targetPrincipal, database.Partition.TenantId, [null!], []))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var kind = shape == NativePublicShape.NullGrant ? OperationKind.ConfigurePrincipal : OperationKind.Batch;
        // Retain exact authored whitespace as part of the existing command identity.
        var json = " \n" + Encoding.UTF8.GetString(payload) + "\t ";
        return new(id, kind, principal, time ?? database.Database.EvaluationClock.GetUtcNow(), json);
    }

    internal static string Detail(NativePublicShape shape) => shape switch
    {
        NativePublicShape.NullMutation => MissingMutation,
        NativePublicShape.NullPatch => InvalidNestedMutation,
        NativePublicShape.NullGrant => InvalidGrant,
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    internal static KeyLoadException EncodingFailure(ReplicatedOperation operation)
        => operation.Kind == OperationKind.Batch
            ? Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(JsonDefaults.Deserialize<CommandRequest>(operation.PayloadJson)))
            : Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(JsonDefaults.Deserialize<ConfigurePrincipalRequest>(operation.PayloadJson)));

    internal static StoredOutcome Stored(TestDatabase database, ReplicatedOperation operation)
        => database.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.Outcome(operation.PrincipalId, operation.Id)))!;

    internal static string Fingerprint(ReplicatedOperation operation)
        => JsonData.Fingerprint(new { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson });

    internal static PrincipalRecord OrdinaryPrincipal(TestDatabase database)
        => new(NonAdministrator, database.Partition.TenantId, [new(Wildcard, Wildcard, Capability.All)], []);
}
