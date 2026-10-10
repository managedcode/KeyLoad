namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLifecyclePublicProtocol
{
    internal const string Queue = "lifecycle-jobs";
    internal const string Collection = "lifecycle-documents";
    internal const string Parked = "parked";
    internal const string Pending = "pending";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"knowledge\":\"повний native lifecycle\"}";
    internal const string Headers = "{\"source\":\"four actual public routes\"}";
    internal const string Ordering = "referenced-entity";
    internal const string ColdScenario = "queue-lifecycle-same-owner-cold";
    internal const int Sdk = 0;
    internal const int Mcp = 1;
    internal const int Q1Sdk = 2;
    internal const int Q1Mcp = 3;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Three = 3;
    internal const int Four = 4;
    internal const int Five = 5;
    internal const int Six = 6;
    internal const int Seven = 7;
    internal const int NoSequence = 0;
    internal const int LeaseSeconds = 30;
    internal static readonly string[] Nodes = ["node1", "node2", "node3"];
    internal static readonly int[] Routes = [Sdk, Mcp, Q1Sdk, Q1Mcp];
}
