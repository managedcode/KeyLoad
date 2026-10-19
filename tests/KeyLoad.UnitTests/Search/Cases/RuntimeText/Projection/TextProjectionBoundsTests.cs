using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextProjectionBoundsTests
{
    private const string Collection = "execution-text-bounds";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string ExactDiskFile = "bound-disk.bin";
    private const long ExactPostingBytes = 75;

    [Test]
    public async Task FileCountAcceptsExactBoundAndRejectsExcessWithoutRemovingFiles()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexRoot = await CreateGenerationAsync(database);
        var generation = FindGeneration(indexRoot);
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var executionOptions = UnitTextOptions.Execution();
        var inventory = TextFileIO.MeasureRegularFiles(generation, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, executionOptions);
        await Assert.That(inventory.Files).IsLessThan(executionOptions.Value.MaximumFiles);
        var addedFiles = await TextBoundedFilePreparation.AddFilesAsync(execution, inventory.Files, executionOptions.Value.MaximumFiles,
            TestContext.Current!.Execution.CancellationToken);

        TextFiles.CheckGenerationBound(generation, executionOptions);
        var manifestPath = Path.Combine(generation, TextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken);
        var excess = Path.Combine(execution, TextBoundedFilePreparation.BoundFilePrefix + "excess");
        await File.WriteAllBytesAsync(excess, [], TestContext.Current!.Execution.CancellationToken);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => TextFiles.CheckGenerationBound(generation, executionOptions));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(manifestBefore,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(addedFiles.All(File.Exists)).IsTrue();
        await Assert.That(File.Exists(excess)).IsTrue();
    }

    [Test]
    public async Task DiskLimitAcceptsExactSparseLengthAndRejectsOneByteExcess()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexRoot = await CreateGenerationAsync(database);
        var generation = FindGeneration(indexRoot);
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var executionOptions = UnitTextOptions.Execution();
        var inventory = TextFileIO.MeasureRegularFiles(generation, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, executionOptions);
        var remainingBytes = executionOptions.Value.MaximumDiskBytes - inventory.Bytes;
        await Assert.That(remainingBytes > 0).IsTrue();
        var sparseFile = Path.Combine(execution, ExactDiskFile);
        TextBoundedFilePreparation.SetSparseLength(sparseFile, remainingBytes);

        TextFiles.CheckGenerationBound(generation, executionOptions);
        var manifestPath = Path.Combine(generation, TextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken);
        TextBoundedFilePreparation.SetSparseLength(sparseFile, remainingBytes + 1);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => TextFiles.CheckGenerationBound(generation, executionOptions));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(new FileInfo(sparseFile).Length).IsEqualTo(remainingBytes + 1);
        await Assert.That(await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(manifestBefore,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ManifestMetadataAcceptsExactByteLimitAndRejectsOneByteLess()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var generation = FindGeneration(await CreateGenerationAsync(database));
        var path = Path.Combine(generation, TextProtocol.ManifestFile);
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken);
        var manifest = TextEnvelopeCodec.Decode<TextManifest>(bytes);
        var exact = TextFiles.ReadManifest(path, manifest.Scope,
            new()
            {
                MaxQueryReadBytes = bytes.LongLength
            }, UnitTextOptions.Execution());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            TextFiles.ReadManifest(path, manifest.Scope,
                new()
                {
                    MaxQueryReadBytes = bytes.LongLength - 1
                }, UnitTextOptions.Execution()));

        await Assert.That(exact.Records.Length).IsEqualTo(1);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(bytes,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task PostingReadBudgetAcceptsExactPostingWorkAndRejectsOneByteLess()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"),
            new PutDocument(Collection, "two", "{\"text\":\"needle\"}"));
        using var projection = new RuntimeTextProjection(Path.Combine(database.Directory, "execution-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitTextOptions.Execution());
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query, Limit: 10);
        _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request);
        var scope = CaptureScope(database);
        var documents = ReadCanonicalDocuments(database);
        VerifyPostingWork(projection, scope, documents, ExactPostingBytes);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            VerifyPostingWork(projection, scope, documents, ExactPostingBytes - 1));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var following = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root", request,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(following.Length).IsEqualTo(2);
    }

    [Test]
    public async Task RecordMetadataReadBudgetAcceptsExactBytesAndRejectsOneByteLess()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"),
            new PutDocument(Collection, "two", "{\"text\":\"needle\"}"));
        var scope = CaptureScope(database);
        var documents = ReadCanonicalDocuments(database);
        var exactBytes = documents.Select((document, index) => (long)OrleansSerialization.Measure(
            new TextRecord((ulong)index + 1, document.Reference, document.Revision))).Sum();
        using (var exactProjection = new RuntimeTextProjection(Path.Combine(database.Directory, "exact-execution-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitTextOptions.Execution()))
        {
            using var exactLease = exactProjection.Acquire(scope, new(UnitExecutionOptions.DatabaseLimits(new()
            {
                MaxQueryReadBytes = exactBytes
            })));
            BeginAllRecords(exactLease, documents);
        }
        using var boundedProjection = new RuntimeTextProjection(Path.Combine(database.Directory, "bounded-execution-text"), UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitTextOptions.Execution());
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryReadBytes = exactBytes - 1
        }));
        using var boundedLease = boundedProjection.Acquire(scope, budget);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => BeginAllRecords(boundedLease, documents));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsGreaterThan(0L);
    }

    private static async Task<string> CreateGenerationAsync(TestDatabase database)
    {
        var root = Path.Combine(database.Directory, "execution-text");
        using var projection = new RuntimeTextProjection(root, UnitExecutionOptions.DatabaseLimits(database.Database.Limits), database.Store.Identity.NodeId, UnitTextOptions.Execution());
        _ = await new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection).SearchAsync("root",
            new(database.Partition, Collection, TextPath, Query));
        return root;
    }

    private static string FindGeneration(string root)
        => Directory.EnumerateDirectories(root).Single(path => Path.GetFileName(path)
            .StartsWith(TextProtocol.GenerationPrefix, StringComparison.Ordinal));

    private static TextProjectionScope CaptureScope(TestDatabase database)
    {
        var identity = database.Store.Identity;
        var principal = database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal("root")))!;
        var resource = database.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            database.Partition.TenantId, database.Partition.DatabaseId, Collection)))!;
        return new(identity.NodeId, identity.Incarnation, identity.FormatVersion, identity.ReadGeneration,
            database.Store.Position, database.Partition, Collection, TextPath, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion);
    }

    private static DocumentRecord[] ReadCanonicalDocuments(TestDatabase database)
        => database.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(database.Partition, Collection),
                database.Database.Limits.MaxScanRecords).Records
            .Select(record => OrleansSerialization.Deserialize<DocumentRecord>(record.Value.Span)).ToArray());

    private static void VerifyPostingWork(RuntimeTextProjection projection, TextProjectionScope scope,
        DocumentRecord[] documents, long maximumBytes)
    {
        var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()
        {
            MaxQueryReadBytes = maximumBytes
        }));
        using var lease = projection.Acquire(scope, budget);
        foreach (var document in documents)
        {
            lease.BeginRecord(document.Reference, document.Revision);
            lease.ObserveToken(Query);
        }
        lease.VerifyCandidates([Query], documents.Select(document => document.Reference).ToArray(), budget);
    }

    private static void BeginAllRecords(ITextProjectionLease lease, DocumentRecord[] documents)
    {
        foreach (var document in documents)
        {
            lease.BeginRecord(document.Reference, document.Revision);
        }
    }
}
