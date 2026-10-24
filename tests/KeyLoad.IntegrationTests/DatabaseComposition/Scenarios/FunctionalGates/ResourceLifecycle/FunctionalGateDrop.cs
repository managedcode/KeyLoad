using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using KeyLoad.IntegrationTests.RelationalStorage;
using P = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateRelationalProtocol;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateDrop
{
    internal static async Task<FunctionalGateDropState> RunAsync(FunctionalGateContext context,
        CancellationToken token)
    {
        var original = new CommandRequest(Guid.NewGuid(), context.Partition,
            [.. Enumerable.Range(P.First, P.TableCount).Select(index => new PutDocument(P.Tables[index],
                FunctionalGateDropRows.OriginalId, FunctionalGateDropRows.Json(context, index,
                    FunctionalGateDropRows.OriginalId), P.Absent))]);
        var receipt = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(original, token));
        await FunctionalGateCompositionCalls.ReceiptAsync(context, receipt, original.CommandId,
            [.. P.Tables.Select(name => new MutationReceipt(P.PutKind, name,
                FunctionalGateDropRows.OriginalId, P.Initial))]);
        var references = await CaptureAsync(context, token);
        var analyticsLink = await FunctionalGateAnalyticsLinkLifecycle.PrepareAsync(context, token);
        var drops = new DropTableResult[P.TableCount];
        var parents = Enumerable.Range(P.First, P.TableCount).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var index in new[] { P.Second, P.First, P.TableCount - 1 })
        {
            var request = Request(context, index);
            drops[index] = await FunctionalGateDropCalls.InvokeAsync(context, parents[index], request,
                (DropLifecycleClusterRoute)index, token);
            await FunctionalGateDropAssertions.ResultAsync(context, parents[index], request, drops[index]);
        }
        var tables = ImmutableArray.CreateBuilder<FunctionalGateDropTable>();
        foreach (var index in Enumerable.Range(P.First, P.TableCount))
        {
            var create = new SqlOperationRequest(context.Partition, P.Create[index], CommandId: Guid.NewGuid());
            await SqlClusterProtocol.EqualAsync(P.Definition(context.Partition, index),
                await FunctionalGateRelationalCalls.SqlSdkAsync<ResourceDefinition>(context,
                    context.Administrator, create, token));
            var expectedLifetime = FunctionalGateCreationLifetime.Create(context, create.CommandId!.Value,
                P.Definition(context.Partition, index), table: true);
            tables.Add(await RecreateAsync(context, index, references[index], parents[index], drops[index], expectedLifetime, token));
        }
        var state = new FunctionalGateDropState(original, receipt, tables.ToImmutable());
        await FunctionalGateAnalyticsLinkLifecycle.VerifyAsync(context, state, analyticsLink, token);
        await VerifyAsync(context, state, token);
        return state;
    }

    private static async Task<EntityRef[]> CaptureAsync(FunctionalGateContext context, CancellationToken token)
    {
        var references = new EntityRef[P.TableCount];
        foreach (var index in Enumerable.Range(P.First, P.TableCount))
        {
            var expected = new DocumentResult(FunctionalGateDropRows.Reference(context, index,
                FunctionalGateDropRows.OriginalId), P.Initial, FunctionalGateDropRows.Json(context, index,
                FunctionalGateDropRows.OriginalId), false, []);
            references[index] = await FunctionalGateEntityReferences.CaptureAsync(context,
                context.Administrator, expected, token);
        }
        return references;
    }

    private static async Task<FunctionalGateDropTable> RecreateAsync(FunctionalGateContext context, int index,
        EntityRef original, Guid parent, DropTableResult dropped, EntityRefLifetime expectedLifetime, CancellationToken token)
    {
        var reference = FunctionalGateDropRows.Reference(context, index, FunctionalGateDropRows.FreshId);
        var json = FunctionalGateDropRows.Json(context, index, reference.Id);
        var command = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(reference.Collection, reference.Id, json, P.Absent)]);
        var receipt = await context.McpAsync<CommitReceipt>(context.Administrator,
            McpCallerTools.DocumentsCommit, command, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, receipt, command.CommandId,
            [new(P.PutKind, reference.Collection, reference.Id, P.Initial)]);
        var bound = await FunctionalGateEntityReferences.CaptureRecreatedAsync(context,
            context.Administrator, original, new(reference, P.Initial, json, false, []), token);
        await Assert.That(bound.Lifetime).IsEqualTo(expectedLifetime);
        await Assert.That(bound.Lifetime).IsNotEqualTo(original.Lifetime);
        return new(index, original, parent, Request(context, index), dropped,
            new(bound, P.Initial, json, false, []), command, receipt);
    }

    internal static Task VerifyAsync(FunctionalGateContext context, FunctionalGateDropState state,
        CancellationToken token) => FunctionalGateDropReplay.VerifyAsync(context, state, token);

    private static DropTableRequest Request(FunctionalGateContext context, int index)
        => new(context.Partition.TenantId, context.Partition.DatabaseId, P.Tables[index]);
}
