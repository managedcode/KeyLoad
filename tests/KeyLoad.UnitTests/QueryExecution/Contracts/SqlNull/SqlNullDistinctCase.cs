using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlNullDistinctCase(string Clause, Predicate Predicate, string[] Ids);
