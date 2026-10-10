using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class SubscriptionFilterCrashScenario
{
    internal const string Mode = "subscription-filter-cas";
    internal const string OperationFile = "filter-command.json";
    internal const string OldTokenFile = "filter-old-token.json";
    private const string FirstEvent = "one";
    private const string SecondEvent = "two";
    private const string ThirdEvent = "three";
    internal const string ReplacementType = "Changed";
    internal const long OriginalGeneration = 1;
    internal const int WindowSize = 3;
    private const int UnacknowledgedDeliveryIndex = 1;
    internal static SubscriptionRef Subscription => SubscriptionCrashScenario.Subscription;
    internal static Guid CommandId => SubscriptionCrashScenario.CommandId;

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary)
    {
        var db = CrashDatabase.Create(store);
        CrashDatabase.Submit(db, OperationKind.ConfigureResource, new ConfigureResourceRequest(
            Subscription.Source.Partition.TenantId, Subscription.Source.Partition.DatabaseId,
            new(CrashFixtureValues.Topic, ResourceKind.Topic, CrashFixtureValues.Orders)), Guid.NewGuid()).Get<ResourceDefinition>();
        var definition = new SubscriptionDefinition(CrashFixtureValues.Principal) { Policy = new() { MaxWindow = WindowSize } };
        var configure = Guid.NewGuid();
        CrashDatabase.Submit(db, OperationKind.ConfigureSubscription, new ConfigureSubscriptionRequest(configure,
            Subscription, definition), configure).Get<SubscriptionInfo>();
        var publish = Guid.NewGuid();
        CrashDatabase.Submit(db, OperationKind.Batch, new CommandRequest(publish, Subscription.Source.Partition,
            [new PublishTopic(CrashFixtureValues.Topic, [new(FirstEvent, CrashFixtureValues.EventType, CrashFixtureValues.EmptyJson),
                new(SecondEvent, ReplacementType, CrashFixtureValues.EmptyJson), new(ThirdEvent, CrashFixtureValues.EventType, CrashFixtureValues.EmptyJson)])]), publish).Get<CommitReceipt>();
        var receive = Guid.NewGuid();
        var received = CrashDatabase.Submit(db, OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(receive, Subscription, WindowSize), receive).Get<ReceiveSubscriptionResult>();
        foreach (var delivery in new[] { received.Deliveries.First(), received.Deliveries.Last() })
        {
            var ack = Guid.NewGuid();
            CrashDatabase.Submit(db, OperationKind.SubscriptionDelivery,
                new SubscriptionDeliveryCommand(ack, Subscription, delivery.Token, DeliveryAction.Ack), ack).Get<CommitReceipt>();
        }
        var pause = Guid.NewGuid();
        CrashDatabase.Submit(db, OperationKind.SetSubscriptionPaused,
            new SetSubscriptionPausedRequest(pause, Subscription, OriginalGeneration, true), pause).Get<SubscriptionInfo>();
        var operation = CrashDatabase.Operation(OperationKind.ConfigureSubscription,
            new ConfigureSubscriptionRequest(CommandId, Subscription, definition with { EventTypes = [ReplacementType] },
                ExpectedGeneration: OriginalGeneration), CommandId);
        await File.WriteAllBytesAsync(Path.Combine(directory, OperationFile), JsonDefaults.Serialize(operation));
        await File.WriteAllBytesAsync(Path.Combine(directory, OldTokenFile), JsonDefaults.Serialize(received.Deliveries[UnacknowledgedDeliveryIndex].Token));
        boundary.Position = checked(store.Position + OriginalGeneration);
        boundary.Armed = true;
        db.Apply(operation).Get<SubscriptionInfo>();
        await CrashHostPause.WaitForKillAsync();
    }
}
