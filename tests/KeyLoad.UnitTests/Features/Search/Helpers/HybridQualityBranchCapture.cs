using System.Collections.Immutable;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class HybridQualityBranchCapture
{
    internal static async Task<ImmutableArray<HybridQualityBranch>> CaptureAsync(SearchEngine engine,
        TestDatabase database, HybridQualityQuery query, CancellationToken cancellationToken)
    {
        var branches = ImmutableArray.CreateBuilder<HybridQualityBranch>(3);
        if (query.Text is not null)
        {
            var textRequest = new SearchRequest(database.Partition, HybridQualityCorpus.Collection,
                HybridQualityCorpus.TextField, query.Text, Limit: HybridQualityCorpus.ResultLimit,
                TextWeight: HybridQualityCorpus.BranchWeight, VectorWeight: 0,
                FusionConstant: HybridQualityCorpus.FusionConstant, AllowedIds: query.EligibleIds);
            var hits = await engine.SearchAsync(HybridQualityCorpus.PrincipalId, textRequest, cancellationToken);
            branches.Add(new("text", GlobalBranchKind.Text, [.. hits]));
        }
        if (query.Vector is { } vector)
        {
            var vectorRequest = new SearchRequest(database.Partition, HybridQualityCorpus.Collection,
                VectorField: HybridQualityCorpus.VectorField, Vector: vector, Space: HybridQualityCorpus.Space,
                Limit: HybridQualityCorpus.ResultLimit, TextWeight: 0,
                VectorWeight: HybridQualityCorpus.BranchWeight,
                FusionConstant: HybridQualityCorpus.FusionConstant, AllowedIds: query.EligibleIds);
            var hits = await engine.SearchAsync(HybridQualityCorpus.PrincipalId, vectorRequest, cancellationToken);
            branches.Add(new("vector", GlobalBranchKind.Vector, [.. hits]));
        }
        if (query.IncludeGraph)
        {
            var graphSearch = new SearchRequest(database.Partition, HybridQualityCorpus.Collection,
                Limit: HybridQualityCorpus.ResultLimit, FusionConstant: HybridQualityCorpus.FusionConstant,
                AllowedIds: query.EligibleIds);
            var walk = new GraphWalkSpec(HybridQualityCorpus.Graph, [HybridQualityCorpus.Root(database)],
                MaxDepth: 1, MaxVertices: HybridQualityCorpus.ResultLimit, MaxEdges: 64);
            var graphRequest = new GraphSearchRequest(1, graphSearch,
                Retriever: new(walk, HybridQualityCorpus.BranchWeight));
            var result = await engine.GraphSearchAsync(HybridQualityCorpus.PrincipalId, graphRequest, cancellationToken);
            branches.Add(new("graph", GlobalBranchKind.Graph, result.Hits));
        }
        return branches.ToImmutable();
    }
}
