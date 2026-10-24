using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsColdUser(McpPersistedIdentity Identity,
    ImmutableArray<FunctionalGateAnalyticsColdStore> Stores);
