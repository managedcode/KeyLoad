using KeyLoad.Storage.ZoneTree;
using ZoneTree;
using ZoneTree.Options;

namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed class NonTransactionalWalAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StoredWalModeIsRejectedBeforeOpenWithoutRewritingItsMetadata(bool plain)
    {
        using var fixture = new NonTransactionalTableFixture();
        (await fixture.ConfigureAsync()).Get<ResourceDefinition>();
        (await fixture.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.First, RelationalTestData.Row()))).Get<CommitReceipt>();
        var original = fixture.Store.Read(view => view.ReadOwnedValue(fixture.RowKey()))!;
        var identityPath = Path.Combine(fixture.Owner.Directory, "identity.json");
        var identity = await File.ReadAllBytesAsync(identityPath);
        fixture.Store.Dispose();

        if (plain)
        {
            PersistAsynchronousMode(ZoneTreeTreeFactory.Plain(fixture.Destination(NonTransactionalTableFixture.PlainDirectory)));
        }
        else
        {
            PersistAsynchronousMode(ZoneTreeTreeFactory.Storage(fixture.Destination("tree")));
        }
        var metadataPath = Path.Combine(fixture.Destination(plain ? NonTransactionalTableFixture.PlainDirectory : "tree"), "0.json");
        var metadata = await File.ReadAllBytesAsync(metadataPath);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var refused = NonTransactionalTableFixture.Open(fixture.Owner.Directory);
        });
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That((await File.ReadAllBytesAsync(identityPath)).SequenceEqual(identity)).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(metadataPath)).SequenceEqual(metadata)).IsTrue();
        using (var native = ZoneTreeTreeFactory.Plain(fixture.Destination(NonTransactionalTableFixture.PlainDirectory)).Open())
        {
            await Assert.That(native.TryGet(fixture.RowKey(), out var retained)).IsTrue();
            await Assert.That(retained.Value.Span.SequenceEqual(original)).IsTrue();
        }

        using var healthy = new NonTransactionalTableFixture();
        (await healthy.ConfigureAsync()).Get<ResourceDefinition>();
        (await healthy.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table,
            RelationalTestData.First, RelationalTestData.Row()))).Get<CommitReceipt>();
        await Assert.That(healthy.Database.GetDocument(RelationalTestData.Root,
            new(healthy.Partition, RelationalTestData.Table, RelationalTestData.First))!.Json)
            .IsEqualTo(RelationalTestData.Row());
    }

    private static void PersistAsynchronousMode<TKey, TValue>(ZoneTreeFactory<TKey, TValue> factory)
    {
        factory.ConfigureWriteAheadLogOptions(options => options.WriteAheadLogMode = WriteAheadLogMode.AsyncCompressed);
        using var native = factory.Open();
        native.Maintenance.MoveMutableSegmentForward();
    }
}
