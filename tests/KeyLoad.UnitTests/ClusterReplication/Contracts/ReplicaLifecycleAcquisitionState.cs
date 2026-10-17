using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaLifecycleAcquisitionState(ReplicaConfiguration Configuration, ReplicatedOperation Original,
    CommitReceipt Receipt, EntityRef Reference, ReplicaLifecycleStoreCut Canonical, ReplicaLifecycleStoreCut Replica,
    byte[] HardState, long Applied);
