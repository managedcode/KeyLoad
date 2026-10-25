using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed record SqlBooleanTruthCase(string Clause, Predicate Predicate, string[] Ids);
