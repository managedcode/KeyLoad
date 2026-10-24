using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlBetweenSymmetricClusterCase(string Clause, Predicate Predicate, string[] Ids);
