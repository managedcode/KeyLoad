using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeContractFamilyTests
{
    private const long OwnershipEpoch = 7;
    private static readonly byte[] RawKey = [0x00, 0xff, 0x80];

    [Test]
    [Arguments(NativeContractFamily.Document)]
    [Arguments(NativeContractFamily.Authorization)]
    [Arguments(NativeContractFamily.Messaging)]
    [Arguments(NativeContractFamily.Events)]
    [Arguments(NativeContractFamily.Graph)]
    [Arguments(NativeContractFamily.TimeSeries)]
    [Arguments(NativeContractFamily.Vector)]
    [Arguments(NativeContractFamily.Blob)]
    [Arguments(NativeContractFamily.Outbox)]
    [Arguments(NativeContractFamily.Relational)]
    [Arguments(NativeContractFamily.Query)]
    public async Task AcIs001ActualFamilyRetainsNativeTypeAndCompletePublicValue(NativeContractFamily family)
    {
        var value = NativeContractCases.Family(family);
        var bytes = NativeSerialization.Serialize(value);
        var actual = NativeSerialization.Deserialize<object>(bytes);
        await Assert.That(actual.GetType()).IsEqualTo(value.GetType());
        // Public JSON remains its existing boundary. This comparison proves every ordinary member,
        // including initialized immutable collections and nullable values, survived native decoding.
        await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(value), CollectionOrdering.Matching);
        await Assert.That(NativeSerialization.Measure(value)).IsEqualTo((long)bytes.Length);
    }

    [Test]
    public async Task AcIs002NativeScalarTypesAndExactMeasurementsStayIntact()
    {
        object[] values = [0L, 0, 0d, false, string.Empty, Guid.Empty, DateTimeOffset.UnixEpoch];
        foreach (var value in values)
        {
            var bytes = NativeSerialization.Serialize(value);
            var actual = NativeSerialization.Deserialize<object>(bytes);
            await Assert.That(actual.GetType()).IsEqualTo(value.GetType());
            await Assert.That(actual).IsEqualTo(value);
            await Assert.That(NativeSerialization.Measure(value)).IsEqualTo((long)bytes.Length);
        }
    }

    [Test]
    public async Task AcIs001AllConcreteMutationsRetainBaseScopeAndDerivedFields()
    {
        var request = new CommandRequest(Guid.NewGuid(), NativeContractCases.Partition, NativeContractCases.Mutations, OwnershipEpoch);
        var actual = NativeSerialization.Deserialize<CommandRequest>(NativeSerialization.Serialize(request));
        await Assert.That(actual.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(actual.OwnershipEpoch).IsEqualTo(OwnershipEpoch);
        await Assert.That(actual.Mutations.Length).IsEqualTo(request.Mutations.Length);
        for (var index = 0; index < actual.Mutations.Length; index++)
        {
            await Assert.That(actual.Mutations[index].GetType()).IsEqualTo(request.Mutations[index].GetType());
            await Assert.That(actual.Mutations[index].Resource).IsEqualTo(request.Mutations[index].Resource);
            await Assert.That(JsonDefaults.Serialize(actual.Mutations[index])).IsEquivalentTo(JsonDefaults.Serialize(request.Mutations[index]), CollectionOrdering.Matching);
        }
    }

    [Test]
    public async Task AcIs002RawBytesEmptyValuesAndTombstonesStayDistinctAndOwned()
    {
        var key = RawKey.ToArray();
        StorageMutation[] values = [new(key, ReadOnlyMemory<byte>.Empty), new(key, null), new(key, new byte[] { 0x81, 0xfe })];
        var encoded = NativeSerialization.Serialize(values);
        var actual = NativeSerialization.Deserialize<StorageMutation[]>(encoded);
        Array.Clear(key);
        Array.Clear(encoded);
        await Assert.That(actual[0].Key.ToArray()).IsEquivalentTo(RawKey, CollectionOrdering.Matching);
        await Assert.That(actual[0].Value.HasValue).IsTrue();
        await Assert.That(actual[0].Value!.Value.IsEmpty).IsTrue();
        await Assert.That(actual[1].Value).IsNull();
        await Assert.That(actual[2].Value!.Value.ToArray()).IsEquivalentTo(new byte[] { 0x81, 0xfe }, CollectionOrdering.Matching);
    }
}
