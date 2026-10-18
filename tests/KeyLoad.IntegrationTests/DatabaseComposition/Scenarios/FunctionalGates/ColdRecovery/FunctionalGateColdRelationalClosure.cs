using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Preserves original typed rows, real document links and complete joins through warm and cold callers.</summary>
internal static class FunctionalGateColdRelationalClosure
{
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string ValuePath = "/value";
    private const string ReferencePath = "/reference";
    private const string ChangedFirstValue = "38";
    private const string ChangedIndependentValue = "39";
    private const string InvalidTextValue = "false";
    private const int OriginalRevision = 1;
    private const int OriginalValue = 37;

    internal static async Task OriginalsAsync(FunctionalGateContext context, FunctionalGateColdState state,
        CancellationToken token)
    {
        await Assert.That(state.Models.Length).IsEqualTo(FunctionalGateColdProtocol.Resources);
        await ConstraintAsync(context, state, token);
        foreach (var caller in new[] { context.Writer, context.Reader, context.QueueAdministrator })
        {
            await VerifyAsync(context with
            {
                Reader = caller
            }, state, token);
        }
    }

    internal static async Task VerifyAsync(FunctionalGateContext context, FunctionalGateColdState state,
        CancellationToken token)
    {
        await Assert.That(state.Models.Length).IsEqualTo(FunctionalGateColdProtocol.Resources);
        foreach (var model in state.Models)
        {
            var next = state.Models.Single(item => item.Index == (model.Index + 1) % state.Models.Length);
            var request = Join(context, model, next);
            var expected = new QueryRow(FunctionalGateColdProtocol.Id, OriginalRevision,
                Projection(context, model.Index, next.Index), false, [],
                [new(LeftAlias, FunctionalGateColdProtocol.Id, OriginalRevision),
                    new(RightAlias, FunctionalGateColdProtocol.Id, OriginalRevision)]);
            var sdk = await FunctionalGateRelationalCalls.SqlSdkAsync<QueryPage>(context, context.Reader, request, token);
            var mcp = await FunctionalGateRelationalCalls.SqlMcpAsync<QueryPage>(context, context.Reader, request, token);
            await FunctionalGateRelationalAssertions.PageAsync(sdk, [expected]);
            await FunctionalGateRelationalAssertions.PageAsync(mcp, [expected]);
            await Assert.That(sdk.AccessPath).IsEqualTo(FunctionalGateRelationalProtocol.JoinAccessPath);
            await Assert.That(mcp.AccessPath).IsEqualTo(FunctionalGateRelationalProtocol.JoinAccessPath);
        }
    }

    private static async Task ConstraintAsync(FunctionalGateContext context, FunctionalGateColdState state,
        CancellationToken token)
    {
        var first = state.Models.Single(model => model.Index == 0);
        var constrained = state.Models.Single(model => model.Index == 1);
        var independent = state.Models.Single(model => model.Index == 2);
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
        [
            new PatchDocument(first.TableDefinition.Name, FunctionalGateColdProtocol.Id,
                [new(ValuePath, PatchKind.Set, ChangedFirstValue)], OriginalRevision),
            new PatchDocument(independent.TableDefinition.Name, FunctionalGateColdProtocol.Id,
                [new(ValuePath, PatchKind.Set, ChangedIndependentValue)], OriginalRevision),
            new PatchDocument(constrained.TableDefinition.Name, FunctionalGateColdProtocol.Id,
                [new(ReferencePath, PatchKind.Set, InvalidTextValue)], OriginalRevision)
        ]);
        var denied = await context.Writer.Sdk.CommitAsync(command, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.Validation.ToString());
        await Assert.That(denied.Problem?.StatusCode).IsEqualTo(Errors.Status(ErrorCode.Validation));
        await VerifyAsync(context, state, token);
        var official = await context.Writer.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            FunctionalGateRelationalCalls.Commit(command), token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.Validation, dispatched: true);
        await VerifyAsync(context, state, token);
        await SqlClusterProtocol.EqualAsync(first.TableReceipt,
            await FunctionalGateRelationalCalls.SqlSdkAsync<CommitReceipt>(context, context.Writer, first.TableInsert, token));
        await SqlClusterProtocol.EqualAsync(first.TableReceipt,
            await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context, context.Writer, first.TableInsert, token));
    }

    private static SqlOperationRequest Join(FunctionalGateContext context, FunctionalGateColdModel left,
        FunctionalGateColdModel right)
        => FunctionalGateColdProtocol.Select(context.Partition, left.Index) with
        {
            Sql = "SELECT TOP (2) l.[value] AS left_value,r.[value] AS right_value,"
                + "l.[reference] AS left_reference,r.[reference] AS right_reference FROM ["
                + left.TableDefinition.Name + "] AS l INNER JOIN [" + right.TableDefinition.Name
                + "] AS r ON l.id = r.id WHERE l.id = @id ORDER BY l.id"
        };

    private static string Projection(FunctionalGateContext context, int left, int right)
        => JsonSerializer.Serialize(new
        {
            left_value = OriginalValue,
            right_value = OriginalValue,
            left_reference = JsonSerializer.Serialize(context.Bind(new EntityRef(context.Partition,
                FunctionalGateColdProtocol.Resource("docs", left), FunctionalGateColdProtocol.Id)), JsonDefaults.Options),
            right_reference = JsonSerializer.Serialize(context.Bind(new EntityRef(context.Partition,
                FunctionalGateColdProtocol.Resource("docs", right), FunctionalGateColdProtocol.Id)), JsonDefaults.Options)
        }, JsonDefaults.Options);
}
