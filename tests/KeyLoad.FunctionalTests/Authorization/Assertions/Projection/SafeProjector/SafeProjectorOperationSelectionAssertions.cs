using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class SafeProjectorOperationSelectionAssertions
{
    private const string Point = "point";

    internal static async Task WaveAsync(SafeProjectorOperationDatabase owner, long revision, bool raw, CancellationToken token)
    {
        var query = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
        foreach (var scenario in SafeProjectorOperationSelectionCases.Cases)
        {
            foreach (var ast in new[] { false, true })
            {
                await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
                {
                    var cut = owner.Store.Position;
                    var actual = ast ? query.ExecuteAst(SafeProjectorOperationProtocol.Reader,
                        SafeProjectorOperationSelectionCases.Ast(owner.Source.Partition, SafeProjectorOperationProtocol.Collection,
                            scenario.Id, scenario.Path), cancellationToken: token)
                        : query.Execute(SafeProjectorOperationProtocol.Reader,
                            SafeProjectorOperationSelectionCases.Sql(owner.Source.Partition, SafeProjectorOperationProtocol.Collection,
                                scenario.Id, scenario.SqlSource), cancellationToken: token);
                    var hidden = scenario.HiddenSourcePath;
                    var paths = !raw && hidden is not null ? ImmutableArray.Create(hidden) : ImmutableArray<string>.Empty;
                    var expectedRevision = scenario.Id == SafeProjectorOperationProtocol.First
                        ? revision : SafeProjectorOperationProtocol.InitialRevision;
                    await SafeProjectorOperationSeed.EqualAsync(actual, new QueryPage(
                        [new(scenario.Id, expectedRevision, raw ? scenario.RawJson : scenario.MaskedJson, !paths.IsEmpty, paths)],
                        null, cut, Point));
                });
            }
        }
        await SafeProjectorOperationSelectionRefusals.VerifyAsync(owner, query, token);
    }
}
