using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadCancellationR49Seed
{
    internal static async Task RunAsync(LiveReadCancellationR49Owner owner, CancellationToken token)
    {
        var partition = owner.Identity.Partition;
        var definition = new ResourceDefinition(LiveReadCancellationR49Protocol.Collection,
            ResourceKind.Collection, partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, definition), token)));
        var principal = new PrincipalRecord(owner.Identity.PrincipalId, partition.TenantId,
            [new(partition.DatabaseId, LiveReadCancellationR49Protocol.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.ChangesRead)], [])
        { PolicyEpoch = RequestCqrsPhaseFaultProvisioning.InitialPolicyEpoch + 1 };
        await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
        var placement = new AtomicPartitionPlacementReadRequest(1, partition);
        owner.Placement = await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.ReadAtomicPartitionPlacementAsync(placement, token));
        await SqlRf3Protocol.EqualAsync(owner.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await owner.Administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value);
        owner.Query = KeyLoadQuery.From<LiveReadCancellationR49Protocol.DocumentModel>(partition,
            LiveReadCancellationR49Protocol.Collection, IntegrationClientOptions.Translation()).ToRequest(true);
        owner.Seed = new(Guid.NewGuid(), partition, [new PutDocument(LiveReadCancellationR49Protocol.Collection,
            LiveReadCancellationR49Protocol.Document, global::System.Text.Json.JsonSerializer.Serialize(
                new LiveReadCancellationR49Protocol.DocumentModel(LiveReadCancellationR49Protocol.InitialValue), JsonDefaults.Options))]);
        owner.SeedReceipt = await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.CommitAsync(owner.Seed, token));
        await LiveReadCancellationR49Current.ReceiptAsync(owner.SeedReceipt, owner.Seed.CommandId,
            LiveReadCancellationR49Protocol.InitialRevision, owner);
        owner.Outbox = await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.OutboxStatusAsync(partition, token));
        await Assert.That(owner.Outbox.Head.Tail).IsEqualTo(LiveReadCancellationR49Protocol.InitialTail);
        await Assert.That(owner.Outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(owner.Outbox.Head.StoredRecords).IsEqualTo(LiveReadCancellationR49Protocol.InitialTail);
        await Assert.That(owner.Outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(owner.Outbox.Consumers.IsEmpty).IsTrue();
    }
}
