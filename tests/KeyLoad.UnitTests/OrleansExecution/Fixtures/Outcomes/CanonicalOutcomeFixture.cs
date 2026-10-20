using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class CanonicalOutcomeFixture
{
    internal const string ResourceName = "canonical-outcome-documents";
    internal const string AdminId = "root";
    internal const string SuccessMessage = "The original canonical outcome is absent.";
    internal TestDatabase Source { get; } = new();
    internal ZoneTreeStore Store => Source.Store;
    internal DatabaseEngine Database => Source.Database;
    internal PartitionRef Partition => Source.Partition;
    internal StoreIdentity Identity => Store.Identity;
    internal Guid Incarnation => Identity.Incarnation;
    internal string DirectoryPath => Source.Directory;
    internal Guid CommandId { get; } = Guid.NewGuid();

    internal static async Task RunOwnedAsync(Func<CanonicalOutcomeFixture, Task> body)
    {
        CanonicalOutcomeFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            fixture.Source.Configure(ResourceName, ResourceKind.Collection);
            fixture.CommitBatch(fixture.Partition, fixture.CommandId, "inspection-outcome-seed");
            await body(fixture);
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Source.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void CommitBatch(PartitionRef partition, Guid id, string document)
        => Source.Submit(OperationKind.Batch, new CommandRequest(id, partition,
            [new PutDocument(ResourceName, document, "{\"seed\":true}", 0)]), id: id).Get<CommitReceipt>();

    internal void CloseStore() => Source.JoinOwnedResources();

    internal static string[] Image(IAtomicStore store) => store.Read(view =>
    {
        var page = view.Scan([], 4096);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The canonical outcome fixture image exceeded its bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    internal async Task AssertUnrelatedCurrentOperationAsync(byte[] original, byte[] locator)
    {
        using var reopened = ZoneTreeExistingStore.Open(new(DirectoryPath) { Incarnation = Identity.Incarnation },
            Identity.NodeId, Source.StorageExecution);
        var database = Source.CreateConfiguredDatabase(reopened);
        var id = Guid.NewGuid();
        _ = database.Apply(database.CreateCommand(OperationKind.Batch, id, AdminId, database.EvaluationClock.GetUtcNow(),
            OrleansSerialization.Serialize(new CommandRequest(id, Partition,
                [new PutDocument(ResourceName, "healthy-follow-up", "{}", 0)])))).Get<CommitReceipt>();
        await Assert.That(database.GetDocument(AdminId, new(Partition, ResourceName, "healthy-follow-up"))!.Json).IsEqualTo("{}");
        var retained = reopened.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(Partition, AdminId, CommandId)));
        var link = reopened.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocator(Partition, AdminId, CommandId)));
        await Assert.That(retained!.AsSpan().SequenceEqual(original)).IsTrue();
        await Assert.That(link!.AsSpan().SequenceEqual(locator)).IsTrue();
    }
}
