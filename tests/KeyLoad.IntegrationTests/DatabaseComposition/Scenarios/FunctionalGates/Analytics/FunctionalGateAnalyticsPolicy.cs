namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsPolicy
{
    // Original cold authorization advances writer/additional subjects twice; monotonic Core policy accepts this next epoch.
    private const long AfterOriginalAuthorization = 3;
    private const long Step = 1;
    private const long Absent = 0;

    internal static async Task RunAsync(FunctionalGateContext context, PrincipalRecord baseline,
        IDictionary<string, long> generations, CancellationToken token)
    {
        var states = new List<FunctionalGateAnalyticsPolicyState>();
        foreach (var store in generations.Keys.Order(StringComparer.Ordinal).ToArray())
        {
            var state = FunctionalGateAnalyticsPolicyState.Create(store, baseline.Id);
            var command = new CommandRequest(Guid.NewGuid(), context.Partition,
                [new UpsertAnalyticsEvents(store, [state.Owned, state.Foreign],
                    FunctionalGateAnalyticsColdSeed.Revisions((state.Owned.Id, Absent), (state.Foreign.Id, Absent)))]);
            generations[store] = checked(generations[store] + Step);
            await FunctionalGateAnalyticsCommands.CommitAsync(context, command, AnalyticsStoreProtocol.UpsertKind,
                store, state.Foreign.Id, generations[store], token);
            states.Add(state);
        }
        var restricted = baseline with
        {
            PolicyEpoch = checked(baseline.PolicyEpoch + AfterOriginalAuthorization),
            ClusterAdministrator = false,
            RestrictRows = true,
            OwnerId = baseline.Id,
            FieldGrants = [.. baseline.FieldGrants.Concat(FunctionalGateAnalyticsAccess.Grants).Distinct(StringComparer.Ordinal)]
        };
        await ConfigureAsync(context, restricted, token);
        await FunctionalGateAnalyticsPolicyReads.VerifyAsync(context, states, rawRead: true, token);
        await FunctionalGateAnalyticsPolicyReads.ForeignWritesAsync(context, states, token);
        var noUse = restricted with
        {
            PolicyEpoch = checked(restricted.PolicyEpoch + Step),
            FieldGrants = [.. restricted.FieldGrants.Where(grant => grant != FunctionalGateAnalyticsAccess.UseGrant)]
        };
        await ConfigureAsync(context, noUse, token);
        await FunctionalGateAnalyticsPolicyReads.DeniedAsync(context, states, token);
        var noRead = restricted with
        {
            PolicyEpoch = checked(noUse.PolicyEpoch + Step),
            FieldGrants = [.. restricted.FieldGrants.Where(grant => grant != FunctionalGateAnalyticsAccess.ReadGrant)]
        };
        await ConfigureAsync(context, noRead, token);
        await FunctionalGateAnalyticsPolicyReads.VerifyAsync(context, states, rawRead: false, token);
        var restored = restricted with
        {
            PolicyEpoch = checked(noRead.PolicyEpoch + Step)
        };
        await ConfigureAsync(context, restored, token);
        await FunctionalGateAnalyticsPolicyReads.VerifyAsync(context, states, rawRead: true, token);
        await ConfigureAsync(context, baseline with
        {
            PolicyEpoch = checked(restored.PolicyEpoch + Step),
            FieldGrants = restricted.FieldGrants
        }, token);
        await CleanupAsync(context, states, generations, token);
    }

    private static async Task ConfigureAsync(FunctionalGateContext context, PrincipalRecord expected, CancellationToken token)
        => await FunctionalGateUserAnalyticsAssertions.EqualAsync(await context.SdkAsync(
            context.Administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)), expected);

    private static async Task CleanupAsync(FunctionalGateContext context, IEnumerable<FunctionalGateAnalyticsPolicyState> states,
        IDictionary<string, long> generations, CancellationToken token)
    {
        foreach (var state in states)
        {
            var store = state.Query.Store;
            var command = new CommandRequest(Guid.NewGuid(), context.Partition,
                [new DeleteAnalyticsEvents(store, FunctionalGateAnalyticsColdSeed.Revisions(
                    (state.Owned.Id, Step), (state.Foreign.Id, Step)))]);
            generations[store] = checked(generations[store] + Step);
            await FunctionalGateAnalyticsCommands.CommitAsync(context, command, AnalyticsStoreProtocol.DeleteKind,
                store, state.Foreign.Id, generations[store], token);
            await FunctionalGateAnalyticsColdAssertions.StateAsync(context, state.Query, [], new([], Absent), token);
        }
    }
}
