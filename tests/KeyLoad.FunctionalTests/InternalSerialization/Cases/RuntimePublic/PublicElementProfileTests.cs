namespace KeyLoad.FunctionalTests.InternalSerialization;

internal sealed class PublicElementProfileTests
{
    private const string Collection = "orders";
    private const string Entity = "entry";
    private const string Content = "{}";

    [Test]
    public async Task PublicElementsUseTheSameBytesAndKeepOrdinaryPersistenceStrict()
    {
        using var database = new TestDatabase();
        var value = new CommandRequest(Guid.NewGuid(), database.Partition, [null!]);
        var bytes = OrleansSerialization.Serialize(value, ValidationProfile.PublicInputElements);
        using var destination = new MemoryStream();
        OrleansSerialization.Serialize(value, destination, ValidationProfile.PublicInputElements);
        await Assert.That(destination.ToArray().AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(OrleansSerialization.Deserialize<CommandRequest>(bytes,
            ValidationProfile.PublicInputElements).Mutations[0]).IsNull();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<CommandRequest>(bytes)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Measure(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<object>(bytes,
            ValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    [Arguments(RequiredCommandShape.Partition)]
    [Arguments(RequiredCommandShape.Mutations)]
    [Arguments(RequiredCommandShape.Collection)]
    [Arguments(RequiredCommandShape.Patches)]
    public async Task RequiredMembersAndInitializedCollectionsRemainStrict(RequiredCommandShape shape)
    {
        using var database = new TestDatabase();
        var id = Guid.NewGuid();
        var command = shape switch
        {
            RequiredCommandShape.Partition => new CommandRequest(id, null!, [null!]),
            RequiredCommandShape.Mutations => new CommandRequest(id, database.Partition, default),
            RequiredCommandShape.Collection => new CommandRequest(id, database.Partition,
                [null!, new PutDocument(null!, Entity, Content)]),
            RequiredCommandShape.Patches => new CommandRequest(id, database.Partition,
                [null!, new PatchDocument(Collection, Entity, default, 1)]),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(command,
            ValidationProfile.PublicInputElements));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(OutcomeStoreOracle.ReadPartition(database.Store, database.Partition, "root", id)).IsNull();
    }

    [Test]
    public async Task NullRootsAndIncompleteInputsRemainRejected()
    {
        using var database = new TestDatabase();
        var command = new CommandRequest(Guid.NewGuid(), database.Partition, [null!]);
        var bytes = OrleansSerialization.Serialize(command, ValidationProfile.PublicInputElements);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize<CommandRequest>(null!,
            ValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<CommandRequest>(bytes.AsSpan(0, bytes.Length - 1),
            ValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
        var trailing = new byte[bytes.Length + 1];
        bytes.CopyTo(trailing, 0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<CommandRequest>(trailing,
            ValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
    }
}
