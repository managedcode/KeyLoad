using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdDrop
{
    internal static async Task<FunctionalGateColdDropState> RunAsync(FunctionalGateContext context,
        FunctionalGateColdState original, CancellationToken token)
    {
        await Assert.That(original.Models.Length).IsEqualTo(FunctionalGateColdProtocol.Resources);
        var tables = ImmutableArray.CreateBuilder<FunctionalGateColdDropTable>();
        foreach (var model in original.Models.OrderBy(value => value.Index))
        {
            var literal = FunctionalGateColdDropRows.Original(context, model);
            var oldReference = await FunctionalGateEntityReferences.CaptureAsync(context,
                context.Administrator, literal, token);
            var administrator = await SeedAdministratorAsync(context, model, token);
            var parentId = Guid.NewGuid();
            var request = new DropTableRequest(context.Partition.TenantId, context.Partition.DatabaseId,
                model.TableDefinition.Name);
            var dropped = await FunctionalGateDropCalls.InvokeAsync(context, parentId, request,
                (DropLifecycleRf3Route)model.Index, token);
            await FunctionalGateDropAssertions.ResultAsync(parentId, request, dropped);
            var create = model.TableCreate with
            {
                CommandId = Guid.NewGuid()
            };
            await SqlRf3Protocol.EqualAsync(model.TableDefinition,
                await FunctionalGateRelationalCalls.SqlSdkAsync<ResourceDefinition>(context,
                    context.Administrator, create, token));
            tables.Add(await RecreateAsync(context, model, oldReference, parentId, request, dropped, administrator, token));
        }
        var state = new FunctionalGateColdDropState(tables.ToImmutable());
        await VerifyAsync(context, original, state, token);
        return state;
    }

    private static async Task<FunctionalGateColdDropTable> RecreateAsync(FunctionalGateContext context,
        FunctionalGateColdModel model, EntityRef oldReference, Guid parentId, DropTableRequest request,
        DropTableResult dropped,
        (SqlOperationRequest Query, CommandRequest Command, CommitReceipt Receipt) administrator, CancellationToken token)
    {
        var literal = FunctionalGateColdDropRows.Fresh(context, model);
        var command = new CommandRequest(Guid.NewGuid(), context.Partition,
            [new PutDocument(literal.Reference.Collection, literal.Reference.Id, literal.Json,
                FunctionalGateColdDropRows.Absent)]);
        var receipt = await context.McpAsync<CommitReceipt>(context.Administrator,
            McpCallerTools.DocumentsCommit, command, token);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, receipt, command.CommandId,
            [new("putDocument", literal.Reference.Collection, literal.Reference.Id,
                FunctionalGateColdDropRows.Initial)], token);
        var reference = await FunctionalGateEntityReferences.CaptureRecreatedAsync(context,
            context.Administrator, oldReference, literal, token);
        var originalInsert = new CommandRequest(model.TableInsert.CommandId!.Value, context.Partition,
            [new InsertTypedRow(model.TableDefinition.Name, FunctionalGateColdDropRows.Original(context, model).Json)]);
        return new(model.Index, oldReference, parentId, request, dropped, originalInsert,
            literal with
            {
                Reference = reference
            }, command, receipt,
            administrator.Query, administrator.Command, administrator.Receipt);
    }

    private static async Task<(SqlOperationRequest Query, CommandRequest Command, CommitReceipt Receipt)> SeedAdministratorAsync(
        FunctionalGateContext context, FunctionalGateColdModel model, CancellationToken token)
    {
        var parameters = new Dictionary<string, JsonElement>(model.TableInsert.Parameters!, StringComparer.Ordinal)
        {
            [FunctionalGateColdProtocol.IdColumn] = JsonSerializer.SerializeToElement(FunctionalGateColdDropRows.AdministratorId)
        };
        var query = model.TableInsert with
        {
            CommandId = Guid.NewGuid(),
            Parameters = parameters
        };
        var receipt = await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context,
            context.Administrator, query, token);
        var literal = FunctionalGateColdDropRows.Administrator(context, model);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, receipt, query.CommandId!.Value,
            [new("putDocument", model.TableDefinition.Name, literal.Reference.Id, FunctionalGateColdDropRows.Initial)], token);
        await FunctionalGateEntityReferences.CaptureAsync(context, context.Administrator, literal, token);
        return (query, new(query.CommandId.Value, context.Partition,
            [new InsertTypedRow(model.TableDefinition.Name, literal.Json)]), receipt);
    }

    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdState original,
        FunctionalGateColdDropState state, CancellationToken token)
    {
        await Assert.That(state.Tables.Length).IsEqualTo(original.Models.Length);
        foreach (var model in original.Models)
        {
            await FunctionalGateColdReadAssertions.VerifyRecreatedAsync(context, model,
                () => VerifyTableAsync(context, model, state, token), token);
        }
        await FunctionalGateColdDropReplay.VerifyAsync(context, original, state, token);
    }

    internal static Task VerifyTableAsync(FunctionalGateContext context, FunctionalGateColdModel model,
        FunctionalGateColdDropState state, CancellationToken token)
        => FunctionalGateColdDropAssertions.TableAsync(context,
            state.Tables.Single(table => table.Index == model.Index), token);
}
