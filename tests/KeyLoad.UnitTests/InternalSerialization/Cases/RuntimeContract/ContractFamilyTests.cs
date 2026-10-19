using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ContractFamilyTests
{
    private static readonly byte[] RawKey = [0x00, 0xff, 0x80];

    [Test]
    [Arguments(ContractFamily.Document)]
    [Arguments(ContractFamily.Authorization)]
    [Arguments(ContractFamily.Messaging)]
    [Arguments(ContractFamily.Events)]
    [Arguments(ContractFamily.Graph)]
    [Arguments(ContractFamily.TimeSeries)]
    [Arguments(ContractFamily.Vector)]
    [Arguments(ContractFamily.Outbox)]
    [Arguments(ContractFamily.Relational)]
    [Arguments(ContractFamily.Query)]
    public async Task AcIs001ActualFamilyRetainsTypeAndCompletePublicValue(ContractFamily family)
    {
        var value = ContractCases.Family(family);
        var bytes = OrleansSerialization.Serialize(value);
        var actual = OrleansSerialization.Deserialize<object>(bytes);
        await Assert.That(actual.GetType()).IsEqualTo(value.GetType());
        // Public JSON remains its existing boundary. This comparison proves every ordinary member,
        // including initialized immutable collections and nullable values, survived execution decoding.
        await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(value), CollectionOrdering.Matching);
        await Assert.That(OrleansSerialization.Measure(value)).IsEqualTo((long)bytes.Length);
    }

    [Test]
    public async Task AcIs002ScalarTypesAndExactMeasurementsStayIntact()
    {
        object[] values = [0L, 0, 0d, false, string.Empty, Guid.Empty, DateTimeOffset.UnixEpoch];
        foreach (var value in values)
        {
            var bytes = OrleansSerialization.Serialize(value);
            var actual = OrleansSerialization.Deserialize<object>(bytes);
            await Assert.That(actual.GetType()).IsEqualTo(value.GetType());
            await Assert.That(actual).IsEqualTo(value);
            await Assert.That(OrleansSerialization.Measure(value)).IsEqualTo((long)bytes.Length);
        }
    }

    [Test]
    public async Task AcIs001AllConcreteMutationsRetainBaseScopeAndDerivedFields()
    {
        var request = new CommandRequest(Guid.NewGuid(), ContractCases.Partition, ContractCases.Mutations);
        var actual = OrleansSerialization.Deserialize<CommandRequest>(OrleansSerialization.Serialize(request));
        await Assert.That(actual.CommandId).IsEqualTo(request.CommandId);
        await Assert.That(actual.Partition).IsEqualTo(request.Partition);
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
        var encoded = OrleansSerialization.Serialize(values);
        var actual = OrleansSerialization.Deserialize<StorageMutation[]>(encoded);
        Array.Clear(key);
        Array.Clear(encoded);
        await Assert.That(actual[0].Key.ToArray()).IsEquivalentTo(RawKey, CollectionOrdering.Matching);
        await Assert.That(actual[0].Value.HasValue).IsTrue();
        await Assert.That(actual[0].Value!.Value.IsEmpty).IsTrue();
        await Assert.That(actual[1].Value).IsNull();
        await Assert.That(actual[2].Value!.Value.ToArray()).IsEquivalentTo(new byte[] { 0x81, 0xfe }, CollectionOrdering.Matching);
    }
}
