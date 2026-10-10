using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3CommitOwner
{
    internal static async Task<CommitInboxResult> ExecuteAsync(ClusterFixture fixture, MessagingRf3Identity identity,
        CommitInboxRequest request, CancellationToken token)
    {
        var failures = new List<Exception>();
        var returned = await TargetInboxRf3ResponseCall.SendAsync(fixture, identity, request, failures, token);
        CommitInboxResult? observed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, identity.Secret, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                observed = (await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
                    TargetInboxRf3Protocol.Tool, request, token))).Value;
                await Assert.That(observed.AlreadyProcessed).IsFalse();
                if (returned is not null)
                { await TargetInboxRf3Assertions.EqualAsync(returned, observed); }
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return observed ?? throw new InvalidOperationException("The original target inbox result was not retained.");
    }
}
