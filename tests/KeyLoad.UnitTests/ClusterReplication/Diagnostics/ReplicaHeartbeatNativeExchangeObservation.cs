using System.Diagnostics;
using System.Runtime.ExceptionServices;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClusterRouting;
using D = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaHeartbeatAdmissionProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Counts completed native exchanges to exactly two original follower addresses without retaining activities.</summary>
internal sealed class ReplicaHeartbeatNativeExchangeObservation : IDisposable
{
    private readonly string firstTarget;
    private readonly string secondTarget;
    private readonly ActivityListener listener;
    private long first;
    private long second;
    private long startedUtcTicks;
    private int armed;
    private Exception? failure;
    internal (long First, long Second) Counts => (Interlocked.Read(ref first), Interlocked.Read(ref second));

    internal ReplicaHeartbeatNativeExchangeObservation(string firstAddress, string secondAddress)
    {
        firstTarget = D.AddressSeparator + firstAddress;
        secondTarget = D.AddressSeparator + secondAddress;
        listener = new()
        {
            ShouldListenTo = static source => source.Name == OrleansRuntimeTelemetryTokens.ApplicationSource,
            Sample = static (ref ActivityCreationOptions<ActivityContext> options) => options.Name == D.ExchangeOperation
                ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStopped = Observe
        };
        ActivitySource.AddActivityListener(listener);
    }

    internal void Arm(DateTime started)
    {
        Volatile.Write(ref startedUtcTicks, started.Ticks);
        Volatile.Write(ref armed, D.One);
    }

    internal void Stop() => Volatile.Write(ref armed, D.Empty);
    internal void RequireHealthy()
    {
        if (Volatile.Read(ref failure) is { } original)
        {
            ExceptionDispatchInfo.Capture(original).Throw();
        }
    }
    public void Dispose() => listener.Dispose();

    private void Observe(Activity activity)
    {
        try
        {
            ObserveActual(activity);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            _ = Interlocked.CompareExchange(ref failure, error, null);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            _ = Interlocked.CompareExchange(ref failure, error, null);
        }
    }

    private void ObserveActual(Activity activity)
    {
        if (Volatile.Read(ref armed) != D.One || activity.Kind != ActivityKind.Client
            || activity.Status == ActivityStatusCode.Error || activity.StartTimeUtc.Ticks < Volatile.Read(ref startedUtcTicks)
            || activity.GetTagItem(D.MethodTag) is not string method || method != D.Exchange
            || activity.GetTagItem(D.TargetTag) is not string target)
        {
            return;
        }
        if (target.EndsWith(firstTarget, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref first);
        }
        else if (target.EndsWith(secondTarget, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref second);
        }
    }
}
