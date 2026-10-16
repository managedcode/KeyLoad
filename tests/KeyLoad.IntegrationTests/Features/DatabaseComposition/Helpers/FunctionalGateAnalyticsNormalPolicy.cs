namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsNormalPolicy
{
    internal static async Task RunAsync(FunctionalGateContext[] contexts, IReadOnlyList<PrincipalRecord> principals,
        CancellationToken token)
    {
        await Assert.That(principals.Count).IsEqualTo(contexts.Length);
        for (var index = 0; index < contexts.Length; index++)
        {
            await Assert.That(contexts[index].AnalyticsGenerations.Count).IsEqualTo(contexts[index].ResourceCount);
            var original = contexts[index];
            await Assert.That(original.WriterPrincipalId).IsEqualTo(principals[index].Id);
            var policy = original with
            {
                Reader = original.Writer,
                ReaderPrincipalId = original.WriterPrincipalId
            };
            await FunctionalGateAnalyticsPolicy.RunAsync(policy, principals[index],
                original.AnalyticsGenerations, token);
        }
    }
}
