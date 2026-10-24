using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record FunctionalGateDropState(CommandRequest Original, CommitReceipt Receipt,
    ImmutableArray<FunctionalGateDropTable> Tables);
