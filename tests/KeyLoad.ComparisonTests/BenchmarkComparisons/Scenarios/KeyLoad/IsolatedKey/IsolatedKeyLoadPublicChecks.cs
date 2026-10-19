using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/004/005: untimed genuine SDK/MCP qualification of the selected fixed execution membership.</summary>
internal static class IsolatedKeyLoadPublicChecks
{
    private const string AdminParameter = "admin-key";

    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, 3);
        using var deadlineTimeout = new CancellationTokenSource(ExecutionPolicyFixture.Harness().Value.KeyLoadPublicChecksTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, deadlineTimeout.Token);
        var key = await app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ParameterResource>().Single(item => item.Name == AdminParameter).GetValueAsync(deadline.Token)
            ?? throw new InvalidOperationException("The persisted administrator credential is missing.");
        await VerifyMembershipAsync(app, key, nodeCount, deadline.Token);
        using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, 1);
        var sdk = new KeyLoadClient(http, key, ComparisonClientOptions.Execution());
        await using var mcp = await IsolatedKeyLoadPublicChecksMcp.ConnectAsync(app, nodeCount, key, deadline.Token);
        var scenario = await IsolatedKeyLoadPublicChecksScenario.CreateAsync(sdk, nodeCount, deadline.Token);
        await IsolatedKeyLoadPublicChecksDocuments.VerifyAsync(sdk, mcp, scenario, deadline.Token);
        await IsolatedKeyLoadPublicChecksAuthorization.VerifyAsync(app, sdk, scenario, nodeCount, deadline.Token);
        await IsolatedKeyLoadPublicChecksMessaging.VerifyAsync(sdk, mcp, scenario, deadline.Token);
        await IsolatedKeyLoadPublicChecksBlobs.VerifyAsync(http, key, scenario, deadline.Token);
        await VerifyMembershipAsync(app, key, nodeCount, deadline.Token);
    }

    private static async Task VerifyMembershipAsync(DistributedApplication app, string key, int nodeCount, CancellationToken token)
    {
        var statuses = new List<NodeStatus>(nodeCount);
        for (var node = 1; node <= nodeCount; node++)
        {
            using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, node);
            var status = await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await new KeyLoadClient(http, key, ComparisonClientOptions.Execution()).StatusAsync(token));
            await Assert.That(status.SiloCount).IsEqualTo(nodeCount);
            await Assert.That(status.ReplicaCount).IsEqualTo(nodeCount);
            await Assert.That(status.RoutingReady).IsTrue();
            await Assert.That(status.Incarnation).IsNotEqualTo(Guid.Empty);
            await Assert.That(status.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
            statuses.Add(status);
        }
        await Assert.That(statuses.Select(item => item.NodeId).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(nodeCount);
        await Assert.That(statuses.Select(item => item.Incarnation).Distinct().Count()).IsEqualTo(1);
    }
}
