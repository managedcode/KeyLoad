using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileRf3Setup
{
    internal static async Task<DocumentLiveProfileRf3State> RunAsync(RequestCqrsRf3Callers root, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var state = new DocumentLiveProfileRf3State(new("live-tenant-" + suffix, "live-database", "live-domain", "live-partition-" + suffix),
            "live-reader-" + suffix, "live-key-" + suffix + ".credential-at-least-32");
        var resource = new ResourceDefinition(DocumentLiveProfileRf3State.Collection, ResourceKind.Collection, state.Partition.TransactionDomainId)
        {
            FieldPolicies = [new("/secret", "private", "private.read", "private.use")]
        };
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(state.Partition.TenantId, state.Partition.DatabaseId, resource), token));
        await PrincipalAsync(root, state, 1L, true, false, token);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(state.Key.Split('.')[0], state.PrincipalId, state.Key), token));
        await CommitAsync(root, state,
            [new PutDocument(DocumentLiveProfileRf3State.Collection, "a", DocumentLiveProfileRf3State.Json(1), 0L, Access: new("live-owner")),
             new PutDocument(DocumentLiveProfileRf3State.Collection, "hidden", DocumentLiveProfileRf3State.Json(99), 0L, Access: new("other-owner"))],
            [new("putDocument", DocumentLiveProfileRf3State.Collection, "a", 1L), new("putDocument", DocumentLiveProfileRf3State.Collection, "hidden", 1L)], token);
        return state;
    }
    internal static async Task PrincipalAsync(RequestCqrsRf3Callers root, DocumentLiveProfileRf3State state,
        long epoch, bool feed, bool revoked, CancellationToken token)
    {
        var expected = state.Principal(epoch, feed, revoked);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
    internal static async Task CommitAsync(RequestCqrsRf3Callers root, DocumentLiveProfileRf3State state,
        Mutation[] mutations, MutationReceipt[] effects, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Partition, [.. mutations]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        await DocumentLiveProfileRf3Values.ReceiptAsync(root, command, effects, receipt, token);
        state.Commands.Add((command, receipt));
    }
    internal static Task UpdateAsync(RequestCqrsRf3Callers root, DocumentLiveProfileRf3State state, CancellationToken token)
        => CommitAsync(root, state,
            [new PutDocument(DocumentLiveProfileRf3State.Collection, "a", DocumentLiveProfileRf3State.Json(2), 1L, Access: new("live-owner")),
             new PutDocument(DocumentLiveProfileRf3State.Collection, "b", DocumentLiveProfileRf3State.Json(3), 0L, Access: new("live-owner")),
             new DeleteDocument(DocumentLiveProfileRf3State.Collection, "a", 2L)],
            [new("putDocument", DocumentLiveProfileRf3State.Collection, "a", 2L), new("putDocument", DocumentLiveProfileRf3State.Collection, "b", 1L),
             new("deleteDocument", DocumentLiveProfileRf3State.Collection, "a", 3L)], token);
    internal static Task FreshAsync(RequestCqrsRf3Callers root, DocumentLiveProfileRf3State state, CancellationToken token)
        => CommitAsync(root, state, [new PutDocument(DocumentLiveProfileRf3State.Collection, "c", DocumentLiveProfileRf3State.Json(5), 0L, Access: new("live-owner"))],
            [new("putDocument", DocumentLiveProfileRf3State.Collection, "c", 1L)], token);
}
