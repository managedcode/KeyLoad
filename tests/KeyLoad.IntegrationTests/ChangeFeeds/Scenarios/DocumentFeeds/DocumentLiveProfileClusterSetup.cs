using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileClusterSetup
{
    internal static async Task<DocumentLiveProfileClusterState> RunAsync(RequestCqrsClusterCallers root, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var state = new DocumentLiveProfileClusterState(new("live-tenant-" + suffix, "live-database", "live-domain", "live-partition-" + suffix),
            "live-reader-" + suffix, "live-key-" + suffix + ".credential-at-least-32");
        var resource = new ResourceDefinition(DocumentLiveProfileClusterState.Collection, ResourceKind.Collection, state.Partition.TransactionDomainId)
        {
            FieldPolicies = [new("/secret", "private", "private.read", "private.use")]
        };
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(state.Partition.TenantId, state.Partition.DatabaseId, resource), token));
        await PrincipalAsync(root, state, 1L, true, false, token);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(state.Key.Split('.')[0], state.PrincipalId, state.Key), token));
        await CommitAsync(root, state,
            [new PutDocument(DocumentLiveProfileClusterState.Collection, "a", DocumentLiveProfileClusterState.Json(1), 0L, Access: new("live-owner")),
             new PutDocument(DocumentLiveProfileClusterState.Collection, "hidden", DocumentLiveProfileClusterState.Json(99), 0L, Access: new("other-owner"))],
            [new("putDocument", DocumentLiveProfileClusterState.Collection, "a", 1L), new("putDocument", DocumentLiveProfileClusterState.Collection, "hidden", 1L)], token);
        return state;
    }
    internal static async Task PrincipalAsync(RequestCqrsClusterCallers root, DocumentLiveProfileClusterState state,
        long epoch, bool feed, bool revoked, CancellationToken token)
    {
        var expected = state.Principal(epoch, feed, revoked);
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
    internal static async Task CommitAsync(RequestCqrsClusterCallers root, DocumentLiveProfileClusterState state,
        Mutation[] mutations, MutationReceipt[] effects, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Partition, [.. mutations]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await DocumentLiveProfileClusterValues.ReceiptAsync(command, effects, receipt);
        state.Commands.Add((command, receipt));
    }
    internal static Task UpdateAsync(RequestCqrsClusterCallers root, DocumentLiveProfileClusterState state, CancellationToken token)
        => CommitAsync(root, state,
            [new PutDocument(DocumentLiveProfileClusterState.Collection, "a", DocumentLiveProfileClusterState.Json(2), 1L, Access: new("live-owner")),
             new PutDocument(DocumentLiveProfileClusterState.Collection, "b", DocumentLiveProfileClusterState.Json(3), 0L, Access: new("live-owner")),
             new DeleteDocument(DocumentLiveProfileClusterState.Collection, "a", 2L)],
            [new("putDocument", DocumentLiveProfileClusterState.Collection, "a", 2L), new("putDocument", DocumentLiveProfileClusterState.Collection, "b", 1L),
             new("deleteDocument", DocumentLiveProfileClusterState.Collection, "a", 3L)], token);
    internal static Task FreshAsync(RequestCqrsClusterCallers root, DocumentLiveProfileClusterState state, CancellationToken token)
        => CommitAsync(root, state, [new PutDocument(DocumentLiveProfileClusterState.Collection, "c", DocumentLiveProfileClusterState.Json(5), 0L, Access: new("live-owner"))],
            [new("putDocument", DocumentLiveProfileClusterState.Collection, "c", 1L)], token);
}
