namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionIndependentCheckpointR52Protocol
{
    internal const string HealthyEvent = "independent-checkpoint-six";
    internal const string PublishKind = "publishTopic";
    internal const string ChangedOutcomeDetail = "The principal policy changed since this command was evaluated.";
    internal const string ChangedDeliveryDetail = "A subscription principal policy changed after delivery.";
    internal const string SafePermissionDetail = "The authenticated principal is not permitted to perform this operation.";
    internal const string ScopeDetail = "The subscription token scope or generation is invalid.";
    internal const string StaleDetail = "The subscription delivery lease is stale.";
    internal const string SafeDetail = "The database operation could not be completed.";
    internal const string Payload = "{\"value\":1}";
    internal const string MissingState = "The original independent checkpoint state is unavailable.";
    internal static EventData[] Corpus(long tail)
    {
        EventData[] all = [
            new(SubscriptionFilterClusterProtocol.FirstEvent, SubscriptionFilterClusterProtocol.Created, Payload),
            new(SubscriptionFilterClusterProtocol.SecondEvent, SubscriptionFilterClusterProtocol.Changed, Payload),
            new(SubscriptionFilterClusterProtocol.ThirdEvent, SubscriptionFilterClusterProtocol.Created, Payload),
            new(SubscriptionFilterClusterProtocol.FourthEvent, SubscriptionFilterClusterProtocol.Changed, Payload),
            new(SubscriptionFilterClusterProtocol.Healthy, SubscriptionFilterClusterProtocol.Changed, Payload),
            new(HealthyEvent, SubscriptionFilterClusterProtocol.Changed, Payload)];
        return all.Take(checked((int)tail)).ToArray();
    }
}
