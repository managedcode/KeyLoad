using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdWorkloadAuthorization
{
    internal static async Task RunAsync(FunctionalGateContext context, FunctionalGateColdWorkload proof,
        FunctionalGateColdState state, CancellationToken token)
    {
        foreach (var foreign in state.Workloads.Where(item => item.Identity.Principal.Id != proof.Identity.Principal.Id))
        {
            await DeniedAsync(context, foreign.Command, foreign.Expected.Document!.Reference, token);
        }
        var revoked = proof.Identity.Principal with
        {
            PolicyEpoch = checked(proof.Identity.Principal.PolicyEpoch + 1),
            Grants = []
        };
        await SqlClusterProtocol.EqualAsync(revoked, await context.SdkAsync(
            context.Administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), revoked, token)));
        await DeniedAsync(context, proof.Command, proof.Expected.Document!.Reference, token);
        var restored = proof.Identity.Principal with
        {
            PolicyEpoch = checked(revoked.PolicyEpoch + 1)
        };
        await SqlClusterProtocol.EqualAsync(restored, await context.SdkAsync(
            context.Administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), restored, token)));
        await HealthyAsync(context, proof, token);
        foreach (var model in state.Models)
        {
            await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        }
    }

    private static async Task DeniedAsync(FunctionalGateContext context, CommandRequest command,
        EntityRef reference, CancellationToken token)
    {
        var write = await context.Writer.Sdk.CommitAsync(command, token);
        await Assert.That(write.IsFailed).IsTrue();
        await Assert.That(write.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        _ = (await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token), ErrorCode.PermissionDenied, dispatched: true)).Value;
        var read = await context.Reader.Sdk.GetAsync(reference, token);
        await Assert.That(read.IsFailed).IsTrue();
        await Assert.That(read.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        _ = (await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token), ErrorCode.PermissionDenied, dispatched: true)).Value;
    }

    private static async Task HealthyAsync(FunctionalGateContext context, FunctionalGateColdWorkload proof,
        CancellationToken token)
    {
        var original = proof.Expected.Document!;
        var reference = original.Reference with
        {
            Id = "healthy-after-repair"
        };
        var command = new CommandRequest(Guid.NewGuid(), reference.Partition,
            [new PutDocument(reference.Collection, reference.Id, original.Json, 0)]);
        var receipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateRelationalAssertions.ReceiptAsync(context, receipt, command.CommandId, reference.Partition,
            [new("putDocument", reference.Collection, reference.Id, 1)]);
        await SqlClusterProtocol.EqualAsync(receipt, await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, command, token));
        var expected = new DocumentReadResult(original with
        {
            Reference = reference
        });
        await SqlClusterProtocol.EqualAsync(expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(reference, token)));
        await SqlClusterProtocol.EqualAsync(expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token));
        await SqlClusterProtocol.EqualAsync(proof.Expected, await context.SdkAsync(context.Reader.Sdk.GetAsync(original.Reference, token)));
        await SqlClusterProtocol.EqualAsync(proof.Expected, await context.McpAsync<DocumentReadResult>(context.Reader,
            McpCallerTools.DocumentsGet, new GetDocumentRequest(original.Reference), token));
    }
}
