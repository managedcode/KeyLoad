using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SubscriptionCrashScenario
{
    internal static PartitionRef Partition { get; } = new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
        CrashFixtureValues.Orders, CrashFixtureValues.Partition);
    internal static SubscriptionRef Subscription { get; } = new(new(Partition, CrashFixtureValues.Topic, EventSourceKind.Topic), CrashFixtureValues.Group);
    internal static Guid CommandId { get; } = new(CrashFixtureValues.SubscriptionCommandId);

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        const int ExecutionGenerationSingleItemCount = 1;
        const int ExpectedRevisionEmptyCount = 0;
        const int PositionStep = 1;

        var database = CrashDatabase.Create(store);
        Configure(database);
        var receive = Guid.NewGuid();
        var delivery = CrashDatabase.Submit(database, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(receive, Subscription, LeaseSeconds: CrashFixtureValues.LeaseSeconds), receive)
            .Get<ReceiveSubscriptionResult>().Deliveries.Single();
        var request = new SubscriptionProcessingRequest(CommandId, Subscription, delivery.Token, CrashFixtureValues.Handler, ExecutionGenerationSingleItemCount,
            [new PutDocument(CrashFixtureValues.Orders, CrashFixtureValues.Effect, CrashFixtureValues.EffectJson, ExpectedRevisionEmptyCount)]);
        var operation = CrashDatabase.Operation(OperationKind.SubscriptionProcessing, request, request.CommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, CrashFixtureValues.ProcessingCommandFile), JsonDefaults.Serialize(operation));
        boundary.Position = store.Position + PositionStep;
        boundary.Armed = true;
        database.Apply(operation).Get<SubscriptionProcessingResult>();
        await CrashHostPause.WaitForKillAsync();
    }

    private static void Configure(DatabaseEngine database)
    {
        foreach (var resource in new[]
        {
            new ResourceDefinition(CrashFixtureValues.Orders, ResourceKind.Collection, CrashFixtureValues.Orders),
            new(CrashFixtureValues.Topic, ResourceKind.Topic, CrashFixtureValues.Orders)
        })
        {
            CrashDatabase.Submit(database, OperationKind.ConfigureResource,
                new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, resource), Guid.NewGuid()).Get<ResourceDefinition>();
        }
        var configure = Guid.NewGuid();
        CrashDatabase.Submit(database, OperationKind.ConfigureSubscription,
            new ConfigureSubscriptionRequest(configure, Subscription, new(CrashFixtureValues.Principal)), configure).Get<SubscriptionInfo>();
        var publish = Guid.NewGuid();
        CrashDatabase.Submit(database, OperationKind.Batch,
            new CommandRequest(publish, Partition,
                [new PublishTopic(CrashFixtureValues.Topic, [new(CrashFixtureValues.Input, CrashFixtureValues.EventType, CrashFixtureValues.EmptyJson)])]),
            publish).Get<CommitReceipt>();
    }
}
