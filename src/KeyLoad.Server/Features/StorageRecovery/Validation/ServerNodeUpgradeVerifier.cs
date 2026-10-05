using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeVerifier
{
    internal static ReplicaHardState Verify(string directory, ServerNodeUpgradeReceipt receipt, ServerRuntimeOptions options, bool published)
        => ServerNodeUpgradePrivateDirectory.Run(Path.GetDirectoryName(receipt.FinalDestination)!, verifier =>
        {
            ServerNodeUpgradeAuthority.CopyStore(directory, verifier, ServerNodeUpgradeProtocol.Canonical);
            ServerNodeUpgradeAuthority.CopyStore(directory, verifier, ServerNodeUpgradeProtocol.Replica);
            var oldCopies = Path.Combine(verifier, "prior");
            ServerNodeUpgradeAuthority.CopyInputs(receipt.OriginalSource, oldCopies);
            var authority = ServerNodeUpgradeAuthority.VerifyCopies(oldCopies, options);
            return VerifyCopies(directory, verifier, receipt, options, authority, published);
        });

    private static ReplicaHardState VerifyCopies(string original, string verifier, ServerNodeUpgradeReceipt receipt,
        ServerRuntimeOptions options, ServerNodeUpgradeAuthority authority, bool published)
        => ServerNodeUpgradeStores.Run(verifier, options, stores =>
        {
            var database = new DatabaseEngine(stores.Canonical, new AuthorizationPolicy(), options.Core.DatabaseLimits, options.Core.DueWork, options.Core.EventSource, options.Core.Messaging, options.Core.GraphExecution);
            var configuration = ServerNodeUpgradeConfiguration.Replica(options, verifier);
            ServerNodeUpgradeCurrentState.VerifyPersisted(stores.Replica);
            using var log = new DurableReplicaLog(stores.Replica, configuration, canonicalDatabase: database);
            ServerNodeUpgradeCurrentState.Verify(receipt, database, stores.Replica, log.State, authority, published);
            ServerNodeUpgradeCurrentImages.Verify(original, stores.Canonical, log.State, configuration.Value.MaxSnapshotBytes,
                Path.Combine(verifier, "verified-images"));
            return log.State;
        });
}
