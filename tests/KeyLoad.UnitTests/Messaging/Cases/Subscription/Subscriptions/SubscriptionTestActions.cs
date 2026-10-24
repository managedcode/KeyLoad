namespace KeyLoad.UnitTests.Messaging;

internal static class SubscriptionTestActions
{
    internal static EventSourceRef Topic(TestDatabase db) => new(db.Partition, "topic", EventSourceKind.Topic);
    internal static SubscriptionRef Configure(TestDatabase db, string group = "group", SubscriptionPolicy? policy = null,
        SubscriptionStart start = SubscriptionStart.FromBeginning, string? cursor = null, string[]? types = null, string dataPrincipal = "root")
    {
        var subscription = new SubscriptionRef(Topic(db), group);
        var id = Guid.NewGuid();
        db.Submit(OperationKind.ConfigureSubscription, new ConfigureSubscriptionRequest(id, subscription,
            new(dataPrincipal)
            {
                Policy = policy ?? new(),
                EventTypes = [.. (types ?? [])]
            }, start, cursor), id: id).Get<SubscriptionInfo>();
        return subscription;
    }
    internal static void Publish(TestDatabase db, int count = 3, string prefix = "event", string type = "Created")
        => db.Commit(new PublishTopic("topic", [.. Enumerable.Range(1, count).Select(i => new EventData(prefix + i, type, "{\"n\":" + i + "}"))]));
    internal static ReceiveSubscriptionResult Receive(TestDatabase db, SubscriptionRef subscription, int count = 3,
        int lease = 30, DateTimeOffset? time = null, string principal = "root")
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.ReceiveSubscription,
            new ReceiveSubscriptionRequest(id, subscription, count, LeaseSeconds: lease), id: id, time: time, principal: principal).Get<ReceiveSubscriptionResult>();
    }
    internal static CommitReceipt Complete(TestDatabase db, SubscriptionRef subscription, SubscriptionDelivery delivery,
        DeliveryAction action = DeliveryAction.Ack, DateTimeOffset? time = null)
    {
        var id = Guid.NewGuid();
        return db.Submit(OperationKind.SubscriptionDelivery,
            new SubscriptionDeliveryCommand(id, subscription, delivery.Token, action), id: id, time: time).Get<CommitReceipt>();
    }
}
