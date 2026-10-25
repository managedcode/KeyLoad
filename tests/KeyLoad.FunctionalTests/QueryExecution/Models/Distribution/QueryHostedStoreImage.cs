namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed record QueryHostedStoreImage(long AppliedPosition, long StorePosition, string[] Records);
