using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionRestartTests
{
    private const string Collection = "native-text-restart";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string RootReceipt = NativeTextProtocol.RootReceiptFile;
    private const string GenerationPrefix = NativeTextProtocol.GenerationPrefix;
    private const string NativeDirectory = NativeTextProtocol.NativeDirectory;

    [Test]
    public async Task RestartRebuildsFromCanonicalStoreUsingRecognizedNativeZoneTreeFiles()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle needle\"}"),
            new PutDocument(Collection, "two", "{\"text\":\"needle other\"}"));
        var indexDirectory = Path.Combine(database.Directory, "native-text");
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query, Limit: 10);
        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);

        var beforeRestart = new NativeTextProjection(indexDirectory, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var firstResult = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), beforeRestart).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);
        await AssertEquivalentAsync(expected, firstResult);
        await AssertRecognizedNativeGenerationAsync(indexDirectory, database);
        beforeRestart.Dispose();

        using var afterRestart = new NativeTextProjection(indexDirectory, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
        var rebuiltResult = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), afterRestart).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);

        await AssertEquivalentAsync(expected, rebuiltResult);
        await AssertRecognizedNativeGenerationAsync(indexDirectory, database);
    }

    [Test]
    public async Task CorruptManifestIsRejectedAndPreservedForDiagnosis()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexDirectory = Path.Combine(database.Directory, "native-text");
        using (var projection = new NativeTextProjection(indexDirectory, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
                   database.Store.Identity.NodeId, UnitNativeTextOptions.Execution()))
        {
            _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
                new(database.Partition, Collection, TextPath, Query));
        }
        var generation = Directory.EnumerateDirectories(indexDirectory)
            .Single(path => Path.GetFileName(path).StartsWith(GenerationPrefix, StringComparison.Ordinal));
        var manifest = Path.Combine(generation, NativeTextProtocol.ManifestFile);
        var damaged = await File.ReadAllBytesAsync(manifest, cancellationToken);
        damaged[^1] ^= 1;
        await File.WriteAllBytesAsync(manifest, damaged, cancellationToken);
        var damagedReceipt = await File.ReadAllBytesAsync(manifest, cancellationToken);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenForRestart(indexDirectory, database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(await File.ReadAllBytesAsync(manifest, cancellationToken)).IsEquivalentTo(damagedReceipt,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Directory.Exists(generation)).IsTrue();
    }

    [Test]
    public async Task UnknownProjectionEntryIsRejectedAndPreserved()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var indexDirectory = Path.Combine(database.Directory, "native-text");
        Directory.CreateDirectory(indexDirectory);
        var unknown = Path.Combine(indexDirectory, "user-owned.bin");
        var content = new byte[] { 0x11, 0x20, 0x31 };
        await File.WriteAllBytesAsync(unknown, content, cancellationToken);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenForRestart(indexDirectory, database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(await File.ReadAllBytesAsync(unknown, cancellationToken)).IsEquivalentTo(content,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task LinkedNativeEntryIsRejectedWithoutFollowingOrRemovingIt()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexDirectory = Path.Combine(database.Directory, "native-text");
        using (var projection = new NativeTextProjection(indexDirectory, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
                   database.Store.Identity.NodeId, UnitNativeTextOptions.Execution()))
        {
            _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
                new(database.Partition, Collection, TextPath, Query));
        }
        var generation = Directory.EnumerateDirectories(indexDirectory)
            .Single(path => Path.GetFileName(path).StartsWith(GenerationPrefix, StringComparison.Ordinal));
        var native = Path.Combine(generation, NativeDirectory);
        var protectedFile = Path.Combine(database.Directory, "outside-native.bin");
        var protectedBytes = new byte[] { 0x17, 0x28, 0x39 };
        await File.WriteAllBytesAsync(protectedFile, protectedBytes, cancellationToken);
        var link = Path.Combine(native, "external.bin");
        File.CreateSymbolicLink(link, protectedFile);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenForRestart(indexDirectory, database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(new FileInfo(link).LinkTarget).IsNotNull();
        await Assert.That(await File.ReadAllBytesAsync(protectedFile, cancellationToken)).IsEquivalentTo(protectedBytes,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task AssertRecognizedNativeGenerationAsync(string indexDirectory, TestDatabase database)
    {
        var rootReceiptPath = Path.Combine(indexDirectory, RootReceipt);
        await Assert.That(File.Exists(rootReceiptPath)).IsTrue();
        var rootReceipt = NativeTextEnvelopeCodec.Decode<NativeTextOwnerReceipt>(
            await File.ReadAllBytesAsync(rootReceiptPath, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(rootReceipt.SourceNodeId).IsEqualTo(database.Store.Identity.NodeId);
        var generation = Directory.EnumerateDirectories(indexDirectory)
            .Single(path => Path.GetFileName(path).StartsWith(GenerationPrefix, StringComparison.Ordinal));
        var ownerPath = Path.Combine(generation, NativeTextProtocol.OwnerFile);
        var owner = NativeTextEnvelopeCodec.Decode<NativeTextOwnerReceipt>(
            await File.ReadAllBytesAsync(ownerPath, TestContext.Current!.Execution.CancellationToken));
        var manifest = NativeTextEnvelopeCodec.Decode<NativeTextManifest>(await File.ReadAllBytesAsync(
            Path.Combine(generation, NativeTextProtocol.ManifestFile), TestContext.Current!.Execution.CancellationToken));
        await Assert.That(owner.GenerationLeaf).IsEqualTo(Path.GetFileName(generation));
        await Assert.That(owner.RootDirectory).IsEqualTo(Path.GetFullPath(indexDirectory));
        await Assert.That(manifest.FormatVersion).IsEqualTo(NativeTextProtocol.FormatVersion);
        await Assert.That(manifest.TokenizerVersion).IsEqualTo(TextProjectionProtocol.TokenizerVersion);
        await Assert.That(manifest.HashVersion).IsEqualTo(TextProjectionProtocol.HashVersion);
        await Assert.That(manifest.Scope.NodeId).IsEqualTo(database.Store.Identity.NodeId);
        await Assert.That(manifest.Scope.Position).IsEqualTo(database.Store.Position);
        await Assert.That(manifest.Scope.Partition).IsEqualTo(database.Partition);
        await Assert.That(manifest.Scope.Collection).IsEqualTo(Collection);
        await Assert.That(manifest.Scope.Field).IsEqualTo(TextPath);
        await Assert.That(manifest.Records.Length).IsEqualTo(2);
        var nativeFiles = Directory.EnumerateFiles(Path.Combine(generation, NativeDirectory), "*",
            SearchOption.AllDirectories).ToArray();
        await Assert.That(nativeFiles.Length).IsGreaterThan(0);
    }

    private static void OpenForRestart(string indexDirectory, TestDatabase database)
    {
        using var projection = new NativeTextProjection(indexDirectory, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
    }

    private static async Task AssertEquivalentAsync(RankedDocument[] expected, RankedDocument[] actual)
    {
        await Assert.That(actual.Select(result => result.Document.Reference.Id).ToArray())
            .IsEquivalentTo(expected.Select(result => result.Document.Reference.Id).ToArray(), CollectionOrdering.Matching);
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].Document).IsEquivalentTo(expected[index].Document);
            await Assert.That(actual[index].Score).IsEqualTo(expected[index].Score);
        }
    }
}
