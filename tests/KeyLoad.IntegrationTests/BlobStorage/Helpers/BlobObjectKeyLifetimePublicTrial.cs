using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobObjectKeyLifetimePublicTrial
{
    private const string FirstPartitionKey = "first";
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            var states = await ConfigureAsync(callers, token);
            states[0].Admitted = states[0].Original;
            await BlobObjectKeyLifetimePublicOperations.BeginAsync(callers, states[0], states, false, token);
            await BlobObjectKeyLifetimePublicRefusal.RequireAsync(callers, states[1], states, token);
            states[2].Admitted = states[2].Original;
            await BlobObjectKeyLifetimePublicOperations.BeginAsync(callers, states[2], states, true, token);
            await BlobObjectKeyLifetimePublicRefusal.RequireAsync(callers, states[1], states, token);
            await BlobObjectKeyLifetimePublicOperations.AbortAsync(callers, states[0], states, token);
            await BlobObjectKeyLifetimePublicOperations.ReclaimAsync(callers, states[0], states, token);
            await BlobObjectKeyLifetimePublicRefusal.RequireAsync(callers, states[1], states, token);
            var freshAttempt = new BlobObjectKeyLifetimePublicState(states[1].Blob);
            states = [.. states, freshAttempt];
            await BlobObjectKeyLifetimePublicRefusal.RequireAsync(callers, freshAttempt, states, token);
            await BlobObjectKeyLifetimePublicOperations.PublishAsync(callers, states[3], states, token);
            await BlobObjectKeyLifetimePublicRefusal.RequireAsync(callers, states[1], states, token);
            await BlobObjectKeyLifetimePublicRefusal.RequireAsync(callers, freshAttempt, states, token);
            await BlobObjectKeyLifetimePublicOperations.AbortAsync(callers, states[2], states, token);
            await BlobObjectKeyLifetimePublicOperations.ReclaimAsync(callers, states[2], states, token);
            await BlobObjectKeyLifetimePublicRefusal.RequireAsync(callers, states[1], states, token);
            return true;
        }, token);
    }

    private static async Task<BlobObjectKeyLifetimePublicState[]> ConfigureAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var first = new PartitionRef("blob-key-slot-" + Guid.NewGuid().ToString("N"), "public-key-slot", "domain", FirstPartitionKey);
        var second = first with
        {
            PartitionKey = "second"
        };
        var a = new ResourceDefinition("resource-a", ResourceKind.BlobStore, first.TransactionDomainId)
        {
            BlobPolicy = new() { MaxBlobBytes = 1L, MaxReservedBytes = 1L, MaxObjectKeys = 1, MaxVersions = 3, MaxUploads = 3 }
        };
        var b = a with
        {
            Name = "resource-b"
        };
        await SqlRf3Protocol.EqualAsync(a, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(first.TenantId, first.DatabaseId, a), token)));
        await SqlRf3Protocol.EqualAsync(b, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(first.TenantId, first.DatabaseId, b), token)));
        return [new(new(first, a.Name, "first-empty")), new(new(second, a.Name, "second-empty")), new(new(second, b.Name, "independent-empty")), new(new(first, a.Name, "first-empty"))];
    }
}
