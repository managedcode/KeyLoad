using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Runs one actual SDK or MCP interruption against a separately owned current-image RF3 wave.</summary>
internal sealed class CanonicalApplyNetworkScenario
{
    private const long BeforeFirstEntryPosition = 0;
    private const long UnelectedEntryTerm = 0;
    private const string DataDirectoryName = "data";
    private const string MissingWave = "The probed current-image RF3 wave did not transfer ownership.";
    private const string MissingTask = "The original public operation task was not retained.";
    private string root = string.Empty;
    private string dataRoot = string.Empty;
    private readonly List<Exception> failures = [];
    private RequestCqrsProbeFixture? controls;
    private RequestCqrsRf3Wave? wave;
    private RequestCqrsRf3Callers? administrator;
    private RequestCqrsRf3Callers? caller;
    private CommandRequest? command;
    private RequestCqrsProbeMarkerRecord? observed;
    private IReadOnlyList<ReplicaSiloDiscovery>? discovery;
    private CancellationTokenSource? scenarioDeadline;
    private CancellationTokenSource? callerCancellation;
    private Task<Result<CommitReceipt>>? sdkCall;
    private Guid armId;
    private Guid submissionArmId;
    private string targetVoter = string.Empty;
    private RequestCqrsPhaseFaultIdentity? preparedIdentity;
    private CommitReceipt? originalReceipt;
    private Guid commandId;
    private bool rootOwned;
    private bool waveStartupAttempted;
    private CanonicalApplyNetworkPhase phase;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var parentTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, parentTimeout.Token);
        var scenario = new CanonicalApplyNetworkScenario();
        await ServerFailureObserver.ObserveAsync(() => scenario.ExecuteAsync(parent.Token), scenario.failures)
            .ConfigureAwait(false);
        var originalEvidence = scenario.CaptureEvidence();
        await CanonicalApplyNetworkCleanup.JoinAsync(scenario.controls, scenario.discovery, scenario.armId, scenario.failures);
        await RequestCqrsPhaseFaultCleanup.RunAsync(scenario.root, scenario.rootOwned, scenario.controls,
            scenario.wave, scenario.waveStartupAttempted, scenario.caller, scenario.administrator, scenario.discovery,
            scenario.callerCancellation, scenario.scenarioDeadline, scenario.sdkCall, null,
            scenario.submissionArmId, scenario.failures)
            .ConfigureAwait(false);
        ServerFailureObserver.Observe(() => CanonicalApplyNetworkEvidence.Save(originalEvidence,
            scenario.sdkCall?.Status, scenario.failures), scenario.failures);
        ServerFailureObserver.ThrowIfAny(scenario.failures);
    }

    private async Task ExecuteAsync(CancellationToken parentToken)
    {
        root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        using var waveDeadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        using var waveDeadline = CancellationTokenSource.CreateLinkedTokenSource(parentToken, waveDeadlineTimeout.Token);
        scenarioDeadline = CancellationTokenSource.CreateLinkedTokenSource(waveDeadline.Token);
        await PrepareAndStartAsync(scenarioDeadline.Token).ConfigureAwait(false);
        await VerifyHeldNetworkProgressAsync(scenarioDeadline.Token).ConfigureAwait(false);
        await VerifyReleasedOperationAsync(scenarioDeadline.Token).ConfigureAwait(false);
        phase = CanonicalApplyNetworkPhase.Completed;
    }

    private async Task PrepareAndStartAsync(CancellationToken cancellationToken)
    {
        dataRoot = Path.Combine(root, DataDirectoryName);
        phase = CanonicalApplyNetworkPhase.PriorProfile;
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, cancellationToken).ConfigureAwait(false)).Profile;
        phase = CanonicalApplyNetworkPhase.ImageProof;
        var images = await RequestCqrsRf3ImageProof.ReadAsync(cancellationToken).ConfigureAwait(false);
        controls = RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid());
        waveStartupAttempted = true;
        phase = CanonicalApplyNetworkPhase.NativeWaveStartup;
        wave = await RequestCqrsRf3Wave.StartProbedAsync(dataRoot,
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), controls, cancellationToken)
            .ConfigureAwait(false);
        var activeWave = wave ?? throw new InvalidOperationException(MissingWave);
        phase = CanonicalApplyNetworkPhase.SignedDiscovery;
        discovery = await ReadDiscoveryAsync(activeWave.App, profile, cancellationToken).ConfigureAwait(false);
        phase = CanonicalApplyNetworkPhase.AdminConnection;
        var adminOwner = await RequestCqrsRf3Callers.ConnectAsync(activeWave.App,
            RequestCqrsRf3Protocol.Node1, profile.AdminKey, cancellationToken).ConfigureAwait(false);
        administrator = adminOwner;
        phase = CanonicalApplyNetworkPhase.PersistedIdentity;
        var persistedIdentity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(adminOwner.Sdk,
            cancellationToken)
            .ConfigureAwait(false);
        preparedIdentity = persistedIdentity;
        phase = CanonicalApplyNetworkPhase.LeaderStatus;
        var status = await McpCallerAssertions.SdkSuccessAsync(await adminOwner.Sdk.StatusAsync(cancellationToken));
        await Assert.That(status.Leader).IsNotNull();
        targetVoter = discovery.First(item => item.VoterId != status.Leader).VoterId;
        phase = CanonicalApplyNetworkPhase.SeedState;
        await CanonicalApplyNetworkData.PrepareAsync(adminOwner, persistedIdentity, cancellationToken);
        phase = CanonicalApplyNetworkPhase.CallerConnection;
        var callerOwner = await RequestCqrsPhaseFaultProvisioning.ConnectAsync(activeWave.App, persistedIdentity,
            cancellationToken).ConfigureAwait(false);
        caller = callerOwner;
        commandId = Guid.NewGuid();
        var activeCommand = CanonicalApplyNetworkData.Command(persistedIdentity, commandId);
        command = activeCommand;
        submissionArmId = controls.WriteArm(persistedIdentity.PrincipalId, commandId, null,
            RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbeAction.Hold);
        var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callerCancellation = originalCancellation;
        phase = CanonicalApplyNetworkPhase.OriginalSdkAdmission;
        StartOriginalCall(callerOwner, activeCommand, originalCancellation.Token);
    }

    private async Task VerifyHeldNetworkProgressAsync(CancellationToken token)
    {
        var active = controls ?? throw new InvalidOperationException(MissingWave);
        var signed = discovery ?? throw new InvalidOperationException(MissingWave);
        phase = CanonicalApplyNetworkPhase.BeforeSubmitObserved;
        var submitting = await active.WaitForMarkerAsync(submissionArmId, RequestCqrsProbePhase.BeforeSubmit,
            RequestCqrsProbeOutcome.Observed, signed, token);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(submitting, submissionArmId, commandId,
            RequestCqrsProbePhase.BeforeSubmit, signed);
        var identity = preparedIdentity ?? throw new InvalidOperationException(MissingTask);
        armId = active.WriteArm(identity.PrincipalId, commandId, null, RequestCqrsProbePhase.CanonicalJournalFlushed,
            RequestCqrsProbeAction.Hold, identity.Partition, submitting.RequestId, targetVoter, submissionArmId);
        active.WriteRelease(submissionArmId, submitting.RequestId);
        phase = CanonicalApplyNetworkPhase.BeforeSubmitReleased;
        await active.WaitForMarkerAsync(submissionArmId, RequestCqrsProbePhase.BeforeSubmit,
            RequestCqrsProbeOutcome.Released, signed, token);
        phase = CanonicalApplyNetworkPhase.CanonicalFlushObserved;
        var marker = await active.WaitForMarkerAsync(armId, RequestCqrsProbePhase.CanonicalJournalFlushed,
            RequestCqrsProbeOutcome.Observed, signed, token);
        observed = marker;
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, commandId,
            RequestCqrsProbePhase.CanonicalJournalFlushed, signed);
        await Assert.That(marker.RequestId).IsEqualTo(submitting.RequestId);
        await Assert.That(marker.Voter).IsEqualTo(targetVoter);
        await Assert.That(marker.EntryIndex is > BeforeFirstEntryPosition).IsTrue();
        await Assert.That(marker.EntryTerm is > UnelectedEntryTerm).IsTrue();
        phase = CanonicalApplyNetworkPhase.OriginalSdkReceipt;
        originalReceipt = await McpCallerAssertions.SdkSuccessAsync(await (sdkCall ?? throw new InvalidOperationException(MissingTask)).WaitAsync(token));
        phase = CanonicalApplyNetworkPhase.OriginalProducerSettled;
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(active, submissionArmId, submitting.RequestId, commandId, signed, token);
        phase = CanonicalApplyNetworkPhase.IndependentAppendObserved;
        var progress = await active.WaitForMarkerAsync(armId, RequestCqrsProbePhase.CanonicalIndependentAppendCompleted,
            RequestCqrsProbeOutcome.Observed, signed, token);
        await Assert.That(progress.RequestId).IsEqualTo(marker.RequestId);
        await Assert.That(progress.Voter).IsEqualTo(marker.Voter);
        await Assert.That(progress.SiloAddress).IsEqualTo(marker.SiloAddress);
        CanonicalApplyNetworkMarkers.RequireNoOutbound(active, armId);
        await Assert.That(progress.EntryIndex).IsEqualTo(marker.EntryIndex);
        await Assert.That(progress.EntryTerm).IsEqualTo(marker.EntryTerm);
        active.WriteRelease(armId, marker.RequestId);
    }
    private async Task VerifyReleasedOperationAsync(CancellationToken token)
    {
        var active = controls ?? throw new InvalidOperationException(MissingWave);
        var signed = discovery ?? throw new InvalidOperationException(MissingWave);
        var marker = observed ?? throw new InvalidOperationException(MissingTask);
        var original = originalReceipt ?? throw new InvalidOperationException(MissingTask);
        phase = CanonicalApplyNetworkPhase.CanonicalReleased;
        await active.WaitForMarkerAsync(armId, RequestCqrsProbePhase.CanonicalJournalFlushed, RequestCqrsProbeOutcome.Released, signed, token);
        phase = CanonicalApplyNetworkPhase.CanonicalOwnerDisposed;
        var closed = await active.WaitForMarkerAsync(armId, RequestCqrsProbePhase.CanonicalOwnerDisposed, RequestCqrsProbeOutcome.Observed, signed, token);
        await Assert.That(closed.EntryIndex).IsEqualTo(marker.EntryIndex);
        await Assert.That(closed.EntryTerm).IsEqualTo(marker.EntryTerm);
        CanonicalApplyNetworkMarkers.RequireNoOutbound(active, armId);
        phase = CanonicalApplyNetworkPhase.ArmRetirement;
        await active.RetireArmAsync(armId, token);
        await active.RetireArmAsync(submissionArmId, token);
        phase = CanonicalApplyNetworkPhase.HealthyReplay;
        await CanonicalApplyNetworkAssertions.VerifyAsync(caller ?? throw new InvalidOperationException(MissingWave),
            command ?? throw new InvalidOperationException(MissingTask), original, token);
    }
    private CanonicalApplyNetworkSnapshot CaptureEvidence()
        => CanonicalApplyNetworkEvidence.Capture(phase, sdkCall, callerCancellation?.IsCancellationRequested ?? false,
            scenarioDeadline?.IsCancellationRequested ?? false, observed is not null, originalReceipt is not null);

    private void StartOriginalCall(RequestCqrsRf3Callers callers, CommandRequest command, CancellationToken token)
        => sdkCall = RequestCqrsFaultCallers.CommitSdkAsync(callers.Sdk, command, token);

    private static async Task<ReplicaSiloDiscovery[]> ReadDiscoveryAsync(DistributedApplication app,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var observations = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < observations.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observations[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(app,
                RequestCqrsRf3Protocol.NodeName(index), profile, cancellationToken).ConfigureAwait(false);
        }
        return observations;
    }
}
