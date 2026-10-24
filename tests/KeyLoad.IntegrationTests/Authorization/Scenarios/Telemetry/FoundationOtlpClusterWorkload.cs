using System.Diagnostics;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class FoundationOtlpClusterWorkload
{
    internal static async Task<(SensitiveAuthorityState State, string Trace)> RunAsync(ClusterFixture fixture,
        CancellationToken token)
    {
        using var parent = new Activity(FoundationOtlpClusterProtocol.ParentOperation);
        parent.SetIdFormat(ActivityIdFormat.W3C);
        parent.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        parent.Start();
        var state = await SensitiveAuthorityWarm.RunAsync(fixture, token);
        foreach (var node in FoundationOtlpClusterProtocol.Nodes)
        {
            await FoundationOtlpClusterRejectedHandshake.RunAsync(fixture, node, state.Identity.Secret, token);
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var http = McpCallerHttp.Create(fixture, node);
                await using var mcp = await McpOfficialClient.ConnectAsync(fixture, node, state.Identity.Secret, token);
                await ServerFailureObserver.ObserveAsync(() => SensitiveAuthorityAssertions.HealthyAsync(
                    new KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution()), mcp,
                    state.Scenario, patched: true, token), failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return (state, parent.TraceId.ToString());
    }
}
