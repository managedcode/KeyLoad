using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateDropState(CommandRequest Original, CommitReceipt Receipt,
    ImmutableArray<FunctionalGateDropTable> Tables);
