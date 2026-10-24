using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlBooleanTruthClusterCase(string Clause, Predicate Predicate, string[] Ids);
