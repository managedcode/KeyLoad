namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record SqlBetweenSymmetricClusterState(SqlNullOrderingClusterState Seed, long LowRevision, string LowJson, CommitReceipt Current)
{
    internal const string Low = "low";
    internal const long FaultRevision = 2;
    internal const long RepairedRevision = 3;
    internal const string FaultJson = "{\"label\":\"low\",\"score\":true}";
    internal const string HealthyJson = "{\"label\":\"low\",\"score\":1}";
    internal const string AccessPath = "bounded-full-scan";
    internal const string PutKind = "putDocument";
    internal const int AgainstOne = 0;
    internal const int First = 0;
    internal static readonly (string Id, string Json)[] Rows =
    [
        ("missing", "{\"label\":\"missing\"}"),
        ("null_a", "{\"label\":\"null_a\",\"score\":null}"),
        ("null_b", "{\"label\":\"null_b\",\"score\":null}"),
        ("low", HealthyJson),
        ("high", "{\"label\":\"high\",\"score\":2}")
    ];
}
