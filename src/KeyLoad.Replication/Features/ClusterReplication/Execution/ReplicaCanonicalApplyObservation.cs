namespace KeyLoad.Replication;

/// <summary>Correlates optional private native transport observations with their actual synchronous apply owner.</summary>
public static class ReplicaCanonicalApplyObservation
{
    private const string OwnershipChanged = "Canonical apply observation ownership changed.";
    private static readonly AsyncLocal<Scope?> Current = new();

    /// <summary>Opens an explicitly owned scope on the actual canonical apply worker.</summary>
    /// <param name="outbound">Bounded private observation, never network work or database authority.</param>
    /// <returns>The scope restoring its original caller context after the original apply settles.</returns>
    public static IDisposable Enter(Action outbound)
    {
        ArgumentNullException.ThrowIfNull(outbound);
        var scope = new Scope(Current.Value, outbound);
        Current.Value = scope;
        return scope;
    }

    /// <summary>Observes real native outbound transport invocation in an active owning scope only.</summary>
    public static void ObserveOutbound() => Current.Value?.Observe();

    private sealed class Scope(Scope? previous, Action outbound) : IDisposable
    {
        private const int Open = 0;
        private const int Closed = 1;
        private int closed;
        internal void Observe()
        {
            if (Volatile.Read(ref closed) == Open)
            { outbound(); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, Closed) != Open)
            { return; }
            if (!ReferenceEquals(Current.Value, this))
            { throw new InvalidOperationException(OwnershipChanged); }
            Current.Value = previous;
        }
    }
}
