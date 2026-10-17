namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Admits a fixed number of analytical reads without a wait queue.</summary>
internal sealed class AnalyticalReadGate
{
    private const int AdjacentElementOffset = 1;

    private const string Exhausted = "The query concurrency budget is exhausted.";
    private readonly int capacity;
    private int inFlight;

    /// <summary>Creates a gate with a strictly positive allowance.</summary>
    internal AnalyticalReadGate(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
    }

    /// <summary>Gets the current number of owned reservations.</summary>
    internal int InFlight => Volatile.Read(ref inFlight);

    /// <summary>Atomically reserves one read or rejects saturation immediately.</summary>
    internal IDisposable Admit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var count = Volatile.Read(ref inFlight);
            if (count >= capacity)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw Errors.Fail(ErrorCode.ResourceExhausted, Exhausted);
            }
            if (Interlocked.CompareExchange(ref inFlight, count + AdjacentElementOffset, count) == count)
            {
                break;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Reservation(this);
        }
        catch (Exception)
        {
            Release();
            throw;
        }
    }

    private void Release() => Interlocked.Decrement(ref inFlight);

    private sealed class Reservation(AnalyticalReadGate gate) : IDisposable
    {
        private AnalyticalReadGate? owner = gate;

        /// <summary>Releases this allowance once, including repeated disposal.</summary>
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
}
