using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueWorkerRf3OriginalUnknown
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
    private DeliveryCommand? ack;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new QueueWorkerRf3OriginalUnknown();
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
        var currentCaller = caller ?? throw new InvalidOperationException(Missing);
        var currentRequest = request ?? throw new InvalidOperationException(Missing);
        var currentIdentity = identity ?? throw new InvalidOperationException(Missing);
        var currentControls = controls ?? throw new InvalidOperationException(Missing);
        originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        sdkCall = currentCaller.Sdk.RunQueueAckWorkerAsync(currentRequest.Requests[QueueWorkerRf3Protocol.FirstLaneIndex].Lane,
            currentRequest.Requests[QueueWorkerRf3Protocol.FirstLaneIndex].RequestId, async (context, handlerToken) =>
            {
                handlerToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref callbacks);
                await QueueWorkerRf3Claims.RequireAsync(signingKey, incarnation, context.Lane,
                    MultiLaneReceiveRf3Flow.Message, currentIdentity.PrincipalId, context.Delivery).ConfigureAwait(false);
                await QueueAckResponseLossRf3Assertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.FirstLaneIndex,
                    handlerToken).ConfigureAwait(false), QueueAckResponseLossRf3Assertions.Leased(context.Delivery,
                    currentIdentity.PrincipalId)).ConfigureAwait(false);
                ack = new(context.AcknowledgementId, context.Lane, context.Delivery.Token, DeliveryAction.Ack);
                armId = currentControls.WriteArm(currentIdentity.PrincipalId, ack.CommandId, null,
                    RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold);
                armed.TrySetResult();
            },
            QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery), TimeProvider.System, originalCancellation.Token);
        return Task.CompletedTask;
    }

    private async Task ObserveAndCancelAsync(CancellationToken token)
    {
        await armed.Task.WaitAsync(token).ConfigureAwait(false);
        var marker = await controls!.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery!, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, ack!.CommandId,
            RequestCqrsProbePhase.SubmitReturned, discovery!).ConfigureAwait(false);
        await Assert.That(sdkCall!.IsCompleted).IsFalse();
        committedFirst = await InspectAsync(QueueWorkerRf3Protocol.FirstLaneIndex, token).ConfigureAwait(false);
        await QueueAckResponseLossRf3Assertions.EqualAsync(committedFirst,
            QueueAckResponseLossRf3Assertions.Acked(MultiLaneReceiveRf3Flow.Message));
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.SecondLaneIndex, token).ConfigureAwait(false), beforeSecond!);
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
        var result = await (sdkCall ?? throw new InvalidOperationException(Missing)).ConfigureAwait(false);
        await Assert.That(callbacks).IsEqualTo(QueueWorkerRf3Protocol.SingleDelivery);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.NoAcknowledgements);
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.CommandId == ack!.CommandId)).HasSingleItem();
        var completion = QueueWorkerRf3Completion.Require(original);
        await Assert.That(completion.IsFailed).IsTrue();
        await Assert.That(completion.Value).IsNull();
        await Assert.That(completion.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(result.Operations.Any(row => row.Stage == QueueWorkerOperationStage.Prepared
            && row.Command == ack)).IsTrue();
        await controls!.RetireArmAsync(armId, token).ConfigureAwait(false);
        var reconciled = await caller!.Sdk.ReconcileQueueWorkerOperationAsync(original, token).ConfigureAwait(false);
        await Assert.That(QueueWorkerRf3Completion.Require(reconciled).IsSuccess).IsTrue();
        await Assert.That(reconciled.Command).IsEqualTo(ack);
        await Assert.That(callbacks).IsEqualTo(QueueWorkerRf3Protocol.SingleDelivery);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.FirstLaneIndex, token).ConfigureAwait(false), committedFirst!);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.SecondLaneIndex, token).ConfigureAwait(false), beforeSecond!);
    }

    private async Task<MessageInspection> InspectAsync(int index, CancellationToken token)
        => await PublicReadAssertions.MessageAsync(await administrator!.Sdk.InspectAsync(
            new(request!.Requests[index].Lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false))
            .ConfigureAwait(false) ?? throw new InvalidOperationException(Missing);
}
