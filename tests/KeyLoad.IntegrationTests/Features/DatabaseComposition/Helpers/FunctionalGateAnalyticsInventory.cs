namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsInventory
{
    internal static async Task RequireAsync(FunctionalGateColdState state, bool seeded)
    {
        await Assert.That(state.UserAnalytics.Length).IsEqualTo(FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities);
        await Assert.That(state.UserAnalytics.Select(user => user.Identity.Principal.Id).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities);
        if (seeded)
        {
            foreach (var user in state.UserAnalytics)
            {
                await Assert.That(user.Stores.Length).IsEqualTo(FunctionalGateColdProtocol.Resources);
            }
            await Assert.That(state.UserAnalytics.SelectMany(user => user.Stores).Select(store => store.State.Store)
                .Distinct(StringComparer.Ordinal).Count())
                .IsEqualTo(FunctionalGateColdWorkloadProvision.TotalWorkloadIdentities * FunctionalGateColdProtocol.Resources);
        }
    }
}
