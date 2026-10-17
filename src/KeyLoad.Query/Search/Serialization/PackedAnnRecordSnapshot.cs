using System.Collections.Immutable;
using System.Runtime.InteropServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal sealed record PackedAnnRecordSnapshot(ImmutableArray<VectorRecord> Records, long OwnedBytesUpperBound);

internal static class PackedAnnRecordSnapshots
{
    private const int Empty = 0;
    private const int ReferenceBytes = 8;
    private const int SingleWorkUnit = 1;
    private const long RecordObjectBytes = 64;

    internal static PackedAnnRecordSnapshot Capture(PackedAnnState state, string field,
        IOptions<PackedAnnStorageOptions> configured, AnnWorkBudget budget)
    {
        var storage = configured.Value;
        storage.Validate();
        JsonData.PathSegments(field);
        budget.Check();
        var owned = PackedAnnReservations.Array(ReferenceBytes, state.Count);
        for (var ordinal = Empty; ordinal < state.Count; ordinal++)
        {
            budget.Charge(SingleWorkUnit);
            owned = checked(owned + RecordObjectBytes + PackedAnnReservations.String(state.Ids[ordinal].Length)
                + PackedAnnReservations.Array(sizeof(float), state.Space.Dimension));
        }
        PackedAnnStorageCodec.RequirePeak(checked(state.RetainedBytesUpperBound + owned), storage);
        var records = new VectorRecord[state.Count];
        for (var ordinal = Empty; ordinal < state.Count; ordinal++)
        {
            budget.Charge(state.Space.Dimension);
            var values = state.Vectors.Span(ordinal).ToArray();
            records[ordinal] = new(new string(state.Ids[ordinal].AsSpan()), field, state.Space,
                ImmutableCollectionsMarshal.AsImmutableArray(values), state.Revisions[ordinal]);
        }
        budget.Check();
        return new(ImmutableCollectionsMarshal.AsImmutableArray(records), owned);
    }
}
