using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleWitnessStores
{
    internal static ZoneTreeStore Open(string path, PhysicalShardRecord owner)
    {
        var execution = IntegrationExecutionOptions.StorageExecution();
        var options = new ZoneTreeStoreOptions(path) { Incarnation = owner.Incarnation }
            .ResolveExecutionOptions(execution);
        var identity = ZoneTreeIdentityFile.Read(
            KeyLoad.IntegrationTests.Features.ClusterRouting.NativeBlobWholePaths.Identity(path),
            options.MaximumIdentityFileBytes, options.StreamBufferBytes);
        return ZoneTreeExistingStore.Open(options, identity.NodeId, execution);
    }

    internal static void Require(ZoneTreeStore canonical, ZoneTreeStore replica,
        PhysicalShardRecord owner, DropTableLifecycleTable table)
    {
        var identity = canonical.Identity;
        var cuts = table.Inventory.OwnerCuts.Where(cut => cut.NodeId == identity.NodeId
            && PhysicalOwnerEntryValidation.SameOwner(cut.ObservedOwner, owner)).ToArray();
        if (identity.Incarnation != owner.Incarnation || replica.Identity.Incarnation != owner.Incarnation
            || replica.Identity.NodeId == identity.NodeId || cuts.Length != 1
            || cuts[0].StoreIncarnation != identity.Incarnation
            || identity.ReadGeneration < cuts[0].ReadGeneration
            || !CryptographicOperations.FixedTimeEquals(identity.SigningKey.Span, replica.Identity.SigningKey.Span))
        {
            throw DropTableLifecycleWitnessHistory.Invalid();
        }
    }

    internal static DatabaseEngine Database(ZoneTreeStore canonical, PhysicalShardRecord owner)
        => new(canonical, new AuthorizationPolicy(), IntegrationExecutionOptions.DatabaseLimits(),
            IntegrationExecutionOptions.DueWork(), IntegrationExecutionOptions.EventSource(),
            IntegrationExecutionOptions.Messaging(), IntegrationExecutionOptions.GraphExecution(),
            IntegrationExecutionOptions.ChangeFeedExecution(), IntegrationExecutionOptions.BlobExecution(),
            IntegrationExecutionOptions.NativeClaimsExecution(), IntegrationExecutionOptions.TimeSeriesExecution(),
            IntegrationExecutionOptions.MovementCheckpoints(), UnavailablePartitionMovementCheckpointVerifier.Instance,
            physicalOwner: owner);
}
