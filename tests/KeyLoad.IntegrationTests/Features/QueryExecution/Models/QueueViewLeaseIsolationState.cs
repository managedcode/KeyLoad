using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class QueueViewLeaseIsolationState(SqlModelViewRf3Scenario scenario, McpPersistedIdentity reader)
{
    internal SqlModelViewRf3Scenario Scenario { get; } = scenario;
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
internal sealed record QueueViewLeaseIsolationReceipt(DeliveryCommand Command, CommitReceipt Receipt);
internal sealed record QueueViewLeaseIsolationImage(MessageInspection[] Messages, StreamPage Source, OutboxStatus Outbox);
