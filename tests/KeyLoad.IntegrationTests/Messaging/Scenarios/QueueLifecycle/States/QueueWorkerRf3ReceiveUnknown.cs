using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueWorkerRf3ReceiveUnknown
{
    private const string Missing = "The owned ACK response-loss scenario is incomplete.";
    private readonly List<Exception> failures = [];
    private string root = string.Empty;
    private byte[] signingKey = [];
    private Guid incarnation;
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
    private Task<QueueWorkerResult>? sdkCall;
    private int callbacks;
    private readonly TaskCompletionSource armed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Guid armId;
    private MessageInspection? beforeSecond;
    private MessageInspection? committedFirst;
    private Guid operationId;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new QueueWorkerRf3ReceiveUnknown();
        await ServerFailureObserver.ObserveAsync(() => scenario.ExecuteAsync(deadline.Token), scenario.failures)
            .ConfigureAwait(false);
        await RequestCqrsPhaseFaultCleanup.RunAsync(scenario.root, scenario.rootOwned, scenario.controls,
            scenario.wave, scenario.startupAttempted, scenario.caller, scenario.administrator, scenario.discovery,
            scenario.originalCancellation, null, scenario.sdkCall, null, scenario.armId, scenario.failures)
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
        var data = Path.Combine(root, QueueWorkerRf3Protocol.DataDirectory);
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(data, token).ConfigureAwait(false)).Profile;
        signingKey = Convert.FromBase64String(profile.SigningKey);
        incarnation = profile.Incarnation;
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
        beforeSecond = await InspectAsync(QueueWorkerRf3Protocol.SecondLaneIndex, token).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.EqualAsync(beforeSecond,
            new MessageInspection(new(MultiLaneReceiveRf3Flow.Message, MessageState.Ready, QueueWorkerRf3Protocol.NoAttempts, QueueWorkerRf3Protocol.ReadyStateVersion, QueueWorkerRf3Protocol.FirstReadySequence, null, null, DeliveryGeneration: QueueWorkerRf3Protocol.InitialDeliveryGeneration),
                MultiLaneReceiveRf3Flow.Payload, MultiLaneReceiveRf3Flow.Headers));
    }

    private Task ClaimAndDispatchAsync(CancellationToken token)
    {
        var currentRequest = request ?? throw new InvalidOperationException(Missing);
        operationId = currentRequest.Requests[QueueWorkerRf3Protocol.FirstLaneIndex].RequestId;
        armId = controls!.WriteArm(identity!.PrincipalId, operationId, null,
            RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold);
        armed.TrySetResult();
        originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        sdkCall = caller!.Sdk.RunQueueAckWorkerAsync(currentRequest.Requests[QueueWorkerRf3Protocol.FirstLaneIndex].Lane,
            operationId, (context, handlerToken) =>
            {
                Interlocked.Increment(ref callbacks);
                throw new InvalidOperationException(Missing);
            }, QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery), TimeProvider.System, originalCancellation.Token);
        return Task.CompletedTask;
    }

    private async Task ObserveAndCancelAsync(CancellationToken token)
    {
        await armed.Task.WaitAsync(token).ConfigureAwait(false);
        var marker = await controls!.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery!, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, operationId,
            RequestCqrsProbePhase.SubmitReturned, discovery!).ConfigureAwait(false);
        await Assert.That(sdkCall!.IsCompleted).IsFalse();
        committedFirst = await InspectAsync(QueueWorkerRf3Protocol.FirstLaneIndex, token).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(committedFirst);
        await QueueAckResponseLossRf3Assertions.EqualAsync(committedFirst,
            new MessageInspection(new(MultiLaneReceiveRf3Flow.Message, MessageState.Leased,
                QueueWorkerRf3Protocol.ClaimAttempt, QueueWorkerRf3Protocol.ClaimStateVersion, QueueWorkerRf3Protocol.FirstReadySequence, null, null,
                identity!.PrincipalId, QueueWorkerRf3Protocol.InitialLeaseVersion, committedFirst.Metadata.LeaseUntil, DeliveryGeneration: QueueWorkerRf3Protocol.InitialDeliveryGeneration),
                MultiLaneReceiveRf3Flow.Payload, MultiLaneReceiveRf3Flow.Headers));
        await Assert.That(committedFirst.Metadata.LeaseUntil).IsNotNull();
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.SecondLaneIndex, token).ConfigureAwait(false), beforeSecond!);
        await originalCancellation!.CancelAsync().ConfigureAwait(false);
        var cancelled = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Cancelled, discovery!, token).ConfigureAwait(false);
        await Assert.That(cancelled.RequestId).IsEqualTo(marker.RequestId);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, armId, marker.RequestId,
            operationId, discovery!, token).ConfigureAwait(false);
    }

    private async Task VerifyOriginalAsync(CancellationToken token)
    {
        var result = await sdkCall!.ConfigureAwait(false);
        await Assert.That(callbacks).IsEqualTo(QueueWorkerRf3Protocol.NoDeliveries);
        await Assert.That(result.PeakActiveHandlers).IsEqualTo(QueueWorkerRf3Protocol.NoActiveHandlers);
        await Assert.That(result.Operations.Any(row => row.Command is not null)).IsFalse();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Receive?.RequestId == operationId)).HasSingleItem();
        await Assert.That(original.ReceiveFailure?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(result.Operations.Select(row => row.Receive!.RequestId).Distinct()).HasSingleItem();
        await controls!.RetireArmAsync(armId, token).ConfigureAwait(false);
        var reconciled = await caller!.Sdk.ReconcileQueueWorkerOperationAsync(original, token).ConfigureAwait(false);
        await Assert.That(reconciled.ReceiveFailure).IsNull();
        await Assert.That(reconciled.Receive!.RequestId).IsEqualTo(operationId);
        await CurrentImagesAsync(token).ConfigureAwait(false);
        var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, original.Receive!, token).ConfigureAwait(false)).ConfigureAwait(false);
        var delivery = await Assert.That(replay.Value.Deliveries).HasSingleItem();
        await Assert.That(replay.Value.RequestId).IsEqualTo(operationId);
        await QueueAckResponseLossRf3Assertions.EqualAsync(reconciled.ReceiveToken, replay.Value.Token).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.EqualAsync(delivery,
            new Delivery(MultiLaneReceiveRf3Flow.Message, MultiLaneReceiveRf3Flow.Payload,
                MultiLaneReceiveRf3Flow.Headers, delivery.Token, QueueWorkerRf3Protocol.InitialLeaseVersion,
                committedFirst!.Metadata.LeaseUntil!.Value, QueueWorkerRf3Protocol.ClaimAttempt, QueueWorkerRf3Protocol.InitialDeliveryGeneration)).ConfigureAwait(false);
        await QueueWorkerRf3Claims.RequireAsync(signingKey, incarnation, original.Receive!.Lane,
            MultiLaneReceiveRf3Flow.Message, identity!.PrincipalId, delivery).ConfigureAwait(false);
        await CurrentImagesAsync(token).ConfigureAwait(false);
        await Assert.That(callbacks).IsEqualTo(QueueWorkerRf3Protocol.NoDeliveries);
        var parentAck = new DeliveryCommand(Guid.NewGuid(), original.Receive!.Lane, delivery.Token, DeliveryAction.Ack);
        _ = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync(parentAck, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.ReconcileAsync(administrator!, caller!, request!, parentAck,
            QueueAckResponseLossRf3Assertions.Acked(MultiLaneReceiveRf3Flow.Message), beforeSecond!, identity!.PrincipalId,
            token).ConfigureAwait(false);
    }

    private async Task CurrentImagesAsync(CancellationToken token)
    {
        await QueueAckResponseLossRf3Assertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.FirstLaneIndex, token)
            .ConfigureAwait(false), committedFirst).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.SecondLaneIndex, token)
            .ConfigureAwait(false), beforeSecond).ConfigureAwait(false);
    }

    private async Task<MessageInspection> InspectAsync(int index, CancellationToken token)
        => await PublicReadAssertions.MessageAsync(await administrator!.Sdk.InspectAsync(
            new(request!.Requests[index].Lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false))
            .ConfigureAwait(false) ?? throw new InvalidOperationException(Missing);
}
