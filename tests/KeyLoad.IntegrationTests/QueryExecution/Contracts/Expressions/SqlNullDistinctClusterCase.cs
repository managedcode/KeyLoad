using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlNullDistinctClusterCase(string Clause, Predicate Predicate, string[] Ids);
