using System.Collections.Immutable;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record QueryAdapterCollectionDocument(List<decimal> Values, ImmutableArray<decimal> Fixed,
    decimal[] Slots, string Status);
