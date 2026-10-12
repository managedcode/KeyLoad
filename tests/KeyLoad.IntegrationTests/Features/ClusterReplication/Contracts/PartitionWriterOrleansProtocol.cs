namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class PartitionWriterOrleansProtocol
{
    internal const string AuthorityDetail = "The native command authority does not match its scope or payload.";
    internal const string OwnerMismatch = "The actual stopped native replica scope differs from its persisted control owner.";
    internal const string MissingRetainedEntry = "The actual terminal movement entry is not retained above the snapshot cut.";
    internal const string IncompleteImage = "The complete native writer image exceeds its original bound.";
    internal const long PositionStep = 1;
}
