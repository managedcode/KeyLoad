using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DatabaseComposition;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.BlobStorage;

[NotInParallel]
internal sealed class FileStorageHttpTests
{
    private const int SingleNode = 1;
    private const int FirstNode = 0;
    private const string TenantPrefix = "files-";
    private const string Database = "database";
    private const string Domain = "domain";
    private const string PartitionKey = "partition";
    private const string Resource = "gate_blobs_0";
    private const string GuidFormat = "N";

    [Test]
    public async Task NativeStorageTransfersEnforcePersistedGrantsAndServeVerifiedRanges()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await FunctionalGateFixture.StartAsync(SingleNode, SingleNode, token);
        _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, fixture.NodeNames[FirstNode], fixture.AdminKey, async administrator =>
        {
            var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString(GuidFormat), Database, Domain, PartitionKey);
            _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(Resource, ResourceKind.BlobStore, Domain)), token));
            var writer = await McpPersistedIdentity.CreateAsync(administrator.Sdk, partition, Resource,
                Capability.BlobRead | Capability.BlobWrite | Capability.BlobDelete | Capability.BlobManage, token);
            var reader = await McpPersistedIdentity.CreateAsync(administrator.Sdk, partition, Resource, Capability.BlobRead, token);
            await using var writerCaller = await RequestCqrsClusterCallers.ConnectAsync(fixture.App, fixture.NodeNames[FirstNode], writer.Secret, token);
            await using var readerCaller = await RequestCqrsClusterCallers.ConnectAsync(fixture.App, fixture.NodeNames[FirstNode], reader.Secret, token);
            var context = new FunctionalGateContext(administrator, writerCaller, readerCaller, partition,
                new(partition, Resource, Resource), SingleNode);
            await FunctionalGateFiles.ExerciseAsync(context, FirstNode, FirstNode, token);
            var file = await FunctionalGateFiles.UploadAsync(context, Resource, Resource, token);
            await FileStorageHttpOperations.VerifyAsync(context, file, token);
            _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(),
                reader.Principal with { PolicyEpoch = reader.Principal.PolicyEpoch + SingleNode, Grants = [] }, token));
            await FileStorageHttpOperations.RequireDeniedReadAsync(readerCaller, file, token);
            await FunctionalGateFiles.VerifyAsync(writerCaller, file, token);
            return true;
        }, token);
    }
}
