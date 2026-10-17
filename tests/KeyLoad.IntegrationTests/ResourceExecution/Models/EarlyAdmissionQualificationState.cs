using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal sealed record EarlyAdmissionQualificationState(PartitionRef Partition, CommandRequest Seed,
    CommitReceipt Receipt, OutboxStatus Outbox, McpPersistedIdentity Identity);
