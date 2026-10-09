using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed class FollowerDocumentRf3Scenario(FollowerDocumentCaller mode, FollowerDocumentChange change)
{
    private readonly FollowerDocumentRf3State state = new(mode, change);

    internal static async Task RunAsync(FollowerDocumentCaller mode, FollowerDocumentChange change, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new FollowerDocumentRf3Scenario(mode, change);
        await ServerFailureObserver.ObserveAsync(() => scenario.RunOwnedAsync(parent.Token), scenario.state.Failures).ConfigureAwait(false);
        await RequestCqrsPhaseFaultCleanup.RunAsync(scenario.state.Root, scenario.state.RootOwned, scenario.state.Controls,
            scenario.state.Wave, scenario.state.StartupAttempted, scenario.state.Caller, scenario.state.Administrator, scenario.state.Discovery,
            scenario.state.CallLifetime, scenario.state.WaveLifetime, scenario.state.Pending, null, scenario.state.ArmId, scenario.state.Failures)
            .ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(scenario.state.Failures);
    }

    private async Task RunOwnedAsync(CancellationToken token)
    {
        try
        { await ExecuteOwnedAsync(token).ConfigureAwait(false); }
        catch (Exception original)
        {
            FollowerDocumentRf3FailureObservation.Write(original, state, token);
            throw;
        }
    }

    private async Task ExecuteOwnedAsync(CancellationToken token)
    {
        state.Root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(state.Root, () => state.RootOwned = true);
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        state.WaveLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        await StartAsync(state.WaveLifetime.Token).ConfigureAwait(false);
        state.Stage = FollowerDocumentRf3FailureStage.CapturingHeldRead;
        await new FollowerDocumentRf3HeldFlow(state).CaptureAndHoldAsync(state.WaveLifetime.Token).ConfigureAwait(false);
        state.Stage = FollowerDocumentRf3FailureStage.ChangingAndReleasing;
        await new FollowerDocumentRf3HeldFlow(state).ChangeAndReleaseAsync(state.WaveLifetime.Token).ConfigureAwait(false);
        state.Stage = FollowerDocumentRf3FailureStage.ObservingOriginalAndContinuing;
        await new FollowerDocumentRf3HeldFlow(state).AssertOriginalAndContinueAsync(state.WaveLifetime.Token).ConfigureAwait(false);
    }

    private async Task StartAsync(CancellationToken token)
    {
        var dataRoot = Path.Combine(state.Root, "data");
        state.Profile = (await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, token).ConfigureAwait(false)).Profile;
        var images = await RequestCqrsRf3ImageProof.ReadAsync(token).ConfigureAwait(false);
        state.Controls = RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid());
        state.StartupAttempted = true;
        state.Wave = await RequestCqrsRf3Wave.StartProbedAsync(dataRoot,
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), state.Controls, token).ConfigureAwait(false);
        var records = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(state.Wave.App,
                RequestCqrsRf3Protocol.NodeName(index), state.Profile, token).ConfigureAwait(false);
        }
        state.Discovery = records;
        await ConnectSelectedFollowerAsync(state.Wave.App, records, token).ConfigureAwait(false);
    }

    private async Task ConnectSelectedFollowerAsync(DistributedApplication app, ReplicaSiloDiscovery[] records, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
        var initial = new KeyLoadClient(http, state.Profile.AdminKey, IntegrationClientOptions.Execution());
        state.Identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(initial, token).ConfigureAwait(false);
        var status = await McpCallerAssertions.SdkSuccessAsync(await initial.StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
        var index = Array.FindIndex(records, item => item.VoterId != status.Leader);
        if (index < 0)
        { throw new InvalidOperationException(FollowerDocumentRf3Protocol.MissingOwner); }
        state.SelectedIndex = index;
        state.ReplicaId = records[index].VoterId;
        var node = RequestCqrsRf3Protocol.NodeName(index);
        state.Administrator = await RequestCqrsRf3Callers.ConnectAsync(app, node, state.Profile.AdminKey, token).ConfigureAwait(false);
        state.Caller = await RequestCqrsRf3Callers.ConnectAsync(app, node, state.Identity.Secret, token).ConfigureAwait(false);
        state.Principal = new(state.Identity.PrincipalId, state.Identity.Partition.TenantId,
            [new(state.Identity.Partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        { ClusterAdministrator = false };
        state.Credential = KeyLoad.Core.DatabaseEngine.Credential(state.Identity.PrincipalId + FollowerDocumentRf3Protocol.KeySuffix,
            state.Identity.PrincipalId, state.Identity.Secret);
    }
}
