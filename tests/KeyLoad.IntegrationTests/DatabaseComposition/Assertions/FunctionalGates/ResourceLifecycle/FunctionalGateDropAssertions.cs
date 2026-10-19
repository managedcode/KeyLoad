using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateDropAssertions
{
    internal static async Task ResultAsync(FunctionalGateContext context, Guid parentId, DropTableRequest request, DropTableResult actual)
    {
        await Assert.That(actual.Receipt.CommandId).IsNotEqualTo(parentId);
        await Assert.That(actual.Receipt.CommandId).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Receipt.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(P.Absent);
        await Assert.That(actual.Receipt.PersistedCopies).IsEqualTo(context.ReplicaCount);
        await SqlClusterProtocol.EqualAsync(new DropTableResult(request.TenantId, request.DatabaseId,
            request.ResourceName, new(actual.Receipt.CommandId, actual.Receipt.Token, [],
                DurabilityProfile.ProcessDurable, context.ReplicaCount)), actual);
        await SubmissionColdAssertions.ReceiptAsync(actual.Receipt,
            new(actual.Receipt.CommandId, actual.Receipt.Token, [], DurabilityProfile.ProcessDurable, context.ReplicaCount));
    }

    internal static async Task FreshAsync(FunctionalGateContext context, FunctionalGateDropState state,
        CancellationToken token)
    {
        foreach (var table in state.Tables)
        {
            var expected = new DocumentReadResult(table.Fresh);
            await SqlClusterProtocol.EqualAsync(expected, await context.SdkAsync(
                context.Reader.Sdk.GetAsync(table.Fresh.Reference, token)));
            await SqlClusterProtocol.EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader,
                McpCallerTools.DocumentsGet, new GetDocumentRequest(table.Fresh.Reference), token));
            var absent = table.Fresh.Reference with
            {
                Id = table.OriginalReference.Id
            };
            await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null), await context.SdkAsync(
                context.Reader.Sdk.GetAsync(absent, token)));
            await SqlClusterProtocol.EqualAsync(new DocumentReadResult(null),
                await context.McpAsync<DocumentReadResult>(context.Reader, McpCallerTools.DocumentsGet,
                    new GetDocumentRequest(absent), token));
        }
        await FunctionalGateCompositionAssertions.AnchorAsync(context, token);
    }

    internal static async Task StaleAsync(FunctionalGateContext context, EntityRef original,
        Func<Task> unchanged, CancellationToken token)
    {
        var sdk = await context.Reader.Sdk.GetAsync(original, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(ErrorCode.OwnershipLost.ToString());
        await Assert.That(sdk.Problem?.StatusCode).IsEqualTo(Errors.Status(ErrorCode.OwnershipLost));
        await unchanged();
        await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(original), token), ErrorCode.OwnershipLost, dispatched: true);
        await unchanged();
    }
}
