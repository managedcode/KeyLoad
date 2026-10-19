using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class CredentialSubjectReplacementCold
{
    private const int FirstCold = 1;
    private const int LastCold = 2;
    private const string Separator = "-";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, CredentialSubjectReplacementState state, CancellationToken token)
    {
        var before = await AdversarialAuthorityTrial.WithRootAsync(fixture, McpCallerProtocol.Node1,
            root => CredentialSubjectReplacementAssertions.ImageAsync(root, state, token));
        for (var cold = FirstCold; cold <= LastCold; cold++)
        {
            await RestartAsync(fixture, state.ColdNamespace + Separator
                + cold.ToString(System.Globalization.CultureInfo.InvariantCulture), token);
            await SqlClusterProtocol.EqualAsync(before, await AdversarialAuthorityTrial.WithRootAsync(fixture,
                McpCallerProtocol.Node1, root => CredentialSubjectReplacementAssertions.ImageAsync(root, state, token)));
            await CredentialSubjectReplacementClients.RunAsync(fixture, state, warm: false, token);
        }
    }

    private static async Task RestartAsync(ClusterFixture fixture, string scenario, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, scenario, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                node, Aspire.Hosting.ApplicationModel.WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
