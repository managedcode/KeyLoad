namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Independent operation identities and bounds for the literal still-live former-leader acceptance flow.</summary>
internal static class ReplicaIsolationFlowProtocol
{
    internal const int MaximumRetainedStatusObservations = 32;
    internal const int Zero = 0;
    internal const int First = 1;
    internal const int Second = 2;
    internal const int Voters = 3;
    internal const int ServiceUnavailable = 503;
    internal const string NoLeader = "The cluster has no current leader with a reachable majority.";
    internal const string Mutation = "putDocument";
    internal const string ListCounters = "-L";
    internal const string Verbose = "-v";
    internal const string Numeric = "-n";
    internal const string ExactCounters = "-x";
    internal const int PacketColumn = 0;
    internal const int ByteColumn = 1;
    internal const int HeaderRows = 2;
}
