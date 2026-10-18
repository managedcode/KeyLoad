using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobActiveUploadSlotPublicTrial
{
    private const string FirstPartitionKey = "first";
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            var states = await ConfigureAsync(callers, token);
            states[0].Admitted = states[0].Original;
            await BlobActiveUploadSlotPublicOperations.BeginAsync(callers, states[0], states, false, token);
            await BlobActiveUploadSlotPublicRefusal.RequireAsync(callers, states[1], states, token);
            states[2].Admitted = states[2].Original;
            await BlobActiveUploadSlotPublicOperations.BeginAsync(callers, states[2], states, true, token);
            await BlobActiveUploadSlotPublicRefusal.RequireAsync(callers, states[1], states, token);
            await BlobActiveUploadSlotPublicOperations.AbortAsync(callers, states[0], states, token);
            await BlobActiveUploadSlotPublicRefusal.RequireAsync(callers, states[1], states, token);
            await BlobActiveUploadSlotPublicOperations.PublishAsync(callers, states[1], states, token);
            await BlobActiveUploadSlotPublicRefusal.RequireAsync(callers, states[1], states, token);
            await BlobActiveUploadSlotPublicOperations.ReclaimAsync(callers, states[0], states, token);
            await BlobActiveUploadSlotPublicOperations.AbortAsync(callers, states[2], states, token);
            await BlobActiveUploadSlotPublicOperations.ReclaimAsync(callers, states[2], states, token);
            await BlobActiveUploadSlotPublicRefusal.RequireAsync(callers, states[1], states, token);
            return true;
        }, token);
    }

    private static async Task<BlobActiveUploadSlotPublicState[]> ConfigureAsync(RequestCqrsClusterCallers callers, CancellationToken token)
    {
        var first = new PartitionRef("blob-upload-slot-" + Guid.NewGuid().ToString("N"), "public-upload-slot", "domain", FirstPartitionKey);
        var second = first with
        {
            PartitionKey = "second"
        };
        var a = new ResourceDefinition("resource-a", ResourceKind.BlobStore, first.TransactionDomainId)
        {
            BlobPolicy = new() { MaxBlobBytes = 1L, MaxReservedBytes = 1L, MaxObjectKeys = 3, MaxVersions = 3, MaxUploads = 1 }
        };
        var b = a with
        {
            Name = "resource-b"
        };
        await SqlClusterProtocol.EqualAsync(a, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(first.TenantId, first.DatabaseId, a), token)));
        await SqlClusterProtocol.EqualAsync(b, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(first.TenantId, first.DatabaseId, b), token)));
        return [new(new(first, a.Name, "first-empty")), new(new(second, a.Name, "second-empty")), new(new(second, b.Name, "independent-empty"))];
    }
}
