using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Authorization;

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
