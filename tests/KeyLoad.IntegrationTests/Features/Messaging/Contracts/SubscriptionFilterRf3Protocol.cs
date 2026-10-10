namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Protocol
{
    internal const string TenantPrefix = "subscription-filter-";
    internal const string Database = "database";
    internal const string Domain = "events";
    internal const string Topic = "topic";
    internal const string Collection = "effects";
    internal const string Group = "workers";
    internal const string Independent = "independent";
    internal const string Created = "Created";
    internal const string Changed = "Changed";
    internal const string Payload = "{\"value\":1}";
    internal const string Healthy = "healthy";
    internal const string Handler = "filter-handler";
    internal const string ColdScenario = "subscription-filter-cold";
    internal const int Window = 3;
    internal const long Initial = 1;
    internal const long Updated = 2;
    internal const long Gap = 1;
    internal const long Completed = 4;
    internal const long HealthyCheckpoint = 5;
    internal const Capability Worker = Capability.TopicsRead | Capability.SubscriptionsConsume | Capability.SubscriptionsAck;
    internal const Capability Manager = Worker | Capability.TopicsPublish | Capability.SubscriptionsManage;
    internal static string[] Nodes { get; } = ["node1", "node2", "node3"];
    internal const string FirstEvent = "one";
    internal const string SecondEvent = "two";
    internal const string ThirdEvent = "three";
    internal const string FourthEvent = "four";
    internal const string MissingGroup = "missing";
    internal const string InvalidCursor = "unused";
    internal const string HealthyEvent = "healthy";
    internal const string TopicRecords = "topic-event";
    internal const string TopicHead = "topic-head";
    internal const string TopicIdentities = "topic-event-id";
}
