using System.Collections.Immutable;

namespace KeyLoad.Query.Search;

internal sealed record PackedAnnRecordSnapshot(ImmutableArray<VectorRecord> Records, long OwnedBytesUpperBound);
