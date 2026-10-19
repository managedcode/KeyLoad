namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class CommandObservedPolicyProtocol
{
    internal const string Tenant = "system";
    internal const string Database = "observed-policy-execution";
    internal const string Domain = "observed-policy-domain";
    internal const string PartitionKey = "observed-policy-partition";
    internal const string ObserverOwned = "The actual execution codec already has another phase observer.";
    internal static readonly PartitionRef Partition = new(Tenant, Database, Domain, PartitionKey);
    internal const string Queue = "observed-policy-queue";
    internal const string Worker = "observed-policy-worker";
    internal const string Root = "root";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Path = "/secret";
    internal const string Classification = "private";
    internal const string BeforeGrant = "observed-policy-before";
    internal const string AfterGrant = "observed-policy-after";
    internal const string Payload = "{\"public\":\"visible\",\"secret\":\"private-payload\"}";
    internal const string Headers = "{\"trace\":\"visible\",\"secret\":\"private-header\"}";
    internal const string Refusal = "The current resource policy protects data retained in the original delivery.";
    internal const string Missing = "The genuine original receive outcome is absent.";
    internal const string WrongReply = "The execution observed result has an unexpected type.";
    internal const string EnqueueKind = "enqueue";
    internal const long Initial = 1;
    internal const long Step = 1;
    internal const int One = 1;
}
