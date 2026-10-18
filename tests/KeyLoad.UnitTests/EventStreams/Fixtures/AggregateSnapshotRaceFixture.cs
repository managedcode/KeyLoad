using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateSnapshotRaceFixture : IDisposable
{
    internal const string StreamSet = "snapshot-race-events";
    internal const string StreamId = "counter";
    internal const string Worker = "snapshot-race-worker";
    internal const string Reducer = "counter.reducer.v1";
    internal const string PayloadRead = "race.payload.read";
    internal const string PayloadUse = "race.payload.use";
    internal const string HeaderRead = "race.header.read";
    internal const string HeaderUse = "race.header.use";
    internal static readonly DateTimeOffset Epoch = new(2041, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness { get; } = new(timeProvider: new ControlledReadClock(Epoch));
    internal ZoneTreeStore Store { get; set; } = null!;
    internal DatabaseEngine Database { get; set; } = null!;
    internal Result<object>? HealthyResult
    {
        get; set;
    }
    internal byte[] HealthyRaw { get; set; } = [];
    internal StreamRef Stream => new(Harness.Partition, StreamSet, StreamId);
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Raw)> Outcomes { get; } = [];
    internal Command<DatabaseCommand> SeedOperation { get; private set; } = null!;
    internal Result<object> SeedResult { get; private set; }
    internal byte[] SeedRaw { get; private set; } = [];
    internal long SeedPosition
    {
        get; private set;
    }
    internal CommandRequest[] Commands { get; private set; } = [];
    internal CommandRequest? Winner
    {
        get; set;
    }
    internal CommandRequest? Healthy
    {
        get; set;
    }
    internal string[] ColdImage { get; set; } = [];
    internal StoreIdentity ColdIdentity { get; set; } = null!;
    internal long ColdPosition
    {
        get; set;
    }
    internal string[] OriginalEvents { get; private set; } = [];
    internal void Initialize()
    {
        Store = Harness.Store;
        Database = Harness.Database;
        var resource = new ResourceDefinition(StreamSet, ResourceKind.StreamSet, Harness.Partition.TransactionDomainId)
        {
            FieldPolicies = [new("/secret", "race-payload", PayloadRead, PayloadUse, RequiredForProcessing: false)],
            HeaderPolicies = [new("/privateHeader", "race-header", HeaderRead, HeaderUse, RequiredForProcessing: false)]
        };
        Harness.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(Harness.Partition.TenantId,
            Harness.Partition.DatabaseId, resource)).Get<ResourceDefinition>();
        Policy(1, true);
        var seed = new CommandRequest(Guid.NewGuid(), Harness.Partition, [new AppendEvents(StreamSet, StreamId,
            [Data("first", 1), Data("second", 2), Data("third", 3)], ExpectedStreamRevision.NoStream)]);
        SeedOperation = Database.CreateCommand(OperationKind.Batch, seed.CommandId, "root", Epoch, JsonSerializer.Serialize(seed, JsonDefaults.Options));
        SeedPosition = Database.LastApplied + 1;
        SeedResult = Database.Apply(SeedOperation);
        SeedRaw = Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, "root", seed.CommandId)))
            ?? throw new InvalidOperationException("The literal seed outcome is absent.");
        Commands = [Snapshot(1, "{ \"total\" : 1 }", 0), Snapshot(2, "{\"total\":3}", 0)];
        OriginalEvents = EventImage();
    }
    internal void Policy(long epoch, bool allowed)
        => Harness.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new PrincipalRecord(Worker, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, StreamSet, Capability.EventsRead | Capability.EventsReplay | Capability.EventsSnapshotsManage)],
            allowed ? [PayloadRead, PayloadUse, HeaderRead, HeaderUse] : [PayloadRead, HeaderRead, HeaderUse])
        {
            PolicyEpoch = epoch
        })).Get<PrincipalRecord>();
    internal CommandRequest Snapshot(long source, string state, long version)
        => new(Guid.NewGuid(), Harness.Partition, [new StoreAggregateSnapshot(StreamSet, StreamId,
            source, Reducer, 1, state, version)]);
    internal Command<DatabaseCommand> Operation(CommandRequest command)
        => Database.CreateCommand(OperationKind.Batch, command.CommandId, Worker, Epoch, JsonSerializer.Serialize(command, JsonDefaults.Options));
    internal string[] EventImage() => Store.Read(view =>
        view.Scan(KeySpace.Partition("event", Harness.Partition, StreamSet, StreamId, 1L), 4).Records
            .Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray());
    internal AggregateReplayPage Read(bool beginning = false)
        => Database.ReadAggregateReplay(Worker, new(Stream, Reducer, 1, beginning, 3));
    private static EventData Data(string id, int amount) => new(id, "CounterChanged",
        "{\"increment\":" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"secret\":\"PRIVATE-RACE-PAYLOAD\"}",
        "{\"privateHeader\":\"PRIVATE-RACE-HEADER\"}");
    public void Dispose() => Harness.Dispose();
}
