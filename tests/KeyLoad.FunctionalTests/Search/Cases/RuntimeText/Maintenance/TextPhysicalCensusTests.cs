using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Server.Search;
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
        var healthy = await Assert.That(await engine.SearchAsync("root", request, token)).HasSingleItem();
        await Assert.That(healthy.Document.Reference).IsEqualTo(original.Document.Reference);
        await Assert.That(healthy.Document.Revision).IsEqualTo(original.Document.Revision);
        await Assert.That(healthy.Document.Json).IsEqualTo(Payload);
        await Assert.That(database.Store.Position).IsEqualTo(position);
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
