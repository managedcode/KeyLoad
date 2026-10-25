using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed record SqlBetweenSymmetricCase(string Clause, Predicate Predicate, string[] Ids);
