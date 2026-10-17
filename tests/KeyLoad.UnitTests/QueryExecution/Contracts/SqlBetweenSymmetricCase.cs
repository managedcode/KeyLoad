using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlBetweenSymmetricCase(string Clause, Predicate Predicate, string[] Ids);
