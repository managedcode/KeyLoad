using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class PostDispatchAdmissionReturnFlow
{
    private const int FirstCold = 1;
    private const int ColdCount = 2;

    internal static Task RunAsync(bool disconnect, CancellationToken token)
        => ConnectionRf3Scenario.RunAsync((scenario, current) => OwnedAsync(scenario, disconnect, current), token);

    private static async Task OwnedAsync(ConnectionRf3Scenario scenario, bool disconnect, CancellationToken token)
    {
        var original = await PostDispatchAdmissionReturnAttempt.RunAsync(scenario, disconnect, token);
        var committed = await PostDispatchAdmissionReturnContinuation.FirstCommitAsync(scenario, original, token);
        await PostDispatchAdmissionReturnContinuation.ReplayAsync(scenario, original, committed, token);
        for (var ordinal = FirstCold; ordinal <= ColdCount; ordinal++)
        {
            await scenario.RestartQueryContextsColdAsync(ordinal, token);
            await PostDispatchAdmissionReturnContinuation.ReplayAsync(scenario, original, committed, token);
            await scenario.RequireQueryColdReceiptsAsync(token);
        }
    }
}
