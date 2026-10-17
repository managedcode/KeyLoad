namespace KeyLoad.Storage.ZoneTree;

/// <summary>
/// Cumulative logical read work for one open ZoneTree store. Fields are sampled independently,
/// so exact multi-field deltas require an isolated, quiescent store.
/// </summary>
/// <param name="SessionId">Fresh, nonpersisted identity for this store opening.</param>
/// <param name="OwnedPointLookups">Owned point lookups, including misses and staged results.</param>
/// <param name="BorrowedPointLookups">Borrowed point lookups, including misses and staged results.</param>
/// <param name="PointExaminedBytes">Keys plus logical values examined by point lookups.</param>
/// <param name="RangeVisitAttempts">Range calls, including invalid and precancelled calls.</param>
/// <param name="RangeBaselineEntries">Matching baseline entries fetched, including overwritten entries and prefetch.</param>
/// <param name="RangeStagedEntries">Selected staged entries, including tombstones and lookahead.</param>
/// <param name="RangeStagedTombstones">Selected staged entries representing deletion.</param>
/// <param name="RangeLimitLookaheads">Live entries examined solely to establish that a result limit has more data.</param>
/// <param name="RangeExaminedBytes">Keys plus logical values examined in matching baseline and selected staged entries.</param>
public readonly record struct ZoneTreeReadSnapshot(
    Guid SessionId,
    long OwnedPointLookups,
    long BorrowedPointLookups,
    long PointExaminedBytes,
    long RangeVisitAttempts,
    long RangeBaselineEntries,
    long RangeStagedEntries,
    long RangeStagedTombstones,
    long RangeLimitLookaheads,
    long RangeExaminedBytes);
