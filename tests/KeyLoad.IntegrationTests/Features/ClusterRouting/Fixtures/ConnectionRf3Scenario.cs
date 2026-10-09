using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class ConnectionRf3Scenario : IAsyncDisposable
{
    private string root = string.Empty;
    private bool rootOwned;
    private bool retainEvidence;
    private LocalRf3ImageTestSession? localImage;
    private RequestCqrsRf3Wave? wave;
    private RequestCqrsRf3Callers? administrator;
    private RequestCqrsProbeFixture? controls;
    private readonly CancellationTokenSource requestWork;
    private readonly ConnectionRf3OperationJoin operations = new();
    private readonly List<ConnectionRf3Caller> callers = [];
    internal ReplicaSiloDiscovery[] Discovery { get; private set; } = [];
    internal RequestCqrsProbeFixture Controls => controls ?? throw Missing();
    internal KeyLoadClient Administrator => administrator?.Sdk ?? throw Missing();
    internal RequestCqrsPhaseFaultIdentity Identity { get; private set; } = null!;
    internal RequestCqrsPhaseFaultIdentity SecondIdentity { get; private set; } = null!;

    private ConnectionRf3Scenario(CancellationToken cancellationToken)
        => requestWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    internal static async Task RunAsync(Func<ConnectionRf3Scenario, CancellationToken, Task> execute,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var failures = new List<Exception>();
        try
        {
            await using var scenario = new ConnectionRf3Scenario(bounded.Token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await scenario.InitializeAsync(scenario.requestWork.Token).ConfigureAwait(false);
                await execute(scenario, scenario.requestWork.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                scenario.retainEvidence = true;
                scenario.controls?.RetainEvidence();
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        localImage = await LocalRf3ImageTestSession.StartIfSelectedAsync(cancellationToken).ConfigureAwait(false);
        root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        var data = Path.Combine(root, "data");
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(data, cancellationToken).ConfigureAwait(false)).Profile;
        var images = await RequestCqrsRf3ImageProof.ReadAsync(cancellationToken, localImage?.Selection).ConfigureAwait(false);
        controls = RequestCqrsProbeFixture.Create(data, Guid.NewGuid());
        wave = await RequestCqrsRf3Wave.StartProbedAsync(data,
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), controls, cancellationToken,
            selection: localImage?.Selection).ConfigureAwait(false);
        var discovery = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < discovery.Length; index++)
        {
            discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.App,
                RequestCqrsRf3Protocol.NodeName(index), profile, cancellationToken).ConfigureAwait(false);
        }
        Discovery = discovery;
        administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1,
            profile.AdminKey, cancellationToken).ConfigureAwait(false);
        Identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(Administrator, cancellationToken)
            .ConfigureAwait(false);
        SecondIdentity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(Administrator, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<ConnectionRf3Caller> CallerAsync(CancellationToken cancellationToken, bool multiplexed = false)
    {
        var caller = await ConnectionRf3Caller.ConnectAsync((wave ?? throw Missing()).App,
            RequestCqrsRf3Protocol.Node1, Identity.Secret, cancellationToken, multiplexed).ConfigureAwait(false);
        callers.Add(caller);
        return caller;
    }

    internal async Task<(T Result, ConnectionProbeWitness Witness, RequestCqrsProbeMarkerRecord Marker)> ObserveAsync<T>(
        RequestCqrsPhaseFaultIdentity identity, Guid commandId, GrainReadKind? readKind,
        Func<Task<T>> execute, CancellationToken cancellationToken)
    {
        var held = await StartHeldAsync(identity, commandId, readKind, execute, cancellationToken).ConfigureAwait(false);
        Controls.WriteRelease(held.Arm, held.Marker.RequestId);
        var result = await held.Original.ConfigureAwait(false);
        await SettleAsync(held.Arm, held.Marker, cancellationToken).ConfigureAwait(false);
        return (result, held.Witness, held.Marker);
    }

    internal async Task<ConnectionRf3HeldOperation<T>> StartHeldAsync<T>(RequestCqrsPhaseFaultIdentity identity,
        Guid commandId, GrainReadKind? readKind, Func<Task<T>> execute, CancellationToken cancellationToken)
    {
        var arm = Controls.WriteArm(identity.PrincipalId, commandId, readKind,
            RequestCqrsProbePhase.RequestStarted, RequestCqrsProbeAction.Hold);
        var original = execute();
        operations.Retain(arm, original);
        var marker = await Controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.RequestStarted,
            RequestCqrsProbeOutcome.Observed, Discovery, cancellationToken).ConfigureAwait(false);
        var witness = await ConnectionRf3WitnessReader.WaitAsync(Controls, marker, Discovery, closed: false,
            cancellationToken).ConfigureAwait(false);
        return new ConnectionRf3HeldOperation<T>(arm, original, witness, marker);
    }

    internal async Task SettleAsync(Guid arm, RequestCqrsProbeMarkerRecord marker, CancellationToken cancellationToken)
    {
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(Controls, arm, marker.RequestId, marker.CommandId,
            Discovery, cancellationToken).ConfigureAwait(false);
        await Controls.RetireArmAsync(arm, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ConnectionProbeWitness> CloseAsync(ConnectionRf3Caller caller,
        RequestCqrsProbeMarkerRecord marker, CancellationToken cancellationToken)
    {
        await caller.DisposeAsync().ConfigureAwait(false);
        return await ConnectionRf3WitnessReader.WaitAsync(Controls, marker, Discovery, closed: true,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task VerifyDocumentAsync(RequestCqrsPhaseFaultIdentity identity, string json, long revision,
        CancellationToken cancellationToken)
        => await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(administrator ?? throw Missing(), identity,
            json, revision, cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(requestWork.CancelAsync, failures).ConfigureAwait(false);
        if (operations.HasPending)
        {
            foreach (var caller in callers) { caller.AbortTransport(failures); }
        }
        await operations.JoinBoundedAsync(failures, timeout.Token).ConfigureAwait(false);
        if (controls is not null && Discovery.Length == RequestCqrsRf3Protocol.NodeCount)
        {
            await operations.JoinProducersAsync(controls, Discovery, failures, timeout.Token).ConfigureAwait(false);
        }
        foreach (var caller in callers)
        { await ServerFailureObserver.ObserveAsync(() => caller.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        await operations.JoinAfterTransportDisposedAsync(failures).ConfigureAwait(false);
        if (controls is not null && Discovery.Length == RequestCqrsRf3Protocol.NodeCount)
        {
            await operations.JoinProducersAsync(controls, Discovery, failures, timeout.Token).ConfigureAwait(false);
            if (operations.ProducersDisposed(controls))
            {
                await ServerFailureObserver.ObserveAsync(
                    () => controls.ReleaseOpenArmsAsync(Discovery, timeout.Token), failures).ConfigureAwait(false);
            }
        }
        if (administrator is not null)
        { await ServerFailureObserver.ObserveAsync(() => administrator.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is not null)
        { await ServerFailureObserver.ObserveAsync(wave.StopAsync, failures).ConfigureAwait(false); }
        try { requestWork.Dispose(); }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        if (localImage is not null)
        {
            try { await localImage.DisposeAsync(removeImage: failures.Count == 0).ConfigureAwait(false); }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        }
        if (controls is not null)
        {
            if (failures.Count > 0) { controls.RetainEvidence(); }
            await ServerFailureObserver.ObserveAsync(controls.DisposeAfterResourcesJoinedAsync, failures).ConfigureAwait(false);
        }
        if (rootOwned && !retainEvidence && failures.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static InvalidOperationException Missing() => new(ConnectionRf3Protocol.MissingOwner);
}
