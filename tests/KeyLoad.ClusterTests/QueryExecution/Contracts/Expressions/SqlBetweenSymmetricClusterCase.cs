using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SqlBetweenSymmetricClusterCase(string Clause, Predicate Predicate, string[] Ids);
