namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlNullDistinctClusterState(SqlNullOrderingClusterState Seed, long LowRevision, string LowJson, CommitReceipt Current)
{
    internal const string Low = "low";
    internal const long FaultRevision = 2;
    internal const long RepairedRevision = 3;
    internal const string FaultJson = "{\"label\":\"low\",\"score\":true}";
    internal const string HealthyJson = "{\"label\":\"low\",\"score\":1}";
    internal const string AccessPath = "bounded-full-scan";
    internal const string PutKind = "putDocument";
    internal const int AgainstOne = 2;
    internal const int First = 0;
}
