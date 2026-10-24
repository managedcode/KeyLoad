using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.ResourceExecution;

internal sealed record EarlyAdmissionQualificationState(PartitionRef Partition, CommandRequest Seed,
    CommitReceipt Receipt, OutboxStatus Outbox, McpPersistedIdentity Identity);
