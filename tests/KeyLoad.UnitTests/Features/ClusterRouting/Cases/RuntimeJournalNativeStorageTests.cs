#pragma warning disable ORLEANSEXP005
using System.Buffers;
using KeyLoad.Orleans;
using Orleans.Journaling;
using Orleans.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RuntimeJournalNativeDataSource]
[NotInParallel]
internal sealed class RuntimeJournalNativeStorageTests(RuntimeJournalNativeFixture fixture)
{
    [Test]
    public async Task NativeStorageCreatesWritesReplaysReplacesAndDeletesCommittedContent()
    {
        var name = NewName("content");
        var storage = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await storage.CreateIfNotExistsAsync(new Dictionary<string, string> { ["kind"] = "durable" })).IsTrue();
        var content = Enumerable.Range(0, 80_000).Select(index => (byte)(index % byte.MaxValue)).ToArray();
        await storage.AppendAsync(new ReadOnlySequence<byte>(content), CancellationToken.None);
        using var replay = new RuntimeJournalNativeReadAccumulator();
        await storage.ReadAsync(replay, CancellationToken.None);
        await Assert.That(replay.IsCompleted).IsTrue();
        await Assert.That(replay.ToArray().SequenceEqual(content)).IsTrue();
        await Assert.That(replay.Metadata?.Format).IsEqualTo(RuntimeJournalStoragePolicy.BinaryFormat);

        var replacement = new byte[] { 9, 4, 7 };
        await storage.ReplaceAsync(new ReadOnlySequence<byte>(replacement), CancellationToken.None);
        using var replaced = new RuntimeJournalNativeReadAccumulator();
        await storage.ReadAsync(replaced, CancellationToken.None);
        await Assert.That(replaced.ToArray().SequenceEqual(replacement)).IsTrue();
        await storage.DeleteAsync(CancellationToken.None);
        await Assert.That(await storage.GetMetadataAsync()).IsNull();
    }

    [Test]
    public async Task NativeMetadataCasReturnsConflictAndFencesStaleBodyWriter()
    {
        var name = NewName("fence");
        var first = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await first.CreateIfNotExistsAsync()).IsTrue();
        var stale = fixture.Provider.CreateStorage(new(name));
        var metadata = await first.GetMetadataAsync();
        var updated = await first.UpdateMetadataAsync(
            new Dictionary<string, string> { ["DurableJobsOwner"] = "worker-a" },
            expectedETag: metadata!.ETag);
        await Assert.That(updated).IsNotNull();
        await Assert.That(updated!.ETag).IsNotEqualTo(metadata.ETag);
        await Assert.That(await first.UpdateMetadataAsync(new Dictionary<string, string> { ["stale"] = "1" },
            expectedETag: metadata.ETag)).IsNull();
        await Assert.ThrowsExactlyAsync<InconsistentStateException>(async () =>
            await stale.AppendAsync(new ReadOnlySequence<byte>(new byte[] { 1 }), CancellationToken.None));

        var current = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await current.GetMetadataAsync()).IsNotNull();
        await current.DeleteAsync(CancellationToken.None);
        await Assert.That(await first.GetMetadataAsync()).IsNotNull();
    }

    [Test]
    public async Task NativeCatalogAppliesOrdinalPrefixAndInclusiveRangeWithMetadata()
    {
        var prefix = NewName("catalog");
        var firstName = prefix + "/a";
        var secondName = prefix + "/b";
        var outsideName = NewName("elsewhere");
        var first = fixture.Provider.CreateStorage(new(firstName));
        var second = fixture.Provider.CreateStorage(new(secondName));
        var outside = fixture.Provider.CreateStorage(new(outsideName));
        await first.CreateIfNotExistsAsync(new Dictionary<string, string> { ["index"] = "one" });
        await second.CreateIfNotExistsAsync(new Dictionary<string, string> { ["index"] = "two" });
        await outside.CreateIfNotExistsAsync();

        var options = new ListOptions { Prefix = new(prefix), MinId = new(firstName), MaxId = new(secondName), IncludeMetadata = true };
        var entries = new List<JournalCatalogEntry>();
        await foreach (var entry in fixture.Catalog.ListAsync(options)) entries.Add(entry);
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.All(entry => entry.Metadata?.Format == RuntimeJournalStoragePolicy.BinaryFormat)).IsTrue();
        await Assert.That(entries.Select(entry => entry.Id.Value).ToHashSet(StringComparer.Ordinal))
            .IsEquivalentTo(new[] { firstName, secondName });
        await first.DeleteAsync(CancellationToken.None);
        await second.DeleteAsync(CancellationToken.None);
        await outside.DeleteAsync(CancellationToken.None);
    }

    private static string NewName(string prefix) => $"native/{prefix}/{Guid.NewGuid():N}";
}
#pragma warning restore ORLEANSEXP005
