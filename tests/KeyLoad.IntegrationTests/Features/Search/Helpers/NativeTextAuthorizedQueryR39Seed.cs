using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextAuthorizedQueryR39Seed
{
    internal const string OwnerA = "authorized-text-owner-a";
    internal const string OwnerB = "authorized-text-owner-b";
    internal const string ReadGrant = "authorized-text.read";
    internal const string UseGrant = "authorized-text.use";
    internal const string UkrainianJson = "{\"text\":\"привіт needle\",\"owner\":\"authorized-text-owner-a\"}";
    internal const string EnglishJson = "{\"text\":\"hello needle\",\"owner\":\"authorized-text-owner-b\"}";

    internal static async Task<NativeTextAuthorizedQueryR39State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers admin, CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        var resource = new ResourceDefinition(NativeTextMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        { FieldPolicies = [new(NativeTextMaintenanceRf3Scenario.Field, "private", ReadGrant, UseGrant)] };
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(scenario.Partition.TenantId,
                scenario.Partition.DatabaseId, resource), token)));
        var request = await scenario.RequestAsync(admin.Sdk, admin.Mcp, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(
            new(1, scenario.Partition), token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, new AtomicPartitionPlacementReadRequest(1,
                scenario.Partition), token))).Value);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(resource.Name, NativeTextMaintenanceRf3Scenario.Ukrainian, UkrainianJson, 0L, new(OwnerA)),
             new PutDocument(resource.Name, NativeTextMaintenanceRf3Scenario.English, EnglishJson, 0L, new(OwnerB))]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await SqlRf3Protocol.EqualAsync(new CommitToken(placement.Incarnation, scenario.Partition.AtomicPartitionId,
            receipt.Token.Position, placement.PlacementEpoch), receipt.Token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new MutationReceipt[] { new("putDocument", resource.Name,
            NativeTextMaintenanceRf3Scenario.Ukrainian, 1L), new("putDocument", resource.Name,
            NativeTextMaintenanceRf3Scenario.English, 1L) }, receipt.Mutations.ToArray());
        var before = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(admin.Sdk, admin.Mcp, request,
            NativeTextMaintenancePath.Sdk, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
        await NativeTextReleasedRepairPublicAssertions.FirstAsync(built, request, before, after);
        await Assert.That(built.Source!.Incarnation).IsEqualTo(request.Placement.Incarnation);
        await Assert.That(built.Source.ResourceSha256).IsEqualTo(Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(NativeSerialization.Serialize(resource))));
        var principal = new PrincipalRecord("authorized-text-reader-" + Guid.NewGuid().ToString("N"),
            scenario.Partition.TenantId, [new(scenario.Partition.DatabaseId, resource.Name,
                Capability.Query | Capability.DocumentsRead)], [ReadGrant, UseGrant])
        { OwnerId = OwnerA, RestrictRows = true, PolicyEpoch = 1L };
        var identity = await NativeTextRf3Scenario.ConfigureIdentityAsync(fixture, principal, token);
        await SqlRf3Protocol.EqualAsync(principal, identity.Principal);
        return new(new(scenario, resource, command, receipt, request, built), identity);
    }
}
