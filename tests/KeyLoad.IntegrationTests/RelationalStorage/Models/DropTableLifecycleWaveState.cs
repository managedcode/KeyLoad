using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record DropTableLifecycleWaveState(ResourcePartitionInventoryRf3Seed NativeSeed, DropTableLifecycleSeedState Seed,
    string DeniedCredential, ImmutableArray<DropTableLifecycleCompletedDrop> Drops);
