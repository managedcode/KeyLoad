using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleSeedState(PartitionRef Partition,
    ImmutableArray<DropTableLifecycleTable> Tables, ImmutableArray<ResourceDefinition> Controls,
    CommandRequest Original, CommitReceipt Receipt, DocumentResult Anchor, DocumentResult ControlRow);
