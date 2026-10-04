using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeAuthorityFixture
{
    internal const string Root = "root";
    internal const string OtherPrincipal = "other";
    internal const string ChangedDetail = "Changed safe detail.";
    internal const string Collection = "native-authority";
    internal const string Entity = "entry";
    internal const string Json = "{\"value\":\"界λ😀\",\"number\":1.00}";
    internal const string InvalidJson = "{";
    internal const string WrongPurpose = "wrong-purpose";

    internal static ReplicatedOperation Create(TestDatabase database)
        => database.Database.CreateNativeOperation(OperationKind.SetDispatch, Guid.NewGuid(), Root,
            database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(true));

    internal static NativeCommandPayload Read(ReplicatedOperation operation)
        => NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);

    internal static ReplicatedOperation Wrap(ReplicatedOperation operation, NativeCommandPayload payload)
        => operation with { NativePayload = NativeSerialization.Serialize(payload) };

    internal static ReplicatedOperation Resign(TestDatabase database, ReplicatedOperation operation, NativeCommandAuthority claims)
    {
        var authority = NativeSerialization.Serialize(claims);
        return Wrap(operation, Read(operation) with
        {
            Authority = authority,
            Signature = HMACSHA256.HashData(database.Store.Identity.SigningKey.Span, authority)
        });
    }

    internal static async Task RejectWithoutEffects(TestDatabase database, ReplicatedOperation operation, ErrorCode code = ErrorCode.Corruption)
    {
        var before = database.Store.Position;
        var apply = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(operation));
        var resolve = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.ResolveOutcome(operation));
        await Assert.That(apply.Code).IsEqualTo(code);
        await Assert.That(resolve.Code).IsEqualTo(code);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)).IsNull();
    }
}
