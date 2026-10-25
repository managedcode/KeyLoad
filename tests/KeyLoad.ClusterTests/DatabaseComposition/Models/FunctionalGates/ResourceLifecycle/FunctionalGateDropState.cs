using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateDropState(CommandRequest Original, CommitReceipt Receipt,
    ImmutableArray<FunctionalGateDropTable> Tables);
