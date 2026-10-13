using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextPartitionGenerationPublicSeed
{
    internal const string Id = "same-document";
    internal const string Term = "needle";
    internal const string LeftJson = "{\"text\":\"needle\",\"side\":\"left\"}";
    internal const string RightJson = "{\"text\":\"needle\",\"side\":\"right\"}";

    internal static async Task<NativeTextPartitionGenerationPublicState[]> CreateAsync(
        RequestCqrsRf3Callers caller, CancellationToken token)
    {
        var left = NativeTextMaintenanceRf3Scenario.CreatePartition() with { PartitionKey = "left" };
        var right = left with { PartitionKey = "right" };
        await Assert.That(left.AtomicPartitionId).IsNotEqualTo(right.AtomicPartitionId);
        var resource = new ResourceDefinition(NativeTextMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, left.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(left.TenantId,
                left.DatabaseId, resource), token)));
        var first = await CreateOneAsync(caller, left, LeftJson, resource, NativeTextMaintenancePath.Sdk, token);
        var second = await CreateOneAsync(caller, right, RightJson, resource, NativeTextMaintenancePath.Mcp, token);
        await Assert.That(first.Build.Consumer.Name).IsEqualTo(second.Build.Consumer.Name);
        await Assert.That(first.Build.IndexGeneration).IsEqualTo(second.Build.IndexGeneration);
        await Assert.That(first.Build.Consumer.Partition).IsNotEqualTo(second.Build.Consumer.Partition);
        return [first, second];
    }

    private static async Task<NativeTextPartitionGenerationPublicState> CreateOneAsync(
        RequestCqrsRf3Callers caller, PartitionRef partition, string json, ResourceDefinition resource,
        NativeTextMaintenancePath path, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(resource.Name, Id, json, 0L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt[] { new("putDocument", resource.Name, Id, 1L) },
            receipt.Mutations.ToArray());
        var build = await new NativeTextMaintenanceRf3Scenario(partition).RequestAsync(caller.Sdk, caller.Mcp, token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, build, path, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        await NativeTextPartitionGenerationPublicAssertions.FirstAsync(built, build, before, after);
        await Assert.That(built.Source!.ResourceSha256).IsEqualTo(Convert.ToHexStringLower(
            SHA256.HashData(NativeSerialization.Serialize(resource))));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.OutboxStatusAsync(partition, token));
        return new(partition, json, command, receipt, build, built, outbox);
    }
}
