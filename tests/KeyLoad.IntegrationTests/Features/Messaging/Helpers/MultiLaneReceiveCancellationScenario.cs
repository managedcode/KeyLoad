using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class MultiLaneReceiveCancellationScenario(bool useMcp)
{
    private const string Missing = "The owned multi-lane cancellation scenario is incomplete.";
    private readonly List<Exception> failures = [];
    private string root = string.Empty;
    private bool rootOwned;
    private bool startupAttempted;
    private RequestCqrsRf3Wave? wave;
    private RequestCqrsProbeFixture? controls;
    private RequestCqrsRf3Callers? administrator;
    private RequestCqrsRf3Callers? caller;
    private RequestCqrsPhaseFaultIdentity? identity;
    private MultiLaneReceiveRequest? request;
    private IReadOnlyList<ReplicaSiloDiscovery>? discovery;
    private CancellationTokenSource? originalCancellation;
    private Task<Result<MultiLaneReceiveResult>>? sdkCall;
    private Task<RequestCqrsFaultMcpObservation>? mcpCall;
    private Guid armId;
    private MessageInspection? beforeSecond;
    private MessageInspection? committedFirst;

    internal static async Task RunAsync(bool useMcp, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new MultiLaneReceiveCancellationScenario(useMcp);
        await ServerFailureObserver.ObserveAsync(() => scenario.ExecuteAsync(deadline.Token), scenario.failures)
            .ConfigureAwait(false);
        await RequestCqrsPhaseFaultCleanup.RunAsync(scenario.root, scenario.rootOwned, scenario.controls,
            scenario.wave, scenario.startupAttempted, scenario.caller, scenario.administrator, scenario.discovery,
            scenario.originalCancellation, null, scenario.sdkCall, scenario.mcpCall, scenario.armId, scenario.failures)
            .ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(scenario.failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        await PrepareAsync(deadline.Token).ConfigureAwait(false);
        await ObserveAndCancelAsync(deadline.Token).ConfigureAwait(false);
        await VerifyOriginalAsync(deadline.Token).ConfigureAwait(false);
        await MultiLaneReceiveCancellationAssertions.ReconcileAsync(administrator!, caller!, request!,
            committedFirst!, beforeSecond!, deadline.Token).ConfigureAwait(false);
    }

    private async Task PrepareAsync(CancellationToken token)
    {
        root = RequestCqrsPhaseFaultProvisioning.NewPrivateRootPath();
        RequestCqrsPhaseFaultProvisioning.CreatePrivateRoot(root, () => rootOwned = true);
        var data = Path.Combine(root, "data");
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(data, token).ConfigureAwait(false)).Profile;
        var images = await RequestCqrsRf3ImageProof.ReadAsync(token).ConfigureAwait(false);
        controls = RequestCqrsProbeFixture.Create(data, Guid.NewGuid());
        startupAttempted = true;
        wave = await RequestCqrsRf3Wave.StartProbedAsync(data,
            RequestCqrsPhaseFaultProvisioning.CurrentImages(images.Current), controls, token).ConfigureAwait(false);
        var observed = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < observed.Length; index++)
        {
            observed[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.App,
                RequestCqrsRf3Protocol.NodeName(index), profile, token).ConfigureAwait(false);
        }
        discovery = observed;
        administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.App, RequestCqrsRf3Protocol.Node1,
            profile.AdminKey, token).ConfigureAwait(false);
        identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(administrator.Sdk, token)
            .ConfigureAwait(false);
        request = await MultiLaneReceiveCancellationProvisioning.SeedAsync(administrator.Sdk, identity, token)
            .ConfigureAwait(false);
        caller = await RequestCqrsPhaseFaultProvisioning.ConnectAsync(wave.App, identity, token).ConfigureAwait(false);
        var tool = await caller.Mcp.Client.DiscoverKeyLoadToolAsync(MultiLaneReceiveProtocol.ToolName, token)
            .ConfigureAwait(false);
        await McpDiscoveryAssertions.VerifyAsync(tool).ConfigureAwait(false);
        beforeSecond = await InspectAsync(1, token).ConfigureAwait(false);
        await Assert.That(beforeSecond.Metadata.State).IsEqualTo(MessageState.Ready);
        armId = controls.WriteArm(identity.PrincipalId, request.Requests[0].RequestId, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold);
        originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (useMcp)
        { mcpCall = MultiLaneReceiveCancellationCallers.CallAsync(caller, request, originalCancellation.Token); }
        else
        { sdkCall = caller.Sdk.ReceiveAcrossLanesAsync(request, originalCancellation.Token); }
    }

    private async Task ObserveAndCancelAsync(CancellationToken token)
    {
        var marker = await controls!.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery!, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, request!.Requests[0].RequestId,
            RequestCqrsProbePhase.SubmitReturned, discovery!).ConfigureAwait(false);
        await Assert.That((useMcp ? (Task)mcpCall! : sdkCall!).IsCompleted).IsFalse();
        committedFirst = await InspectAsync(0, token).ConfigureAwait(false);
        await Assert.That(committedFirst.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(committedFirst.Metadata.LeaseOwner).IsEqualTo(identity!.PrincipalId);
        await Assert.That(committedFirst.Metadata.Attempts).IsEqualTo(1);
        await Assert.That(committedFirst.Metadata.LeaseVersion).IsEqualTo(1L);
        await Assert.That(committedFirst.Metadata.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(committedFirst.Metadata.Id).IsEqualTo(MultiLaneReceiveRf3Flow.Message);
        await Assert.That(committedFirst.Metadata.LeaseUntil).IsNotNull();
        await Assert.That(committedFirst.PayloadJson).IsEqualTo(MultiLaneReceiveRf3Flow.Payload);
        await Assert.That(committedFirst.HeadersJson).IsEqualTo(MultiLaneReceiveRf3Flow.Headers);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(1, token).ConfigureAwait(false), beforeSecond!);
        await originalCancellation!.CancelAsync().ConfigureAwait(false);
        var cancelled = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Cancelled, discovery!, token).ConfigureAwait(false);
        await Assert.That(cancelled.RequestId).IsEqualTo(marker.RequestId);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, armId, marker.RequestId,
            request.Requests[0].RequestId, discovery!, token).ConfigureAwait(false);
    }

    private async Task VerifyOriginalAsync(CancellationToken token)
    {
        await Assert.That(originalCancellation!.IsCancellationRequested).IsTrue();
        if (useMcp)
        {
            var observation = await (mcpCall ?? throw new InvalidOperationException(Missing)).ConfigureAwait(false);
            await RequestCqrsFaultOutcome.AssertMcpInterruptionAsync(observation).ConfigureAwait(false);
        }
        else
        {
            var result = await (sdkCall ?? throw new InvalidOperationException(Missing)).ConfigureAwait(false);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.UnknownWriteOutcome.ToString());
            await Assert.That(result.Problem?.Detail).IsEqualTo(
                "The write response is unavailable. Retry the same command ID.");
        }
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(0, token).ConfigureAwait(false), committedFirst!);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(1, token).ConfigureAwait(false), beforeSecond!);
        await controls!.RetireArmAsync(armId, token).ConfigureAwait(false);
    }

    private async Task<MessageInspection> InspectAsync(int index, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await administrator!.Sdk.InspectAsync(
            new(request!.Requests[index].Lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false))
            .ConfigureAwait(false) ?? throw new InvalidOperationException(Missing);
}
