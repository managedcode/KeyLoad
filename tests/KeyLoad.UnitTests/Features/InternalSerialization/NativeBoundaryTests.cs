using System.Collections.Immutable;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeBoundaryTests
{
    private const string Principal = "principal";
    private const string Tenant = "tenant";
    private const uint UnsupportedVersion = 2;

    [Test]
    public async Task AcIs002MalformedNullTrailingAndWrongTypesFailClosed()
    {
        using var serializer = new NativeSerializerFixture();
        var valid = NativeSerialization.Serialize(new OutboxHead(1, 1, 1, 1));
        byte[][] malformed = [[], [0xff], serializer.EncodeNullRoot(), serializer.Encode(null), serializer.EncodeEnvelope(NativeEnvelopeFault.WrongRoot), [.. valid, 0x01]];
        foreach (var bytes in malformed)
        {
            var failure = Capture(() => NativeSerialization.Deserialize<OutboxHead>(bytes));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(failure.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
        }
        await Assert.That(Capture(() => NativeSerialization.Deserialize<QueueCounters>(valid)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Capture(() => NativeSerialization.Deserialize<OutboxHead>(serializer.Encode(new OutboxHead(1, 1, 1, 1), UnsupportedVersion))).Code)
            .IsEqualTo(ErrorCode.FormatUnsupported);
    }

    [Test]
    public async Task AcIs002RequiredReferencesAndDefaultCollectionsAreRejectedInBothDirections()
    {
        using var serializer = new NativeSerializerFixture();
        PrincipalRecord[] malformed =
        [
            new(null!, Tenant, [], []),
            new(Principal, Tenant, default, []),
            new(Principal, Tenant, [], []) { Projects = default }
        ];
        foreach (var value in malformed)
        {
            await Assert.That(Capture(() => NativeSerialization.Serialize(value)).Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(Capture(() => NativeSerialization.Deserialize<PrincipalRecord>(serializer.Encode(value))).Code).IsEqualTo(ErrorCode.Corruption);
        }
        var empty = NativeSerialization.Deserialize<PrincipalRecord>(NativeSerialization.Serialize(new PrincipalRecord(Principal, Tenant, [], [])));
        await Assert.That(empty.Grants.IsDefault).IsFalse();
        await Assert.That(empty.Grants.IsEmpty).IsTrue();
    }

    [Test]
    public async Task AcIs002RequiredMutationElementsCannotBeNull()
    {
        using var serializer = new NativeSerializerFixture();
        var malformed = new CommandRequest(Guid.NewGuid(), NativeContractCases.Partition, [null!]);
        await Assert.That(Capture(() => NativeSerialization.Serialize(malformed)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Capture(() => NativeSerialization.Deserialize<CommandRequest>(serializer.Encode(malformed))).Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002UntypedNullableElementsRemainNativeValues()
    {
        object?[] value = [null, string.Empty, 0];
        var actual = NativeSerialization.Deserialize<object?[]>(NativeSerialization.Serialize(value));
        await Assert.That(actual[0]).IsNull();
        await Assert.That(actual[1]).IsEqualTo(string.Empty);
        await Assert.That(actual[2]).IsEqualTo(0);
    }

    [Test]
    public async Task AcIs002NullableCollectionsKeepNullDistinctFromInitializedEmpty()
    {
        var absent = new QueryRow(Principal, 1, NativeContractCases.Json, RedactedFields: null);
        var empty = absent with { RedactedFields = ImmutableArray<string>.Empty };
        var actualAbsent = NativeSerialization.Deserialize<QueryRow>(NativeSerialization.Serialize(absent));
        var actualEmpty = NativeSerialization.Deserialize<QueryRow>(NativeSerialization.Serialize(empty));
        await Assert.That(actualAbsent.RedactedFields).IsNull();
        await Assert.That(actualEmpty.RedactedFields!.Value.IsDefault).IsFalse();
        await Assert.That(actualEmpty.RedactedFields!.Value.IsEmpty).IsTrue();
        using var serializer = new NativeSerializerFixture();
        var malformed = absent with { RedactedFields = [null!] };
        await Assert.That(Capture(() => NativeSerialization.Serialize(malformed)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Capture(() => NativeSerialization.Deserialize<QueryRow>(serializer.Encode(malformed))).Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002ScalarDomainMeaningRemainsAtTheOwningConsumer()
    {
        // Native evolution permits zero/default scalars. Authority and cut validators must reject
        // invalid domain values before effects; attributes do not grant an authority incarnation.
        var value = new StorageSnapshot(Guid.Empty, 0, 0, 0);
        var actual = NativeSerialization.Deserialize<StorageSnapshot>(NativeSerialization.Serialize(value));
        await Assert.That(actual).IsEqualTo(value);
    }

    private static KeyLoadException Capture(Action action)
    {
        try
        {
            action();
        }
        catch (KeyLoadException exception)
        {
            return exception;
        }
        throw new InvalidOperationException(NativeContractCases.ExpectedFailure);
    }
}
