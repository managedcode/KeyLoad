namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3Protocol
{
    internal const string Node1 = "node1";
    internal const string Node2 = "node2";
    internal const string Node3 = "node3";
    internal const string CohortEnabled = "--KeyLoadTests:ProtocolCohort:Enabled=true";
    internal const string VoterPrefix = "--KeyLoadTests:ProtocolCohort:Voters:";
    internal const string DataRootPrefix = "--KeyLoad:DataRoot=";
    internal const string Ephemeral = "--KeyLoad:Ephemeral=true";
    internal const string Rpc1ImageEnvironment = "KEYLOAD_RPC1_SERVER_IMAGE";
    internal const string Rpc1ReceiptEnvironment = "KEYLOAD_RPC1_IMAGE_RECEIPT";
    internal const string Rpc1ManifestEnvironment = "KEYLOAD_RPC1_SERVER_MANIFEST";
    internal const string AdminCollection = "cluster-routing-c1";
    internal const string Database = "database";
    internal const string Domain = "cluster-routing-c1";
    internal const string DocumentId = "stable-command";
    internal const string DocumentJson = "{\"cohort\":\"same-data-epoch\",\"value\":1}";
    internal const string ChangedDocumentJson = "{\"cohort\":\"same-data-epoch\",\"value\":2}";
    internal const string FollowerLossScenario = "cluster-routing-c1-compatible-follower-loss";
    internal const string ReadyPath = "health/ready";
    internal static readonly Uri ReadyUri = new(ReadyPath, UriKind.Relative);
    internal const int NodeCount = 3;
    internal const int CurrentMajority = 2;
    internal const int ApplicationProtocolVersion = 3;
    internal const int PeerEnvelopeVersion = 3;
    internal const int RequestCount = 20;
    internal static readonly TimeSpan ParentDeadline = TimeSpan.FromMinutes(12);
    internal static readonly TimeSpan WaveDeadline = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan RequestDeadline = TimeSpan.FromSeconds(30);

    internal static string NodeName(int index) => index switch
    {
        0 => Node1,
        1 => Node2,
        2 => Node3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
