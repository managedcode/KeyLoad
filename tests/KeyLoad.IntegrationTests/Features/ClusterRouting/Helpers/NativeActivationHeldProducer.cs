using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns one actual caller token/task and joins its original native probe lifecycle.</summary>
internal sealed class NativeActivationHeldProducer<T> : IAsyncDisposable
{
    private readonly CancellationTokenSource caller;
    private readonly RequestCqrsProbeFixture controls;
    private readonly IReadOnlyList<ReplicaSiloDiscovery> discovery;
    private readonly List<Exception> failures;
    private RequestCqrsProbeMarkerRecord? observed;
    private Task<RequestCqrsProbeMarkerRecord>? markerReader;
    private Task<RequestCqrsProbeActivationRecord>? witnessReader;
    internal Guid Arm { get; }
    internal Task<T> Producer { get; }

    internal NativeActivationHeldProducer(RequestCqrsProbeFixture controls,
        IReadOnlyList<ReplicaSiloDiscovery> discovery, string principal, Guid command,
        string? voter, Func<CancellationToken, Task<T>> send, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        this.controls = controls;
        this.discovery = discovery;
        this.failures = failures;
        caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            Arm = controls.WriteArm(principal, command, null, RequestCqrsProbePhase.BeforeSubmit,
                RequestCqrsProbeAction.Hold, targetVoter: voter);
            Producer = send(caller.Token);
        }
        catch (Exception primary)
        {
            var construction = new List<Exception> { primary };
            ServerFailureObserver.Observe(caller.Dispose, construction);
            ServerFailureObserver.ThrowIfAny(construction);
            throw;
        }
    }

    internal async Task<RequestCqrsProbeActivationRecord> ObserveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var marker = markerReader = controls.WaitForMarkerAsync(Arm, RequestCqrsProbePhase.BeforeSubmit,
            RequestCqrsProbeOutcome.Observed, discovery, caller.Token);
        _ = await Task.WhenAny(marker, Producer).ConfigureAwait(false);
        if (Producer.IsCompleted)
        { throw new InvalidOperationException(NativeActivationRf3Protocol.Missing); }
        observed = await marker.ConfigureAwait(false);
        var witness = witnessReader = NativeActivationRf3Witness.ReadAsync(controls, observed.Value, discovery, caller.Token);
        _ = await Task.WhenAny(witness, Producer).ConfigureAwait(false);
        if (Producer.IsCompleted)
        { throw new InvalidOperationException(NativeActivationRf3Protocol.Missing); }
        return await witness.ConfigureAwait(false);
    }

    internal void Release()
        => controls.WriteRelease(Arm, (observed ?? throw new InvalidOperationException(NativeActivationRf3Protocol.Missing)).RequestId);

    public async ValueTask DisposeAsync()
    {
        await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false);
        using var cleanup = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(async () => { _ = await Producer.WaitAsync(cleanup.Token).ConfigureAwait(false); }, failures);
        if (markerReader is { } originalMarker)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await originalMarker.WaitAsync(cleanup.Token); }, failures); }
        if (witnessReader is { } originalWitness)
        { await ServerFailureObserver.ObserveAsync(async () => { _ = await originalWitness.WaitAsync(cleanup.Token); }, failures); }
        if (observed is { } marker)
        {
            await ServerFailureObserver.ObserveAsync(() => RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls,
                Arm, marker.RequestId, marker.CommandId, discovery, cleanup.Token), failures);
            await ServerFailureObserver.ObserveAsync(() => controls.RetireArmAsync(Arm, cleanup.Token), failures);
        }
        if (Producer.IsCompleted && (markerReader is null || markerReader.IsCompleted)
            && (witnessReader is null || witnessReader.IsCompleted))
        {
            try
            { caller.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        }
        else
        { failures.Add(new InvalidOperationException(NativeActivationRf3Protocol.Missing)); }
    }
}
