namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaHeartbeatAdmissionProtocol
{
    private const string PartitionKey = "original";
    internal const string Root = "root";
    internal const string Collection = "heartbeat-admission";
    internal const string Document = "original";
    internal const string FirstJson = "{\"value\":1}";
    internal const string SecondJson = "{\"value\":2}";
    internal const string PutKind = "putDocument";
    internal const string MethodTag = "rpc.method";
    internal const string TargetTag = "orleans.rpc.target_id";
    internal const string ExchangeOperation = "IPartitionReplicaGrainService/ExchangeAsync";
    internal const string Exchange = "KeyLoad.Clustering.IPartitionReplicaGrainService/ExchangeAsync";
    internal const string AddressSeparator = "/";
    internal const string Missing = "The real native heartbeat admission operation did not reach its held check.";
    internal const int Empty = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Voters = 3;
    internal const int Majority = 2;
    internal const long FirstRevision = 1;
    internal const long SecondRevision = 2;
    internal static readonly PartitionRef Partition = new("tenant", "database", "heartbeat", PartitionKey);
}
