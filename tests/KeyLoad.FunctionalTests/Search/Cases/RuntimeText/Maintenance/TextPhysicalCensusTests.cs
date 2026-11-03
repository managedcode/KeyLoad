using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextPhysicalCensusTests
{
    private const string Collection = "physical-census";
    private const string Payload = "{\"text\":\"needle\"}";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    [Test]
    public async Task PhysicalCensusJoinsNativeDeletionBeforeHealthyOwnedFilesAndCanonicalSearch()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", Payload));
        var position = database.Store.Position;
        var options = UnitTextOptions.Execution();
        var resources = new TextResourceOwnership(options);
        var root = Path.Combine(database.Directory, Collection);
        var node = database.Store.Identity.NodeId;
        using var projection = new RuntimeTextProjection(root, node, options, resources);
        var engine = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
        var request = new SearchRequest(database.Partition, Collection, "/text", "needle");
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await Assert.That(await engine.SearchAsync("root", request, token)).HasSingleItem();
        var generation = Directory.EnumerateDirectories(root).Single();
        var provider = new TextFileStreamProvider(root, Path.GetFileName(generation), node, options, resources);
        var path = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore, "owned-retiring.bin");
        Write(provider, path, [0x12, 0x34]);
        using var pause = new TextPhysicalCensusPause(root, node, options);
        resources.RegisterRoot(root, pause.Verify);
        await OverlapAsync(() => provider.DeleteFile(path), projection, pause, path, token);
        await Assert.That(File.Exists(path)).IsFalse();
        var closePath = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore, "owned-close.bin");
        using (var stream = provider.CreateFileStream(closePath, FileMode.CreateNew, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete, options: FileOptions.DeleteOnClose))
        {
            stream.ToStream().Write([0xAB, 0xCD]);
            await OverlapAsync(stream.Dispose, projection, pause, closePath, token);
        }
        await Assert.That(File.Exists(closePath)).IsFalse();
        byte[] expected = [0x56, 0x78, 0x90];
        Write(provider, path, expected);
        await Assert.That(provider.ReadAllBytes(path)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        provider.DeleteFile(path);
        await ChurnAsync(provider, root, generation, node, options);
        await RefuseForeignAsync(provider, generation, database.Directory);
        var healthy = await Assert.That(await engine.SearchAsync("root", request, token)).HasSingleItem();
        await Assert.That(healthy.Document.Reference).IsEqualTo(original.Document.Reference);
        await Assert.That(healthy.Document.Revision).IsEqualTo(original.Document.Revision);
        await Assert.That(healthy.Document.Json).IsEqualTo(Payload);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        projection.Dispose();
        using var cold = new RuntimeTextProjection(root, node, options, new TextResourceOwnership(options));
        var reopened = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), cold);
        await RequireCanonicalAsync(database, reopened, request, token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
    }

    private static async Task ChurnAsync(TextFileStreamProvider provider, string root, string generation,
        Guid node, IOptions<TextExecutionOptions> options)
    {
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        for (var index = 0; index <= options.Value.MaximumFiles; index++)
        {
            var path = Path.Combine(execution, "retired-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Write(provider, path, [0x45]);
            provider.DeleteFile(path);
        }
        var healthy = Path.Combine(execution, "current-ledger.bin");
        Write(provider, healthy, [0x67, 0x89]);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(generation, TextProtocol.OwnerFile),
            root, Path.GetFileName(generation), node, options);
        await Assert.That(owner.OwnedPaths.Count(path => !path.IsDirectory)).IsEqualTo(
            Directory.EnumerateFiles(execution, "*", SearchOption.AllDirectories).Count());
        await Assert.That(owner.OwnedPaths.Any(path => Path.GetFileName(path.RelativePath)
            .StartsWith("retired-", StringComparison.Ordinal))).IsFalse();
        var ownerPath = Path.Combine(generation, TextProtocol.OwnerFile);
        var receipt = await File.ReadAllBytesAsync(ownerPath, TestContext.Current!.Execution.CancellationToken);
        provider.DeleteFile(Path.Combine(execution, "retired-0"));
        await Assert.That(await File.ReadAllBytesAsync(ownerPath, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(receipt, CollectionOrdering.Matching);
        await Assert.That(provider.ReadAllBytes(healthy)).IsEquivalentTo(new byte[] { 0x67, 0x89 }, CollectionOrdering.Matching);
        provider.DeleteFile(healthy);
    }

    private static async Task RefuseForeignAsync(TextFileStreamProvider provider, string generation, string directory)
    {
        var ownerPath = Path.Combine(generation, TextProtocol.OwnerFile);
        var original = await File.ReadAllBytesAsync(ownerPath, TestContext.Current!.Execution.CancellationToken);
        var foreign = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore, "unowned-or-dangling.bin");
        foreach (var link in new[] { false, true })
        {
            if (link)
            {
                File.CreateSymbolicLink(foreign, Path.Combine(directory, "absent-link-target"));
            }
            else
            {
                File.WriteAllBytes(foreign, [0xAB]);
            }
            try
            {
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => provider.CreateFileStream(foreign,
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
                await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => provider.DeleteFile(foreign)).Code)
                    .IsEqualTo(ErrorCode.FormatUnsupported);
                await Assert.That(await File.ReadAllBytesAsync(ownerPath, TestContext.Current!.Execution.CancellationToken))
                    .IsEquivalentTo(original, CollectionOrdering.Matching);
            }
            finally { File.Delete(foreign); }
        }
    }

    private static async Task RequireCanonicalAsync(TestDatabase database, SearchEngine engine,
        SearchRequest request, CancellationToken token)
    {
        RankedDocument[] expected = [new(new(database.ExpectedReference(new(database.Partition, Collection, "one")),
            1, Payload, false, []), 1d / 61d)];
        await Assert.That(JsonDefaults.Serialize(await engine.SearchAsync("root", request, token)))
            .IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
    }

    private static async Task OverlapAsync(Action mutation, RuntimeTextProjection projection,
        TextPhysicalCensusPause pause, string path, CancellationToken token)
    {
        pause.Arm();
        var census = Task.Run(() => projection.CheckPhysical(new ReadExecutionBudget(TimeProvider.System, token)),
            CancellationToken.None);
        var deletion = Task.CompletedTask;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                pause.WaitUntilEntered(token);
                deletion = Task.Run(() =>
                {
                    started.SetResult();
                    mutation();
                }, CancellationToken.None);
                await started.Task.WaitAsync(Wait, TimeProvider.System, token);
                await Assert.That(deletion.IsCompleted).IsFalse();
                await Assert.That(File.Exists(path)).IsTrue();
            }, failures);
        }
        finally
        {
            pause.Resume();
            await ServerFailureObserver.ObserveAsync(() => Task.WhenAll(census, deletion)
                .WaitAsync(Wait, TimeProvider.System), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Write(TextFileStreamProvider provider, string path, byte[] bytes)
    {
        using var stream = provider.CreateFileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.ToStream().Write(bytes);
    }
}
