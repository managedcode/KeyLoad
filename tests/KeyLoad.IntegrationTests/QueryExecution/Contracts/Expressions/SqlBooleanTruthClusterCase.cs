using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlBooleanTruthClusterCase(string Clause, Predicate Predicate, string[] Ids);
