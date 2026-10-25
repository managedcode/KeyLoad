namespace KeyLoad.ClusterTests.QueryExecution;

/// <summary>Builds the frozen Q1.Search statement from a real graph Orleans scenario.</summary>
internal static class SqlGraphSearchClusterScenario
{
    internal const string QuerySearchTool = "keyload_query_search";
    internal const string SearchStatement = "SELECT * FROM GRAPH_SEARCH('graphdocs', NULL, NULL, NULL, "
        + "GRAPH_RETRIEVER('graphlinks',GRAPH_SEEDS(ENTITY('graphprojects','root'),ENTITY('graphprojects','second-seed')),"
        + "4,30,60,NULL,1), NULL, NULL, 10, 60)";
    internal const string EmptyStatement = "SELECT * FROM GRAPH_SEARCH('graphdocs', NULL, NULL, NULL, "
        + "GRAPH_RETRIEVER('graphlinks',GRAPH_SEEDS(ENTITY('graphprojects','root'),ENTITY('graphprojects','second-seed')),"
        + "4,30,60,GRAPH_LABELS(),1), NULL, GRAPH_IDS(), 10, 60)";
    internal const string LabeledStatement = "SELECT * FROM GRAPH_SEARCH('graphdocs', NULL, NULL, NULL, "
        + "GRAPH_RETRIEVER('graphlinks',GRAPH_SEEDS(ENTITY('graphprojects','root'),ENTITY('graphprojects','second-seed')),"
        + "4,30,60,GRAPH_LABELS('related'),1), NULL, NULL, 10, 60)";

    internal static SqlGraphSearchRequest Request(PartitionRef partition, string statement = SearchStatement)
        => new(1, new QueryRequest(partition, statement, AllowFullScan: true));
}
