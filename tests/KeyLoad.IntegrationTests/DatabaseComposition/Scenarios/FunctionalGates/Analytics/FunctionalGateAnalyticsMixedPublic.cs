using P = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateAnalyticsMixedPublicProtocol;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsMixedPublic
{
    internal static async Task RunAsync(FunctionalGateContext context, bool verifyIncidentBound, CancellationToken token)
    {
        await Assert.That(context.AnalyticsGenerations.Count).IsEqualTo(context.ResourceCount);
        for (var index = P.Empty; index < context.ResourceCount; index++)
        {
            var state = FunctionalGateAnalyticsMixedPublicState.Create(context, index);
            await FunctionalGateAnalyticsMixedPublicReads.VerifyAsync(context, state, present: false, P.Empty, token);
            var rejected = state.Command(context, Guid.NewGuid(), P.Created);
            await FunctionalGateAnalyticsMixedPublicCalls.RefuseAsync(context, rejected,
                () => FunctionalGateAnalyticsMixedPublicReads.VerifyAsync(context, state, present: false, P.Empty, token), token);
            await Assert.That(context.AnalyticsGenerations[state.Store]).IsEqualTo(state.OriginalGeneration);
            var command = state.Command(context, Guid.NewGuid(), P.Empty);
            var receipt = await FunctionalGateAnalyticsMixedPublicCalls.CommitAsync(context, state, command, token);
            context.AnalyticsGenerations[state.Store] = checked(state.OriginalGeneration + P.Created);
            await FunctionalGateAnalyticsMixedPublicReads.VerifyAsync(context, state, present: true, receipt.Token.Position, token);
            await FunctionalGateAnalyticsIdentityConflict.VerifyAsync(context, command,
                () => FunctionalGateAnalyticsMixedPublicReads.VerifyAsync(context, state, present: true, receipt.Token.Position, token), token);
            if (verifyIncidentBound && index == context.ResourceCount - P.Created)
            {
                await FunctionalGateAnalyticsMixedPublicBounds.RunAsync(context, state, command, receipt, token);
            }
        }
    }
}
