using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlNullDistinctClusterCase(string Clause, Predicate Predicate, string[] Ids);
