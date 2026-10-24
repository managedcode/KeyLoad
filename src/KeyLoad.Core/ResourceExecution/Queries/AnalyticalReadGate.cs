namespace KeyLoad.Core.ResourceExecution;

/// <summary>Counts in-flight analytical reads so their owners can prove release; it never refuses a read.</summary>
internal sealed class AnalyticalReadGate
{
    private int inFlight;

    /// <summary>Gets the current number of owned reservations.</summary>
    internal int InFlight => Volatile.Read(ref inFlight);

    /// <summary>Records one read until the returned reservation is disposed.</summary>
    internal IDisposable Admit(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref inFlight);
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
