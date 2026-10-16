using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.ResourceExecution;
using P = KeyLoad.IntegrationTests.Features.RelationalStorage.DropTableLifecycleSeedProtocol;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropTableLifecycleSeed
{
    internal static async Task<DropTableLifecycleSeedState> CreateAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed nativeSeed, RemoteTransferNativeMembership membership,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var partition = P.Partition;
        var creations = await CreateTablesAsync(administrator, partition, token);
        ImmutableArray<ResourceDefinition> controls =
        [new(P.AnchorCollection, ResourceKind.Collection, partition.TransactionDomainId),
            P.Table(P.ControlTable, partition), new(P.Graph, ResourceKind.Graph, partition.TransactionDomainId)];
        foreach (var definition in controls)
        {
            var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
            await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(), request, token)));
        }
        var bound = await DropTableLifecycleSeedReferences.CaptureAsync(wave, nativeSeed, membership, partition, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(new(P.InitialVersion, partition), token));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            DropTableLifecycleSeedMutations.Create(bound.Anchor, bound.Rows), placement.PlacementEpoch);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await administrator.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value;
        await DropTableLifecycleSeedAssertions.ReceiptAsync(command, receipt, placement);
        var tables = await CaptureTablesAsync(wave, nativeSeed, membership, creations, bound, token);
        var state = new DropTableLifecycleSeedState(partition, tables, controls, command, receipt, bound.Anchor, bound.Control);
        await DropTableLifecycleSeedAssertions.BeforeAsync(administrator, state, token);
        return state;
    }

    private static async Task<ImmutableArray<DropTableLifecycleTable>> CaptureTablesAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        ImmutableArray<DropTableLifecycleCreation> creations, DropTableLifecycleBoundSeed bound, CancellationToken token)
    {
        var tables = ImmutableArray.CreateBuilder<DropTableLifecycleTable>();
        for (var ordinal = P.First; ordinal < P.TableCount; ordinal++)
        {
            var definition = creations[ordinal].Definition;
            var inventory = await InventoryAsync(wave, seed, membership, definition.Name, token);
            await SqlRf3Protocol.EqualAsync(definition, inventory.Control.Definition);
            await SqlRf3Protocol.EqualAsync(bound.Before[ordinal].Control.Lifetime, inventory.Control.Lifetime);
            var entry = await Assert.That(inventory.Entries).HasSingleItem();
            await SqlRf3Protocol.EqualAsync(P.Partition, entry.Partition);
            await SqlRf3Protocol.EqualAsync(inventory.Control.Lifetime, entry.Lifetime);
            tables.Add(new(definition.Name, definition, creations[ordinal].CommandId,
                new(P.Partition.TenantId, P.Partition.DatabaseId, definition), P.Create(definition.Name), bound.Rows[ordinal], inventory));
        }
        return tables.ToImmutable();
    }

    internal static async Task<ResourceOwnerInventoryResultV1> InventoryAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        string resource, CancellationToken token)
        => await ResourcePartitionInventoryRf3Assertions.SuccessAsync(
            await ResourcePartitionInventoryRf3Bridge.ReadAsync(wave, seed, membership, resource, token));

    internal static async Task<DropTableLifecycleCreation> RecreateAsync(RequestCqrsRf3Callers callers,
        DropTableLifecycleTable table, CancellationToken token)
    {
        var commandId = Guid.NewGuid();
        var partition = table.Rows[P.First].Reference.Partition;
        var actual = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(callers.Sdk,
            new(partition, table.CreateSql, CommandId: commandId), token);
        await SqlRf3Protocol.EqualAsync(table.Definition, actual);
        return new(commandId, actual);
    }

    private static async Task<ImmutableArray<DropTableLifecycleCreation>> CreateTablesAsync(
        RequestCqrsRf3Callers callers, PartitionRef partition, CancellationToken token)
    {
        var result = ImmutableArray.CreateBuilder<DropTableLifecycleCreation>();
        for (var ordinal = P.First; ordinal < P.TableCount; ordinal++)
        {
            var definition = P.Table(P.Names[ordinal], partition);
            var id = Guid.NewGuid();
            var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
            ResourceDefinition actual;
            if (ordinal == P.First)
            {
                actual = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(id, request, token));
            }
            else if (ordinal == P.Second)
            {
                var arguments = McpOfficialClient.Arguments(request);
                arguments.Add(McpCallerProtocol.CommandId, id);
                actual = (await McpCallerAssertions.SuccessAsync<ResourceDefinition>(await callers.Mcp.Client
                    .InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure, arguments, token))).Value;
            }
            else
            {
                actual = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(callers.Sdk,
                    new(partition, P.Create(definition.Name), CommandId: id), token);
            }

            await SqlRf3Protocol.EqualAsync(definition, actual);
            result.Add(new(id, actual));
        }
        return result.ToImmutable();
    }
}
