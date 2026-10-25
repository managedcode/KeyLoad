using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateColdDropState(ImmutableArray<FunctionalGateColdDropTable> Tables);
