using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateAnalyticsColdUser(McpPersistedIdentity Identity,
    ImmutableArray<FunctionalGateAnalyticsColdStore> Stores);
