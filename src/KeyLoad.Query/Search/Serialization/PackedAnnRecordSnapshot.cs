using System.Collections.Immutable;

namespace KeyLoad.Query.Features.Search;

internal sealed record PackedAnnRecordSnapshot(ImmutableArray<VectorRecord> Records, long OwnedBytesUpperBound);
