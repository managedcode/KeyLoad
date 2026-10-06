using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeVerifier
{
    private const string PriorInputsDirectoryName = "prior";

    private const string VerifiedImagesDirectory = "verified-images";

    internal static ReplicaHardState Verify(string directory, ServerNodeUpgradeReceipt receipt, ServerRuntimeOptions options, bool published)
        => ServerNodeUpgradePrivateDirectory.Run(Path.GetDirectoryName(receipt.FinalDestination)!, verifier =>
        {
            ServerNodeUpgradeAuthority.CopyStore(directory, verifier, ServerNodeUpgradeProtocol.Canonical, executionOptions: options.NodeUpgrade);
            ServerNodeUpgradeAuthority.CopyStore(directory, verifier, ServerNodeUpgradeProtocol.Replica, executionOptions: options.NodeUpgrade);
            var oldCopies = Path.Combine(verifier, PriorInputsDirectoryName);
            ServerNodeUpgradeAuthority.CopyInputs(receipt.OriginalSource, oldCopies, executionOptions: options.NodeUpgrade);
            var authority = ServerNodeUpgradeAuthority.VerifyCopies(oldCopies, options);
            return VerifyCopies(directory, verifier, receipt, options, authority, published);
        });

    private static ReplicaHardState VerifyCopies(string original, string verifier, ServerNodeUpgradeReceipt receipt,
        ServerRuntimeOptions options, ServerNodeUpgradeAuthority authority, bool published)
        => ServerNodeUpgradeStores.Run(verifier, options, stores =>
        {
            var database = new DatabaseEngine(stores.Canonical, new AuthorizationPolicy(), options.Core.DatabaseLimits, options.Core.DueWork, options.Core.EventSource, options.Core.Messaging, options.Core.GraphExecution, options.Core.ChangeFeedExecution, options.Core.BlobExecution, options.Core.NativeClaimsExecution, options.Core.TimeSeriesExecution);
            var configuration = ServerNodeUpgradeConfiguration.Replica(options, verifier);
            ServerNodeUpgradeCurrentState.VerifyPersisted(stores.Replica);
            using var log = new DurableReplicaLog(stores.Replica, configuration, canonicalDatabase: database);
            ServerNodeUpgradeCurrentState.Verify(receipt, database, stores.Replica, log.State, authority, published);
            ServerNodeUpgradeCurrentImages.Verify(original, stores.Canonical, log.State, configuration,
                Path.Combine(verifier, VerifiedImagesDirectory), executionOptions: options.NodeUpgrade,
                recoveryOptions: options.OfflineRecovery);
            return log.State;
        });
}
