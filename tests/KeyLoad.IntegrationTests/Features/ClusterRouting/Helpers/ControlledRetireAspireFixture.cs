using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using KeyLoad.Server.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Borrows the original Aspire wave and joins its native stop before any offline reader.</summary>
internal sealed class ControlledRetireAspireFixture : IAsyncDisposable
{
    private readonly Dictionary<Guid, (RequestCqrsProbeMarkerRecord Marker, ReplicaSiloDiscovery[] Owners)> observed = [];
    private ControlledRetireAspireFixture()
    {
    }

    internal TwoRf3MembershipWave Wave
    {
        get; private set;
    } = null!;

    internal static async Task<ControlledRetireAspireFixture> StartAsync(CancellationToken token)
    {
        var owner = new ControlledRetireAspireFixture();
        try
        {
            await owner.InitializeAsync(token).ConfigureAwait(false);
            return owner;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        Wave = await NativeCanonicalModelCutWaveStartup.StartAsync(
            PartitionMovementPublicParentRf3Administrator.PrincipalId, token).ConfigureAwait(false);
    }

    internal Guid Arm(PartitionMoveRequest request, long cleanupGeneration)
    {
        var command = PartitionMovementParentPhaseIds.For(request,
            PartitionMovementPublicParentRf3Administrator.PrincipalId,
            PartitionMovementParentPhaseRole.Retire, cleanupGeneration: cleanupGeneration);
        return Wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            command, null, RequestCqrsProbePhase.RetireOperationSealed, RequestCqrsProbeAction.Hold);
    }

    internal async Task WaitSealedAsync(Guid armId, Task<Result<PartitionMoveResult>> producer, CancellationToken token)
    {
        var owners = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(Wave.Application, Wave.Profile, token);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        var marker = Wave.QueryControls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.RetireOperationSealed,
            RequestCqrsProbeOutcome.Observed, owners, wait.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (await Task.WhenAny(producer, marker) == producer)
            {
                await ObserveOriginalFirstAsync(producer, marker, wait.Token);
                throw new InvalidOperationException(P.Missing);
            }
            var actual = await marker;
            await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(actual, armId,
                Wave.QueryControls.ArmFor(armId).CommandId, RequestCqrsProbePhase.RetireOperationSealed, owners);
            observed.Add(armId, (actual, owners));
        }, failures);
        await ServerFailureObserver.ObserveAsync(wait.CancelAsync, failures);
        try
        {
            _ = await marker;
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested && !token.IsCancellationRequested) { }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        if (failures.Count != P.First)
        {
            Wave.RetainRoots();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ObserveOriginalFirstAsync(Task<Result<PartitionMoveResult>> original,
        Task<RequestCqrsProbeMarkerRecord> capture, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await McpCallerAssertions.SdkSuccessAsync(await original);
        }, failures);
        if (failures.Count != P.First)
        {
            var message = $"NativeRetireOriginalFirst selected=original originalTask={original.Id} captureTask={capture.Id}"
                + $" originalCompleted={original.IsCompleted} originalCanceled={original.IsCanceled}"
                + $" originalFaulted={original.IsFaulted} captureCompleted={capture.IsCompleted}"
                + $" captureSucceeded={capture.IsCompletedSuccessfully} captureCanceled={capture.IsCanceled}"
                + $" captureFaulted={capture.IsFaulted} captureWaitCanceled={cancellationToken.IsCancellationRequested}";
            await ServerFailureObserver.ObserveAsync(() => Console.Error.WriteLineAsync(message), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task JoinCancelledAsync(Guid armId, CancellationToken token)
    {
        var original = observed[armId];
        var cancelled = await Wave.QueryControls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.RetireOperationSealed,
            RequestCqrsProbeOutcome.Cancelled, original.Owners, token);
        await Assert.That(cancelled.RequestId).IsEqualTo(original.Marker.RequestId);
        await Assert.That(cancelled.CommandId).IsEqualTo(original.Marker.CommandId);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(Wave.QueryControls, armId,
            original.Marker.RequestId, original.Marker.CommandId, original.Owners, token);
    }

    internal async Task<PartitionMovementPublicParentRf3NativeCut[]> StopAndReadAsync(
        PartitionMoveRequest request, Guid phaseId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = await Wave.StopForDirectoryReadAsync();
        return TwoRf3MembershipProtocol.Nodes.Select(node =>
            PartitionMovementPublicParentRf3Cut.ReadStopped(Wave, node, request, phaseId)).ToArray();
    }

    internal Task RestartAsync(CancellationToken token) => Wave.RestartJoinedAsync(token);

    internal void RetainRoots() => Wave.RetainRoots();

    public ValueTask DisposeAsync() => Wave?.DisposeAsync() ?? ValueTask.CompletedTask;
}
