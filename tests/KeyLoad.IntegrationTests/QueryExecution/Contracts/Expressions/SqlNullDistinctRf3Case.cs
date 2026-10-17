using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlNullDistinctRf3Case(string Clause, Predicate Predicate, string[] Ids);
