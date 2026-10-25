using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Search;

internal static class Bm25CorpusReuse
{
    internal static SearchScore[] Rank(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, SearchRequest request, ReadExecutionBudget work, TextRanker ranker)
    {
        var resource = database.Resource(work.CreateView(view), request.Partition,
            request.Collection, ResourceKind.Collection);
        var statistics = (database.ReadBm25CorpusCurrent(view, principal, resource, request.Partition,
            request.Collection, request.TextField!, TextProjectionProtocol.TokenizerVersion, ranker.Terms, work)).Value;
        return statistics is null ? ranker.Rank() : ranker.Rank(statistics);
    }
}
