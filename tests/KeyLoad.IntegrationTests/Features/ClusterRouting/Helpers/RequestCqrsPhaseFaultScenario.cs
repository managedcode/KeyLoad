using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Runs one actual SDK or MCP interruption against a separately owned current-image RF3 wave.</summary>
internal sealed class RequestCqrsPhaseFaultScenario(bool useMcp, RequestCqrsProbePhase phase)
{
    private const string MissingWave = "The probed current-image RF3 wave did not transfer ownership.";
    private const string MissingTask = "The original public operation task was not retained.";
    private string root = string.Empty;
    private string dataRoot = string.Empty;
    private readonly List<Exception> failures = [];
    private RequestCqrsProbeFixture? controls;
    private RequestCqrsRf3Wave? wave;
    private RequestCqrsRf3Callers? administrator;
    private RequestCqrsRf3Callers? caller;
    private RequestCqrsPhaseFaultIdentity? identity;
    private RequestCqrsFaultReceiptOracle? oracle;
    private CommandRequest? command;
    private RequestCqrsProbeMarkerRecord? observed;
    private IReadOnlyList<ReplicaSiloDiscovery>? discovery;
    private CancellationTokenSource? scenarioDeadline;
    private CancellationTokenSource? callerCancellation;
    private Task<Result<CommitReceipt>>? sdkCall;
    private Task<RequestCqrsFaultMcpObservation>? mcpCall;
    private Guid armId;
    private Guid commandId;
    private bool rootOwned;
    private bool waveStartupAttempted;

    internal static async Task RunAsync(bool useMcp, RequestCqrsProbePhase phase,
        CancellationToken cancellationToken)
    {
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        parent.CancelAfter(RequestCqrsRf3Protocol.ParentDeadline);
        var scenario = new RequestCqrsPhaseFaultScenario(useMcp, phase);
        await ServerFailureObserver.ObserveAsync(() => scenario.ExecuteAsync(parent.Token), scenario.failures)
            .ConfigureAwait(false);
        await RequestCqrsPhaseFaultCleanup.RunAsync(scenario.root, scenario.rootOwned, scenario.controls,
            scenario.wave, scenario.waveStartupAttempted, scenario.caller, scenario.administrator, scenario.discovery,
            scenario.callerCancellation, scenario.scenarioDeadline, scenario.sdkCall, scenario.mcpCall,
            scenario.armId, scenario.failures)
            .ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(scenario.failures);
    }

    private async Task ExecuteAsync(CancellationToken parentToken)
    {
        root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        using var waveDeadline = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        waveDeadline.CancelAfter(RequestCqrsRf3Protocol.WaveDeadline);
        scenarioDeadline = CancellationTokenSource.CreateLinkedTokenSource(waveDeadline.Token);
        await PrepareAndStartAsync(scenarioDeadline.Token).ConfigureAwait(false);
        await VerifyPhaseAndCancelAsync(scenarioDeadline.Token).ConfigureAwait(false);
        await VerifySettledOutcomeAndRetryAsync(scenarioDeadline.Token).ConfigureAwait(false);
    }

    private async Task PrepareAndStartAsync(CancellationToken cancellationToken)
    {
        dataRoot = Path.Combine(root, "data");
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, cancellationToken).ConfigureAwait(false)).Profile;
        var images = await RequestCqrsRf3ImageProof.ReadAsync(cancellationToken).ConfigureAwait(false);
        controls = RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid());
        waveStartupAttempted = true;
        wave = await RequestCqrsRf3Wave.StartProbedAsync(dataRoot,
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), controls, cancellationToken)
            .ConfigureAwait(false);
        var activeWave = wave ?? throw new InvalidOperationException(MissingWave);
        discovery = await ReadDiscoveryAsync(activeWave.App, profile, cancellationToken).ConfigureAwait(false);
        var adminOwner = await RequestCqrsRf3Callers.ConnectAsync(activeWave.App,
            RequestCqrsRf3Protocol.Node1, profile.AdminKey, cancellationToken).ConfigureAwait(false);
        administrator = adminOwner;
        identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(adminOwner.Sdk,
            cancellationToken)
            .ConfigureAwait(false);
        var persistedIdentity = identity ?? throw new InvalidOperationException(MissingTask);
        var callerOwner = await RequestCqrsPhaseFaultProvisioning.ConnectAsync(activeWave.App, persistedIdentity,
            cancellationToken).ConfigureAwait(false);
        caller = callerOwner;
        oracle = await RequestCqrsFaultReceiptOracle.CaptureAsync(callerOwner, persistedIdentity.Partition,
            RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
            RequestCqrsRf3Protocol.DocumentJson, RequestCqrsRf3Protocol.ChangedDocumentJson,
            cancellationToken).ConfigureAwait(false);
        commandId = Guid.NewGuid();
        var activeCommand = RequestCqrsPhaseFaultProvisioning.UpdateCommand(persistedIdentity, commandId);
        command = activeCommand;
        armId = controls.WriteArm(
            persistedIdentity.PrincipalId, commandId, null, phase, RequestCqrsProbeAction.Hold);
        var originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callerCancellation = originalCancellation;
        StartOriginalCall(callerOwner, activeCommand, originalCancellation.Token);
    }

    private async Task VerifyPhaseAndCancelAsync(CancellationToken cancellationToken)
    {
        var activeControls = controls ?? throw new InvalidOperationException(MissingWave);
        var actualDiscovery = discovery ?? throw new InvalidOperationException(MissingWave);
        var marker = await activeControls.WaitForMarkerAsync(armId, phase, RequestCqrsProbeOutcome.Observed,
            actualDiscovery, cancellationToken).ConfigureAwait(false);
        observed = marker;
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, commandId, phase, actualDiscovery)
            .ConfigureAwait(false);
        var beforeSubmit = phase == RequestCqrsProbePhase.BeforeSubmit;
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(
            administrator ?? throw new InvalidOperationException(MissingWave),
            identity ?? throw new InvalidOperationException(MissingWave),
            beforeSubmit ? RequestCqrsRf3Protocol.DocumentJson : RequestCqrsRf3Protocol.ChangedDocumentJson,
            beforeSubmit ? 1 : 2, cancellationToken).ConfigureAwait(false);
        await (callerCancellation ?? throw new InvalidOperationException(MissingTask)).CancelAsync().ConfigureAwait(false);
    }

    private async Task VerifySettledOutcomeAndRetryAsync(CancellationToken cancellationToken)
    {
        var marker = observed ?? throw new InvalidOperationException(MissingTask);
        var actualDiscovery = discovery ?? throw new InvalidOperationException(MissingWave);
        var activeControls = controls ?? throw new InvalidOperationException(MissingWave);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(activeControls, armId, marker.RequestId,
            commandId, actualDiscovery, cancellationToken).ConfigureAwait(false);
        await AssertOriginalOutcomeAsync(marker.RequestId).ConfigureAwait(false);
        await activeControls.RetireArmAsync(armId, cancellationToken).ConfigureAwait(false);
        var activeOracle = oracle ?? throw new InvalidOperationException(MissingTask);
        var activeCaller = caller ?? throw new InvalidOperationException(MissingTask);
        var activeCommand = command ?? throw new InvalidOperationException(MissingTask);
        var retry = await activeOracle.RetryAndVerifyAsync(activeCaller, activeCommand, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(retry.SdkReceipt.CommandId).IsEqualTo(commandId);
        await Assert.That(retry.McpReceipt.CommandId).IsEqualTo(commandId);
        await Assert.That(retry.McpRequestId).IsNotEqualTo(Guid.Empty);
    }

    private async Task AssertOriginalOutcomeAsync(Guid requestId)
    {
        var cancellation = callerCancellation ?? throw new InvalidOperationException(MissingTask);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        if (useMcp)
        {
            var observation = await (mcpCall ?? throw new InvalidOperationException(MissingTask))
                .ConfigureAwait(false);
            var actualMcpRequestId = await RequestCqrsFaultOutcome.AssertMcpInterruptionAsync(observation)
                .ConfigureAwait(false);
            if (actualMcpRequestId is { } value)
            { await Assert.That(value).IsEqualTo(requestId); }
            return;
        }
        var sdkResult = await (sdkCall ?? throw new InvalidOperationException(MissingTask)).ConfigureAwait(false);
        await RequestCqrsFaultOutcome.AssertSdkUnknownWriteAsync(sdkResult).ConfigureAwait(false);
    }

    private void StartOriginalCall(RequestCqrsRf3Callers callers, CommandRequest command,
        CancellationToken cancellationToken)
    {
        if (useMcp)
        { mcpCall = RequestCqrsFaultCallers.CommitMcpAsync(callers.Mcp, command, cancellationToken); }
        else
        { sdkCall = RequestCqrsFaultCallers.CommitSdkAsync(callers.Sdk, command, cancellationToken); }
    }

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
