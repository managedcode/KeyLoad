using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterSelectionAssertions
{
    private const string Point = "point";

    internal static async Task WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool raw, CancellationToken token)
    {
        foreach (var scenario in SafeProjectorOperationClusterSelectionCases.Cases)
        {
            foreach (var ast in new[] { false, true })
            {
                await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
                {
                    var actual = ast ? await reader.Sdk.QueryAstAsync(SafeProjectorOperationClusterSelectionCases.Ast(
                        history.State.Partition, history.State.Collection, scenario.Id, scenario.Path), token)
                        : await reader.Sdk.QueryAsync(SafeProjectorOperationClusterSelectionCases.Sql(
                            history.State.Partition, history.State.Collection, scenario.Id, scenario.SqlSource), token);
                    await PageAsync(await McpCallerAssertions.SdkSuccessAsync(actual), history, scenario, raw);
                }, token);
                await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
                {
                    var reply = await reader.Mcp.CallAsync(ast ? McpCallerTools.QueryAst : McpCallerTools.QueryExecute,
                        ast ? (object)SafeProjectorOperationClusterSelectionCases.Ast(history.State.Partition,
                            history.State.Collection, scenario.Id, scenario.Path) : SafeProjectorOperationClusterSelectionCases.Sql(
                            history.State.Partition, history.State.Collection, scenario.Id, scenario.SqlSource), token);
                    await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value, history, scenario, raw);
                    if (!raw)
                    {
                        await McpCallerAssertions.DoesNotDiscloseAsync(reply, history.State.Key, SafeProjectorOperationClusterProtocol.Canary);
                    }
                }, token);
            }
        }
        await SafeProjectorOperationClusterSelectionRefusals.VerifyAsync(root, reader, history, token);
    }

    private static async Task PageAsync(QueryPage actual, SafeProjectorOperationClusterHistory history,
        SafeProjectorOperationClusterSelection scenario, bool raw)
    {
        var hidden = scenario.HiddenSourcePath;
        var paths = !raw && hidden is not null ? ImmutableArray.Create(hidden) : ImmutableArray<string>.Empty;
        var revision = scenario.Id == SafeProjectorOperationClusterProtocol.First
            ? history.Revision : SafeProjectorOperationClusterProtocol.InitialRevision;
        await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationClusterProtocol.PositivePublicPosition);
        await SqlClusterProtocol.EqualAsync(new QueryPage(
            [new(scenario.Id, revision, raw ? scenario.RawJson : scenario.MaskedJson, !paths.IsEmpty, paths)],
            null, actual.CutPosition, Point), actual);
    }
}
