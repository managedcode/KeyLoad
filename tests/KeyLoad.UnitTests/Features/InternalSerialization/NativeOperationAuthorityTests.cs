using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationAuthorityTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task MutatedNativeBodyProofOrFrozenIdentityCannotProduceAnyOutcome(int mutation)
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var payload = NativeAuthorityFixture.Read(operation);
        var changed = mutation switch
        {
            0 => NativeAuthorityFixture.Wrap(operation, payload with { Value = NativeSerialization.Serialize(false) }),
            1 => NativeAuthorityFixture.Wrap(operation, payload with { Error = ErrorCode.Validation }),
            2 => NativeAuthorityFixture.Wrap(operation, payload with { SafeDetail = NativeAuthorityFixture.ChangedDetail }),
            3 => NativeAuthorityFixture.Wrap(operation, payload with { Signature = new byte[NativeAuthorityContract.DigestBytes] }),
            4 => operation with { PayloadJson = bool.FalseString },
            5 => operation with { Id = Guid.NewGuid() },
            6 => operation with { Kind = OperationKind.Membership },
            7 => operation with { PrincipalId = NativeAuthorityFixture.OtherPrincipal },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await NativeAuthorityFixture.RejectWithoutEffects(database, changed);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task MissingMalformedOrUnsignedAuthorityIsRejectedBeforeEffects(int mutation)
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var payload = NativeAuthorityFixture.Read(operation);
        var changed = mutation switch
        {
            0 => payload with { Authority = ReadOnlyMemory<byte>.Empty },
            1 => payload with { Signature = ReadOnlyMemory<byte>.Empty },
            2 => payload with { Authority = NativeSerialization.Serialize(true) },
            3 => new NativeCommandPayload(payload.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await NativeAuthorityFixture.RejectWithoutEffects(database, NativeAuthorityFixture.Wrap(operation, changed));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EvenValidMacMustMatchActualAuthorityFields(int mutation)
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var claims = NativeSerialization.Deserialize<NativeCommandAuthority>(NativeAuthorityFixture.Read(operation).Authority.Span);
        var changed = mutation switch
        {
            0 => claims with { Purpose = NativeAuthorityFixture.WrongPurpose },
            1 => claims with { Incarnation = Guid.NewGuid() },
            2 => claims with { ValueHash = new byte[NativeAuthorityContract.DigestBytes] },
            3 => claims with { Error = ErrorCode.Validation, SafeDetail = NativeAuthorityFixture.ChangedDetail },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await NativeAuthorityFixture.RejectWithoutEffects(database, NativeAuthorityFixture.Resign(database, operation, changed));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthenticatedWrongTypeOrTrailingAuthorityStillFailsStrictNativeDecode(bool trailing)
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var payload = NativeAuthorityFixture.Read(operation);
        var authority = trailing ? (byte[])[.. payload.Authority.ToArray(), 0] : NativeSerialization.Serialize(true);
        var changed = payload with
        {
            Authority = authority,
            Signature = System.Security.Cryptography.HMACSHA256.HashData(database.Store.Identity.SigningKey.Span, authority)
        };
        await NativeAuthorityFixture.RejectWithoutEffects(database, NativeAuthorityFixture.Wrap(operation, changed));
    }

    [Test]
    public async Task ProofFromAnotherDurableStoreFailsBeforeMutation()
    {
        using var source = new TestDatabase();
        using var target = new TestDatabase();
        await NativeAuthorityFixture.RejectWithoutEffects(target, NativeAuthorityFixture.Create(source));
    }
}
