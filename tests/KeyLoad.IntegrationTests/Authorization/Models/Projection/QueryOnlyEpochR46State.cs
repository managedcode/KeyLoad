using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record QueryOnlyEpochR46State(PolicyEpochPublicState Original,
    PrincipalRecord Baseline, QueryPage[] GrantedPages, OutboxStatus Outbox,
    McpPersistedIdentity CurrentReader)
{
    public override string ToString() => nameof(QueryOnlyEpochR46State);
}
