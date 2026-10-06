using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class ProjectionCrashScenario
{
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
        CrashFixtureValues.Orders, CrashFixtureValues.Partition);
    internal static ProjectionConsumerRef Consumer { get; } = new(Partition, CrashFixtureValues.ProjectionConsumer);
    internal static Guid CommandId { get; } = new(CrashFixtureValues.ProjectionCommandId);

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        const int ExpectedRevisionEmptyCount = 0;
        const int PositionStep = 1;

        var database = CrashDatabase.Create(store, boundOutbox: true);
        Configure(database);
        var batch = database.ReadProjectionBatch(CrashFixtureValues.Principal, new(Consumer));
        var request = new CommitProjectionBatchRequest(CommandId, Consumer, batch.Token,
            [new PutDocument(CrashFixtureValues.Projection, CrashFixtureValues.Effect, CrashFixtureValues.EffectJson, ExpectedRevisionEmptyCount)]);
        var operation = CrashDatabase.Operation(OperationKind.CommitProjectionBatch, request, request.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, CrashFixtureValues.ProcessingCommandFile), JsonDefaults.Serialize(operation));
        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
        database.Apply(operation).Get<ProjectionBatchResult>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static void Configure(DatabaseEngine database)
    {
        const int IndexGenerationSingleItemCount = 1;
        const int ExpectedRevisionEmptyCount = 0;

        foreach (var resource in new[]
        {
            new ResourceDefinition(CrashFixtureValues.Orders, ResourceKind.Collection, CrashFixtureValues.Orders),
            new(CrashFixtureValues.Projection, ResourceKind.Collection, CrashFixtureValues.Orders)
        })
        {
            CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
        var configure = Guid.NewGuid();
        CrashDatabase.Submit(database, OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configure, Consumer, new(IndexGenerationSingleItemCount, [CrashFixtureValues.Orders], [])), configure).Get<ProjectionConsumerInfo>();
        var producer = Guid.NewGuid();
        CrashDatabase.Submit(database, OperationKind.Batch,
            new CommandRequest(producer, Partition, [new PutDocument(CrashFixtureValues.Orders, CrashFixtureValues.Input, CrashFixtureValues.EmptyJson, ExpectedRevisionEmptyCount)]),
            producer).Get<CommitReceipt>();
    }
}
