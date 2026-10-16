using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateRelational
{
    internal static async Task InitializeAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        await Assert.That(ctx.ResourceCount).IsEqualTo(P.TableCount);
        await Assert.That(ctx.Bind(ctx.Anchor).Partition).IsEqualTo(ctx.Partition);
        foreach (var table in Enumerable.Range(P.First, P.TableCount))
        {
            var request = new SqlOperationRequest(ctx.Partition, P.Create[table], CommandId: Guid.NewGuid());
            var actual = table == P.First
                ? await FunctionalGateRelationalCalls.SqlSdkAsync<ResourceDefinition>(ctx, ctx.Administrator, request, token)
                : await FunctionalGateRelationalCalls.SqlMcpAsync<ResourceDefinition>(ctx, ctx.Administrator, request, token);
            await SqlRf3Protocol.EqualAsync(P.Definition(ctx.Partition, table), actual);
        }
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition,
        [
            new PutDocument(P.Parent, P.Baseline, FunctionalGateRelationalRows.Parent(ctx), P.Absent),
            new PutDocument(P.Child, P.Baseline, FunctionalGateRelationalRows.Child(P.Baseline, P.Baseline), P.Absent),
            new PutDocument(P.Independent, P.Baseline, FunctionalGateRelationalRows.Independent(P.Baseline), P.Absent)
        ]);
        var receipt = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateRelationalAssertions.ReceiptAsync(receipt, command.CommandId, ctx.Partition,
            [new(P.PutKind, P.Parent, P.Baseline, P.Initial), new(P.PutKind, P.Child, P.Baseline, P.Initial),
                new(P.PutKind, P.Independent, P.Baseline, P.Initial)]);
        DocumentResult[] baseline =
        [
            new(new(ctx.Partition, P.Parent, P.Baseline), P.Initial,
                FunctionalGateRelationalRows.Parent(ctx), false, []),
            new(new(ctx.Partition, P.Child, P.Baseline), P.Initial,
                FunctionalGateRelationalRows.Child(P.Baseline, P.Baseline), false, []),
            new(new(ctx.Partition, P.Independent, P.Baseline), P.Initial,
                FunctionalGateRelationalRows.Independent(P.Baseline), false, [])
        ];
        foreach (var expected in baseline)
        {
            await FunctionalGateEntityReferences.CaptureAsync(ctx, ctx.Administrator, expected, token);
        }
        await FunctionalGateRelationalAssertions.AnchorAsync(ctx, P.Baseline, receipt.Token, token);
        await FunctionalGateRelationalAssertions.BaselineAsync(ctx, token);
    }

    internal static async Task ExerciseAsync(FunctionalGateContext ctx, int iteration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = FunctionalGateRelationalRows.Identity(iteration);
        await FunctionalGateRelationalMutations.AddAsync(ctx, id, token);
        await FunctionalGateRelationalAssertions.ParentAsync(ctx, id, P.Initial, P.ParentBefore, token);
        await FunctionalGateRelationalAssertions.JoinAsync(ctx, id, false, token);
        await FunctionalGateRelationalMutations.LinkAsync(ctx, id, token);
        await FunctionalGateRelationalAssertions.ParentAsync(ctx, id, P.Updated, P.ParentAfter, token);
        await FunctionalGateRelationalAssertions.JoinAsync(ctx, id, true, token);
        await FunctionalGateRelationalMutations.UnlinkAsync(ctx, id, token);
        await FunctionalGateRelationalAssertions.JoinAsync(ctx, id, false, token);
        await FunctionalGateRelationalMutations.DeleteAsync(ctx, id, token);
        await FunctionalGateRelationalAssertions.MissingAsync(ctx, id, token);
        await FunctionalGateRelationalAssertions.BaselineAsync(ctx, token);
    }
}
