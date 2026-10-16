namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardMetadataProtocol
{
    internal const string Writer = "forward-metadata-writer";
    internal const string Missing = "The genuine forwarded metadata observation is missing.";
    internal const string Overlap = "The original forwarded metadata observation is already active.";
    internal const string Overflow = "The genuine forwarded metadata observation exceeds its bound.";
    internal const int MaximumObserved = 32;
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    internal const int NoRecords = 0;
    internal const long NoBytes = 0;
    internal const int SingleStep = 1;
    internal const int SourceAndReceiverPrincipalReads = 2;
    internal const int SecondForward = 2;
    internal const long FirstRevision = 1;
    internal const long NewDocument = 0;
}
