namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record QueryHostedStoreImage(long AppliedPosition, long StorePosition, string[] Records);
