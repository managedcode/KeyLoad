using System.Net;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogRf3MismatchAssertions
{
    internal static async Task<EntityRef> VerifyAsync(Aspire.Hosting.DistributedApplication app, NodeEpochRf3Profile profile,
        RequestCqrsRf3Workload workload, string mismatchedNode, CancellationToken cancellationToken)
    {
        var denied = CreateDeniedWrite(workload);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(RequestCqrsRf3Protocol.Node1, cancellationToken)
            .ConfigureAwait(false);
        await app.ResourceNotifications.WaitForResourceHealthyAsync(RequestCqrsRf3Protocol.Node2, cancellationToken)
            .ConfigureAwait(false);
        await VerifyReadyAsync(app, RequestCqrsRf3Protocol.Node1, HttpStatusCode.OK, cancellationToken)
            .ConfigureAwait(false);
        await VerifyReadyAsync(app, RequestCqrsRf3Protocol.Node2, HttpStatusCode.OK, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await VerifyReadyAsync(app, mismatchedNode, HttpStatusCode.ServiceUnavailable, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException failure) when (failure.StatusCode is null)
        {
            await VerifyStoppedProcessDenialAsync(app, profile, denied, mismatchedNode, failure,
                cancellationToken).ConfigureAwait(false);
            return denied.Reference;
        }

        await VerifySdkDenialAsync(app, profile, denied, mismatchedNode, cancellationToken).ConfigureAwait(false);
        await VerifyMcpDenialAsync(app, profile, denied, mismatchedNode, cancellationToken).ConfigureAwait(false);
        return denied.Reference;
    }

    private static async Task VerifyReadyAsync(Aspire.Hosting.DistributedApplication app, string node,
        HttpStatusCode expected, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        using var response = await http.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
    }

    private static async Task VerifySdkDenialAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DeniedWrite denied, string node,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, node);
        var sdk = new KeyLoadClient(http, profile.AdminKey, IntegrationClientOptions.Execution());
        var result = await sdk.CommitAsync(denied.Command, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
    }

    private static async Task VerifyMcpDenialAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DeniedWrite denied, string node,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(app, node, profile.AdminKey,
                cancellationToken).ConfigureAwait(false);
            var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, denied.Command, cancellationToken)
                .ConfigureAwait(false);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.OwnershipLost, dispatched: false)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException failure) when (failure.StatusCode == HttpStatusCode.ServiceUnavailable)
        { await Assert.That(failure.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable); }
    }

    private static async Task VerifyStoppedProcessDenialAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DeniedWrite denied, string node,
        HttpRequestException readinessFailure, CancellationToken cancellationToken)
    {
        await Assert.That(readinessFailure.StatusCode).IsNull();
        await app.ResourceNotifications.WaitForResourceAsync(node, resource =>
        {
            var state = resource.Snapshot.State?.Text;
            return IsTerminalState(state);
        }, cancellationToken).ConfigureAwait(false);
        if (!app.ResourceNotifications.TryGetCurrentState(node, out var snapshot)
            || !IsTerminalState(snapshot?.Snapshot.State?.Text))
        { throw new InvalidOperationException("The Aspire node did not report a terminal state after its HTTP endpoint stopped."); }
        using var http = McpCallerHttp.Create(app, node);
        var sdk = new KeyLoadClient(http, profile.AdminKey, IntegrationClientOptions.Execution());
        var unknown = await sdk.CommitAsync(denied.Command, cancellationToken).ConfigureAwait(false);
        await Assert.That(unknown.IsFailed).IsTrue();
        await Assert.That(unknown.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => McpOfficialClient.ConnectAsync(app,
            node, profile.AdminKey, cancellationToken));
        await Assert.That(failure!.StatusCode).IsNull();
    }

    private static bool IsTerminalState(string? state) => state == KnownResourceStates.Exited
        || state == KnownResourceStates.FailedToStart || state == KnownResourceStates.Finished;

    private static DeniedWrite CreateDeniedWrite(RequestCqrsRf3Workload workload)
    {
        var reference = new EntityRef(workload.Partition, RequestCqrsRf3Protocol.AdminCollection,
            PhysicalShardCatalogRf3Protocol.DeniedWriteDocumentPrefix + Guid.NewGuid().ToString("N"));
        var command = new CommandRequest(Guid.NewGuid(), workload.Partition,
            [new PutDocument(reference.Collection, reference.Id, PhysicalShardCatalogRf3Protocol.DeniedWriteJson, 0)]);
        return new(reference, command);
    }

    private sealed record DeniedWrite(EntityRef Reference, CommandRequest Command);
}
