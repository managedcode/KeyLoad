using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record OperationalAnalyticsNativeCut(ReplicatedOperation Original, CommitReceipt Receipt,
    string[] Canonical, long Position, KeyLoad.Storage.StoreIdentity Identity, (long LastIndex, long CommittedIndex, long Term) Journal,
    ReplicaNodeState Snapshot);
