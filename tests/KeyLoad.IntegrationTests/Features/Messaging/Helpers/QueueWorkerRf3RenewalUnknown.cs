using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueWorkerRf3RenewalUnknown
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
    private DeliveryCommand? renewal;
    private DeliveryCommand? parentAck;
    private QueueWorkerDeliveryContext? context;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var scenario = new QueueWorkerRf3RenewalUnknown();
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
        var principal = new PrincipalRecord(identity.PrincipalId, identity.Partition.TenantId,
            [.. request.Requests.Select(leaf => new ScopeGrant(leaf.Lane.Partition.DatabaseId, leaf.Lane.Queue,
                Capability.QueueConsume | Capability.QueueAck | Capability.QueueRenew))], [])
        {
            PolicyEpoch = QueueWorkerRf3Protocol.RenewalProbePolicyEpoch
        };
        await QueueWorkerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)), principal);
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
        var first = request!.Requests[QueueWorkerRf3Protocol.FirstLaneIndex];
        originalCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var policy = QueueWorkerRf3Protocol.Options(QueueWorkerRf3Protocol.SingleDelivery).Value with
        {
            RenewalInterval = TimeSpan.FromSeconds(QueueWorkerRf3Protocol.RenewalCadenceSeconds)
        };
        sdkCall = caller!.Sdk.RunQueueAckWorkerAsync(first.Lane, first.RequestId, async (actual, handlerToken) =>
        {
            context = actual;
            Interlocked.Increment(ref callbacks);
            renewal = new(actual.FirstRenewalId, actual.Lane, actual.Delivery.Token, DeliveryAction.Renew, policy.LeaseSeconds);
            parentAck = new(actual.AcknowledgementId, actual.Lane, actual.Delivery.Token, DeliveryAction.Ack);
            armId = controls!.WriteArm(identity!.PrincipalId, renewal.CommandId, null,
                RequestCqrsProbePhase.SubmitReturned, RequestCqrsProbeAction.Hold);
            armed.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, handlerToken).ConfigureAwait(false);
        }, Microsoft.Extensions.Options.Options.Create(policy), TimeProvider.System, originalCancellation.Token);
        return Task.CompletedTask;
    }

    private async Task ObserveAndCancelAsync(CancellationToken token)
    {
        await armed.Task.WaitAsync(token).ConfigureAwait(false);
        var marker = await controls!.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Observed, discovery!, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(marker, armId, renewal!.CommandId,
            RequestCqrsProbePhase.SubmitReturned, discovery!).ConfigureAwait(false);
        await Assert.That(sdkCall!.IsCompleted).IsFalse();
        committedFirst = await InspectAsync(QueueWorkerRf3Protocol.FirstLaneIndex, token).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(committedFirst);
        await QueueWorkerRf3RenewalUnknownContinuation.LeasedAsync(committedFirst, context!, identity!.PrincipalId);
        await QueueWorkerRf3Claims.RequireAsync(signingKey, incarnation, context!.Lane,
            MultiLaneReceiveRf3Flow.Message, identity.PrincipalId, context.Delivery);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(await InspectAsync(QueueWorkerRf3Protocol.SecondLaneIndex, token).ConfigureAwait(false), beforeSecond!);
        await originalCancellation!.CancelAsync().ConfigureAwait(false);
        var cancelled = await controls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.SubmitReturned,
            RequestCqrsProbeOutcome.Cancelled, discovery!, token).ConfigureAwait(false);
        await Assert.That(cancelled.RequestId).IsEqualTo(marker.RequestId);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, armId, marker.RequestId,
            renewal!.CommandId, discovery!, token).ConfigureAwait(false);
    }

    private async Task VerifyOriginalAsync(CancellationToken token)
    {
        var result = await sdkCall!.ConfigureAwait(false);
        await Assert.That(callbacks).IsEqualTo(QueueWorkerRf3Protocol.SingleDelivery);
        await Assert.That(result.Acknowledged).IsEqualTo(QueueWorkerRf3Protocol.NoAcknowledgements);
        await Assert.That(result.Operations.Any(row => row.Command?.Action == DeliveryAction.Ack)).IsFalse();
        var original = await Assert.That(result.Operations.Where(row => row.Stage == QueueWorkerOperationStage.Returned
            && row.Command?.CommandId == renewal!.CommandId)).HasSingleItem();
        await Assert.That(original.Completion!.IsFailed).IsTrue();
        await Assert.That(original.Completion.Value).IsNull();
        await Assert.That(original.Completion.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnknownWriteOutcome));
        await controls!.RetireArmAsync(armId, token).ConfigureAwait(false);
        await QueueWorkerRf3RenewalUnknownContinuation.ReconcileAsync(administrator!, caller!, request!, original,
            parentAck!, committedFirst!, beforeSecond!, token).ConfigureAwait(false);
        await Assert.That(callbacks).IsEqualTo(QueueWorkerRf3Protocol.SingleDelivery);
    }

    private async Task<MessageInspection> InspectAsync(int index, CancellationToken token)
        => await PublicReadAssertions.MessageAsync(await administrator!.Sdk.InspectAsync(
            new(request!.Requests[index].Lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false))
            .ConfigureAwait(false) ?? throw new InvalidOperationException(Missing);
}
