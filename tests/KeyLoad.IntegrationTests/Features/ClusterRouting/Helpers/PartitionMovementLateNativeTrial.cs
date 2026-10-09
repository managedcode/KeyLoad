using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Actual native full-parent factory producer; embedded cold application is separately admitted support.</summary>
internal sealed class PartitionMovementLateNativeTrial : IAsyncDisposable
{
    private readonly List<Exception> failures;
    private PartitionMovementLateNativeOwners? owners;
    private PartitionMovementLateNativeSeed? seed;
    private Task<Result<PartitionMoveResult>>? producer;
    private Task? disposal;
    private const int InitialGeneration = 0;
    private const int NextGeneration = 1;

    private PartitionMovementLateNativeTrial(List<Exception> failures) => this.failures = failures;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var failures = new List<Exception>();
        await using (var trial = new PartitionMovementLateNativeTrial(failures))
        {
            try
            { await ServerFailureObserver.ObserveAsync(() => trial.ExecuteAsync(lifetime.Token), failures); }
            finally
            { ServerFailureObserver.Observe(lifetime.Cancel, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        var cleanupFailures = new List<Exception>();
        if (owners is { } owned)
        { await ServerFailureObserver.ObserveAsync(owned.StopAsync, cleanupFailures); }
        if (producer is { } operation && owners is { } producerOwner)
        { await ServerFailureObserver.ObserveAsync(() => producerOwner.JoinProducerAsync(operation), cleanupFailures); }
        if (producer is { IsCompleted: false })
        { cleanupFailures.Add(new InvalidOperationException("Retain the original clients and roots while the SDK producer is unjoined.")); }
        else if (seed is not null)
        {
            try
            { seed.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanupFailures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanupFailures.Add(error); }
        }
        if (owners is not null)
        {
            if (cleanupFailures.Count != 0 || failures.Count != 0)
            { owners.RetainRoots(); }
            try
            { await owners.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanupFailures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanupFailures.Add(error); }
        }
        failures.AddRange(cleanupFailures);
    }

    private async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        owners = new();
        await owners.StartAsync(cancellationToken);
        seed = new(owners.Settings);
        await seed.CreateAsync(owners.Settings, cancellationToken);
        var firstBorrow = owners.Borrow.Arm();
        producer = seed.Source.MovePartitionAsync(seed.Request, cancellationToken);
        owners.TrackProducer(producer);
        var original = await firstBorrow.WaitAsync(cancellationToken);
        await owners.Borrow.JoinFirstAsync(cancellationToken);
        await Assert.That((await producer.ConfigureAwait(false)).IsFailed).IsTrue();
        var originalPhase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(original.Operation);
        var originalAdmission = originalPhase.ReceiverEffectAdmission
            ?? throw new InvalidOperationException("The original receiver did not seal its actual proofs.");
        await Assert.That(original.Operation.Id).IsEqualTo(PartitionMovementParentPhaseIds.For(seed.Request, "root", PartitionMovementParentPhaseRole.Retire, cleanupGeneration: InitialGeneration));
        await WaitForOriginalExpiryAsync(originalAdmission.OriginalExpiresAt, cancellationToken);
        var secondBorrow = owners.Borrow.Arm();
        producer = seed.Source.MovePartitionAsync(seed.Request with { Mode = PartitionMoveMode.Resume }, cancellationToken);
        owners.TrackProducer(producer);
        var next = await secondBorrow.WaitAsync(cancellationToken);
        var nextPhase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(next.Operation);
        var nextAdmission = nextPhase.ReceiverEffectAdmission
            ?? throw new InvalidOperationException("The next receiver did not seal its actual proofs.");
        await Assert.That(next.Operation.Id).IsEqualTo(PartitionMovementParentPhaseIds.For(seed.Request, "root", PartitionMovementParentPhaseRole.Retire, cleanupGeneration: NextGeneration));
        await Assert.That(next.Operation.Id).IsNotEqualTo(original.Operation.Id);
        await owners.StopAsync();
        await owners.Borrow.JoinSecondAsync(cancellationToken);
        await Assert.That((await producer.ConfigureAwait(false)).IsFailed).IsTrue();
        await PartitionMovementLateNativeColdOracle.RequireAsync(owners, original, cancellationToken);
        await WaitForOriginalExpiryAsync(nextAdmission.OriginalExpiresAt, cancellationToken);
        await owners.StartAsync(cancellationToken);
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(
            seed.Request with { Mode = PartitionMoveMode.Resume }, cancellationToken));
        await seed.RequireOriginalAsync(cancellationToken);
        await owners.StopAsync();
        await PartitionMovementLateNativeColdOracle.RequirePersistedAsync(owners, original, cancellationToken);
        await owners.StartAsync(cancellationToken);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.Request, cancellationToken));
        await KeyLoad.IntegrationTests.Features.QueryExecution.SqlRf3Protocol.EqualAsync(terminal, replay);
        await seed.RequireHealthyAsync(replay, cancellationToken);
    }

    private static async Task WaitForOriginalExpiryAsync(DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var remaining = expiresAt - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        { await Task.Delay(remaining, TimeProvider.System, cancellationToken).ConfigureAwait(false); }
        await Assert.That(TimeProvider.System.GetUtcNow() >= expiresAt).IsTrue();
    }
}
