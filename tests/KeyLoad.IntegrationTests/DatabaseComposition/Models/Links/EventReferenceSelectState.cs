using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record EventReferenceSelectState(PartitionRef Partition, McpPersistedIdentity Identity,
    CommandRequest Seed, CommitReceipt Receipt, StreamPage OriginalStream, StreamPage OriginalInvalid, CommandRequest? Repair = null, CommitReceipt? RepairReceipt = null);
