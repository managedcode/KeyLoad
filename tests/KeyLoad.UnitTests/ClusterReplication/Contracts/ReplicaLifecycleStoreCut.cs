using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaLifecycleStoreCut(long Position, StoreIdentity Identity, byte[] SignerDigest, string[] Rows);
