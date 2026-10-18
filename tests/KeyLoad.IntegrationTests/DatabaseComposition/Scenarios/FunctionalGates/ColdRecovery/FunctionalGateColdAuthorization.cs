using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdAuthorization
{
    internal static async Task RunAsync(FunctionalGateContext context, FunctionalGateColdState state, CancellationToken token)
    {
        var reference = context.Bind(new(context.Partition, context.MutableDocumentCollection, "healthy-after-cold"));
        var command = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(reference.Collection, reference.Id, "{\"stage\":\"healthy-after-cold\"}", 0)]);
        await RefuseAsync(context.Reader, command with
        {
            CommandId = Guid.NewGuid()
        }, token);
        var revoked = state.Writer.Principal with
        {
            PolicyEpoch = checked(state.Writer.Principal.PolicyEpoch + 1),
            Grants = []
        };
        await SqlClusterProtocol.EqualAsync(revoked, await context.SdkAsync(context.Administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token)));
        await RefuseAsync(context.Writer, state.Models[0].Command, token);
        var restored = state.Writer.Principal with
        {
            PolicyEpoch = checked(revoked.PolicyEpoch + 1)
        };
        await SqlClusterProtocol.EqualAsync(restored, await context.SdkAsync(context.Administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token)));
        var receipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateRelationalAssertions.ReceiptAsync(receipt, command.CommandId, context.Partition,
            [new("putDocument", reference.Collection, reference.Id, 1)]);
        await SqlClusterProtocol.EqualAsync(receipt, await context.McpAsync<CommitReceipt>(context.Writer, McpCallerTools.DocumentsCommit, command, token));
        var expected = new DocumentReadResult(new(reference, 1, "{\"stage\":\"healthy-after-cold\"}", false, []));
        await SqlClusterProtocol.EqualAsync(expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token)));
        await SqlClusterProtocol.EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader, McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
        foreach (var model in state.Models)
        {
            await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        }
    }

    private static async Task RefuseAsync(KeyLoad.IntegrationTests.Features.ClusterRouting.RequestCqrsClusterCallers caller,
        CommandRequest command, CancellationToken token)
    {
        var denied = await caller.Sdk.CommitAsync(command, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token),
            ErrorCode.PermissionDenied, dispatched: true);
    }
}
