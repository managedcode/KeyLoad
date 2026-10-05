namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Maintains one receiver-local finite cache eligibility lease without granting read authority.</summary>
public sealed class CacheReadPermit : ICacheReadPermit, IDisposable
{
    private sealed record Lease(Guid GrantId, CacheReadPermitAcceptance Acceptance, long PreparedTimestamp);
    private sealed record State(Lease? Lease, long LastSequence, bool Closed);

    private readonly Lock writer = new();
    private readonly TimeProvider clock;
    private State state = new(null, 0, false);

    /// <summary>Creates a cold receiver-local permit that evaluates leases with the supplied monotonic clock.</summary>
    /// <param name="clock">The receiver clock used for preparation age and eligibility.</param>
    public CacheReadPermit(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
    }

    /// <summary>Accepts only a newer, locally fresh grant already authenticated by its trusted receiver.</summary>
    public bool TryAccept(Guid grantId, long sequence, long preparedTimestamp, out CacheReadPermitAcceptance acceptance)
    {
        acceptance = default;
        lock (writer)
        {
            var current = Volatile.Read(ref state);
            if (current.Closed || grantId == Guid.Empty || sequence <= current.LastSequence || sequence <= 0)
            {
                return false;
            }
            var now = clock.GetTimestamp();
            if (!TryElapsed(preparedTimestamp, now, out var age) || age >= CacheReadPermitLimits.PrepareValidity)
            {
                return false;
            }

            var previousRevision = current.LastSequence;
            var continuous = current.Lease is { } previous && IsEligible(previous, now);
            acceptance = new(sequence, previousRevision, continuous);
            var lease = new Lease(grantId, acceptance, preparedTimestamp);
            Volatile.Write(ref state, new(lease, sequence, false));
            return true;
        }
    }

    /// <summary>Withdraws only the exact current grant and revision while retaining sequence history.</summary>
    public bool TryWithdraw(Guid grantId, long revision)
    {
        lock (writer)
        {
            var current = Volatile.Read(ref state);
            if (current.Closed || current.Lease is not { } lease || lease.GrantId != grantId || lease.Acceptance.Revision != revision)
            {
                return false;
            }
            Volatile.Write(ref state, new(null, current.LastSequence, false));
            return true;
        }
    }

    /// <summary>Captures the current positive revision only while its prepare-based lease remains eligible.</summary>
    public bool TryCapture(out long revision)
    {
        var current = Volatile.Read(ref state);
        if (current.Closed || current.Lease is not { } lease || !IsEligible(lease, clock.GetTimestamp()))
        {
            revision = 0;
            return false;
        }
        revision = lease.Acceptance.Revision;
        return true;
    }

    /// <summary>Rechecks one captured revision against the current immutable lease and its prepare-based lifetime.</summary>
    public bool IsCurrent(long revision)
    {
        if (revision <= 0)
        {
            return false;
        }
        var current = Volatile.Read(ref state);
        return !current.Closed && current.Lease is { } lease && lease.Acceptance.Revision == revision
            && IsEligible(lease, clock.GetTimestamp());
    }

    /// <summary>Rechecks every immutable receipt field and its existing prepare-origin lease eligibility.</summary>
    public bool IsCurrentAcceptance(CacheReadPermitAcceptance acceptance)
    {
        var current = Volatile.Read(ref state);
        return acceptance.Revision > 0 && !current.Closed && current.Lease is { } lease
            && lease.Acceptance == acceptance && IsEligible(lease, clock.GetTimestamp());
    }

    /// <summary>Permanently closes acceptance and publishes cold state without coordinating with lock-free readers.</summary>
    public void Dispose()
    {
        lock (writer)
        {
            var current = Volatile.Read(ref state);
            if (!current.Closed)
            {
                Volatile.Write(ref state, new(null, current.LastSequence, true));
            }
        }
    }

    private bool IsEligible(Lease lease, long now)
        => TryElapsed(lease.PreparedTimestamp, now, out var age) && age < CacheReadPermitLimits.LeaseValidity;

    private bool TryElapsed(long preparedTimestamp, long now, out TimeSpan elapsed)
    {
        elapsed = default;
        if (now < preparedTimestamp || preparedTimestamp < 0 && now > long.MaxValue + preparedTimestamp)
        {
            return false;
        }
        try
        {
            elapsed = clock.GetElapsedTime(preparedTimestamp, now);
            return elapsed >= TimeSpan.Zero;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (DivideByZeroException)
        {
            return false;
        }
    }
}
