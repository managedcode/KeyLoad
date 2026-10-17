using D = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaHeartbeatAdmissionProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

/// <summary>Observes only the original operation budget; native runtime time and timers remain system-owned.</summary>
internal sealed class ReplicaHeartbeatAdmissionClock(CancellationToken token) : TimeProvider
{
    private readonly TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int armed;
    internal Task Arrived => arrived.Task;
    internal DateTime ArrivalUtc
    {
        get; private set;
    }
    internal bool IsHeld => arrived.Task.IsCompletedSuccessfully && !released.Task.IsCompleted;
    internal void Arm() => Volatile.Write(ref armed, D.One);
    internal void Release() => released.TrySetResult();
    public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => TimeProvider.System.CreateTimer(callback, state, dueTime, period);

    public override long GetTimestamp()
    {
        if (Interlocked.CompareExchange(ref armed, D.Empty, D.One) == D.One)
        {
            ArrivalUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
            arrived.TrySetResult();
            released.Task.WaitAsync(token).GetAwaiter().GetResult();
        }
        return TimeProvider.System.GetTimestamp();
    }
}
