using System.Net;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogInterface34MixedAssertions
{
    private const string InvalidNodeState = "The mixed interface4 node had an unexpected readiness or resource state.";

    internal static async Task<EntityRef> VerifyAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload, CancellationToken cancellationToken)
    {
        var denied = CreateDeniedWrite(workload);
        try
        {
            using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
            using var ready = await http.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken)
                .ConfigureAwait(false);
            await Assert.That(ready.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
        catch (HttpRequestException failure) when (failure.StatusCode is null)
        {
            await VerifyTerminalDenialAsync(app, profile, denied, failure, cancellationToken).ConfigureAwait(false);
            return denied.Reference;
        }
        await VerifyLiveSdkDenialAsync(app, profile, denied, cancellationToken).ConfigureAwait(false);
        await VerifyLiveMcpDenialAsync(app, profile, denied, cancellationToken).ConfigureAwait(false);
        return denied.Reference;
    }

    private static async Task VerifyTerminalDenialAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DeniedWrite denied, HttpRequestException readinessFailure,
        CancellationToken cancellationToken)
    {
        await Assert.That(readinessFailure.StatusCode).IsNull();
        await app.ResourceNotifications.WaitForResourceAsync(RequestCqrsRf3Protocol.Node1, resource =>
        {
            var state = resource.Snapshot.State?.Text;
            return IsTerminalState(state);
        }, cancellationToken).ConfigureAwait(false);
        if (!app.ResourceNotifications.TryGetCurrentState(RequestCqrsRf3Protocol.Node1, out var current)
            || !IsTerminalState(current?.Snapshot.State?.Text))
        { throw new InvalidOperationException(InvalidNodeState); }
        using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
        var sdk = new KeyLoadClient(http, profile.AdminKey, IntegrationClientOptions.Execution());
        var unknown = await sdk.CommitAsync(denied.Command, cancellationToken).ConfigureAwait(false);
        await Assert.That(unknown.IsFailed).IsTrue();
        await Assert.That(unknown.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        var mcpFailure = await Assert.ThrowsAsync<HttpRequestException>(() => McpOfficialClient.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node1, profile.AdminKey, cancellationToken));
        await Assert.That(mcpFailure!.StatusCode).IsNull();
    }

    private static async Task VerifyLiveSdkDenialAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DeniedWrite denied, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
        var sdk = new KeyLoadClient(http, profile.AdminKey, IntegrationClientOptions.Execution());
        var result = await sdk.CommitAsync(denied.Command, cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
    }

    private static async Task VerifyLiveMcpDenialAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DeniedWrite denied, CancellationToken cancellationToken)
    {
        try
        {
            await using var mcp = await McpOfficialClient.ConnectAsync(app, RequestCqrsRf3Protocol.Node1,
                profile.AdminKey, cancellationToken).ConfigureAwait(false);
            var reply = await mcp.CallAsync(McpCallerTools.DocumentsCommit, denied.Command, cancellationToken)
                .ConfigureAwait(false);
            await McpCallerAssertions.ErrorAsync(reply, ErrorCode.OwnershipLost, dispatched: false)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException failure) when (failure.StatusCode == HttpStatusCode.ServiceUnavailable)
        { await Assert.That(failure.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable); }
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
