using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaLifecycleStoreCut(long Position, StoreIdentity Identity, byte[] SignerDigest, string[] Rows);
internal sealed record ReplicaLifecycleAcquisitionState(ReplicaConfiguration Configuration, ReplicatedOperation Original,
    CommitReceipt Receipt, EntityRef Reference, ReplicaLifecycleStoreCut Canonical, ReplicaLifecycleStoreCut Replica,
    byte[] HardState, long Applied);
