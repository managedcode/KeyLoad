using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ResourceExecution;
using P = KeyLoad.IntegrationTests.Features.RelationalStorage.DropTableLifecycleSeedProtocol;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Uses an actual authenticated public inventory witness; a reference binding grants no role or admission.</summary>
internal static class DropTableLifecycleSeedReferences
{
    internal static async Task<DropTableLifecycleBoundSeed> CaptureAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        PartitionRef partition, CancellationToken token)
    {
        var before = ImmutableArray.CreateBuilder<ResourceOwnerInventoryResultV1>();
        for (var ordinal = P.First; ordinal < P.TableCount; ordinal++)
        {
            before.Add(await DropTableLifecycleSeed.InventoryAsync(wave, seed, membership, P.Names[ordinal], token));
        }

        var rows = Enumerable.Range(P.First, P.TableCount).Select(ordinal =>
            BindRows(P.Rows(partition, ordinal), before[ordinal])).ToImmutableArray();
        var anchor = Bind(new(new(partition, P.AnchorCollection, P.AnchorId), P.Initial, P.AnchorJson, false, []),
            await DropTableLifecycleSeed.InventoryAsync(wave, seed, membership, P.AnchorCollection, token));
        var control = Bind(new(new(partition, P.ControlTable, P.ControlId), P.Initial, P.ControlJson, false, []),
            await DropTableLifecycleSeed.InventoryAsync(wave, seed, membership, P.ControlTable, token));
        return new(before.ToImmutable(), rows, anchor, control);
    }

    internal static ImmutableArray<DocumentResult> BindRows(ImmutableArray<DocumentResult> rows,
        ResourceOwnerInventoryResultV1 actualInventory)
        => rows.Select(row => Bind(row, actualInventory)).ToImmutableArray();

    internal static DocumentResult Bind(DocumentResult row, ResourceOwnerInventoryResultV1 actualInventory)
    {
        var lifetime = actualInventory.Control.Lifetime;
        if (!actualInventory.Complete || lifetime.Kind != ResourceKind.Collection
            || row.Reference.Partition.TenantId != lifetime.TenantId
            || row.Reference.Partition.DatabaseId != lifetime.DatabaseId
            || row.Reference.Partition.TransactionDomainId != lifetime.TransactionDomainId
            || row.Reference.Collection != lifetime.ResourceName)
        {
            throw new InvalidOperationException("The actual inventory lifetime must match the literal document reference.");
        }
        return row with
        {
            Reference = row.Reference with
            {
                Lifetime = new EntityRefLifetimeV1(lifetime.Incarnation.Value)
            }
        };
    }
}
