using System.Collections.Immutable;

namespace KeyLoad.Core;

internal sealed record EventVectorOwnedInventory(EventVectorMap? Header, EventVectorEncodedRow? HeaderRow,
    ImmutableArray<EventVectorEncodedRow> PayloadRows);
