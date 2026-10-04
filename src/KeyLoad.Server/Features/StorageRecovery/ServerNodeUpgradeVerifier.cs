using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;

namespace KeyLoad.Server;

internal static class ServerNodeUpgradeVerifier
{
    internal static ReplicaHardState Verify(string directory, ServerNodeUpgradeReceipt receipt, NodeOptions options, bool published)
    {
        var verifier = Path.Combine(Path.GetDirectoryName(receipt.FinalDestination)!,
            ".node-upgrade-verify-" + Guid.NewGuid().ToString("N"));
        ServerNodeUpgradeFiles.CreatePrivateDirectory(verifier);
        Exception? failure = null;
        try
        {
            ServerNodeUpgradeAuthority.CopyStore(directory, verifier, ServerNodeUpgradeProtocol.Canonical);
            ServerNodeUpgradeAuthority.CopyStore(directory, verifier, ServerNodeUpgradeProtocol.Replica);
            var oldCopies = Path.Combine(verifier, "prior");
            ServerNodeUpgradeAuthority.CopyInputs(receipt.OriginalSource, oldCopies);
            var authority = ServerNodeUpgradeAuthority.VerifyCopies(oldCopies, options);
            return VerifyCopies(directory, verifier, receipt, options, authority, published);
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            DeletePreservingFailure(verifier, failure);
        }
    }

    private static void DeletePreservingFailure(string verifier, Exception? failure)
    {
        try
        { Directory.Delete(verifier, recursive: true); }
        catch (Exception cleanup) when (failure is not null) { throw new AggregateException(failure, cleanup); }
    }

    private static ReplicaHardState VerifyCopies(string original, string verifier, ServerNodeUpgradeReceipt receipt,
        NodeOptions options, ServerNodeUpgradeAuthority authority, bool published)
        => ServerNodeUpgradeStores.Run(verifier, options, stores =>
        {
            var database = new DatabaseEngine(stores.Canonical, new AuthorizationPolicy());
            var configuration = options.CreateReplicaConfiguration(verifier);
            ServerNodeUpgradeCurrentState.VerifyPersisted(stores.Replica);
            using var log = new DurableReplicaLog(stores.Replica, configuration, canonicalDatabase: database);
            ServerNodeUpgradeCurrentState.Verify(receipt, database, stores.Replica, log.State, authority, published);
            ServerNodeUpgradeCurrentImages.Verify(original, stores.Canonical, log.State, configuration.MaxSnapshotBytes,
                Path.Combine(verifier, "verified-images"));
            return log.State;
        });
}
