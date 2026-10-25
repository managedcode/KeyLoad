using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed record SqlNullDistinctCase(string Clause, Predicate Predicate, string[] Ids);
