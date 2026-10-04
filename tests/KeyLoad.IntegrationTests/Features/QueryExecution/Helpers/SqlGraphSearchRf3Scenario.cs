namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Builds the frozen Q1.Search.v1 statement from a real graph RF3 scenario.</summary>
internal static class SqlGraphSearchRf3Scenario
{
    internal const string QuerySearchTool = "keyload_query_search";
    internal const string SearchStatement = "SEARCH FROM graphdocs RETRIEVE GRAPH graphlinks "
        + "SEEDS ((graphprojects, 'root'), (graphprojects, 'second-seed')) "
        + "DEPTH 4 VERTICES 30 EDGES 60 LIMIT 10 FUSION 60";
    internal const string EmptyStatement = "SEARCH FROM graphdocs RETRIEVE GRAPH graphlinks "
        + "SEEDS ((graphprojects, 'root'), (graphprojects, 'second-seed')) "
        + "DEPTH 4 VERTICES 30 EDGES 60 LABELS () ALLOW IDS () LIMIT 10 FUSION 60";
    internal const string LabeledStatement = "SEARCH FROM graphdocs RETRIEVE GRAPH graphlinks "
        + "SEEDS ((graphprojects, 'root'), (graphprojects, 'second-seed')) "
        + "DEPTH 4 VERTICES 30 EDGES 60 LABELS ('related') LIMIT 10 FUSION 60";

    internal static SqlGraphSearchRequest Request(PartitionRef partition, string statement = SearchStatement)
        => new(1, new QueryRequest(partition, statement, AllowFullScan: true));
}
