namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Nonpersisted cache work and modeled retention, without keys or payloads.</summary>
/// <param name="Configured">Whether this store configured an embedded or coordinated owner, including initially cold control.</param>
/// <param name="Enabled">Whether the current helper and, when coordinated, exact binding permit new hits and fills.</param>
/// <param name="Closed">Whether coordinated admission permanently closed, or the embedded helper was disposed.</param>
/// <param name="RetainedBytes">Index, live, retired-pinned and in-flight modeled bytes.</param>
/// <param name="ChargedEntries">Live, retired-pinned and in-flight entry charges.</param>
/// <param name="LiveEntries">Entries currently indexed for lookup.</param>
/// <param name="RetiredPinnedEntries">Detached entries still held by readers.</param>
/// <param name="InFlightEntries">Reserved unpublished fill candidates.</param>
/// <param name="ActivePins">Readers currently borrowing retained bytes.</param>
/// <param name="Hits">Successful cache pins.</param>
/// <param name="Misses">Eligible cache lookups without a pinnable entry.</param>
/// <param name="ReadBypasses">Lookups bypassed because disabled, unavailable, oversized or pin-limited.</param>
/// <param name="NativeLookups">Point lookups actually attempted against native ZoneTree.</param>
/// <param name="NativeMisses">Native point lookups returning no value.</param>
/// <param name="AdmissionBypasses">Positive native values that could not be retained.</param>
/// <param name="Admissions">Owned positive values published into the index.</param>
/// <param name="Evictions">Entries retired by local admission pressure.</param>
/// <param name="EvictionAttempts">Local victim attempts, at most sixteen per candidate preparation.</param>
/// <remarks>
/// Current-helper counters saturate, reset on cold helper replacement and are zero before any helper exists.
/// Hits counts successful pins, including a pin rejected by a subsequent eligibility recheck.
/// Permanent admission closure may retain charged index, pins or fills until their actual release.
/// Modeled bytes are not process RSS or physical I/O; store logical read diagnostics remain cumulative.
/// </remarks>
public readonly record struct ZoneTreePointCacheSnapshot(
    bool Configured, bool Enabled, bool Closed, long RetainedBytes, int ChargedEntries,
    int LiveEntries, int RetiredPinnedEntries, int InFlightEntries, int ActivePins,
    long Hits, long Misses, long ReadBypasses, long NativeLookups, long NativeMisses,
    long AdmissionBypasses, long Admissions, long Evictions, long EvictionAttempts);
