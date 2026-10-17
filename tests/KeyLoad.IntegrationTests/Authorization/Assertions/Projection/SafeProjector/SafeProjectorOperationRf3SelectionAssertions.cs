using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3SelectionAssertions
{
    private const string Point = "point";

    internal static async Task WaveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool raw, CancellationToken token)
    {
        foreach (var scenario in SafeProjectorOperationRf3SelectionCases.Cases)
        {
            foreach (var ast in new[] { false, true })
            {
                await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
                {
                    var actual = ast ? await reader.Sdk.QueryAstAsync(SafeProjectorOperationRf3SelectionCases.Ast(
                        history.State.Partition, history.State.Collection, scenario.Id, scenario.Path), token)
                        : await reader.Sdk.QueryAsync(SafeProjectorOperationRf3SelectionCases.Sql(
                            history.State.Partition, history.State.Collection, scenario.Id, scenario.SqlSource), token);
                    await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, scenario, raw);
                }, token);
                await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
                {
                    var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                        ast ? (object)SafeProjectorOperationRf3SelectionCases.Ast(history.State.Partition,
                            history.State.Collection, scenario.Id, scenario.Path) : SafeProjectorOperationRf3SelectionCases.Sql(
                            history.State.Partition, history.State.Collection, scenario.Id, scenario.SqlSource), token);
                    await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, history, scenario, raw);
                    if (!raw)
                    {
                        await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationRf3Protocol.Canary);
                    }
                }, token);
            }
        }
        await SafeProjectorOperationRf3SelectionRefusals.VerifyAsync(root, reader, history, token);
    }

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationRf3History history,
        SafeProjectorOperationRf3Selection scenario, bool raw)
    {
        var hidden = scenario.HiddenSourcePath;
        var paths = !raw && hidden is not null ? ImmutableArray.Create(hidden) : ImmutableArray<string>.Empty;
        var revision = scenario.Id == SafeProjectorOperationRf3Protocol.First
            ? history.Revision : SafeProjectorOperationRf3Protocol.InitialRevision;
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationRf3Protocol.PositivePublicPosition);
        await SqlRf3Protocol.EqualAsync(new QueryPage(
            [new(scenario.Id, revision, raw ? scenario.RawJson : scenario.MaskedJson, !paths.IsEmpty, paths)],
            null, actual.CutPosition, Point), actual);
    }
}
