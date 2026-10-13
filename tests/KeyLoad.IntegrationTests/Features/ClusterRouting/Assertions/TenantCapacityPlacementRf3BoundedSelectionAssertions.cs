using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3BoundedSelectionAssertions
{
    private const int FirstPartition = 0;
    private const int RefusedPartitionCount = 65;
    private const string McpInvalidDetail = "The database operation could not be completed.";
    internal static TenantPlacementPlanRequest Request(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity)
    {
        var partitions = Enumerable.Range(FirstPartition, RefusedPartitionCount)
            .Select(_ => seed.Partition with { PartitionKey = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat) })
            .ToImmutableArray();
        return new(seed.Partition.TenantId, seed.Partition.DatabaseId, partitions, capacity.Control);
    }

    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantPlacementPlanRequest request, CancellationToken token)
    {
        await Assert.That(request.Partitions.Length).IsEqualTo(RefusedPartitionCount);
        await Assert.That(request.Partitions.Distinct().Count()).IsEqualTo(RefusedPartitionCount);
        var sdk = await seed.Source.PlanTenantPlacementAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.Validation);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid), sdk.Problem!);
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new { request }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Validation, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpInvalidDetail);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
    }
}
