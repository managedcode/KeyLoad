using System.Net;
using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3AuthorizationOracle
{
    private const int DeleteLimit = 256;

    internal static async Task RejectReadOnlyExpiryAsync(NodeEpochRf3Callers reader,
        NodeEpochRf3Workload workload, CancellationToken cancellationToken)
    {
        var expire = new CommandRequest(Guid.NewGuid(), workload.Partition,
            [new ExpireSamples(workload.SeriesSet, workload.SeriesId,
                NodeEpochRf3Protocol.SampleStart.AddMinutes(10), DeleteLimit)]);
        var sdk = await reader.Sdk.CommitAsync(expire, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            expire, cancellationToken).ConfigureAwait(false), ErrorCode.PermissionDenied, dispatched: true)
            .ConfigureAwait(false);
        await NodeEpochRf3ReadOracle.VerifyRetentionStatusAsync(reader, workload, null, 0, false,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task VerifyRevocationAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        NodeEpochRf3Callers reader, NodeEpochRf3Workload workload, CancellationToken cancellationToken)
    {
        await using var admin = await NodeEpochRf3Callers.ConnectAsync(app, NodeEpochRf3Protocol.Node1,
            NodeEpochRf3Protocol.Node2, profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3CredentialRevocationOracle.VerifyAsync(app, admin, workload, cancellationToken)
            .ConfigureAwait(false);
        var revokedPrincipal = workload.Reader.Principal with
        { Revoked = true, PolicyEpoch = workload.Reader.Principal.PolicyEpoch + 1 };
        var saved = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(),
            revokedPrincipal, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(saved.PolicyEpoch).IsEqualTo(revokedPrincipal.PolicyEpoch);
        var revokedCredential = workload.Reader.Credential with { Revoked = true };
        var rejectedKeyUpdate = await admin.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), revokedCredential,
            cancellationToken).ConfigureAwait(false);
        await Assert.That(rejectedKeyUpdate.IsFailed).IsTrue();
        await Assert.That(rejectedKeyUpdate.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        await AssertDeniedAsync(reader, workload, cancellationToken).ConfigureAwait(false);
        await AssertAdminHealthyAsync(admin, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task AssertDeniedAsync(NodeEpochRf3Callers reader, NodeEpochRf3Workload workload,
        CancellationToken cancellationToken)
    {
        var request = new ReadSamplesRequest(workload.Partition, workload.SeriesSet, workload.SeriesId,
            NodeEpochRf3Protocol.SampleStart, NodeEpochRf3Protocol.SampleStart.AddMinutes(70), 100);
        var sdk = await reader.Sdk.ReadSamplesAsync(request, cancellationToken).ConfigureAwait(false);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var mcpFailure = await Assert.ThrowsAsync<HttpRequestException>(() => reader.Mcp.CallAsync(
            McpCallerTools.SeriesRead, request, cancellationToken));
        await Assert.That(mcpFailure!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(mcpFailure.Message.Contains(workload.Reader.Secret, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertAdminHealthyAsync(NodeEpochRf3Callers admin,
        CancellationToken cancellationToken)
    {
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk.RoutingReady).IsTrue();
        var reply = await admin.Mcp.Client.CallToolAsync(McpCallerTools.AdminStatus,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<NodeStatus>(reply).ConfigureAwait(false);
        await Assert.That(mcp.Value.RoutingReady).IsTrue();
    }
}
