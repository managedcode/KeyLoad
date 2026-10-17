using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateColdDropState(ImmutableArray<FunctionalGateColdDropTable> Tables);
