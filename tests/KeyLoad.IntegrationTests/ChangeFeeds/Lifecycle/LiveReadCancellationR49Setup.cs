using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadCancellationR49Setup
{
    internal static async Task StartAsync(LiveReadCancellationR49Owner owner, CancellationToken token)
    {
        owner.Root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(owner.Root, () => owner.RootOwned = true);
        owner.DataRoot = Path.Combine(owner.Root, "data");
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProfile);
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(owner.DataRoot, token)).Profile;
        owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultImages);
        owner.ImageSession = await LocalRf3ImageTestSession.StartIfSelectedAsync(token);
        var proof = await RequestCqrsRf3ImageProof.ReadAsync(token, owner.ImageSession?.Selection);
        var images = RequestCqrsPhaseFaultProvisioning.CurrentImages(proof.Current);
        owner.Controls = RequestCqrsProbeFixture.Create(owner.DataRoot, Guid.NewGuid());
        owner.WaveStartupAttempted = true;
        owner.Wave = await RequestCqrsRf3Wave.StartProbedAsync(owner.DataRoot, images, owner.Controls,
            token, owner.Lifecycle, selection: owner.ImageSession?.Selection);
        await ConnectAsync(true, token);
        owner.Restart = async cancellationToken =>
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => owner.Caller.DisposeAsync().AsTask(), failures);
            await ServerFailureObserver.ObserveAsync(() => owner.Administrator.DisposeAsync().AsTask(), failures);
            await ServerFailureObserver.ObserveAsync(owner.Wave.StopAsync, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            owner.Caller = null!;
            owner.Administrator = null!;
            owner.Wave = null!;
            owner.Wave = await RequestCqrsRf3Wave.StartProbedAsync(owner.DataRoot, images, owner.Controls,
                cancellationToken, owner.Lifecycle, selection: owner.ImageSession?.Selection);
            await ConnectAsync(false, cancellationToken);
        };

        async Task ConnectAsync(bool createIdentity, CancellationToken cancellationToken)
        {
            owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultDiscovery);
            var discovery = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
            for (var index = 0; index < discovery.Length; index++)
            {
                discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(owner.Wave.App,
                    RequestCqrsRf3Protocol.NodeName(index), profile, cancellationToken);
            }
            owner.Discovery = discovery;
            owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultAdministrator);
            owner.Administrator = await RequestCqrsRf3Callers.ConnectAsync(owner.Wave.App,
                RequestCqrsRf3Protocol.Node1, profile.AdminKey, cancellationToken);
            if (createIdentity)
            {
                owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProvisioning);
                owner.Identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(
                    owner.Administrator.Sdk, cancellationToken);
                await LiveReadCancellationR49Seed.RunAsync(owner, cancellationToken);
            }
            owner.Lifecycle.SetStage(RequestCqrsLifecycleStage.FaultCaller);
            owner.Caller = await RequestCqrsPhaseFaultProvisioning.ConnectAsync(owner.Wave.App,
                owner.Identity, cancellationToken);
        }
    }
}
