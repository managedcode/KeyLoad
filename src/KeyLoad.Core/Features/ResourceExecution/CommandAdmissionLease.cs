namespace KeyLoad.Core;

/// <summary>Owns one command admission reservation and releases it exactly once.</summary>
public sealed class CommandAdmissionLease : IDisposable
{
    private CommandAdmissionGovernor? owner;

    /// <summary>Gets whether the reservation belongs to the control lane.</summary>
    internal bool Control { get; }

    /// <summary>Gets the owning tenant identifier.</summary>
    internal string Tenant { get; }

    /// <summary>Gets the owning principal identifier.</summary>
    internal string Principal { get; }

    /// <summary>Gets the reserved retained-byte count.</summary>
    internal long Bytes { get; }

    internal CommandAdmissionLease(CommandAdmissionGovernor owner, bool control, string tenant, string principal, long bytes)
    {
        this.owner = owner;
        Control = control;
        Tenant = tenant;
        Principal = principal;
        Bytes = bytes;
    }

    /// <summary>Releases the owned reservation once; repeated calls are harmless.</summary>
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(this);
}
