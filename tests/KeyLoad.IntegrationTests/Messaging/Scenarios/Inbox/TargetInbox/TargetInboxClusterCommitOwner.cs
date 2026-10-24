using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class TargetInboxClusterCommitOwner
{
    internal static async Task<CommitInboxResult> ExecuteAsync(ClusterFixture fixture, MessagingClusterIdentity identity,
        CommitInboxRequest request, CancellationToken token)
    {
        var failures = new List<Exception>();
        var returned = await TargetInboxClusterResponseCall.SendAsync(fixture, identity, request, failures, token);
        CommitInboxResult? observed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, identity.Secret, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                observed = (await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
                    TargetInboxClusterProtocol.Tool, request, token))).Value;
                await Assert.That(observed.AlreadyProcessed).IsFalse();
                if (returned is not null)
                {
                    await TargetInboxClusterAssertions.EqualAsync(returned, observed);
                }
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return observed ?? throw new InvalidOperationException("The original target inbox result was not retained.");
    }
}
