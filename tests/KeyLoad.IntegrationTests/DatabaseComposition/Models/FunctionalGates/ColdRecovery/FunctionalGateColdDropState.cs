using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record FunctionalGateColdDropState(ImmutableArray<FunctionalGateColdDropTable> Tables);
