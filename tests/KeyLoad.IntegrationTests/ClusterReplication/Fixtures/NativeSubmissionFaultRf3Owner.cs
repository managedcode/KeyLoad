using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed class NativeSubmissionFaultRf3Owner
{
    private string root = string.Empty;
    internal string DataRoot { get; private set; } = string.Empty;
    private bool rootOwned;
    private bool startupAttempted;
    private LocalRf3ImageTestSession? localImage;
    private CancellationTokenSource? operationCancellation;
    private IReadOnlyDictionary<string, string>? waveImages;
    private bool waveClosed;
    private bool waveAcquisitionUnknown;
    private bool waveCloseAttempted;
    internal List<Exception> Failures { get; } = [];
    internal RequestCqrsProbeFixture? Controls
    {
        get; private set;
    }
    internal RequestCqrsRf3Wave? Wave
    {
        get; private set;
    }
    internal RequestCqrsRf3Callers? Administrator
    {
        get; private set;
    }
    internal RequestCqrsRf3Callers? Caller
    {
        get; private set;
    }
    internal NodeEpochRf3Profile? Profile
    {
        get; private set;
    }
    internal RequestCqrsPhaseFaultIdentity? Identity
    {
        get; private set;
    }
    internal ReplicaSiloDiscovery[] Discovery { get; set; } = [];
    internal Task? SdkCall
    {
        get; set;
    }
    internal Task<RequestCqrsFaultMcpObservation>? McpCall
    {
        get; set;
    }
    internal Func<Task>? AssertOriginal
    {
        get; set;
    }
    internal Guid SourceArm
    {
        get; set;
    }
    internal Guid DescendantArm
    {
        get; set;
    }
    internal Task Original => SdkCall ?? (Task?)McpCall ?? throw Missing();
    internal CancellationToken OperationToken => operationCancellation?.Token ?? throw Missing();

    internal static async Task RunAsync(bool officialMcp, CancellationToken callerToken)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        using var waveTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(parent.Token, waveTimeout.Token);
        var owner = new NativeSubmissionFaultRf3Owner();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await owner.InitializeAsync(bounded.Token).ConfigureAwait(false);
            await NativeSubmissionFaultRf3Flow.ExecuteAsync(owner, officialMcp, bounded.Token).ConfigureAwait(false);
        }, owner.Failures).ConfigureAwait(false);
        if (owner.Failures.Count > 0)
        {
            owner.Controls?.RetainEvidence();
        }
        await ServerFailureObserver.ObserveAsync(owner.CleanupAsync, owner.Failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(owner.Failures);
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        localImage = await LocalRf3ImageTestSession.StartIfSelectedAsync(token).ConfigureAwait(false);
        root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        var data = Path.Combine(root, "data");
        DataRoot = data;
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(data, token).ConfigureAwait(false)).Profile;
        Profile = profile;
        var images = await RequestCqrsRf3ImageProof.ReadAsync(token, localImage?.Selection).ConfigureAwait(false);
        waveImages = RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current);
        var controls = RequestCqrsProbeFixture.Create(data, Guid.NewGuid());
        Controls = controls;
        startupAttempted = true;
        var wave = await RequestCqrsRf3Wave.StartProbedAsync(data,
            waveImages, controls, token,
            selection: localImage?.Selection).ConfigureAwait(false);
        Wave = wave;
        var administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1,
            profile.AdminKey, token).ConfigureAwait(false);
        Administrator = administrator;
        Identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(administrator.Sdk, token)
            .ConfigureAwait(false);
    }

    internal async Task ConnectCallerAsync(string nativeLeaderNode, CancellationToken token)
    {
        Caller = await RequestCqrsRf3Callers.ConnectAsync((Wave ?? throw Missing()).App, nativeLeaderNode,
            (Identity ?? throw Missing()).Secret, token).ConfigureAwait(false);
    }

    internal async Task StopForOutcomeInspectionAsync()
    {
        if (waveCloseAttempted)
        {
            throw Missing();
        }
        waveCloseAttempted = true;
        var failures = new List<Exception>();
        if (Caller is { } caller)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await caller.DisposeAsync().ConfigureAwait(false);
                Caller = null;
            }, failures).ConfigureAwait(false);
        }
        if (Administrator is { } administrator)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await administrator.DisposeAsync().ConfigureAwait(false);
                Administrator = null;
            }, failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(
            () => (Wave ?? throw Missing()).StopAsync(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        waveClosed = true;
    }

    internal async Task ResumeAfterInspectionAsync(CancellationToken token)
    {
        if (!waveClosed || Wave?.NativeAdmissionOwnersJoined is not true)
        {
            throw Missing();
        }
        var profile = Profile ?? throw Missing();
        waveClosed = false;
        waveAcquisitionUnknown = true;
        var wave = await RequestCqrsRf3Wave.StartProbedAsync(DataRoot,
            waveImages ?? throw Missing(), Controls ?? throw Missing(), token,
            selection: localImage?.Selection).ConfigureAwait(false);
        Wave = wave;
        waveAcquisitionUnknown = false;
        waveCloseAttempted = false;
        Administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1,
            profile.AdminKey, token).ConfigureAwait(false);
    }

    private async Task CleanupAsync()
    {
        // Preserve the actual original data/control evidence on any failed acceptance step.
        await ServerFailureObserver.ObserveAsync(() => RequestCqrsPhaseFaultCleanup.RunAsync(root,
            rootOwned && Failures.Count == 0, Controls,
            waveAcquisitionUnknown || waveCloseAttempted && !waveClosed ? null : Wave, startupAttempted, Caller, Administrator,
            Discovery.Length == 0 ? null : Discovery, operationCancellation, null, SdkCall, McpCall,
            SourceArm, Failures, DescendantArm == Guid.Empty ? [] : [DescendantArm]), Failures).ConfigureAwait(false);
        if (localImage is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () => await localImage.DisposeAsync(removeImage: Failures.Count == 0).ConfigureAwait(false),
                Failures).ConfigureAwait(false);
        }
    }

    internal static InvalidOperationException Missing()
        => new("The original native RF3 submission fault owner or evidence is missing.");
}
