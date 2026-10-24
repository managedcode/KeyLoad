using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsProvision
{
    private const int WriterIndex = 0;
    private const int ReaderIndex = 1;
    private const int OperatorIndex = 2;
    private const int AdditionalStart = 3;
    private const int Step = 1;
    private const Capability AnalyticsCapabilities = Capability.AnalyticsRead | Capability.AnalyticsWrite | Capability.Query;

    internal static async Task<FunctionalGateColdState> RunAsync(RequestCqrsClusterCallers administrator,
        FunctionalGateColdState state, CancellationToken token)
    {
        var original = new[] { state.Writer, state.Reader, state.QueueOperator }.Concat(state.AdditionalIdentities).ToArray();
        var identities = new List<McpPersistedIdentity>();
        foreach (var identity in original)
        {
            var scopes = new List<ScopeGrant>();
            for (var index = 0; index < FunctionalGateColdProtocol.Resources; index++)
            {
                var name = FunctionalGateUserAnalyticsState.StoreName(identity.Principal.Id, index);
                var definition = new ResourceDefinition(name, ResourceKind.AnalyticsStore, state.Partition.TransactionDomainId)
                {
                    AnalyticsSchema = FunctionalGateUserAnalyticsProtocol.Schema,
                    FieldPolicies = FunctionalGateAnalyticsAccess.Policies
                };
                await FunctionalGateUserAnalyticsAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                    await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                        new(state.Partition.TenantId, state.Partition.DatabaseId, definition), token)), definition);
                scopes.Add(new(state.Partition.DatabaseId, name, AnalyticsCapabilities));
            }
            var expected = identity.Principal with
            {
                PolicyEpoch = checked(identity.Principal.PolicyEpoch + Step),
                Grants = [.. identity.Principal.Grants, .. scopes],
                FieldGrants = [.. identity.Principal.FieldGrants.Concat(FunctionalGateAnalyticsAccess.Grants).Distinct(StringComparer.Ordinal)]
            };
            var actual = await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
            await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, expected);
            identities.Add(identity with
            {
                Principal = actual
            });
        }
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(identities.Count, FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities);
        return state with
        {
            Writer = identities[WriterIndex],
            Reader = identities[ReaderIndex],
            QueueOperator = identities[OperatorIndex],
            AdditionalIdentities = [.. identities.Skip(AdditionalStart)],
            UserAnalytics = [.. identities.Select(identity => new FunctionalGateAnalyticsColdUser(identity, []))]
        };
    }
}
