using System.Globalization;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionBoundsTests
{
    private const string Collection = "native-text-bounds";
    private const string TextPath = "/text";
    private const string Query = "needle";
    private const string BoundFilePrefix = "bound-file-";
    private const string ExactDiskFile = "bound-disk.bin";
    private const long ExactPostingBytes = 75;

    [Test]
    public async Task NativeFileCountAcceptsExactBoundAndRejectsExcessWithoutRemovingFiles()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexRoot = CreateNativeGeneration(database);
        var generation = FindGeneration(indexRoot);
        var native = Path.Combine(generation, NativeTextProtocol.NativeDirectory);
        var inventory = NativeTextFileIO.MeasureRegularFiles(generation, NativeTextProtocol.MaximumFiles,
            NativeTextProtocol.MaximumDiskBytes);
        await Assert.That(inventory.Files).IsLessThan(NativeTextProtocol.MaximumFiles);
        var addedFiles = await AddFilesAsync(native, inventory.Files, NativeTextProtocol.MaximumFiles,
            TestContext.Current!.Execution.CancellationToken);

        NativeTextFiles.CheckGenerationBound(generation);
        var manifestPath = Path.Combine(generation, NativeTextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken);
        var excess = Path.Combine(native, BoundFilePrefix + "excess");
        await File.WriteAllBytesAsync(excess, [], TestContext.Current!.Execution.CancellationToken);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeTextFiles.CheckGenerationBound(generation));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(manifestBefore,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(addedFiles.All(File.Exists)).IsTrue();
        await Assert.That(File.Exists(excess)).IsTrue();
    }

    [Test]
    public async Task NativeDiskLimitAcceptsExactSparseLengthAndRejectsOneByteExcess()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"needle\"}"));
        var indexRoot = CreateNativeGeneration(database);
        var generation = FindGeneration(indexRoot);
        var native = Path.Combine(generation, NativeTextProtocol.NativeDirectory);
        var inventory = NativeTextFileIO.MeasureRegularFiles(generation, NativeTextProtocol.MaximumFiles,
            NativeTextProtocol.MaximumDiskBytes);
        var remainingBytes = NativeTextProtocol.MaximumDiskBytes - inventory.Bytes;
        await Assert.That(remainingBytes > 0).IsTrue();
        var sparseFile = Path.Combine(native, ExactDiskFile);
        SetSparseLength(sparseFile, remainingBytes);

        NativeTextFiles.CheckGenerationBound(generation);
        var manifestPath = Path.Combine(generation, NativeTextProtocol.ManifestFile);
        var manifestBefore = await File.ReadAllBytesAsync(manifestPath, TestContext.Current!.Execution.CancellationToken);
        SetSparseLength(sparseFile, remainingBytes + 1);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeTextFiles.CheckGenerationBound(generation));

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
        var generation = FindGeneration(CreateNativeGeneration(database));
        var path = Path.Combine(generation, NativeTextProtocol.ManifestFile);
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken);
        var manifest = NativeTextEnvelopeCodec.Decode<NativeTextManifest>(bytes);
        var exact = NativeTextFiles.ReadManifest(path, manifest.Scope,
            new() { MaxQueryReadBytes = bytes.LongLength });
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            NativeTextFiles.ReadManifest(path, manifest.Scope,
                new() { MaxQueryReadBytes = bytes.LongLength - 1 }));

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
        using var projection = new NativeTextProjection(Path.Combine(database.Directory, "native-text"),
            database.Database.Limits, database.Store.Identity.NodeId);
        var request = new SearchRequest(database.Partition, Collection, TextPath, Query, Limit: 10);
        _ = new SearchEngine(database.Database, projection).Search("root", request);
        var scope = CaptureScope(database);
        var documents = ReadCanonicalDocuments(database);
        VerifyPostingWork(projection, scope, documents, ExactPostingBytes);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            VerifyPostingWork(projection, scope, documents, ExactPostingBytes - 1));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var following = new SearchEngine(database.Database, projection).Search("root", request,
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
        var exactBytes = documents.Select((document, index) => (long)NativeSerialization.Measure(
            new NativeTextRecord((ulong)index + 1, document.Reference, document.Revision))).Sum();
        using (var exactProjection = new NativeTextProjection(Path.Combine(database.Directory, "exact-native-text"),
                   database.Database.Limits, database.Store.Identity.NodeId))
        {
            using var exactLease = exactProjection.Acquire(scope, new(new() { MaxQueryReadBytes = exactBytes }));
            BeginAllRecords(exactLease, documents);
        }
        using var boundedProjection = new NativeTextProjection(Path.Combine(database.Directory, "bounded-native-text"),
            database.Database.Limits, database.Store.Identity.NodeId);
        var budget = new ReadExecutionBudget(new() { MaxQueryReadBytes = exactBytes - 1 });
        using var boundedLease = boundedProjection.Acquire(scope, budget);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => BeginAllRecords(boundedLease, documents));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(database.Store.Position).IsGreaterThan(0L);
    }

    private static string CreateNativeGeneration(TestDatabase database)
    {
        var root = Path.Combine(database.Directory, "native-text");
        using var projection = new NativeTextProjection(root, database.Database.Limits, database.Store.Identity.NodeId);
        _ = new SearchEngine(database.Database, projection).Search("root",
            new(database.Partition, Collection, TextPath, Query));
        return root;
    }

    private static string FindGeneration(string root)
        => Directory.EnumerateDirectories(root).Single(path => Path.GetFileName(path)
            .StartsWith(NativeTextProtocol.GenerationPrefix, StringComparison.Ordinal));

    private static async Task<string[]> AddFilesAsync(string directory, int existingCount, int totalCount,
        CancellationToken cancellationToken)
    {
        var added = new string[totalCount - existingCount];
        for (var index = existingCount; index < totalCount; index++)
        {
            var path = Path.Combine(directory, BoundFilePrefix + index.ToString("D3", CultureInfo.InvariantCulture));
            await File.WriteAllBytesAsync(path, [], cancellationToken);
            added[index - existingCount] = path;
        }
        return added;
    }

    private static void SetSparseLength(string path, long length)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.SetLength(length);
    }

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
            .Select(record => NativeSerialization.Deserialize<DocumentRecord>(record.Value.Span)).ToArray());

    private static void VerifyPostingWork(NativeTextProjection projection, TextProjectionScope scope,
        DocumentRecord[] documents, long maximumBytes)
    {
        var budget = new ReadExecutionBudget(new() { MaxQueryReadBytes = maximumBytes });
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
