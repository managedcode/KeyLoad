using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Provisioning
{
    private const int Version = 1;
    private const long PromotionBytes = 1;
    private const long DemotionBytes = 0;
    private const long UnavailableFreeBytes = long.MaxValue;
    private const long MinimumFreeBytes = 256L * 1024 * 1024;
    private const int MinimumFreeBasisPoints = 2000;
    private const int MaximumMemoryBasisPoints = 8000;
    private const int MaximumCpuBasisPoints = 8500;

    internal static async Task<State> CreateAsync(PartitionMovementPublicParentRf3Seed seed,
        CancellationToken token)
    {
        var control = seed.Partition with
        {
            PartitionKey = "capacity-control-original"
        };
        var command = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents,
            "capacity-control", "{\"original\":true}")) with
        {
            Partition = control
        };
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var unavailable = new TenantPlacementPolicyV1(Version, seed.Partition.TenantId,
            seed.Partition.DatabaseId, Version, true, PromotionBytes, DemotionBytes,
            UnavailableFreeBytes, MinimumFreeBasisPoints, MaximumMemoryBasisPoints,
            MaximumCpuBasisPoints, false,
            [new(seed.Directory.ControlOwner.PhysicalShardId, Version),
                new(seed.FirstRequest.DestinationPhysicalShardId, Version)]);
        var configure = new ConfigurePlacementPolicyRequest(0, unavailable, control);
        var id = Guid.NewGuid();
        var original = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigurePlacementPolicyAsync(id, configure, token));
        await SqlRf3Protocol.EqualAsync(unavailable, original);
        await SqlRf3Protocol.EqualAsync(original, (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.ConfigureTool,
                new
                {
                    commandId = id,
                    request = configure
                }, token))).Value);
        return new(control, command, receipt, id, configure, unavailable with
        {
            Revision = Version + Version,
            MinimumFreeBytes = MinimumFreeBytes
        });
    }

    internal sealed record State(PartitionRef Control, CommandRequest ControlCommand,
        CommitReceipt ControlReceipt, Guid OriginalPolicyCommand,
        ConfigurePlacementPolicyRequest OriginalPolicy, TenantPlacementPolicyV1 HealthyPolicy);
}
