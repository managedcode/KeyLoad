using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationRows
{
    private const string Point = "point";
    private const string PublicAliasPrefix = "{\"odd.name\":\"";
    private const string PublicAliasSuffix = "\"}";
    private const string CanaryJsonPrefix = "{\"exposed\":\"";
    private const string CanaryJsonSuffix = "\"}";

    internal static async Task WaveAsync(SafeProjectorOperationDatabase owner, string rawJson, string safeJson,
        string status, long revision, bool raw, CancellationToken token)
    {
        var expectedJson = raw ? rawJson : safeJson;
        var paths = raw ? System.Collections.Immutable.ImmutableArray<string>.Empty : SafeProjectorOperationProtocol.RedactedPaths;
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var reference = new EntityRef(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First);
            var actual = owner.Database.GetDocument(SafeProjectorOperationProtocol.Reader, reference, cancellationToken: token);
            await SafeProjectorOperationSeed.EqualAsync(actual, new DocumentResult(reference, revision, expectedJson, !raw, paths));
        });
        await SelectionAsync(owner, SafeProjectorOperationRequests.StarSql, SafeProjectorOperationProtocol.Star,
            SafeProjectorOperationProtocol.Star, expectedJson, revision, raw, token);
        var selected = raw ? CanaryJsonPrefix + SafeProjectorOperationProtocol.Canary + CanaryJsonSuffix : SafeProjectorOperationProtocol.TypedNullJson;
        await SelectionAsync(owner, SafeProjectorOperationRequests.ProtectedSql, SafeProjectorOperationProtocol.LiteralPath,
            SafeProjectorOperationProtocol.Exposed, selected, revision, raw, token);
        await SelectionAsync(owner, SafeProjectorOperationRequests.PublicAliasSql, SafeProjectorOperationProtocol.StatusPath,
            SafeProjectorOperationProtocol.LiteralAlias, PublicAliasPrefix + status + PublicAliasSuffix, revision, raw, token);
        await SelectionAsync(owner, SafeProjectorOperationRequests.NullSql, SafeProjectorOperationProtocol.NullPath,
            SafeProjectorOperationProtocol.Exposed, SafeProjectorOperationProtocol.TypedNullJson, revision, raw, token);
        await SafeProjectorOperationSelectionAssertions.WaveAsync(owner, revision, raw, token);
    }

    private static async Task SelectionAsync(SafeProjectorOperationDatabase owner, string sql, string path,
        string alias, string expectedJson, long revision, bool raw, CancellationToken token)
    {
        var fields = raw ? System.Collections.Immutable.ImmutableArray<string>.Empty
            : path == SafeProjectorOperationProtocol.Star ? SafeProjectorOperationProtocol.RedactedPaths
            : path == SafeProjectorOperationProtocol.LiteralPath ? [SafeProjectorOperationProtocol.LiteralPath] : [];
        var queries = new QueryEngine(owner.Database, UnitExecutionOptions.QueryExecution());
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var expectedCut = owner.Store.Position;
            var actual = queries.Execute(SafeProjectorOperationProtocol.Reader,
                new(owner.Source.Partition, sql, AllowFullScan: true), cancellationToken: token);
            await PageAsync(actual, expectedJson, revision, raw, fields, expectedCut);
        });
        await SafeProjectorOperationImages.ObserveAsync(owner, async () =>
        {
            var expectedCut = owner.Store.Position;
            var actual = queries.ExecuteAst(SafeProjectorOperationProtocol.Reader,
                SafeProjectorOperationRequests.Select(owner.Source.Partition, path, alias), cancellationToken: token);
            await PageAsync(actual, expectedJson, revision, raw, fields, expectedCut);
        });
    }

    private static async Task PageAsync(QueryPage actual, string json, long revision, bool raw, System.Collections.Immutable.ImmutableArray<string> paths, long cut)
    {
        await SafeProjectorOperationSeed.EqualAsync(actual, new QueryPage(
            [new(SafeProjectorOperationProtocol.First, revision, json, !paths.IsEmpty, paths)], null, cut, Point));
        if (!raw)
        {
            await Assert.That(actual.Rows.Single().Json?.Contains(SafeProjectorOperationProtocol.Canary, StringComparison.Ordinal) is true).IsFalse();
        }
    }
}
