using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPublicCancellationSetup
{
    internal const string Resource = "cancellation-blobs";

    internal static async Task StartAsync(BlobPublicCancellationOwner owner, CancellationToken token)
    {
        owner.Root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(owner.Root, () => owner.RootOwned = true);
        var data = Path.Combine(owner.Root, "data");
        owner.ImageDataRoot = data;
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProfile);
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(data, token)).Profile;
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultImages);
        owner.LocalImageSession = await global::KeyLoad.IntegrationTests.Features.ClusterReplication.LocalRf3ImageTestSession
            .StartIfSelectedAsync(token);
        var images = await RequestCqrsRf3ImageProof.ReadAsync(token, owner.LocalImageSession?.Selection);
        owner.Controls = RequestCqrsProbeFixture.Create(data, Guid.NewGuid());
        owner.WaveStartupAttempted = true;
        owner.Wave = await RequestCqrsRf3Wave.StartProbedAsync(data,
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), owner.Controls, token, owner.Lifecycle,
            selection: owner.LocalImageSession?.Selection);
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultDiscovery);
        var discovery = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < discovery.Length; index++)
        {
            discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(owner.Wave.App,
            RequestCqrsRf3Protocol.NodeName(index), profile, token);
        }
        owner.Discovery = discovery;
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultAdministrator);
        owner.Administrator = await RequestCqrsRf3Callers.ConnectAsync(owner.Wave.App,
            RequestCqrsRf3Protocol.Node1, profile.AdminKey, token);
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProvisioning);
        owner.Identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(owner.Administrator.Sdk, token);
        await ConfigureBlobAsync(owner, token);
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultCaller);
        owner.Caller = await RequestCqrsPhaseFaultProvisioning.ConnectAsync(owner.Wave.App, owner.Identity, token);
    }

    private static async Task ConfigureBlobAsync(BlobPublicCancellationOwner owner, CancellationToken token)
    {
        var partition = owner.Identity.Partition;
        _ = await McpCallerAssertions.SdkSuccessAsync(await owner.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId,
                new(Resource, ResourceKind.BlobStore, partition.TransactionDomainId)), token));
        var principal = new PrincipalRecord(owner.Identity.PrincipalId, partition.TenantId,
            [new(partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite),
             new(partition.DatabaseId, Resource, Capability.BlobRead | Capability.BlobWrite)], [])
        { PolicyEpoch = RequestCqrsPhaseFaultProvisioning.InitialPolicyEpoch + 1 };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await owner.Administrator.Sdk.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, token));
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(principal, actual);
    }
}
