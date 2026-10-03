using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal enum NativeRequiredCommandShape
{
    Partition,
    Mutations,
    Collection,
    Patches
}

internal sealed class NativePublicElementProfileTests
{
    private const string Collection = "orders";
    private const string Entity = "entry";
    private const string Content = "{}";

    [Test]
    public async Task PublicElementsUseTheSameNativeBytesAndKeepOrdinaryPersistenceStrict()
    {
        using var database = new TestDatabase();
        var value = new CommandRequest(Guid.NewGuid(), database.Partition, [null!]);
        var bytes = NativeSerialization.Serialize(value, NativeValidationProfile.PublicInputElements);
        using var destination = new MemoryStream();
        NativeSerialization.Serialize(value, destination, NativeValidationProfile.PublicInputElements);
        await Assert.That(destination.ToArray().AsSpan().SequenceEqual(bytes)).IsTrue();
        await Assert.That(NativeSerialization.Deserialize<CommandRequest>(bytes,
            NativeValidationProfile.PublicInputElements).Mutations[0]).IsNull();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<CommandRequest>(bytes)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Measure(value)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<object>(bytes,
            NativeValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    [Arguments(NativeRequiredCommandShape.Partition)]
    [Arguments(NativeRequiredCommandShape.Mutations)]
    [Arguments(NativeRequiredCommandShape.Collection)]
    [Arguments(NativeRequiredCommandShape.Patches)]
    public async Task RequiredMembersAndInitializedCollectionsRemainStrict(NativeRequiredCommandShape shape)
    {
        using var database = new TestDatabase();
        var id = Guid.NewGuid();
        var command = shape switch
        {
            NativeRequiredCommandShape.Partition => new CommandRequest(id, null!, [null!]),
            NativeRequiredCommandShape.Mutations => new CommandRequest(id, database.Partition, default),
            NativeRequiredCommandShape.Collection => new CommandRequest(id, database.Partition,
                [null!, new PutDocument(null!, Entity, Content)]),
            NativeRequiredCommandShape.Patches => new CommandRequest(id, database.Partition,
                [null!, new PatchDocument(Collection, Entity, default, 1)]),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(command,
            NativeValidationProfile.PublicInputElements));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Database.Outcome(NativeAuthorityFixture.Root, id)).IsNull();
    }

    [Test]
    public async Task NullRootsAndIncompleteNativeInputsRemainRejected()
    {
        using var database = new TestDatabase();
        var command = new CommandRequest(Guid.NewGuid(), database.Partition, [null!]);
        var bytes = NativeSerialization.Serialize(command, NativeValidationProfile.PublicInputElements);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize<CommandRequest>(null!,
            NativeValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<CommandRequest>(bytes.AsSpan(0, bytes.Length - 1),
            NativeValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
        var trailing = new byte[bytes.Length + 1];
        bytes.CopyTo(trailing, 0);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<CommandRequest>(trailing,
            NativeValidationProfile.PublicInputElements)).Code).IsEqualTo(ErrorCode.Corruption);
    }
}
