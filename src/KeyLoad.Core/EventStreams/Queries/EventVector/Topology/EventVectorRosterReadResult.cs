using System.Collections.Immutable;

namespace KeyLoad.Core;

internal sealed record EventVectorRosterReadResult(
    ImmutableArray<AtomicPartitionCatalogEntryV1> Entries,
    ImmutableArray<EventVectorCoverageRow> Rows,
    ImmutableArray<EventVectorCoverageRow> Origins,
    EventVectorCoverageRow? RestoreIdentity);
