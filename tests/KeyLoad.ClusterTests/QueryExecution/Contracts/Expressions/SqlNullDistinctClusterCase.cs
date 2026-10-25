using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SqlNullDistinctClusterCase(string Clause, Predicate Predicate, string[] Ids);
