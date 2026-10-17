using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class RevokedOutcomeAdmissionFixture : IDisposable
{
    internal const string Worker = "retained-admission-worker";
    internal const string Documents = "admission_records";
    internal const string Events = "admission-events";
    internal const string Queue = "admission-queue";
    internal static readonly DateTimeOffset Epoch = new(2042, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness { get; } = new(timeProvider: new ControlledReadClock(Epoch));
    internal ZoneTreeStore Store { get; set; } = null!;
    internal DatabaseEngine Database { get; set; } = null!;
    internal CommandRequest Original { get; private set; } = null!;
    internal OperationResult OriginalResult { get; private set; } = null!;
    internal byte[] OriginalOutcome { get; private set; } = [];
    internal byte[] OriginalLocator { get; private set; } = [];
    internal CommandRequest? Fresh
    {
        get; set;
    }
    internal OperationResult? FreshResult
    {
        get; set;
    }
    internal byte[] FreshOutcome { get; set; } = [];
    internal string Cursor { get; set; } = string.Empty;
    internal long FreshPosition
    {
        get; set;
    }
    internal long OriginalPosition
    {
        get; private set;
    }
    internal void Initialize()
    {
        Store = Harness.Store;
        Database = Harness.Database;
        Harness.Configure(Documents, ResourceKind.Collection);
        Harness.Configure(Events, ResourceKind.StreamSet);
        Harness.Configure(Queue, ResourceKind.WorkQueue);
        Policy(1, false, false);
        Original = new(Guid.NewGuid(), Harness.Partition,
            [new PutDocument(Documents, "a", "{\"value\":1}", 0), new PutDocument(Documents, "b", "{\"value\":2}", 0),
             new AppendEvents(Events, "stream", [new("original", "Created", "{\"event\":1}")], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(Queue, "original", "{\"work\":1}")]);
        OriginalPosition = Store.Position + 1;
        OriginalResult = Database.Apply(Operation(Original));
        OriginalOutcome = ReadOutcome(Original.CommandId);
        OriginalLocator = Store.Read(view => view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(Harness.Partition, Worker, Original.CommandId)))
            ?? throw new InvalidOperationException("The original outcome locator is absent.");
    }
    internal void Policy(long epoch, bool denied, bool revoked)
    {
        var principal = new PrincipalRecord(Worker, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, "*", denied ? Capability.All & ~Capability.DocumentsWrite : Capability.All)], ["*"])
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        };
        Harness.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }
    internal static ReplicatedOperation Operation(CommandRequest command)
        => new(command.CommandId, OperationKind.Batch, Worker, Epoch, JsonSerializer.Serialize(command, JsonDefaults.Options));
    internal CommandRequest Next() => new(Guid.NewGuid(), Harness.Partition,
        [new PutDocument(Documents, "c", "{\"value\":3}", 0),
         new AppendEvents(Events, "stream", [new("healthy", "Created", "{\"event\":2}")], ExpectedStreamRevision.Exact(1)),
         new EnqueueMessage(Queue, "healthy", "{\"work\":2}")]);
    internal byte[] ReadOutcome(Guid id) => Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, Worker, id)))
        ?? throw new InvalidOperationException("The original durable outcome is absent.");
    public void Dispose() => Harness.Dispose();
}
