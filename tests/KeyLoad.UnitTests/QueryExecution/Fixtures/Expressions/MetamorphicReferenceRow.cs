namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record MetamorphicReferenceRow(string Id, string Status, decimal? Score, bool Missing, int Bucket, string Tag, string Json);
