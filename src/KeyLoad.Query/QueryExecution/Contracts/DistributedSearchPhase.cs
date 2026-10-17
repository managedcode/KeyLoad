namespace KeyLoad.Query.Features.QueryExecution;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(DistributedSearchAliases.Phase)]
internal enum DistributedSearchPhase
{
    Statistics = 0,
    Candidates = 1,
    Projection = 2,
    Revalidate = 3
}
