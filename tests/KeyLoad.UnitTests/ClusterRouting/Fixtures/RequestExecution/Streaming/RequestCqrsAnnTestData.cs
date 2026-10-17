using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnTestData
{
    internal const string Principal = "root";
    internal const string Collection = "cqrs-ann-vectors";
    internal const string Field = "/embedding";
    internal const string First = "a";
    internal const string Second = "b";
    internal const string FirstJson = "{\"value\":1}";
    internal const string ChangedJson = "{\"value\":2}";
    internal const long OriginalRevision = 1;
    internal const long ChangedRevision = 2;
    internal const long Generation = 1;
    internal const int Version = 1;
    internal const long OriginalTail = 4;
    internal const long ChangedTail = 7;
    internal const int OriginalCount = 2;
    internal const int ChangedCount = 1;
    internal const int OneSettlement = 1;
    internal const int FirstOffset = 0;
    internal const int OrdinaryFrameCount = 2;
    internal const string PutDocumentKind = "putDocument";
    internal const string PutVectorKind = "putVector";
    internal const string DeleteDocumentKind = "deleteDocument";
    internal const int EmptyCount = 0;
    internal const int OneStep = 1;
    internal const double FirstScore = 1d / 61d;
    internal const double SecondScore = 1d / 62d;
    internal const string Consumer = "cqrs-ann-consumer";
    internal static readonly VectorSpace Space = new("cqrs-ann-space", 3, DistanceMetric.Cosine, "cqrs-ann-model", "v1");
    internal static readonly float[] Query = [1, 0, 0];
    internal static readonly AnnMaintenancePhase[] Phases = [AnnMaintenancePhase.Configure, AnnMaintenancePhase.Capture,
        AnnMaintenancePhase.Verify, AnnMaintenancePhase.NativeIndex, AnnMaintenancePhase.Publish,
        AnnMaintenancePhase.Checkpoint, AnnMaintenancePhase.Completed];

    internal static async Task<AnnMaintenanceRequest> SeedAsync(RequestCqrsHostedRuntimeFixture fixture, CancellationToken token)
    {
        var database = fixture.Database;
        database.Configure(Collection, ResourceKind.Collection);
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition,
            [new PutDocument(Collection, First, FirstJson), new PutVector(Collection, First, Field, [1, 0, 0], Space, OriginalRevision),
             new PutDocument(Collection, Second, FirstJson), new PutVector(Collection, Second, Field, [0, 1, 0], Space, OriginalRevision)]);
        var journal = database.ReadJournalCut();
        var receipt = await RequestCqrsHostedMaintenanceData.ExecuteAsync<CommitReceipt>(database, OperationKind.Batch, command, id, token);
        await RequestCqrsAnnAssertions.SeedReceiptAsync(database, command, receipt, journal.LastIndex + OneStep);
        var placement = database.Database.ReadAtomicPartitionPlacement(Principal, new(Version, database.Partition));
        return new(Guid.NewGuid(), new(database.Partition, Consumer), Collection, Field, Space, Generation,
            database.Store.Identity.NodeId, new(placement.PhysicalShardId, placement.Incarnation,
                placement.VoterIds, placement.PlacementEpoch), AnnMaintenanceMode.Build)
        {
            MaintenanceProfile = new(Collection, Field, Space)
        };
    }

    internal static async Task ChangeAsync(RequestCqrsHostedRuntimeFixture fixture, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, fixture.Database.Partition,
            [new PutDocument(Collection, First, ChangedJson, OriginalRevision, ExplicitReplacement: true),
             new PutVector(Collection, First, Field, [0, 0, 1], Space, ChangedRevision),
             new DeleteDocument(Collection, Second, OriginalRevision)]);
        var journal = fixture.Database.ReadJournalCut();
        var receipt = await RequestCqrsHostedMaintenanceData.ExecuteAsync<CommitReceipt>(fixture.Database,
            OperationKind.Batch, command, id, token);
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        MutationReceipt[] expected = [new(PutDocumentKind, Collection, First, ChangedRevision),
            new(PutVectorKind, Collection, First, ChangedRevision), new(DeleteDocumentKind, Collection, Second, ChangedRevision)];
        await RequestCqrsAnnAssertions.ReceiptAsync(fixture.Database, command, receipt, journal.LastIndex + OneStep, expected);
    }

    internal static ImmutableArray<VectorRecord> Records(bool changed) => changed
        ? [new(First, Field, Space, [0, 0, 1], ChangedRevision)]
        : [new(First, Field, Space, [1, 0, 0], OriginalRevision), new(Second, Field, Space, [0, 1, 0], OriginalRevision)];
}
