namespace KeyLoad;

/// <summary>Checks disposable cache eligibility without granting database read authority.</summary>
/// <remarks>Authorization, quorum barriers and committed-storage checks remain mandatory.</remarks>
public interface ICacheReadPermit
{
    /// <summary>Captures a positive local revision only while its receiver-owned lease is eligible.</summary>
    /// <param name="revision">Current revision, or zero when cold, expired or closed.</param>
    /// <returns>Whether acceleration is locally eligible at the observation point.</returns>
    bool TryCapture(out long revision);

    /// <summary>Rechecks the exact captured revision and receiver-owned finite lifetime.</summary>
    /// <param name="revision">Previously captured positive local revision.</param>
    /// <returns>Whether the same acceptance remains eligible.</returns>
    bool IsCurrent(long revision);

    /// <summary>Validates the complete current acceptance and its prepare-origin lease eligibility.</summary>
    /// <param name="acceptance">The immutable local receipt returned by the trusted receiver's acceptance.</param>
    /// <returns>Whether all receipt fields match the same currently eligible acceptance.</returns>
    /// <remarks>Equal local metadata does not prove wire origin, physical readiness or read authority.</remarks>
    bool IsCurrentAcceptance(CacheReadPermitAcceptance acceptance);
}

/// <summary>Describes a local acceptance; this value is neither persisted nor transmitted.</summary>
/// <param name="Revision">Accepted strictly increasing receiver sequence.</param>
/// <param name="PreviousRevision">Last accepted sequence, including after withdrawal; zero before the first acceptance.</param>
/// <param name="Continuous">Whether the prior local lease was eligible at acceptance.</param>
public readonly record struct CacheReadPermitAcceptance(long Revision, long PreviousRevision, bool Continuous);
