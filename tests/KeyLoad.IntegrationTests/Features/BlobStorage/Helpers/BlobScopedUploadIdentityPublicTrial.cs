using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobScopedUploadIdentityPublicTrial
{
    private const string FirstPartitionKey = "first";
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async callers =>
        {
            var states = await ConfigureAsync(callers, token);
            for (var index = 0; index < 4; index++)
            {
                states[index].Admitted = states[index].Original;
                await BlobScopedUploadIdentityPublicOperations.BeginAsync(callers, states[index], states, index % 2 == 1, token);
            }
            await BlobScopedUploadIdentityPublicOperations.AbortAsync(callers, states[0], states, token);
            await BlobScopedUploadIdentityPublicOperations.ReclaimAsync(callers, states[0], states, token);
            for (var index = 1; index < 4; index++)
            {
                await BlobScopedUploadIdentityPublicOperations.PublishAsync(callers, states[index], states, token);
            }
            await BlobScopedUploadIdentityPublicOperations.RemovedAsync(callers, states[0], states, token);
            return true;
        }, token);
    }

    private static async Task<BlobScopedUploadIdentityPublicState[]> ConfigureAsync(RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var first = new PartitionRef("blob-scoped-upload-" + Guid.NewGuid().ToString("N"), "public-scoped-upload", "domain", FirstPartitionKey);
        var second = first with
        {
            PartitionKey = "second"
        };
        var a = new ResourceDefinition("resource-a", ResourceKind.BlobStore, first.TransactionDomainId)
        {
            BlobPolicy = new() { MaxBlobBytes = 1L, MaxReservedBytes = 1L, MaxObjectKeys = 4, MaxVersions = 4, MaxUploads = 4 }
        };
        var b = a with
        {
            Name = "resource-b"
        };
        await SqlRf3Protocol.EqualAsync(a, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(first.TenantId, first.DatabaseId, a), token)));
        await SqlRf3Protocol.EqualAsync(b, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(first.TenantId, first.DatabaseId, b), token)));
        var uploadId = Guid.NewGuid();
        return [new(new(first, a.Name, "same-name"), uploadId),
            new(new(second, a.Name, "same-name"), uploadId),
            new(new(first, b.Name, "same-name"), uploadId),
            new(new(first, a.Name, "other-name"), uploadId),
            new(new(second, b.Name, "absent-name"), uploadId)];
    }
}
