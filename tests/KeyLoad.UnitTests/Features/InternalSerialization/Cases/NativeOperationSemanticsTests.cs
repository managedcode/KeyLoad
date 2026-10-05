using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationSemanticsTests
{
    private const string QuerySql = "SELECT * FROM items WHERE value = @a";
    private const string FirstParameter = "a";
    private const string SecondParameter = "b";
    private const string FirstJson = "1.00";
    private const string SecondJson = "{\"界\":\"😀\"}";

    [Test]
    public async Task CopiedNativeEnvelopeComparesBySemanticContentAndRejectsChangedBodyUnderSameRawIdentity()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        await Assert.That(database.Database.NativeOperationsEqual(operation,
            operation with { NativePayload = operation.NativePayload.ToArray() })).IsTrue();
        var payload = NativeAuthorityFixture.Read(operation);
        var tampered = NativeAuthorityFixture.Wrap(operation, payload with { Value = NativeSerialization.Serialize(false) });
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.NativeOperationsEqual(operation, tampered));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task IndependentlyAuthenticatedDifferentTypedBodyIsNotAReplayOfTheSameFrozenIdentity()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var value = NativeSerialization.Serialize(false);
        var claims = NativeSerialization.Deserialize<NativeCommandAuthority>(NativeAuthorityFixture.Read(operation).Authority.Span)
            with
        { ValueHash = System.Security.Cryptography.SHA256.HashData(value) };
        var changed = NativeAuthorityFixture.Wrap(operation, NativeAuthorityFixture.Read(operation) with { Value = value });
        changed = NativeAuthorityFixture.Resign(database, changed, claims);
        await Assert.That(database.Database.NativeOperationsEqual(operation, changed)).IsFalse();
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsNull();
    }

    [Test]
    public async Task ActualAttributedQueryDictionaryOrderingDoesNotDefineCanonicalNativeSemanticHash()
    {
        using var database = new TestDatabase();
        using var first = JsonDocument.Parse(FirstJson);
        using var second = JsonDocument.Parse(SecondJson);
        var left = new QueryRequest(database.Partition, QuerySql,
            new() { [FirstParameter] = first.RootElement, [SecondParameter] = second.RootElement });
        var right = left with { Parameters = new() { [SecondParameter] = second.RootElement, [FirstParameter] = first.RootElement } };
        await Assert.That(DatabaseEngine.NativeTypedFingerprint<QueryRequest>(NativeSerialization.Serialize(left)))
            .IsEqualTo(DatabaseEngine.NativeTypedFingerprint<QueryRequest>(NativeSerialization.Serialize(right)));
        await Assert.That(DatabaseEngine.NativeTypedFingerprint<QueryRequest>(NativeSerialization.Serialize(left)))
            .IsEqualTo(JsonData.Fingerprint(left));
    }

    [Test]
    public async Task NativePayloadMappingCoversEverySupportedOperationKindWithoutInventedDtoTypes()
    {
        foreach (var kind in Enum.GetValues<OperationKind>())
        {
            await Assert.That(DatabaseEngine.NativeOperationPayloadType(kind)).IsNotNull();
        }
    }
}
