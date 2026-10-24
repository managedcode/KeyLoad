using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record SqlNullDistinctCase(string Clause, Predicate Predicate, string[] Ids);
