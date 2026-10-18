namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal sealed class RequestCqrsCapabilityLedger
{
    private int calls;
    internal int Count => Volatile.Read(ref calls);

    internal int RecordCall() => Interlocked.Increment(ref calls);
}
