namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardUncertainProtocol
{
    internal const string Root = "root";
    internal const string Collection = "forward_uncertain_documents";
    internal const string Lost = "lost-reply";
    internal const string Healthy = "healthy-followup";
    internal const string Json = "{\"value\":37}";
    internal const string Sentinel = "The genuine post-MAC forward reply was lost.";
    internal const string Missing = "The genuine forward completion observation is missing.";
    internal const int Zero = 0;
    internal const int One = 1;
    internal const int Two = 2;
    internal const int Voters = 3;
    internal const long NoBytes = 0;
    internal const int MaximumObserved = 32;
    internal const long InitialRevision = 1;
    internal const string PutKind = "putDocument";
}
