namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record MetamorphicPublicRow(string Id, string Status, decimal? Score, bool Missing, int Bucket, string Tag, string Json);
