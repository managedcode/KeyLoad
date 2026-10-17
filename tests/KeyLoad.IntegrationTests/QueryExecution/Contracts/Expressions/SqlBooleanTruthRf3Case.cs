using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlBooleanTruthRf3Case(string Clause, Predicate Predicate, string[] Ids);
