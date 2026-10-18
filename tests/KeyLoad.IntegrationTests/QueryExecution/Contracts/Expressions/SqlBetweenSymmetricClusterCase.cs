using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlBetweenSymmetricClusterCase(string Clause, Predicate Predicate, string[] Ids);
