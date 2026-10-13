using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedAssertions
{
    private const long OriginalDocumentRevision = 1;
    private const string McpUnavailableDetail = "The database operation could not be completed.";
    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, CancellationToken token)
    {
        var request = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], capacity.Control);
        var sdk = await seed.Source.PlanTenantPlacementAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.ResourceExhausted);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.ResourceExhausted,
            TenantPlacementPolicyProtocol.ScopeMismatch), sdk.Problem!);
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new { request }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpUnavailableDetail);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, capacity, token);
    }

    internal static async Task SiblingAsync(TwoRf3MembershipWave wave, TenantDedicatedOwnerRf3State packed,
        AtomicPartitionPlacementResolution expected, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            wave.Profile.AdminKey, async root =>
            {
                await SqlRf3Protocol.EqualAsync(expected,
                    await TenantPackingActivationRf3Admission.ReadAsync(root, packed.Sibling.Partition, token));
                return expected;
            }, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            packed.Sibling.Secret, async caller =>
            {
                await TenantPackingActivationRf3Assertions.ReplayAsync(caller,
                    packed.SiblingSeed, packed.SiblingReceipt, token);
                await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(caller, packed.Sibling,
                    RequestCqrsRf3Protocol.DocumentJson, OriginalDocumentRevision, token);
                return packed.SiblingReceipt;
            }, token);
        await NativeActivationMigrationRf3Ownership.RequirePhysicalAsync(packed.Physical,
            await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, token));
    }
}
