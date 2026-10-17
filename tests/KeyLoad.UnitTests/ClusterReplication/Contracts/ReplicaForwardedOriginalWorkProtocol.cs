namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaForwardedOriginalWorkProtocol
{
    internal const string Root = "root";
    internal const string Collection = "forwarded_work_docs";
    internal const string Anchor = "anchor";
    internal const string Cancelled = "cancelled";
    internal const string Healthy = "healthy";
    internal const string AnchorJson = "{\"kind\":\"anchor\"}";
    internal const string HealthyJson = "{\"kind\":\"healthy\"}";
    internal const string PutKind = "putDocument";
    internal const string MissingObservation = "The actual forwarded receiver observation is missing.";
    internal const string ObservationLimit = "The forwarded receiver observation bound is exceeded.";
    internal const int MaximumObserved = 32;
    internal const long InitialRevision = 1;
    internal const long NewRecordVersion = 0;
    internal const long NoReadBytes = 0;
    internal const int NoRetainedEntries = 0;
    internal const int SingleDependency = 1;
    internal const int NextLogEntry = 1;
    internal static readonly TimeSpan LeaderPoll = TimeSpan.FromMilliseconds(100);
}
