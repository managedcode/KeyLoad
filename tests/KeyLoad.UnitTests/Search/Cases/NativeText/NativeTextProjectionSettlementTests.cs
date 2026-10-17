using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProjectionSettlementTests
{
    private const string Collection = "native-text-settlement";
    private const string TextPath = "/text";

    [Test]
    public async Task CancellationAndUntrackedNativeFileAreBothRetainedDuringSettlement()
    {
        using var database = new TestDatabase();
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "one", "{\"text\":\"settlement\"}"));
        var nodeId = database.Store.Identity.NodeId;
        var scope = CaptureScope(database);
        var root = NativeTextFiles.InitializeRoot(Path.Combine(database.Directory, "native-text-settlement"),
            nodeId, database.Database.Limits, UnitNativeTextOptions.Execution());
        var leaf = NativeTextValidation.GenerationLeaf();
        NativeTextFiles.WriteOwner(root, leaf, nodeId, scope, database.Database.Limits, UnitNativeTextOptions.Execution());
        NativeTextGeneration? generation = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            generation = new NativeTextGeneration(root, leaf, nodeId, scope, UnitExecutionOptions.DatabaseLimits(database.Database.Limits),
                new NativeTextFileStreamProvider(root, leaf, nodeId, UnitNativeTextOptions.Execution()), UnitNativeTextOptions.Execution());
            var foreignPath = Path.Combine(generation.Path, NativeTextProtocol.NativeDirectory, "foreign-entry.bin");
            var foreignBytes = new byte[] { 0x4b, 0x4c, 0x01, 0xff };
            await File.WriteAllBytesAsync(foreignPath, foreignBytes, TestContext.Current!.Execution.CancellationToken);
            var sourcePosition = database.Store.Position;
            var sourceBytes = ReadCanonicalDocument(database);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits), cancellationToken: canceled.Token);

            var failure = Assert.ThrowsExactly<AggregateException>(() => generation.CloseAndCapture(budget));
            var cancellationRetained = failure.InnerExceptions.Any(exception => exception is OperationCanceledException);
            var ownershipFailures = failure.InnerExceptions.OfType<KeyLoadException>().ToArray();
            var sourceAfter = ReadCanonicalDocument(database);

            await AssertRetainedFailureAsync(database, generation, foreignPath, foreignBytes, sourcePosition,
                sourceBytes, sourceAfter, cancellationRetained, ownershipFailures);
        }, failures);
        ServerFailureObserver.Observe(() => generation?.DisposeIndex(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertRetainedFailureAsync(TestDatabase database, NativeTextGeneration generation,
        string foreignPath, byte[] foreignBytes, long sourcePosition, KeyValueRecord sourceBytes,
        KeyValueRecord sourceAfter, bool cancellationRetained, KeyLoadException[] ownershipFailures)
    {
        await Assert.That(cancellationRetained).IsTrue();
        await Assert.That(ownershipFailures.Length).IsEqualTo(1);
        await Assert.That(ownershipFailures[0].Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(await File.ReadAllBytesAsync(foreignPath, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(foreignBytes,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(sourceAfter.Key.ToArray()).IsEquivalentTo(sourceBytes.Key.ToArray(),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(sourceAfter.Value.ToArray()).IsEquivalentTo(sourceBytes.Value.ToArray(),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(generation.CurrentIndex).IsNull();
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

    private static KeyValueRecord ReadCanonicalDocument(TestDatabase database)
        => database.Store.Read(view => view.Scan(DocumentStorageKeys.Prefix(database.Partition, Collection), 2)
            .Records.Single());
}
