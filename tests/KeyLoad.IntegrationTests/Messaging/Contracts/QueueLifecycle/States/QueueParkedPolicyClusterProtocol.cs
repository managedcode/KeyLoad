namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueParkedPolicyClusterProtocol
{
    internal const string Tenant = "parked-policy-";
    internal const string Database = "parked-policy-db";
    internal const string Domain = "parked-policy-domain";
    internal const string Queue = "parked-policy-jobs";
    internal const string Collection = "parked-policy-documents";
    internal const string OriginalHead = "original-head";
    internal const string OriginalSibling = "original-sibling";
    internal const string FreshHead = "fresh-head";
    internal const string FreshSibling = "fresh-sibling";
    internal const string Missing = "absent-witness";
    internal const string OrderingKey = "same-key";
    internal const string Payload = "{\"kind\":\"profile\"}";
    internal const string Headers = "{}";
    internal const string Enqueue = "enqueue";
    internal const string Cancel = "cancelQueueMessage";
    internal const string Cold = "queue-parked-policy-public-cold";
    internal const int FutureHours = 1;
    internal const int LeaseSeconds = 30;
}
