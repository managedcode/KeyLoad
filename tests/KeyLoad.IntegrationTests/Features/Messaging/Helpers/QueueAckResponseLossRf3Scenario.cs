using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueAckResponseLossRf3Scenario(bool useMcp)
{
    private const string Missing = "The owned ACK response-loss scenario is incomplete.";
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
    private Task<Result<CommitReceipt>>? sdkCall;
    private Task<RequestCqrsFaultMcpObservation>? mcpCall;
    private Guid armId;
    private MessageInspection? beforeSecond;
    private MessageInspection? committedFirst;
    private DeliveryCommand? ack;

    internal static async Task RunAsync(bool useMcp, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new QueueAckResponseLossRf3Scenario(useMcp);
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
        await QueueAckResponseLossRf3Assertions.ReconcileAsync(administrator!, caller!, request!, ack!,
            committedFirst!, beforeSecond!, identity!.PrincipalId, deadline.Token).ConfigureAwait(false);
    }

    private async Task PrepareAsync(CancellationToken token)
    {
        var administratorKey = await StartWaveAsync(token).ConfigureAwait(false);
        await CreateCallersAsync(administratorKey, token).ConfigureAwait(false);
        await ClaimAndDispatchAsync(token).ConfigureAwait(false);
    }

    private async Task<string> StartWaveAsync(CancellationToken token)
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
        return profile.AdminKey;
    }

    private async Task CreateCallersAsync(string administratorKey, CancellationToken token)
    {
        var application = (wave ?? throw new InvalidOperationException(Missing)).App;
        administrator = await RequestCqrsRf3Callers.ConnectAsync(application, RequestCqrsRf3Protocol.Node1,
            administratorKey, token).ConfigureAwait(false);
        identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(administrator.Sdk, token)
            .ConfigureAwait(false);
        request = await MultiLaneReceiveCancellationProvisioning.SeedAsync(administrator.Sdk, identity, token)
            .ConfigureAwait(false);
        caller = await RequestCqrsPhaseFaultProvisioning.ConnectAsync(application, identity, token).ConfigureAwait(false);
        var tool = await caller.Mcp.Client.DiscoverKeyLoadToolAsync(McpCallerTools.MessagesComplete, token)
            .ConfigureAwait(false);
        await McpDiscoveryAssertions.VerifyAsync(tool).ConfigureAwait(false);
        beforeSecond = await InspectAsync(1, token).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.EqualAsync(beforeSecond,
            new MessageInspection(new(MultiLaneReceiveRf3Flow.Message, MessageState.Ready, 0, 1, 1, null, null),
                MultiLaneReceiveRf3Flow.Payload, MultiLaneReceiveRf3Flow.Headers));
    }

    private async Task ClaimAndDispatchAsync(CancellationToken token)
    {
        var currentCaller = caller ?? throw new InvalidOperationException(Missing);
        var currentRequest = request ?? throw new InvalidOperationException(Missing);
        var currentIdentity = identity ?? throw new InvalidOperationException(Missing);
        var currentControls = controls ?? throw new InvalidOperationException(Missing);
        var claimed = await McpCallerAssertions.SdkSuccessAsync(await currentCaller.Sdk.ReceiveAsync(currentRequest.Requests[0], token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var delivery = await Assert.That(claimed.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(MultiLaneReceiveRf3Flow.Message);
        await Assert.That(delivery.PayloadJson).IsEqualTo(MultiLaneReceiveRf3Flow.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(MultiLaneReceiveRf3Flow.Headers);
        await Assert.That(claimed.RequestId).IsEqualTo(currentRequest.Requests[0].RequestId);
        await QueueAckResponseLossRf3Assertions.EqualAsync(delivery,
            new Delivery(MultiLaneReceiveRf3Flow.Message, MultiLaneReceiveRf3Flow.Payload,
                MultiLaneReceiveRf3Flow.Headers, delivery.Token, 1, delivery.LeaseUntil, 1, 1));
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await QueueAckResponseLossRf3Assertions.EqualAsync(await InspectAsync(0, token).ConfigureAwait(false),
            QueueAckResponseLossRf3Assertions.Leased(delivery, currentIdentity.PrincipalId));
        await QueueAckResponseLossRf3Assertions.EqualAsync(await InspectAsync(1, token).ConfigureAwait(false), beforeSecond);
        ack = new(Guid.NewGuid(), currentRequest.Requests[0].Lane, delivery.Token, DeliveryAction.Ack);
        armId = currentControls.WriteArm(currentIdentity.PrincipalId, ack.CommandId, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold);
        originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (useMcp)
        {
            mcpCall = QueueAckResponseLossRf3Assertions.CallAsync(currentCaller, ack, originalCancellation.Token);
        }
        else
        {
            sdkCall = currentCaller.Sdk.CompleteAsync(ack, originalCancellation.Token);
        }
    }

    private async Task ObserveAndCancelAsync(CancellationToken token)
    {
        var marker = await controls!.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery!, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, ack!.CommandId,
            RequestCqrsProbePhase.SubmitReturned, discovery!).ConfigureAwait(false);
        await Assert.That((useMcp ? (Task)mcpCall! : sdkCall!).IsCompleted).IsFalse();
        committedFirst = await InspectAsync(0, token).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.EqualAsync(committedFirst,
            QueueAckResponseLossRf3Assertions.Acked(MultiLaneReceiveRf3Flow.Message));
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(1, token).ConfigureAwait(false), beforeSecond!);
        await originalCancellation!.CancelAsync().ConfigureAwait(false);
        var cancelled = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Cancelled, discovery!, token).ConfigureAwait(false);
        await Assert.That(cancelled.RequestId).IsEqualTo(marker.RequestId);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, armId, marker.RequestId,
            ack!.CommandId, discovery!, token).ConfigureAwait(false);
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
        => await PublicReadAssertions.MessageAsync(await administrator!.Sdk.InspectAsync(
            new(request!.Requests[index].Lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false))
            .ConfigureAwait(false) ?? throw new InvalidOperationException(Missing);
}
