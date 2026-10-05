using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalCorruptionTests
{
    private const ulong UnknownMagic = 0x554E4B4E4F574EUL;
    private const byte ChangedByteMask = 0x40;
    private const byte MissingMutationKind = 0;
    private const byte UnknownMutationKind = byte.MaxValue;

    [Test]
    [Arguments(InvalidPayload.NullCollection)]
    [Arguments(InvalidPayload.EmptyCollection)]
    [Arguments(InvalidPayload.EmptyKey)]
    [Arguments(InvalidPayload.DefaultRecord)]
    [Arguments(InvalidPayload.DuplicateKey)]
    [Arguments(InvalidPayload.DescendingKeys)]
    [Arguments(InvalidPayload.Malformed)]
    [Arguments(InvalidPayload.TrailingBytes)]
    [Arguments(InvalidPayload.MissingKind)]
    [Arguments(InvalidPayload.PutWithoutValue)]
    [Arguments(InvalidPayload.DeleteWithValue)]
    [Arguments(InvalidPayload.UnknownKind)]
    public async Task AcWal003ChecksummedInvalidBinaryFrameFailsWithoutApplyingAnyMutation(InvalidPayload scenario)
    {
        using var files = new WalFileFixture();
        files.Initialize();
        using var serializer = new WalSerializerFixture();
        var payload = CreateInvalidPayload(serializer, scenario);
        var journal = WalFileFixture.CreateFrame(payload);
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var identity = await File.ReadAllBytesAsync(files.IdentityPath);

        await files.AssertRejectedUnchanged(journal, identity, ErrorCode.Corruption);
        await File.WriteAllBytesAsync(files.JournalPath, []);
        using var recovered = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(recovered.Position).IsEqualTo(0L);
        await Assert.That(recovered.Read(view => view.ReadOwnedValue([0x10]))).IsNull();
        await Assert.That(recovered.Read(view => view.ReadOwnedValue([0x20]))).IsNull();
    }

    [Test]
    [Arguments(InvalidFrame.Checksum)]
    [Arguments(InvalidFrame.Sequence)]
    [Arguments(InvalidFrame.UnknownMagic)]
    public async Task AcWal003FrameEnvelopeCorruptionLeavesJournalAndIdentityUnchanged(InvalidFrame scenario)
    {
        using var files = new WalFileFixture();
        files.Initialize();
        using var serializer = new WalSerializerFixture();
        var payload = serializer.Serialize([Mutation([0x10], [0x30])]);
        var journal = scenario switch
        {
            InvalidFrame.Sequence => WalFileFixture.CreateFrame(payload, sequence: 2),
            InvalidFrame.UnknownMagic => WalFileFixture.CreateFrame(payload, magic: UnknownMagic),
            _ => WalFileFixture.CreateFrame(payload)
        };
        if (scenario == InvalidFrame.Checksum)
        {
            journal[^1] ^= ChangedByteMask;
        }

        await File.WriteAllBytesAsync(files.JournalPath, journal);
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.Corruption);
    }

    [Test]
    public async Task AcWal003TornFinalBinaryFrameTruncatesOnlyTailAndPreservesCommittedCut()
    {
        using var files = new WalFileFixture();
        using (var store = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            store.Commit((transaction, _) => { transaction.Put([0x10], [0x30]); return true; });
        }
        var original = await files.ReadJournalAsync();
        using var serializer = new WalSerializerFixture();
        var tail = WalFileFixture.CreateFrame(serializer.Serialize([Mutation([0x20], [0x40])]), sequence: 2);
        await using (var journal = new FileStream(files.JournalPath, FileMode.Append, FileAccess.Write))
        {
            await journal.WriteAsync(tail.AsMemory(0, tail.Length - 1));
        }

        using var reopened = new ZoneTreeStore(new(files.DirectoryPath), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Position).IsEqualTo(1L);
        await Assert.That(new FileInfo(files.JournalPath).Length).IsEqualTo((long)original.Length);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x10]))!.SequenceEqual(new byte[] { 0x30 })).IsTrue();
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x20]))).IsNull();
    }

    [Test]
    public async Task AcWal003CurrentMagicDoesNotReinterpretJsonAsBinaryPayload()
    {
        using var files = new WalFileFixture();
        files.Initialize();
        var payload = JsonDefaults.Serialize(new KeyLoad.Storage.StorageMutation[]
        {
            new(new byte[] { 0x10 }, new byte[] { 0x30 })
        });
        var journal = WalFileFixture.CreateFrame(payload);
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.Corruption);
    }

    private static byte[] CreateInvalidPayload(WalSerializerFixture serializer, InvalidPayload scenario)
    {
        var first = Mutation([0x10], [0x30]);
        var second = Mutation([0x20], [0x40]);
        return scenario switch
        {
            InvalidPayload.NullCollection => serializer.Serialize(null),
            InvalidPayload.EmptyCollection => serializer.Serialize([]),
            InvalidPayload.EmptyKey => serializer.Serialize([first, Mutation([], [0x40])]),
            InvalidPayload.DefaultRecord => serializer.Serialize([first, default]),
            InvalidPayload.DuplicateKey => serializer.Serialize([first, first]),
            InvalidPayload.DescendingKeys => serializer.Serialize([second, first]),
            InvalidPayload.MissingKind => serializer.Serialize([first, second with { Kind = MissingMutationKind }]),
            InvalidPayload.PutWithoutValue => serializer.Serialize([first, second with { Value = null }]),
            InvalidPayload.DeleteWithValue => serializer.Serialize([first, second with { Kind = ZoneTreeJournalMutation.DeleteKind }]),
            InvalidPayload.UnknownKind => serializer.Serialize([first, second with { Kind = UnknownMutationKind }]),
            InvalidPayload.Malformed => [0xFF, 0xFF, 0xFF],
            InvalidPayload.TrailingBytes => [.. serializer.Serialize([first, second]), 0x00],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static ZoneTreeJournalMutation Mutation(byte[] key, byte[]? value)
        => new()
        {
            Key = key,
            Value = value is null ? null : new ReadOnlyMemory<byte>(value),
            Kind = value is null ? ZoneTreeJournalMutation.DeleteKind : ZoneTreeJournalMutation.PutKind
        };

    internal enum InvalidPayload
    {
        NullCollection,
        EmptyCollection,
        EmptyKey,
        DefaultRecord,
        DuplicateKey,
        DescendingKeys,
        Malformed,
        TrailingBytes,
        MissingKind,
        PutWithoutValue,
        DeleteWithValue,
        UnknownKind
    }

    internal enum InvalidFrame
    {
        Checksum,
        Sequence,
        UnknownMagic
    }
}
