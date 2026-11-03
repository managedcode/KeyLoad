using System.Text.Json;
using KeyLoad.Server.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;
using ZoneTree.FullTextSearch;
using ZoneTree.FullTextSearch.Index;
using ZoneTree.Options;

namespace KeyLoad.FunctionalTests.Search;

internal static class TextBootstrapColdAssertions
{
    private const string NativeIndex = "index1";
    private const string NativeMetadata = "0.json";
    private const string NativeWalOptions = "WriteAheadLogOptions";
    private const string NativeWalMode = "WriteAheadLogMode";

    internal static async Task VerifyAsync(string hostRoot, Guid node, IOptions<TextExecutionOptions> options,
        CancellationToken token)
    {
        var root = Path.Combine(hostRoot, TextHostResourceRoots.BootstrapDirectory);
        var generation = Directory.EnumerateDirectories(root).Single(path => Path.GetFileName(path)
            .StartsWith(TextProtocol.GenerationPrefix, StringComparison.Ordinal));
        var leaf = Path.GetFileName(generation);
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var manifestPath = Path.Combine(generation, TextProtocol.ManifestFile);
        var manifest = await File.ReadAllBytesAsync(manifestPath, token);
        var inventory = Paths(generation);
        await Assert.That(inventory.Any(path => path.Contains(".wal_xxh3.", StringComparison.Ordinal)
            || path.Contains(".wal_crc.", StringComparison.Ordinal))).IsFalse();
        using (var metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(execution, NativeIndex, NativeMetadata), token)))
        {
            await Assert.That(metadata.RootElement.GetProperty(NativeWalOptions)
                .GetProperty(NativeWalMode).GetByte()).IsEqualTo((byte)WriteAheadLogMode.None);
        }
        await EmptyDirectoriesAsync(root, generation, leaf, node, manifest, options);
        var resources = TextHostResourceRoots.Register(hostRoot, node, options);
        using (var index = RuntimeTextIndex.Open(execution,
            new TextFileStreamProvider(root, leaf, node, options, resources, allowReadOnlyInventory: true),
            options, WriteAheadLogMode.None))
        {
            index.IsReadOnly = true;
            index.WaitForBackgroundThreads();
            var maintenance = index.ZoneTree1.Maintenance;
            var native = maintenance.CloneOptions();
            await Assert.That(native.WriteAheadLogOptions.WriteAheadLogMode).IsEqualTo(WriteAheadLogMode.None);
            // ZoneTree does not register its null WAL in the provider's active-writer table.
            await Assert.That(native.WriteAheadLogProvider.GetWAL<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte>(
                maintenance.MutableSegment.SegmentId,
                global::ZoneTree.Core.ZoneTree<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte>.SegmentWalCategory)).IsNull();
            await Assert.That(index.ZoneTree1.Count()).IsEqualTo(2L);
            await PostingAsync(index, "needle", 0);
            await PostingAsync(index, "original", TextHash.Sha256("needle"));
        }
        await Assert.That(Paths(generation)).IsEquivalentTo(inventory, CollectionOrdering.Matching);
        await Assert.That((await File.ReadAllBytesAsync(manifestPath, token)).AsSpan().SequenceEqual(manifest)).IsTrue();
    }

    private static async Task PostingAsync(IndexOfTokenRecordPreviousToken<ulong, ulong> index,
        string term, ulong previous)
    {
        var key = new CompositeKeyOfTokenRecordPrevious<ulong, ulong>
        {
            Token = TextHash.Sha256(term),
            Record = 1,
            PreviousToken = previous
        };
        await Assert.That(index.ZoneTree1.ContainsKey(key)).IsTrue();
    }

    private static async Task EmptyDirectoriesAsync(string root, string generation, string leaf, Guid node,
        byte[] manifest, IOptions<TextExecutionOptions> options)
    {
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(generation, TextProtocol.OwnerFile),
            root, leaf, node, options);
        var files = TextEnvelopeCodec.Decode<TextManifest>(manifest).Files;
        var expected = TextLiveOwnedPaths.FromFiles(files, options);
        TextLiveOwnedPaths.VerifyFilesystem(generation, owner.OwnedPaths, expected, options);
        var unowned = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore, "unowned-empty");
        Directory.CreateDirectory(unowned);
        try
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                TextLiveOwnedPaths.VerifyFilesystem(generation, owner.OwnedPaths, expected, options));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        }
        finally
        {
            Directory.Delete(unowned);
        }
        TextLiveOwnedPaths.VerifyFilesystem(generation, owner.OwnedPaths, expected, options);
    }

    private static string[] Paths(string generation)
        => Directory.EnumerateFileSystemEntries(generation, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(generation, path)).Order(StringComparer.Ordinal).ToArray();
}
