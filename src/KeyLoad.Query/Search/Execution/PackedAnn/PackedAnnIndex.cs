using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Search;

internal sealed class PackedAnnIndex
{
    private readonly PackedAnnState state;

    private PackedAnnIndex(PackedAnnState state) => this.state = state;

    internal VectorSpace Space => state.Space;
    internal int Count => state.Count;
    internal long RetainedBytesUpperBound => state.RetainedBytesUpperBound;
    internal long BuildScratchBytesUpperBound => state.BuildScratchBytesUpperBound;

    internal static PackedAnnIndex Build(VectorSpace space, IReadOnlyList<VectorRecord> records,
        IOptions<PackedAnnOptions> options, AnnWorkBudget budget)
        => new(PackedAnnBuilder.Build(space, records, options, budget));

    internal byte[] Save(Stream destination, IOptions<PackedAnnStorageOptions> storage, AnnWorkBudget budget)
        => PackedAnnStorageCodec.Save(state, destination, storage, budget);

    internal static PackedAnnIndex Load(Stream source, byte[] expectedSha256,
        IOptions<PackedAnnOptions> policy, IOptions<PackedAnnStorageOptions> storage, AnnWorkBudget budget)
        => new(PackedAnnStorageCodec.Load(source, expectedSha256, policy, storage, budget));

    internal PackedAnnRecordSnapshot Snapshot(string field, IOptions<PackedAnnStorageOptions> storage, AnnWorkBudget budget)
        => PackedAnnRecordSnapshots.Capture(state, field, storage, budget);

    internal AnnSearchResult Search(ReadOnlyMemory<float> query, int limit, ReadOnlyMemory<ulong>? eligibility,
        AnnWorkBudget budget)
        => PackedAnnSearch.Run(state, query, limit, eligibility, budget);
}
