using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record SqlBetweenSymmetricCase(string Clause, Predicate Predicate, string[] Ids);
