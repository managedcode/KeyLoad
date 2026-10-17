using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsColdUser(McpPersistedIdentity Identity,
    ImmutableArray<FunctionalGateAnalyticsColdStore> Stores);
