using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed class QueueViewLeaseIsolationState(SqlModelViewClusterScenario scenario, McpPersistedIdentity reader)
{
    internal SqlModelViewClusterScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Reader { get; } = reader;
    internal ReceiveRequest? Claim
    {
        get; set;
    }
    internal ReceiveResult? OriginalDelivery
    {
        get; set;
    }
    internal List<QueueViewLeaseIsolationReceipt> Receipts { get; } = [];
    internal CommandRequest? HealthyCommand
    {
        get; set;
    }
    internal CommitReceipt? HealthyReceipt
    {
        get; set;
    }
    internal QueueViewLeaseIsolationImage? Terminal
    {
        get; set;
    }
    internal bool Acked
    {
        get; set;
    }
    internal bool Healthy
    {
        get; set;
    }
    internal long Version { get; set; } = QueueViewLeaseIsolationProtocol.LeasedVersion;
    internal Delivery Delivery => OriginalDelivery?.Deliveries.Single()
        ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
}
