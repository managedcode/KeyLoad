#pragma warning disable ORLEANSEXP005
using System.Buffers;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Orleans.Journaling;
using Orleans.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RuntimeJournalNativeDataSource]
[NotInParallel]
internal sealed class RuntimeJournalNativeStorageTests(RuntimeJournalNativeFixture fixture)
{
    private const int FirstPatternIndex = 0;
    private const int BinaryReplayPayloadBytes = 98_304;
    private const string KindProperty = "kind";
    private const string DurableKind = "durable";
    private const string StaleProperty = "stale";
    private const string StaleValue = "1";
    private const string IndexProperty = "index";
    private const string OwnerProperty = RuntimeJournalProtocol.OwnerProperty;
    private const string FirstIndexValue = "one";
    private const string SecondIndexValue = "two";
    private const string InvalidNulProperty = "bad\0key";

    [Test]
    public async Task NativeStorageCreatesWritesReplaysReplacesAndDeletesCommittedContent()
    {
        var name = NewName("content");
        var storage = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await storage.CreateIfNotExistsAsync(new Dictionary<string, string> { [KindProperty] = DurableKind })).IsTrue();
        var content = Enumerable.Range(0, 80_000).Select(index => (byte)(index % byte.MaxValue)).ToArray();
        await storage.AppendAsync(new ReadOnlySequence<byte>(content), CancellationToken.None);
        using var replay = new RuntimeJournalNativeReadAccumulator();
        await storage.ReadAsync(replay, CancellationToken.None);
        await Assert.That(replay.IsCompleted).IsTrue();
        await Assert.That(replay.ToArray().SequenceEqual(content)).IsTrue();
        await Assert.That(replay.Metadata?.FormatKey).IsEqualTo(RuntimeJournalStoragePolicy.BinaryFormat);

        var replacement = new byte[] { 9, 4, 7 };
        await storage.ReplaceAsync(new ReadOnlySequence<byte>(replacement), CancellationToken.None);
        using var replaced = new RuntimeJournalNativeReadAccumulator();
        await storage.ReadAsync(replaced, CancellationToken.None);
        await Assert.That(replaced.ToArray().SequenceEqual(replacement)).IsTrue();
        await storage.DeleteAsync(CancellationToken.None);
        await Assert.That(await storage.GetMetadataAsync()).IsNull();
    }

    [Test]
    public async Task NativeOrleansBinaryReplaysOneEntryAcrossReadPageBoundaries()
    {
        var grainId = NewName("binary-replay");
        var grain = fixture.Cluster.Client.GetGrain<IRuntimeJournalReplayGrain>(grainId);
        var expected = Enumerable.Range(FirstPatternIndex, BinaryReplayPayloadBytes)
            .Select(index => (byte)(index % byte.MaxValue)).ToArray();
        await grain.SetAsync(expected);
        var originalActivation = await grain.GetActivationTokenAsync();
        await grain.DeactivateAsync();
        await WaitForActivationChangeAsync(grain, originalActivation);
        var recovered = fixture.Cluster.Client.GetGrain<IRuntimeJournalReplayGrain>(grainId);
        var actual = await recovered.ReadAsync();
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.SequenceEqual(expected)).IsTrue();
        var beforeDeleteActivation = await recovered.GetActivationTokenAsync();
        await recovered.DeleteAsync();
        await recovered.DeactivateAsync();
        await WaitForActivationChangeAsync(recovered, beforeDeleteActivation);
        await Assert.That(await recovered.ReadAsync()).IsNull();
    }

    [Test]
    public async Task NativeMetadataCasReturnsConflictAndFencesStaleBodyWriter()
    {
        var name = NewName("fence");
        var first = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await first.CreateIfNotExistsAsync()).IsTrue();
        var stale = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await stale.GetMetadataAsync()).IsNotNull();
        var metadata = await first.GetMetadataAsync();
        var updated = await first.UpdateMetadataAsync(
            new Dictionary<string, string> { [OwnerProperty] = "worker-a" },
            expectedETag: metadata!.ETag);
        await Assert.That(updated).IsNotNull();
        await Assert.That(updated!.ETag).IsNotEqualTo(metadata.ETag);
        await Assert.That((await first.GetMetadataAsync())!.ETag).IsEqualTo(updated.ETag);
        await Assert.That(await first.UpdateMetadataAsync(new Dictionary<string, string> { [StaleProperty] = StaleValue },
            expectedETag: metadata.ETag)).IsNull();
        await Assert.ThrowsExactlyAsync<InconsistentStateException>(async () =>
            await stale.AppendAsync(new ReadOnlySequence<byte>(new byte[] { 1 }), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InconsistentStateException>(async () =>
            await first.AppendAsync(new ReadOnlySequence<byte>(new byte[] { 2 }), CancellationToken.None));

        var current = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await current.GetMetadataAsync()).IsNotNull();
        await current.AppendAsync(new ReadOnlySequence<byte>(new byte[] { 3 }), CancellationToken.None);
        await current.DeleteAsync(CancellationToken.None);
        await Assert.That(await first.GetMetadataAsync()).IsNull();
        var replacement = fixture.Provider.CreateStorage(new(name));
        await Assert.That(await replacement.CreateIfNotExistsAsync()).IsTrue();
        await Assert.ThrowsExactlyAsync<InconsistentStateException>(async () =>
            await first.AppendAsync(new ReadOnlySequence<byte>(new byte[] { 4 }), CancellationToken.None));
        await replacement.DeleteAsync(CancellationToken.None);
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
        await first.CreateIfNotExistsAsync(new Dictionary<string, string> { [IndexProperty] = FirstIndexValue });
        await second.CreateIfNotExistsAsync(new Dictionary<string, string> { [IndexProperty] = SecondIndexValue });
        await outside.CreateIfNotExistsAsync();

        var options = new JournalCatalogListOptions { Prefix = new(prefix), MinId = new(firstName), MaxId = new(secondName), IncludeMetadata = true };
        var entries = new List<JournalCatalogEntry>();
        await foreach (var entry in fixture.Catalog.ListAsync(options))
        {
            entries.Add(entry);
        }

        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries.All(entry => entry.Metadata?.FormatKey == RuntimeJournalStoragePolicy.BinaryFormat)).IsTrue();
        await Assert.That(entries.Select(entry => entry.Id.Value).ToHashSet(StringComparer.Ordinal))
            .IsEquivalentTo(new[] { firstName, secondName });
        await first.DeleteAsync(CancellationToken.None);
        await second.DeleteAsync(CancellationToken.None);
        await outside.DeleteAsync(CancellationToken.None);
    }

    [Test]
    public async Task NativeMetadataRejectsNulKeysWithoutChangingCommittedMetadata()
    {
        var storage = fixture.Provider.CreateStorage(new(NewName("metadata")));
        await storage.CreateIfNotExistsAsync();
        var before = await storage.GetMetadataAsync();
        await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            await storage.UpdateMetadataAsync(new Dictionary<string, string> { [InvalidNulProperty] = "value" }));
        var after = await storage.GetMetadataAsync();
        await Assert.That(after?.ETag).IsEqualTo(before?.ETag);
        await Assert.That(after?.Properties).IsEmpty();
        await storage.DeleteAsync(CancellationToken.None);
    }

    [Test]
    public async Task NativeOversizeWriteFailsBeforeChangingTheCommittedJournal()
    {
        var storage = fixture.Provider.CreateStorage(new(NewName("capacity")));
        await storage.CreateIfNotExistsAsync();
        var tooLarge = new byte[fixture.JournalOptions.Value.MaximumJournalBytes + 1];
        var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            await storage.ReplaceAsync(new ReadOnlySequence<byte>(tooLarge), CancellationToken.None));
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        using var replay = new RuntimeJournalNativeReadAccumulator();
        await storage.ReadAsync(replay, CancellationToken.None);
        await Assert.That(replay.ToArray()).IsEmpty();
        await storage.DeleteAsync(CancellationToken.None);
    }

    private static string NewName(string prefix) => $"native/{prefix}/{Guid.NewGuid():N}";

    private async Task WaitForActivationChangeAsync(IRuntimeJournalReplayGrain grain, string previousToken)
    {
        using var deadline = new CancellationTokenSource(fixture.TimingOptions.Value.CompletionTimeout);
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            var current = await grain.GetActivationTokenAsync().WaitAsync(deadline.Token);
            if (current != previousToken)
            {
                return;
            }

            await Task.Delay(fixture.TimingOptions.Value.PollInterval, deadline.Token);
        }
    }
}
#pragma warning restore ORLEANSEXP005
