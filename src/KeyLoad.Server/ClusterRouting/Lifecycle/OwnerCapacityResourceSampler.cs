using KeyLoad.Core.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class OwnerCapacityResourceSampler : IAsyncDisposable
{
    private const long FirstSequence = 1;
    private readonly string directory;
    private readonly TimeProvider clock;
    private readonly IOptions<TenantPlacementExecutionOptions> options;
    private readonly CancellationTokenSource closing = new();
    private readonly Lock lifetime = new();
    private Task? producer;
    private readonly TaskCompletionSource firstObservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? shutdown;
    private OwnerCapacityMeasuredSample? latest;
    private bool disposed;
    private bool stopping;

    internal OwnerCapacityResourceSampler(string ownerDirectory, TimeProvider clock,
        IOptions<TenantPlacementExecutionOptions> options)
    {
        options.Value.Validate();
        directory = ownerDirectory;
        this.clock = clock;
        this.options = options;
    }

    internal void Start()
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed || shutdown is not null, this);
            if (options.Value.Enabled)
            {
                producer ??= ProduceAndObserveAsync(closing.Token);
            }
        }
    }

    internal Task AwaitOriginalObservationAsync(CancellationToken token)
        => firstObservation.Task.WaitAsync(token);

    private async Task ProduceAndObserveAsync(CancellationToken token)
    {
        try
        {
            await ProduceAsync(token).ConfigureAwait(false);
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            firstObservation.TrySetException(failure);
            throw;
        }
        catch (Exception failure) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            firstObservation.TrySetException(failure);
            throw;
        }
    }

    internal OwnerResourceSampleV1 Read(string originalProcessDomain)
    {
        if (Volatile.Read(ref stopping))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.Unavailable);
        }
        var measured = Volatile.Read(ref latest);
        var now = clock.GetUtcNow();
        if (measured is null || measured.OperatingSystem.SampledAt > now
            || string.IsNullOrWhiteSpace(measured.OperatingSystem.HostDomain)
            || measured.OperatingSystem.SampledAt + TimeSpan.FromSeconds(options.Value.FreshnessSeconds) <= now
            || string.IsNullOrWhiteSpace(originalProcessDomain))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TenantPlacementCapacityProtocol.Unavailable);
        }
        var original = measured.OperatingSystem;
        return new(measured.Sequence, original.SampledAt,
            original.SampledAt + TimeSpan.FromSeconds(options.Value.FreshnessSeconds),
            original.AvailableDiskBytes, original.TotalDiskBytes, original.MemoryCurrentBytes,
            original.MemoryLimitBytes, measured.CpuBasisPoints, original.HostDomain, originalProcessDomain);
    }

    private async Task ProduceAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.SampleIntervalSeconds), clock);
        var previous = await OwnerCapacityCgroupReader.CaptureAsync(directory, clock, options, token).ConfigureAwait(false);
        var sequence = FirstSequence;
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            var current = await OwnerCapacityCgroupReader.CaptureAsync(directory, clock, options, token).ConfigureAwait(false);
            var usage = previous is not null && current is not null
                ? OwnerCapacityCpuSample.Calculate(previous, current, clock) : null;
            Volatile.Write(ref latest, current is not null && usage is { } cpu
                ? new(sequence, current, cpu) : null);
            firstObservation.TrySetResult();
            sequence = checked(sequence + FirstSequence);
            previous = current;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifetime)
        {
            if (shutdown is null)
            {
                Volatile.Write(ref stopping, true);
                shutdown = StopAsync();
            }
            return new(shutdown);
        }
    }

    private async Task StopAsync()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(closing.Cancel, failures);
        if (producer is not null)
        {
            await JoinProducerAsync(producer, failures).ConfigureAwait(false);
        }
        Volatile.Write(ref latest, null);
        try
        {
            closing.Dispose();
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        disposed = true;
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task JoinProducerAsync(Task original, List<Exception> failures)
    {
        try
        {
            await original.ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (closing.IsCancellationRequested
            && error.CancellationToken == closing.Token)
        {
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
    }
}
