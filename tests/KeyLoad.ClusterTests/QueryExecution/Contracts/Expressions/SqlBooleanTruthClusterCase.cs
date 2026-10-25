using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SqlBooleanTruthClusterCase(string Clause, Predicate Predicate, string[] Ids);
