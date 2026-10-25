using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using KeyLoad.Query.Search;
using KeyLoad.Server.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextProjectionRestartTests
{
    private const string Collection = "execution-text-restart";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string RootReceipt = TextProtocol.RootReceiptFile;
    private const string GenerationPrefix = TextProtocol.GenerationPrefix;
    private const string RuntimeDirectoryCore = TextProtocol.RuntimeDirectoryCore;

    [Test]
    public async Task RestartRebuildsFromCanonicalStoreUsingRecognizedZoneTreeFiles()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle needle\"}"),
            new PutDocument(Collection, "two", "{\"text\":\"needle other\"}"));
        var indexDirectory = Path.Combine(database.Directory, "execution-text");
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query, Limit: 10);
        var expected = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution()).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);

        var beforeRestart = new RuntimeTextProjection(indexDirectory,
            database.Store.Identity.NodeId, UnitTextOptions.Execution());
        var firstResult = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), beforeRestart).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);
        await AssertEquivalentAsync(expected, firstResult);
        await AssertRecognizedGenerationAsync(indexDirectory, database);
        beforeRestart.Dispose();

        using var afterRestart = new RuntimeTextProjection(indexDirectory,
            database.Store.Identity.NodeId, UnitTextOptions.Execution());
        var rebuiltResult = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), afterRestart).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);

        await AssertEquivalentAsync(expected, rebuiltResult);
        await AssertRecognizedGenerationAsync(indexDirectory, database);
    }

    [Test]
    public async Task CorruptManifestIsRejectedAndPreservedForDiagnosis()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexDirectory = Path.Combine(database.Directory, "execution-text");
        using (var projection = new RuntimeTextProjection(indexDirectory,
                   database.Store.Identity.NodeId, UnitTextOptions.Execution()))
        {
            _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
                new(database.Partition, Collection, TextPath, Query));
        }
        var generation = Directory.EnumerateDirectories(indexDirectory)
            .Single(path => Path.GetFileName(path).StartsWith(GenerationPrefix, StringComparison.Ordinal));
        var manifest = Path.Combine(generation, TextProtocol.ManifestFile);
        var original = await File.ReadAllBytesAsync(manifest, cancellationToken);
        var canonical = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var envelope = OrleansSerialization.Deserialize<TextEnvelope>(original);
        envelope.Payload[0] ^= 1;
        var damaged = OrleansSerialization.Serialize(envelope);
        await File.WriteAllBytesAsync(manifest, damaged, cancellationToken);
        var damagedReceipt = await File.ReadAllBytesAsync(manifest, cancellationToken);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenForRestart(indexDirectory, database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(await File.ReadAllBytesAsync(manifest, cancellationToken)).IsEquivalentTo(damagedReceipt,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Directory.Exists(generation)).IsTrue();
        await TextProviderManifestRecovery.VerifyAsync(database, indexDirectory, manifest, original,
            canonical, position, cancellationToken);
    }

    [Test]
    public async Task UnknownProjectionEntryIsRejectedAndPreserved()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var indexDirectory = Path.Combine(database.Directory, "execution-text");
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
    public async Task LinkedEntryIsRejectedWithoutFollowingOrRemovingIt()
    {
        using var database = new TestDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexDirectory = Path.Combine(database.Directory, "execution-text");
        using (var projection = new RuntimeTextProjection(indexDirectory,
                   database.Store.Identity.NodeId, UnitTextOptions.Execution()))
        {
            _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
                new(database.Partition, Collection, TextPath, Query));
        }
        var generation = Directory.EnumerateDirectories(indexDirectory)
            .Single(path => Path.GetFileName(path).StartsWith(GenerationPrefix, StringComparison.Ordinal));
        var execution = Path.Combine(generation, RuntimeDirectoryCore);
        var protectedFile = Path.Combine(database.Directory, "outside-execution.bin");
        var protectedBytes = new byte[] { 0x17, 0x28, 0x39 };
        await File.WriteAllBytesAsync(protectedFile, protectedBytes, cancellationToken);
        var link = Path.Combine(execution, "external.bin");
        File.CreateSymbolicLink(link, protectedFile);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OpenForRestart(indexDirectory, database));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(new FileInfo(link).LinkTarget).IsNotNull();
        await Assert.That(await File.ReadAllBytesAsync(protectedFile, cancellationToken)).IsEquivalentTo(protectedBytes,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task AssertRecognizedGenerationAsync(string indexDirectory, TestDatabase database)
    {
        var rootReceiptPath = Path.Combine(indexDirectory, RootReceipt);
        await Assert.That(File.Exists(rootReceiptPath)).IsTrue();
        var rootReceipt = TextEnvelopeCodec.Decode<TextOwnerReceipt>(
            await File.ReadAllBytesAsync(rootReceiptPath, TestContext.Current!.Execution.CancellationToken));
        await Assert.That(rootReceipt.SourceNodeId).IsEqualTo(database.Store.Identity.NodeId);
        var generation = Directory.EnumerateDirectories(indexDirectory)
            .Single(path => Path.GetFileName(path).StartsWith(GenerationPrefix, StringComparison.Ordinal));
        var ownerPath = Path.Combine(generation, TextProtocol.OwnerFile);
        var owner = TextEnvelopeCodec.Decode<TextOwnerReceipt>(
            await File.ReadAllBytesAsync(ownerPath, TestContext.Current!.Execution.CancellationToken));
        var manifest = TextEnvelopeCodec.Decode<TextManifest>(await File.ReadAllBytesAsync(
            Path.Combine(generation, TextProtocol.ManifestFile), TestContext.Current!.Execution.CancellationToken));
        await Assert.That(owner.GenerationLeaf).IsEqualTo(Path.GetFileName(generation));
        await Assert.That(owner.RootDirectory).IsEqualTo(Path.GetFullPath(indexDirectory));
        await Assert.That(manifest.FormatVersion).IsEqualTo(TextProtocol.FormatVersion);
        await Assert.That(manifest.TokenizerVersion).IsEqualTo(TextProjectionProtocol.TokenizerVersion);
        await Assert.That(manifest.HashVersion).IsEqualTo(TextProjectionProtocol.HashVersion);
        await Assert.That(manifest.Scope.NodeId).IsEqualTo(database.Store.Identity.NodeId);
        await Assert.That(manifest.Scope.Position).IsEqualTo(database.Store.Position);
        await Assert.That(manifest.Scope.Partition).IsEqualTo(database.Partition);
        await Assert.That(manifest.Scope.Collection).IsEqualTo(Collection);
        await Assert.That(manifest.Scope.Field).IsEqualTo(TextPath);
        await Assert.That(manifest.Records.Length).IsEqualTo(2);
        var runtimeFiles = Directory.EnumerateFiles(Path.Combine(generation, RuntimeDirectoryCore), "*",
            SearchOption.AllDirectories).ToArray();
        await Assert.That(runtimeFiles.Length).IsGreaterThan(0);
    }

    private static void OpenForRestart(string indexDirectory, TestDatabase database)
    {
        using var projection = new RuntimeTextProjection(indexDirectory,
            database.Store.Identity.NodeId, UnitTextOptions.Execution());
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
