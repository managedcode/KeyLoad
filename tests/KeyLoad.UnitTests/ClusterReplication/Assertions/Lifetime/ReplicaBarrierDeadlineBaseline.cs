using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Checks a reopened native owner against its actual original pre-journal creation without applying it again.</summary>
internal static class ReplicaBarrierDeadlineBaseline
{
    internal const string MissingOriginal = "The reopened native owner has no retained original resource creation.";
    private const string Changed = "The reopened native owner changed its original resource definition or lifetime.";

    internal static void Require(DatabaseEngine database, ReplicaConfiguration configuration, ReplicatedOperation original)
    {
        PhysicalShardTestBootstrap.RequireExisting(database, configuration.VoterIds);
        var definition = new ResourceDefinition(P.Collection, ResourceKind.Collection, P.Partition.TransactionDomainId);
        var expected = ResourceLifetimeIdentity.Create(original, P.Partition.TenantId, P.Partition.DatabaseId, definition);
        database.Store.Read(view =>
        {
            var current = view.GetRecord<ResourceDefinition>(KeySpace.Resource(P.Partition.TenantId,
                P.Partition.DatabaseId, P.Collection));
            var lifetime = ResourceLifetimeCatalog.Read(view, P.Partition.TenantId, P.Partition.DatabaseId,
                P.Collection, current);
            if (!NativeSerialization.Serialize(definition).AsSpan().SequenceEqual(NativeSerialization.Serialize(current))
                || !NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(lifetime)))
            {
                throw new InvalidOperationException(Changed);
            }
            return true;
        });
    }
}
