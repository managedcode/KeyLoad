using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record QueueReferenceSelectState(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Seed, CommitReceipt Receipt, CommandRequest? Repair = null, CommitReceipt? RepairReceipt = null);
